using System.Buffers.Binary;

namespace TheoraSharp.Ogg;

internal static class OggCrc32
{
    private static readonly uint[] LookupTable = BuildLookupTable();

    public static uint Compute(ReadOnlySpan<byte> header, ReadOnlySpan<byte> body)
    {
        var crc = 0u;

        foreach (var value in header)
        {
            crc = (crc << 8) ^ LookupTable[((crc >> 24) & 0xff) ^ value];
        }

        foreach (var value in body)
        {
            crc = (crc << 8) ^ LookupTable[((crc >> 24) & 0xff) ^ value];
        }

        return crc;
    }

    public static void WriteChecksum(Span<byte> header, ReadOnlySpan<byte> body)
    {
        header.Slice(OggPageHeader.ChecksumOffset, OggPageHeader.ChecksumLength).Clear();
        var checksum = Compute(header, body);
        BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(OggPageHeader.ChecksumOffset, OggPageHeader.ChecksumLength), checksum);
    }

    private static uint[] BuildLookupTable()
    {
        var table = new uint[256];
        for (var i = 0; i < table.Length; i++)
        {
            table[i] = CreateTableEntry(i);
        }

        return table;
    }

    private static uint CreateTableEntry(int index)
    {
        var register = (uint)index << 24;
        for (var i = 0; i < 8; i++)
        {
            register = (register & 0x80000000) != 0
                ? (register << 1) ^ 0x04c11db7
                : register << 1;
        }

        return register;
    }
}
