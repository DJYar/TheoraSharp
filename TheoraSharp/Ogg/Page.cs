using System.Buffers.Binary;

namespace TheoraSharp.Ogg;

public sealed class Page
{
    [Flags]
    private enum Flags : byte
    {
        ContinuedPacket = 0x01,
        BeginningOfStream = 0x02,
        EndOfStream = 0x04
    }
    
    private const int VersionOffset = 4;
    private const int HeaderTypeOffset = 5;
    private const int GranulePositionOffset = 6;
    private const int SerialNumberOffset = 14;
    private const int PageSequenceNumberOffset = 18;
    
    public byte[] HeaderBuffer { get; internal set; }
    public int HeaderOffset { get; internal set; }
    public int HeaderLength { get; internal set; }
    public byte[] BodyBuffer { get; internal set; }
    public int BodyOffset { get; internal set; }
    public int BodyLength { get; internal set; }

    internal int Version => HeaderBuffer[HeaderOffset + VersionOffset] & 0xFF;
    
    public bool IsContinued => (HeaderBuffer[HeaderOffset + HeaderTypeOffset] & (byte)Flags.ContinuedPacket) != 0;
    public bool IsBeginningOfStream => (HeaderBuffer[HeaderOffset + HeaderTypeOffset] & (byte)Flags.BeginningOfStream) != 0;
    public bool IsEndOfStream => (HeaderBuffer[HeaderOffset + HeaderTypeOffset] & (byte)Flags.EndOfStream) != 0;

    public long GranulePosition => BinaryPrimitives.ReadInt64LittleEndian(HeaderBuffer.AsSpan(HeaderOffset + GranulePositionOffset, sizeof(long)));
    public int SerialNumber => BinaryPrimitives.ReadInt32LittleEndian(HeaderBuffer.AsSpan(HeaderOffset + SerialNumberOffset, sizeof(int)));
    internal int SequenceNumber => BinaryPrimitives.ReadInt32LittleEndian(HeaderBuffer.AsSpan(HeaderOffset + PageSequenceNumberOffset, sizeof(int)));

    internal void SetData(byte[] headerBuffer, int headerOffset, int headerLength, byte[] bodyBuffer, int bodyOffset, int bodyLength)
    {
        HeaderBuffer = headerBuffer;
        HeaderOffset = headerOffset;
        HeaderLength = headerLength;
        BodyBuffer = bodyBuffer;
        BodyOffset = bodyOffset;
        BodyLength = bodyLength;
    }

    internal void WriteChecksum()
    {
        OggCrc32.WriteChecksum(HeaderBuffer.AsSpan(HeaderOffset, HeaderLength), BodyBuffer.AsSpan(BodyOffset, BodyLength));
    }
}
