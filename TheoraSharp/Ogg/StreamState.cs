namespace TheoraSharp.Ogg;

public class StreamState
{
    private const int InitialBodyStorage = 16 * 1024;
    private const int InitialLacingStorage = 1024;
    private const int HeaderBufferSize = 282;
    private const int MaxPageSegments = 255;
    private const int NominalPageBodySize = 4096;

    private const int BeginningOfPacketFlag = 0x100;
    private const int EndOfStreamFlag = 0x200;
    private const int LostSyncFlag = 0x400;

    private byte[] _bodyData;
    private int _bodyStorage;
    private int _bodyFill;
    private int _bodyReturned;

    private int[] _lacingValues;
    private int _lacingStorage;
    private int _lacingFill;
    private int _lacingPacket;
    private int _lacingReturned;

    private readonly byte[] _headerBuffer = new byte[HeaderBufferSize];
    private int _headerFill;

    private long[] _granuleValues;
    private long _granulePosition;
    
    private int _beginningOfStream;
    private int _endOfStream;
    private int _serialNumber;
    private int _pageNumber;
    private long _packetNumber;

    public int EndOfStream => _endOfStream;
    
    public StreamState()
    {
        InitializeStorage();
    }

    public void Initialize(int serialNumber)
    {
        if (_bodyData == null)
        {
            InitializeStorage();
        }
        else
        {
            Array.Clear(_bodyData, 0, _bodyData.Length);
            Array.Clear(_lacingValues, 0, _lacingValues.Length);
            Array.Clear(_granuleValues, 0, _granuleValues.Length);
        }

        _serialNumber = serialNumber;
    }

    public void Clear()
    {
        _bodyData = null;
        _lacingValues = null;
        _granuleValues = null;
    }

    public int PacketIn(PacketContext packetContext)
    {
        var lacingValueCount = packetContext.Bytes / 0xFF + 1;

        CompactReturnedBody();
        ExpandBody(packetContext.Bytes);
        ExpandLacing(lacingValueCount);

        Array.Copy(packetContext.PacketBase, packetContext.PacketPos, _bodyData, _bodyFill, packetContext.Bytes);
        _bodyFill += packetContext.Bytes;

        var lacingIndex = _lacingFill;
        for (var i = 0; i < lacingValueCount - 1; i++)
        {
            _lacingValues[lacingIndex + i] = 0xFF;
            _granuleValues[lacingIndex + i] = _granulePosition;
        }

        _lacingValues[lacingIndex + lacingValueCount - 1] = packetContext.Bytes % 0xFF;
        _granulePosition = _granuleValues[lacingIndex + lacingValueCount - 1] = packetContext.GranulePos;
        _lacingValues[lacingIndex] |= BeginningOfPacketFlag;
        _lacingFill += lacingValueCount;

        _packetNumber++;
        if (packetContext.EOS != 0)
        {
            _endOfStream = 1;
        }

        return 0;
    }

    public int PacketOut(PacketContext packetContext)
    {
        var pointer = _lacingReturned;
        if (_lacingPacket <= pointer)
        {
            return 0;
        }

        if ((_lacingValues[pointer] & LostSyncFlag) != 0)
        {
            _lacingReturned++;
            _packetNumber++;
            return -1;
        }

        var size = _lacingValues[pointer] & 0xFF;
        var bytes = size;

        packetContext.PacketBase = _bodyData;
        packetContext.PacketPos = _bodyReturned;
        packetContext.EOS = _lacingValues[pointer] & EndOfStreamFlag;
        packetContext.BOS = _lacingValues[pointer] & BeginningOfPacketFlag;

        while (size == 0xFF)
        {
            var value = _lacingValues[++pointer];
            size = value & 0xFF;
            if ((value & EndOfStreamFlag) != 0)
            {
                packetContext.EOS = EndOfStreamFlag;
            }

            bytes += size;
        }

        packetContext.PacketNo = _packetNumber;
        packetContext.GranulePos = _granuleValues[pointer];
        packetContext.Bytes = bytes;

        _bodyReturned += bytes;
        _lacingReturned = pointer + 1;
        _packetNumber++;
        return 1;
    }

    public int PageIn(Page page)
    {
        var headerBuffer = page.HeaderBuffer;
        var headerOffset = page.HeaderOffset;
        var bodyBuffer = page.BodyBuffer;
        var bodyOffset = page.BodyOffset;
        var bodySize = page.BodyLength;
        var segmentPointer = 0;

        var version = page.Version;
        var isContinued = page.IsContinued;
        var isBeginningOfStream = page.IsBeginningOfStream;
        var isEndOfStream = page.IsEndOfStream;
        var incomingGranulePosition = page.GranulePosition;
        var incomingSerialNumber = page.SerialNumber;
        var incomingPageNumber = page.SequenceNumber;
        var segmentCount = headerBuffer[headerOffset + 26] & 0xFF;

        CompactReturnedData();

        if (incomingSerialNumber != _serialNumber || version > 0)
        {
            return -1;
        }

        ExpandLacing(segmentCount + 1);

        if (incomingPageNumber != _pageNumber)
        {
            RollbackPartialPacket();

            if (_pageNumber != -1)
            {
                _lacingValues[_lacingFill++] = LostSyncFlag;
                _lacingPacket++;
            }
        }

        if (isContinued && (_lacingFill < 1 || _lacingValues[_lacingFill - 1] == LostSyncFlag))
        {
            isBeginningOfStream = false;

            for (; segmentPointer < segmentCount; segmentPointer++)
            {
                var value = headerBuffer[headerOffset + OggPageHeader.Length + segmentPointer] & 0xFF;
                bodyOffset += value;
                bodySize -= value;

                if (value < 0xFF)
                {
                    segmentPointer++;
                    break;
                }
            }
        }

        if (bodySize != 0)
        {
            ExpandBody(bodySize);
            Array.Copy(bodyBuffer, bodyOffset, _bodyData, _bodyFill, bodySize);
            _bodyFill += bodySize;
        }

        var completedPacketIndex = -1;
        while (segmentPointer < segmentCount)
        {
            var value = headerBuffer[headerOffset + OggPageHeader.Length + segmentPointer] & 0xFF;
            _lacingValues[_lacingFill] = value;
            _granuleValues[_lacingFill] = -1;

            if (isBeginningOfStream)
            {
                _lacingValues[_lacingFill] |= BeginningOfPacketFlag;
                isBeginningOfStream = false;
            }

            if (value < 0xFF)
            {
                completedPacketIndex = _lacingFill;
            }

            _lacingFill++;
            segmentPointer++;

            if (value < 0xFF)
            {
                _lacingPacket = _lacingFill;
            }
        }

        if (completedPacketIndex != -1)
        {
            _granuleValues[completedPacketIndex] = incomingGranulePosition;
        }

        if (isEndOfStream)
        {
            _endOfStream = 1;
            if (_lacingFill > 0)
            {
                _lacingValues[_lacingFill - 1] |= EndOfStreamFlag;
            }
        }

        _pageNumber = incomingPageNumber + 1;
        return 0;
    }

    public int Flush(Page page)
    {
        var segmentCount = Math.Min(_lacingFill, MaxPageSegments);
        if (segmentCount == 0)
        {
            return 0;
        }

        var accumulatedBytes = 0;
        var granulePositionForPage = _granuleValues[0];
        var valuesToFlush = 0;

        if (_beginningOfStream == 0)
        {
            granulePositionForPage = 0;
            for (; valuesToFlush < segmentCount; valuesToFlush++)
            {
                if ((_lacingValues[valuesToFlush] & 0xFF) == 0xFF)
                {
                    continue;
                }
                
                valuesToFlush++;
                break;
            }
        }
        else
        {
            for (; valuesToFlush < segmentCount; valuesToFlush++)
            {
                if (accumulatedBytes > NominalPageBodySize)
                {
                    break;
                }

                accumulatedBytes += _lacingValues[valuesToFlush] & 0xFF;
                granulePositionForPage = _granuleValues[valuesToFlush];
            }
        }

        WriteHeader(valuesToFlush, granulePositionForPage, out var bytes);

        page.SetData(_headerBuffer, 0, _headerFill, _bodyData, _bodyReturned, bytes);

        _lacingFill -= valuesToFlush;
        Array.Copy(_lacingValues, valuesToFlush, _lacingValues, 0, _lacingFill);
        Array.Copy(_granuleValues, valuesToFlush, _granuleValues, 0, _lacingFill);
        _bodyReturned += bytes;

        page.WriteChecksum();
        return 1;
    }

    public int PageOut(Page page)
    {
        if ((_endOfStream != 0 && _lacingFill != 0) || _bodyFill - _bodyReturned > NominalPageBodySize || _lacingFill >= MaxPageSegments || (_lacingFill != 0 && _beginningOfStream == 0))
        {
            return Flush(page);
        }

        return 0;
    }

    public void Reset()
    {
        _bodyFill = 0;
        _bodyReturned = 0;
        _lacingFill = 0;
        _lacingPacket = 0;
        _lacingReturned = 0;
        _headerFill = 0;
        _endOfStream = 0;
        _beginningOfStream = 0;
        _pageNumber = -1;
        _packetNumber = 0;
        _granulePosition = 0;
    }

    private void InitializeStorage()
    {
        _bodyStorage = InitialBodyStorage;
        _bodyData = new byte[_bodyStorage];
        _lacingStorage = InitialLacingStorage;
        _lacingValues = new int[_lacingStorage];
        _granuleValues = new long[_lacingStorage];
    }

    private void ExpandBody(int needed)
    {
        if (_bodyStorage > _bodyFill + needed)
        {
            return;
        }

        _bodyStorage += needed + 1024;
        var buffer = new byte[_bodyStorage];
        Array.Copy(_bodyData, 0, buffer, 0, _bodyData.Length);
        _bodyData = buffer;
    }

    private void ExpandLacing(int needed)
    {
        if (_lacingStorage > _lacingFill + needed)
        {
            return;
        }

        _lacingStorage += needed + 32;

        var lacingBuffer = new int[_lacingStorage];
        Array.Copy(_lacingValues, 0, lacingBuffer, 0, _lacingValues.Length);
        _lacingValues = lacingBuffer;

        var granuleBuffer = new long[_lacingStorage];
        Array.Copy(_granuleValues, 0, granuleBuffer, 0, _granuleValues.Length);
        _granuleValues = granuleBuffer;
    }

    private void CompactReturnedBody()
    {
        if (_bodyReturned == 0)
        {
            return;
        }

        _bodyFill -= _bodyReturned;
        if (_bodyFill != 0)
        {
            Array.Copy(_bodyData, _bodyReturned, _bodyData, 0, _bodyFill);
        }

        _bodyReturned = 0;
    }

    private void CompactReturnedData()
    {
        CompactReturnedBody();

        if (_lacingReturned == 0)
        {
            return;
        }

        var remaining = _lacingFill - _lacingReturned;
        if (remaining != 0)
        {
            Array.Copy(_lacingValues, _lacingReturned, _lacingValues, 0, remaining);
            Array.Copy(_granuleValues, _lacingReturned, _granuleValues, 0, remaining);
        }

        _lacingFill -= _lacingReturned;
        _lacingPacket -= _lacingReturned;
        _lacingReturned = 0;
    }

    private void RollbackPartialPacket()
    {
        for (var i = _lacingPacket; i < _lacingFill; i++)
        {
            _bodyFill -= _lacingValues[i] & 0xff;
        }

        _lacingFill = _lacingPacket;
    }

    private void WriteHeader(int valuesToFlush, long granulePositionForPage, out int bodyBytes)
    {
        Array.Copy(BitConverter.GetBytes(OggPageHeader.CapturePatternValue), 0, _headerBuffer, 0, 4);
        _headerBuffer[4] = 0x00;
        _headerBuffer[5] = 0x00;
        if ((_lacingValues[0] & BeginningOfPacketFlag) == 0)
        {
            _headerBuffer[5] |= 0x01;
        }

        if (_beginningOfStream == 0)
        {
            _headerBuffer[5] |= 0x02;
        }

        if (_endOfStream != 0 && _lacingFill == valuesToFlush)
        {
            _headerBuffer[5] |= 0x04;
        }

        _beginningOfStream = 1;
        for (var i = 6; i < 14; i++)
        {
            _headerBuffer[i] = (byte)granulePositionForPage;
            granulePositionForPage >>= 8;
        }

        var serial = _serialNumber;
        for (var i = 14; i < 18; i++)
        {
            _headerBuffer[i] = (byte)serial;
            serial >>= 8;
        }

        if (_pageNumber == -1)
        {
            _pageNumber = 0;
        }

        var pageSequenceNumber = _pageNumber++;
        for (var i = 18; i < 22; i++)
        {
            _headerBuffer[i] = (byte)pageSequenceNumber;
            pageSequenceNumber >>= 8;
        }

        _headerBuffer[22] = 0;
        _headerBuffer[23] = 0;
        _headerBuffer[24] = 0;
        _headerBuffer[25] = 0;

        _headerBuffer[26] = (byte)valuesToFlush;
        bodyBytes = 0;
        for (var i = 0; i < valuesToFlush; i++)
        {
            _headerBuffer[i + OggPageHeader.Length] = (byte)_lacingValues[i];
            bodyBytes += _headerBuffer[i + OggPageHeader.Length] & 0xff;
        }

        _headerFill = valuesToFlush + OggPageHeader.Length;
    }
}
