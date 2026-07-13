using System.Runtime.InteropServices;
using TheoraSharp;

internal sealed class WinMmAudioPlayer : IDisposable
{
    private const int WaveMapper = -1;
    private const int CallbackFunction = 0x00030000;
    private const uint WaveFormatPcm = 1;
    private const uint WaveOutDone = 0x3BD;

    private readonly WaveOutProc _callback;
    private readonly int _headerSize = Marshal.SizeOf<WaveHeader>();
    private IntPtr _handle;
    private int _pendingBuffers;

    public int SampleRate { get; }
    public int Channels { get; }
    public int PendingBuffers => Volatile.Read(ref _pendingBuffers);
    
    public WinMmAudioPlayer(int sampleRate, int channels)
    {
        SampleRate = sampleRate;
        Channels = channels;
        _callback = OnWaveOut;

        var format = new WaveFormat
        {
            FormatTag = (ushort)WaveFormatPcm,
            Channels = (ushort)channels,
            SamplesPerSecond = (uint)sampleRate,
            BitsPerSample = 16,
            BlockAlign = (ushort)(channels * 2),
            AverageBytesPerSecond = (uint)(sampleRate * channels * 2),
            Size = 0
        };

        var result = waveOutOpen(out _handle, WaveMapper, ref format, _callback, IntPtr.Zero, CallbackFunction);
        if (result != 0)
        {
            throw new InvalidOperationException($"waveOutOpen failed: {result}");
        }
    }

    public void Queue(DecodedAudioChunk chunk)
    {
        if (chunk.SampleRate != SampleRate || chunk.Channels != Channels)
        {
            return;
        }

        var data = ConvertToPcm16(chunk.Samples);
        var dataPtr = Marshal.AllocHGlobal(data.Length);
        Marshal.Copy(data, 0, dataPtr, data.Length);

        var buffer = new QueuedBuffer(this, dataPtr);
        var bufferHandle = GCHandle.Alloc(buffer);
        var header = new WaveHeader
        {
            Data = dataPtr,
            BufferLength = data.Length,
            UserData = GCHandle.ToIntPtr(bufferHandle)
        };

        var headerPtr = Marshal.AllocHGlobal(_headerSize);
        buffer.Header = headerPtr;
        Marshal.StructureToPtr(header, headerPtr, false);

        var result = waveOutPrepareHeader(_handle, headerPtr, _headerSize);
        if (result != 0)
        {
            FreeQueuedBuffer(buffer, bufferHandle, false, false);
            throw new InvalidOperationException($"waveOutPrepareHeader failed: {result}");
        }

        Interlocked.Increment(ref _pendingBuffers);

        result = waveOutWrite(_handle, headerPtr, _headerSize);
        if (result != 0)
        {
            FreeQueuedBuffer(buffer, bufferHandle, true, true);
            throw new InvalidOperationException($"waveOutWrite failed: {result}");
        }
    }

    public void Drain()
    {
        while (Volatile.Read(ref _pendingBuffers) > 0)
        {
            Thread.Sleep(10);
        }
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero)
        {
            return;
        }

        Drain();
        waveOutClose(_handle);
        _handle = IntPtr.Zero;
    }

    private void OnWaveOut(IntPtr handle, uint message, IntPtr instance, IntPtr param1, IntPtr param2)
    {
        if (message != WaveOutDone || param1 == IntPtr.Zero)
        {
            return;
        }

        var header = Marshal.PtrToStructure<WaveHeader>(param1);
        var bufferHandle = GCHandle.FromIntPtr(header.UserData);
        if (bufferHandle.Target is QueuedBuffer buffer)
        {
            FreeQueuedBuffer(buffer, bufferHandle, true, true);
        }
    }

    private static void FreeQueuedBuffer(QueuedBuffer buffer, GCHandle bufferHandle, bool unprepare, bool countPending)
    {
        if (unprepare)
        {
            waveOutUnprepareHeader(buffer.Owner._handle, buffer.Header, buffer.Owner._headerSize);
        }

        Marshal.FreeHGlobal(buffer.Data);
        Marshal.FreeHGlobal(buffer.Header);
        bufferHandle.Free();
        if (countPending)
        {
            Interlocked.Decrement(ref buffer.Owner._pendingBuffers);
        }
    }

    private static byte[] ConvertToPcm16(float[] samples)
    {
        var data = new byte[samples.Length * 2];
        var offset = 0;

        foreach (var sample in samples)
        {
            var value = (int)(sample * 32767.0f);
            value = Math.Clamp(value, short.MinValue, short.MaxValue);
            data[offset++] = (byte)value;
            data[offset++] = (byte)(value >> 8);
        }

        return data;
    }

    private sealed class QueuedBuffer
    {
        public WinMmAudioPlayer Owner { get; }
        public IntPtr Data { get; }
        public IntPtr Header { get; set; }
        
        public QueuedBuffer(WinMmAudioPlayer owner, IntPtr data)
        {
            Owner = owner;
            Data = data;
        }
    }

    private delegate void WaveOutProc(IntPtr handle, uint message, IntPtr instance, IntPtr param1, IntPtr param2);

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveFormat
    {
        public ushort FormatTag;
        public ushort Channels;
        public uint SamplesPerSecond;
        public uint AverageBytesPerSecond;
        public ushort BlockAlign;
        public ushort BitsPerSample;
        public ushort Size;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveHeader
    {
        public IntPtr Data;
        public int BufferLength;
        public int BytesRecorded;
        public IntPtr UserData;
        public int Flags;
        public int Loops;
        public IntPtr Next;
        public IntPtr Reserved;
    }

    [DllImport("winmm.dll")]
    private static extern int waveOutOpen(out IntPtr handle, int deviceId, ref WaveFormat format, WaveOutProc callback, IntPtr instance, int flags);

    [DllImport("winmm.dll")]
    private static extern int waveOutPrepareHeader(IntPtr handle, IntPtr header, int size);

    [DllImport("winmm.dll")]
    private static extern int waveOutWrite(IntPtr handle, IntPtr header, int size);

    [DllImport("winmm.dll")]
    private static extern int waveOutUnprepareHeader(IntPtr handle, IntPtr header, int size);

    [DllImport("winmm.dll")]
    private static extern int waveOutClose(IntPtr handle);
}
