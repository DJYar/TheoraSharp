using System.Diagnostics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.PixelFormats;
using Spectre.Console;
using TheoraSharp;

public static class Program
{
    private const int MaxRenderWidth = 160;

    public static void Main(string[] args)
    {
        var fileName = args.Length == 0 ? throw new FileNotFoundException() : args[0];
        if (!File.Exists(fileName))
        {
            Console.WriteLine($"File not found: {fileName}");
            return;
        }

        Play(fileName).GetAwaiter().GetResult();
    }

    private static async Task Play(string fileName)
    {
        var source = new OggVideoReader(fileName);
        using var reader = source.StartReading<uint>(throwOnCorruptedPacket: false);

        var frames = new Queue<uint[]>();
        var eof = false;
        var audioCursor = 0;
        var audioDisabled = false;
        WinMmAudioPlayer? audioPlayer = null;

        try
        {
            eof = !FillFrameQueue(reader, source, frames, 1, false, ref audioCursor, ref audioPlayer, ref audioDisabled);
            if (Console.IsOutputRedirected)
            {
                DecodeWithoutRendering(reader, source, frames, ref eof, ref audioCursor, ref audioPlayer, ref audioDisabled);
                return;
            }

            await AnsiConsole.Live(Text.Empty)
                .StartAsync(async context =>
                {
                    QueuePendingAudio(source, ref audioCursor, ref audioPlayer, ref audioDisabled);

                    while (frames.Count != 0 || !eof)
                    {
                        if (frames.Count == 0 && !eof)
                        {
                            eof = !FillFrameQueue(reader, source, frames, 1, true, ref audioCursor, ref audioPlayer, ref audioDisabled);
                        }

                        if (frames.Count == 0)
                        {
                            continue;
                        }

                        var frameClock = Stopwatch.StartNew();
                        context.UpdateTarget(CreateCanvasImage(frames.Dequeue(), source.Width, source.Height));

                        if (!eof)
                        {
                            var targetBuffer = GetTargetFrameBuffer(source);
                            eof = !FillFrameQueue(reader, source, frames, targetBuffer, true, ref audioCursor, ref audioPlayer, ref audioDisabled);
                        }

                        var delay = GetFrameDelay(source) - frameClock.Elapsed;
                        if (delay > TimeSpan.Zero)
                        {
                            await Task.Delay(delay);
                        }
                    }

                    QueuePendingAudio(source, ref audioCursor, ref audioPlayer, ref audioDisabled);
                    audioPlayer?.Drain();
                });
        }
        finally
        {
            audioPlayer?.Dispose();
        }
    }

    private static void DecodeWithoutRendering(IEnumerator<uint[]> reader, OggVideoReader source, Queue<uint[]> frames,
        ref bool eof, ref int audioCursor, ref WinMmAudioPlayer? audioPlayer, ref bool audioDisabled)
    {
        QueuePendingAudio(source, ref audioCursor, ref audioPlayer, ref audioDisabled);

        while (!eof)
        {
            frames.Clear();
            eof = !FillFrameQueue(reader, source, frames, 1, true, ref audioCursor, ref audioPlayer, ref audioDisabled);
        }

        QueuePendingAudio(source, ref audioCursor, ref audioPlayer, ref audioDisabled);
        audioPlayer?.Drain();
    }

    private static bool FillFrameQueue(IEnumerator<uint[]> reader, OggVideoReader source, Queue<uint[]> frames, int targetFrameCount,
        bool queueAudio, ref int audioCursor, ref WinMmAudioPlayer? audioPlayer, ref bool audioDisabled)
    {
        while (frames.Count < targetFrameCount)
        {
            if (!reader.MoveNext())
            {
                if (queueAudio)
                {
                    QueuePendingAudio(source, ref audioCursor, ref audioPlayer, ref audioDisabled);
                }

                return false;
            }

            if (queueAudio)
            {
                QueuePendingAudio(source, ref audioCursor, ref audioPlayer, ref audioDisabled);
            }

            if (reader.Current != null)
            {
                frames.Enqueue(reader.Current);
            }
        }

        return true;
    }

    private static void QueuePendingAudio(OggVideoReader source, ref int audioCursor, ref WinMmAudioPlayer? audioPlayer, ref bool audioDisabled)
    {
        if (audioDisabled)
        {
            audioCursor = source.AudioChunks.Count;
            return;
        }

        while (audioCursor < source.AudioChunks.Count)
        {
            var chunk = source.AudioChunks[audioCursor++];
            try
            {
                audioPlayer ??= new WinMmAudioPlayer(chunk.SampleRate, chunk.Channels);
                audioPlayer.Queue(chunk);
            }
            catch
            {
                audioPlayer?.Dispose();
                audioPlayer = null;
                audioDisabled = true;
                audioCursor = source.AudioChunks.Count;
                return;
            }
        }
    }

    private static int GetTargetFrameBuffer(OggVideoReader source)
    {
        var fps = source.Fps > 0 ? source.Fps : 30;
        return Math.Clamp((int)Math.Ceiling(fps / 3), 2, 12);
    }

    private static TimeSpan GetFrameDelay(OggVideoReader source)
    {
        var fps = source.Fps > 0 ? source.Fps : 30;
        return TimeSpan.FromSeconds(1.0 / fps);
    }

    private static CanvasImage CreateCanvasImage(uint[] pixels, int sourceWidth, int sourceHeight)
    {
        var width = GetRenderWidth(sourceWidth);
        var height = Math.Max(1, (int)Math.Round(sourceHeight * (double)width / sourceWidth));

        using var image = new Image<Rgba32>(width, height);
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < height; y++)
            {
                var row = accessor.GetRowSpan(y);
                var sourceY = y * sourceHeight / height;
                var sourceRow = sourceY * sourceWidth;

                for (var x = 0; x < width; x++)
                {
                    var sourceX = x * sourceWidth / width;
                    var pixel = pixels[sourceRow + sourceX];
                    row[x] = new Rgba32((byte)(pixel >> 16), (byte)(pixel >> 8), (byte)pixel, (byte)(pixel >> 24));
                }
            }
        });

        using var stream = new MemoryStream();
        image.Save(stream, new BmpEncoder());
        return new CanvasImage(stream.ToArray())
            .MaxWidth(width)
            .NearestNeighborResampler();
    }

    private static int GetRenderWidth(int sourceWidth)
    {
        var terminalWidth = 80;
        try
        {
            if (!Console.IsOutputRedirected)
            {
                terminalWidth = Math.Max(1, Console.WindowWidth - 1);
            }
        }
        catch
        {
            terminalWidth = 80;
        }

        return Math.Max(1, Math.Min(sourceWidth, Math.Min(terminalWidth, MaxRenderWidth)));
    }
}