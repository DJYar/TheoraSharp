using System.Runtime.InteropServices;

namespace TheoraSharp.Ogg;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly struct OggPageHeader
{
    public const int Length = 27;
    public const int ChecksumOffset = 22;
    public const int ChecksumLength = sizeof(uint);
    public const uint CapturePatternValue = 0x5367674f; //OggS

    public readonly uint CapturePattern;
    public readonly byte Version;
    public readonly byte HeaderType;
    public readonly long GranulePosition;
    public readonly uint BitstreamSerialNumber;
    public readonly uint PageSequenceNumber;
    public readonly uint Checksum;
    public readonly byte PageSegments;

    public bool HasValidCapturePattern => CapturePattern == CapturePatternValue;
    public int HeaderSize => Length + PageSegments;
}
