using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace TheoraSharp.Ogg;

public class SyncState
{
    private readonly byte[] checksumBuffer = new byte[OggPageHeader.ChecksumLength];

    private int _headerLength;
    private int _bodyLength;
    private int _position;
    private int _bufferSize;
    private int _bufferFill;
    private bool _unsynced;

    public byte[] Data { get; private set; }

    public int Buffer(int size)
    {
        if (size < 0)
            throw new ArgumentOutOfRangeException(nameof(size));

        if (_position != 0)
        {
            _bufferFill -= _position;
            if (_bufferFill > 0)
            {
                Array.Copy(Data, _position, Data, 0, _bufferFill);
            }

            _position = 0;
        }

        if (size > _bufferSize - _bufferFill)
        {
            var newSize = size + _bufferFill + 4096;
            if (Data != null)
            {
                var buffer = new byte[newSize];
                Array.Copy(Data, 0, buffer, 0, Data.Length);
                Data = buffer;
            }
            else
            {
                Data = new byte[newSize];
            }

            _bufferSize = newSize;
        }

        return _bufferFill;
    }

    public void Clear()
    {
        Data = null;
    }

    public int PageOut(Page page)
    {
        while (true)
        {
            var result = PageSeek(page);
            if (result > 0)
            {
                return 1;
            }

            if (result == 0)
            {
                return 0;
            }

            if (!_unsynced)
            {
                _unsynced = true;
                return -1;
            }
        }
    }

    public int PageSeek(Page page)
    {
        var pageOffset = _position;
        var availableBytes = _bufferFill - _position;

        if (_headerLength == 0)
        {
            if (availableBytes < OggPageHeader.Length)
            {
                return 0;
            }

            var header = ReadHeader(pageOffset);
            if (!header.HasValidCapturePattern)
            {
                ResetPageState();
                return SkipToNextCapturePattern(pageOffset, availableBytes);
            }

            _headerLength = header.HeaderSize;
            if (availableBytes < _headerLength)
            {
                return 0;
            }

            _bodyLength = GetBodySize(pageOffset, header.PageSegments);
        }

        if (_bodyLength + _headerLength > availableBytes)
        {
            return 0;
        }

        if (!HasValidChecksum(pageOffset))
        {
            ResetPageState();
            return SkipToNextCapturePattern(pageOffset, availableBytes);
        }

        page?.SetData(Data, pageOffset, _headerLength, Data, pageOffset + _headerLength, _bodyLength);

        _unsynced = false;
        var pageSize = _headerLength + _bodyLength;
        _position += pageSize;
        ResetPageState();
        return pageSize;
    }

    public int Reset()
    {
        _bufferFill = 0;
        _position = 0;
        _unsynced = false;
        ResetPageState();
        return 0;
    }

    public int Wrote(int bytes)
    {
        if (_bufferFill + bytes > _bufferSize)
        {
            return -1;
        }

        _bufferFill += bytes;
        return 0;
    }

    private OggPageHeader ReadHeader(int pageOffset)
    {
        return MemoryMarshal.Read<OggPageHeader>(Data.AsSpan(pageOffset, OggPageHeader.Length));
    }

    private int GetBodySize(int pageOffset, byte pageSegments)
    {
        var size = 0;
        for (var i = 0; i < pageSegments; i++)
        {
            size += Data[pageOffset + OggPageHeader.Length + i] & 0xff;
        }

        return size;
    }

    private bool HasValidChecksum(int pageOffset)
    {
        Array.Copy(Data, pageOffset + OggPageHeader.ChecksumOffset, checksumBuffer, 0, OggPageHeader.ChecksumLength);

        var checksumPage = new Page();
        checksumPage.SetData(Data, pageOffset, _headerLength, Data, pageOffset + _headerLength, _bodyLength);
        checksumPage.WriteChecksum();

        if (ChecksumMatches(pageOffset))
        {
            return true;
        }

        Array.Copy(checksumBuffer, 0, Data, pageOffset + OggPageHeader.ChecksumOffset, OggPageHeader.ChecksumLength);
        return false;
    }

    private bool ChecksumMatches(int pageOffset)
    {
        var checkLeft = BinaryPrimitives.ReadUInt32LittleEndian(checksumBuffer);
        var checkRead = Data.AsSpan(pageOffset + OggPageHeader.ChecksumOffset, OggPageHeader.ChecksumLength);
        var checkRight = BinaryPrimitives.ReadUInt32LittleEndian(checkRead);
        return checkLeft == checkRight;
    }

    private int SkipToNextCapturePattern(int pageOffset, int availableBytes)
    {
        var next = Array.IndexOf(Data, (byte)'O', pageOffset + 1, Math.Max(0, availableBytes - 1));
        if (next < 0)
        {
            next = _bufferFill;
        }

        _position = next;
        return -(next - pageOffset);
    }

    private void ResetPageState()
    {
        _headerLength = 0;
        _bodyLength = 0;
    }
}
