using TheoraSharp.Ogg;

namespace TheoraSharp.Vorbis;

public class VorbisFile : IDisposable
{
    private const int ChunkSize = 8500;

    public const int OvFalse = -1;
    public const int OvEof = -2;
    public const int OvHole = -3;

    public const int OvERead = -128;
    public const int OvEFault = -129;
    public const int OvEImpl = -130;
    public const int OvEInvalid = -131;
    public const int OvENotVorbis = -132;
    public const int OvEBadHeader = -133;
    public const int OvEVersion = -134;
    public const int OvENotAudio = -135;
    public const int OvEBadPacket = -136;
    public const int OvEBadLink = -137;
    public const int OvENoSeek = -138;

    private Stream _dataSource;
    private bool _ownsDataSource;
    private bool _seekable;
    private long _offset;
    private long _end;

    private readonly SyncState _syncState = new();

    private int _links;
    private long[] _offsets;
    private long[] _dataOffsets;
    private int[] _serialNumbers;
    private long[] _pcmLengths;
    private Info[] _info;
    private Comment[] _comments;

    private long _pcmOffset;
    private bool _decodeReady;
    private int _currentSerialNumber;
    private int _currentLink;

    private float _bitTrack;
    private float _sampleTrack;

    private readonly StreamState _streamState = new();
    private readonly DspState _dspState = new();
    private readonly Block _block;

    public VorbisFile(string file)
    {
        _block = new Block(_dspState);

        var stream = File.OpenRead(file);
        _ownsDataSource = true;

        try
        {
            var result = Open(stream, null, 0);
            if (result != 0)
            {
                throw new JOrbisException($"VorbisFile: open returned {result}");
            }
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public VorbisFile(Stream stream, byte[] initial, int initialBytes)
    {
        _block = new Block(_dspState);

        var result = Open(stream, initial, initialBytes);
        if (result != 0)
        {
            throw new JOrbisException($"VorbisFile: open returned {result}");
        }
    }

    public int Streams()
    {
        return _links;
    }

    public bool Seekable()
    {
        return _seekable;
    }

    public int Bitrate(int link)
    {
        if (link >= _links)
        {
            return -1;
        }

        if (!_seekable && link != 0)
        {
            return Bitrate(0);
        }

        if (link < 0)
        {
            long bits = 0;
            for (var i = 0; i < _links; i++)
            {
                bits += (_offsets[i + 1] - _dataOffsets[i]) * 8;
            }

            return (int)Math.Round(bits / TimeTotal(-1));
        }

        if (_seekable)
        {
            return (int)Math.Round((_offsets[link + 1] - _dataOffsets[link]) * 8 / TimeTotal(link));
        }

        if (_info[link].BitrateNominal > 0)
        {
            return _info[link].BitrateNominal;
        }

        if (_info[link].BitrateUpper <= 0)
        {
            return -1;
        }

        return _info[link].BitrateLower > 0
            ? (_info[link].BitrateUpper + _info[link].BitrateLower) / 2
            : _info[link].BitrateUpper;
    }

    public int BitrateInstant()
    {
        var link = _seekable ? _currentLink : 0;
        if (_sampleTrack == 0)
        {
            return -1;
        }

        var result = (int)(_bitTrack / _sampleTrack * _info[link].Rate + 0.5f);
        _bitTrack = 0;
        _sampleTrack = 0;
        return result;
    }

    public int SerialNumber(int link)
    {
        if (link >= _links)
        {
            return -1;
        }

        if (!_seekable && link >= 0)
        {
            return SerialNumber(-1);
        }

        return link < 0 ? _currentSerialNumber : _serialNumbers[link];
    }

    public long RawTotal(int link)
    {
        if (!_seekable || link >= _links)
        {
            return -1;
        }

        if (link >= 0)
        {
            return _offsets[link + 1] - _offsets[link];
        }

        long total = 0;
        for (var i = 0; i < _links; i++)
        {
            total += RawTotal(i);
        }

        return total;
    }

    public long PcmTotal(int link)
    {
        if (!_seekable || link >= _links)
        {
            return -1;
        }

        if (link >= 0)
        {
            return _pcmLengths[link];
        }

        long total = 0;
        for (var i = 0; i < _links; i++)
        {
            total += PcmTotal(i);
        }

        return total;
    }

    public float TimeTotal(int link)
    {
        if (!_seekable || link >= _links)
        {
            return -1;
        }

        if (link >= 0)
        {
            return (float)_pcmLengths[link] / _info[link].Rate;
        }

        var total = 0f;
        for (var i = 0; i < _links; i++)
        {
            total += TimeTotal(i);
        }

        return total;
    }

    public int RawSeek(long position)
    {
        if (!_seekable)
        {
            return -1;
        }

        if (position < 0 || position > _offsets[_links])
        {
            _pcmOffset = -1;
            DecodeClear();
            return -1;
        }

        _pcmOffset = -1;
        DecodeClear();
        SeekHelper(position);

        switch (ProcessPacket(1))
        {
            case 0:
                _pcmOffset = PcmTotal(-1);
                return 0;
            case -1:
                _pcmOffset = -1;
                DecodeClear();
                return -1;
        }

        while (true)
        {
            switch (ProcessPacket(0))
            {
                case 0:
                    return 0;
                case -1:
                    _pcmOffset = -1;
                    DecodeClear();
                    return -1;
            }
        }
    }

    public int PcmSeek(long position)
    {
        var link = -1;
        var total = PcmTotal(-1);

        if (!_seekable)
        {
            return -1;
        }

        if (position < 0 || position > total)
        {
            _pcmOffset = -1;
            DecodeClear();
            return -1;
        }

        for (link = _links - 1; link >= 0; link--)
        {
            total -= _pcmLengths[link];
            if (position >= total)
            {
                break;
            }
        }

        var target = position - total;
        var end = _offsets[link + 1];
        var begin = _offsets[link];
        var best = begin;

        var page = new Page();
        while (begin < end)
        {
            var bisect = end - begin < ChunkSize ? begin : (end + begin) / 2;

            SeekHelper(bisect);
            var result = GetNextPage(page, end - bisect);

            if (result < 0)
            {
                end = bisect;
            }
            else
            {
                var granulePosition = page.GranulePosition;
                if (granulePosition < target)
                {
                    best = result;
                    begin = _offset;
                }
                else
                {
                    end = bisect;
                }
            }
        }

        if (RawSeek(best) != 0)
        {
            _pcmOffset = -1;
            DecodeClear();
            return -1;
        }

        if (_pcmOffset >= position || position > PcmTotal(-1))
        {
            _pcmOffset = -1;
            DecodeClear();
            return -1;
        }

        while (_pcmOffset < position)
        {
            var info = GetInfo(-1);
            if (info == null)
            {
                _pcmOffset = -1;
                DecodeClear();
                return -1;
            }

            var targetSamples = (int)(position - _pcmOffset);
            var pcm = new float[1][][];
            var pcmIndex = new int[info.Channels];
            var samples = _dspState.SynthesisPcmOut(pcm, pcmIndex);

            if (samples > targetSamples)
            {
                samples = targetSamples;
            }

            _dspState.SynthesisRead(samples);
            _pcmOffset += samples;

            if (samples < targetSamples)
            {
                var result = ProcessPacket(1);
                if (result < 0)
                {
                    _pcmOffset = -1;
                    DecodeClear();
                    return -1;
                }

                if (result == 0)
                {
                    _pcmOffset = PcmTotal(-1);
                }
            }
        }

        return 0;
    }

    public int TimeSeek(float seconds)
    {
        var link = -1;
        var pcmTotal = PcmTotal(-1);
        var timeTotal = TimeTotal(-1);

        if (!_seekable)
        {
            return -1;
        }

        if (seconds < 0 || seconds > timeTotal)
        {
            _pcmOffset = -1;
            DecodeClear();
            return -1;
        }

        for (link = _links - 1; link >= 0; link--)
        {
            pcmTotal -= _pcmLengths[link];
            timeTotal -= TimeTotal(link);
            if (seconds >= timeTotal)
            {
                break;
            }
        }

        var target = (long)(pcmTotal + (seconds - timeTotal) * _info[link].Rate);
        return PcmSeek(target);
    }

    public long RawTell()
    {
        return _offset;
    }

    public long PcmTell()
    {
        return _pcmOffset;
    }

    public float TimeTell()
    {
        var link = _seekable ? -1 : 0;
        long pcmTotal = 0;
        var timeTotal = 0f;

        if (_seekable)
        {
            pcmTotal = PcmTotal(-1);
            timeTotal = TimeTotal(-1);

            for (link = _links - 1; link >= 0; link--)
            {
                pcmTotal -= _pcmLengths[link];
                timeTotal -= TimeTotal(link);
                if (_pcmOffset >= pcmTotal)
                {
                    break;
                }
            }
        }

        if (link < 0 || _info == null || link >= _info.Length || _info[link] == null)
        {
            return -1;
        }

        return timeTotal + (float)(_pcmOffset - pcmTotal) / _info[link].Rate;
    }

    public Info GetInfo(int link)
    {
        if (_seekable)
        {
            if (link < 0)
            {
                return _decodeReady ? _info[_currentLink] : null;
            }

            return link >= _links ? null : _info[link];
        }

        return _decodeReady ? _info[0] : null;
    }

    public Comment GetComment(int link)
    {
        if (_seekable)
        {
            if (link < 0)
            {
                return _decodeReady ? _comments[_currentLink] : null;
            }

            return link >= _links ? null : _comments[link];
        }

        return _decodeReady ? _comments[0] : null;
    }

    public int Read(byte[] buffer, int length, int bigEndian, int word, int signed, int[] bitstream)
    {
        if (word != 1 && word != 2)
        {
            return OvEImpl;
        }

        var index = 0;
        length = Math.Min(length, buffer.Length);

        while (true)
        {
            if (_decodeReady)
            {
                var info = GetInfo(-1);
                if (info == null)
                {
                    return -1;
                }

                if (info.Channels <= 0)
                {
                    return OvEFault;
                }

                var pcmBuffer = new float[1][][];
                var pcmIndex = new int[info.Channels];
                var samples = _dspState.SynthesisPcmOut(pcmBuffer, pcmIndex);
                var pcm = pcmBuffer[0];

                if (samples != 0)
                {
                    var channels = info.Channels;
                    var bytesPerSample = word * channels;
                    if (samples > length / bytesPerSample)
                    {
                        samples = length / bytesPerSample;
                    }

                    if (word == 1)
                    {
                        var offset = signed != 0 ? 0 : 128;
                        for (var sample = 0; sample < samples; sample++)
                        {
                            for (var channel = 0; channel < channels; channel++)
                            {
                                var value = Clip((int)(pcm[channel][pcmIndex[channel] + sample] * 128.0f + 0.5f), -128, 127);
                                buffer[index++] = (byte)(value + offset);
                            }
                        }
                    }
                    else if (word == 2)
                    {
                        var offset = signed != 0 ? 0 : 32768;
                        for (var sample = 0; sample < samples; sample++)
                        {
                            for (var channel = 0; channel < channels; channel++)
                            {
                                var value = Clip((int)(pcm[channel][pcmIndex[channel] + sample] * 32768.0f + 0.5f), -32768, 32767);
                                value += offset;

                                if (bigEndian != 0)
                                {
                                    buffer[index++] = (byte)(value >> 8);
                                    buffer[index++] = (byte)value;
                                }
                                else
                                {
                                    buffer[index++] = (byte)value;
                                    buffer[index++] = (byte)(value >> 8);
                                }
                            }
                        }
                    }
                    _dspState.SynthesisRead(samples);
                    _pcmOffset += samples;
                    if (bitstream != null)
                    {
                        bitstream[0] = _currentLink;
                    }

                    return samples * bytesPerSample;
                }
            }

            switch (ProcessPacket(1))
            {
                case 0:
                    return 0;
                case -1:
                    return -1;
            }
        }
    }

    public Info[] GetInfo()
    {
        return _info;
    }

    public Comment[] GetComment()
    {
        return _comments;
    }

    public int Clear()
    {
        _block.Clear();
        _dspState.Clear();
        _streamState.Clear();

        if (_info != null && _links != 0)
        {
            for (var i = 0; i < _links; i++)
            {
                _info[i]?.Clear();
                _comments[i]?.Clear();
            }
        }

        _info = null;
        _comments = null;
        _dataOffsets = null;
        _pcmLengths = null;
        _serialNumbers = null;
        _offsets = null;
        _syncState.Clear();

        _links = 0;
        _seekable = false;
        _decodeReady = false;
        _offset = 0;
        _end = 0;
        _pcmOffset = 0;
        _currentSerialNumber = 0;
        _currentLink = 0;
        _bitTrack = 0;
        _sampleTrack = 0;

        return 0;
    }

    public void Dispose()
    {
        var dataSource = _dataSource;
        Clear();
        _dataSource = null;

        if (_ownsDataSource)
        {
            dataSource?.Dispose();
        }
    }

    private int Open(Stream stream, byte[] initial, int initialBytes)
    {
        return OpenCallbacks(stream, initial, initialBytes);
    }

    private int OpenCallbacks(Stream stream, byte[] initial, int initialBytes)
    {
        _dataSource = stream;
        _syncState.Reset();

        if (initial != null && initialBytes > 0)
        {
            var index = _syncState.Buffer(initialBytes);
            Array.Copy(initial, 0, _syncState.Data, index, initialBytes);
            _syncState.Wrote(initialBytes);
        }

        var result = stream.CanSeek ? OpenSeekable() : OpenNonSeekable();
        if (result != 0)
        {
            _dataSource = null;
            Clear();
        }

        return result;
    }

    private int GetData()
    {
        var index = _syncState.Buffer(ChunkSize);
        var buffer = _syncState.Data;

        int bytes;
        try
        {
            bytes = _dataSource.Read(buffer, index, ChunkSize);
        }
        catch
        {
            return OvERead;
        }

        return _syncState.Wrote(bytes) < 0 ? OvERead : bytes;
    }

    private void SeekHelper(long offset)
    {
        FSeek(_dataSource, offset, SeekOrigin.Begin);
        _offset = offset;
        _syncState.Reset();
    }

    private long GetNextPage(Page page, long boundary)
    {
        if (boundary > 0)
        {
            boundary += _offset;
        }

        while (true)
        {
            if (boundary > 0 && _offset >= boundary)
            {
                return OvFalse;
            }

            var more = _syncState.PageSeek(page);
            if (more < 0)
            {
                _offset -= more;
                continue;
            }

            if (more == 0)
            {
                if (boundary == 0)
                {
                    return OvFalse;
                }

                var result = GetData();
                if (result == 0)
                {
                    return OvEof;
                }

                if (result < 0)
                {
                    return OvERead;
                }
            }
            else
            {
                var result = _offset;
                _offset += more;
                return result;
            }
        }
    }

    private long GetPrevPage(Page page)
    {
        var begin = _offset;
        long pageOffset = OvFalse;

        while (pageOffset == OvFalse)
        {
            begin -= ChunkSize;
            if (begin < 0)
            {
                begin = 0;
            }

            SeekHelper(begin);
            var searchEnd = begin + ChunkSize;

            while (_offset < searchEnd)
            {
                var result = GetNextPage(page, searchEnd - _offset);
                if (result == OvERead)
                {
                    return OvERead;
                }

                if (result < 0)
                {
                    break;
                }

                pageOffset = result;
            }

            if (begin == 0 && pageOffset == OvFalse)
            {
                return OvFalse;
            }
        }

        SeekHelper(pageOffset);
        var finalResult = GetNextPage(page, ChunkSize);
        return finalResult < 0 ? OvEFault : pageOffset;
    }

    private int BisectForwardSerialNumber(long begin, long searched, long end, int currentNumber, int linkIndex)
    {
        var endSearched = end;
        var next = end;
        var page = new Page();

        while (searched < endSearched)
        {
            var bisect = endSearched - searched < ChunkSize ? searched : (searched + endSearched) / 2;

            SeekHelper(bisect);
            var result = GetNextPage(page, -1);
            if (result == OvERead)
            {
                return OvERead;
            }

            if (result < 0 || page.SerialNumber != currentNumber)
            {
                endSearched = bisect;
                if (result >= 0)
                {
                    next = result;
                }
            }
            else
            {
                searched = result + page.HeaderLength + page.BodyLength;
            }
        }

        SeekHelper(next);
        var finalResult = GetNextPage(page, -1);
        if (finalResult == OvERead)
        {
            return OvERead;
        }

        if (searched >= end || finalResult < 0)
        {
            _links = linkIndex + 1;
            _offsets = new long[linkIndex + 2];
            _offsets[linkIndex + 1] = searched;
        }
        else
        {
            var result = BisectForwardSerialNumber(next, _offset, end, page.SerialNumber, linkIndex + 1);
            if (result < 0)
            {
                return result;
            }
        }

        _offsets[linkIndex] = begin;
        return 0;
    }

    private int FetchHeaders(Info info, Comment comment, int[] serialNumber, Page firstPage)
    {
        var page = new Page();
        var packet = new PacketContext();

        if (firstPage == null)
        {
            var result = GetNextPage(page, ChunkSize);
            if (result == OvERead)
            {
                return OvERead;
            }

            if (result < 0)
            {
                return OvENotVorbis;
            }

            firstPage = page;
        }

        if (serialNumber != null)
        {
            serialNumber[0] = firstPage.SerialNumber;
        }

        _streamState.Initialize(firstPage.SerialNumber);

        info.Initialize();
        comment.Initialize();

        var headerCount = 0;
        while (headerCount < 3)
        {
            _streamState.PageIn(firstPage);
            while (headerCount < 3)
            {
                var result = _streamState.PacketOut(packet);
                if (result == 0)
                {
                    break;
                }

                if (result == -1)
                {
                    info.Clear();
                    comment.Clear();
                    _streamState.Clear();
                    return -1;
                }

                if (info.SynthesisHeaderIn(comment, packet) != 0)
                {
                    info.Clear();
                    comment.Clear();
                    _streamState.Clear();
                    return -1;
                }

                headerCount++;
            }

            if (headerCount < 3 && GetNextPage(firstPage, 1) < 0)
            {
                info.Clear();
                comment.Clear();
                _streamState.Clear();
                return -1;
            }
        }

        return 0;
    }

    private void PrefetchAllHeaders(Info firstInfo, Comment firstComment, long dataOffset)
    {
        var page = new Page();

        _info = new Info[_links];
        _comments = new Comment[_links];
        _dataOffsets = new long[_links];
        _pcmLengths = new long[_links];
        _serialNumbers = new int[_links];

        for (var i = 0; i < _links; i++)
        {
            if (firstInfo != null && firstComment != null && i == 0)
            {
                _info[i] = firstInfo;
                _comments[i] = firstComment;
                _dataOffsets[i] = dataOffset;
            }
            else
            {
                _info[i] = new Info();
                _comments[i] = new Comment();

                SeekHelper(_offsets[i]);
                if (FetchHeaders(_info[i], _comments[i], null, null) < 0)
                {
                    _dataOffsets[i] = -1;
                }
                else
                {
                    _dataOffsets[i] = _offset;
                    _streamState.Clear();
                }
            }

            var end = _offsets[i + 1];
            SeekHelper(end);

            while (true)
            {
                var result = GetPrevPage(page);
                if (result < 0)
                {
                    _info[i].Clear();
                    _comments[i].Clear();
                    break;
                }

                if (page.GranulePosition != -1)
                {
                    _serialNumbers[i] = page.SerialNumber;
                    _pcmLengths[i] = page.GranulePosition;
                    break;
                }
            }
        }
    }

    private int MakeDecodeReady()
    {
        if (_decodeReady)
        {
            return OvEFault;
        }

        var link = _seekable ? _currentLink : 0;
        _dspState.SynthesisInit(_info[link]);
        _block.Initialize(_dspState);
        _decodeReady = true;
        return 0;
    }

    private int OpenSeekable()
    {
        var initialInfo = new Info();
        var initialComment = new Comment();
        var page = new Page();
        var serialNumber = new int[1];

        var result = FetchHeaders(initialInfo, initialComment, serialNumber, null);
        var dataOffset = _offset;
        _streamState.Clear();
        if (result < 0)
        {
            return -1;
        }

        _seekable = true;
        if (FSeek(_dataSource, 0, SeekOrigin.End) != 0)
        {
            return OvENoSeek;
        }

        _offset = Tell(_dataSource);
        _end = _offset;

        var end = GetPrevPage(page);
        if (end < 0)
        {
            return (int)end;
        }

        if (page.SerialNumber != serialNumber[0])
        {
            if (BisectForwardSerialNumber(0, 0, end + 1, serialNumber[0], 0) < 0)
            {
                Clear();
                return OvERead;
            }
        }
        else if (BisectForwardSerialNumber(0, end, end + 1, serialNumber[0], 0) < 0)
        {
            Clear();
            return OvERead;
        }

        PrefetchAllHeaders(initialInfo, initialComment, dataOffset);
        return RawSeek(0);
    }

    private int OpenNonSeekable()
    {
        _links = 1;
        _info = new[] { new Info() };
        _comments = new[] { new Comment() };

        var serialNumber = new int[1];
        if (FetchHeaders(_info[0], _comments[0], serialNumber, null) < 0)
        {
            return -1;
        }

        _currentSerialNumber = serialNumber[0];
        _currentLink = 0;
        return MakeDecodeReady();
    }

    private void DecodeClear()
    {
        _streamState.Clear();
        _dspState.Clear();
        _block.Clear();
        _decodeReady = false;
        _bitTrack = 0;
        _sampleTrack = 0;
    }

    private int ProcessPacket(int read)
    {
        var page = new Page();

        while (true)
        {
            if (_decodeReady)
            {
                var packet = new PacketContext();
                var result = _streamState.PacketOut(packet);

                if (result > 0)
                {
                    var granulePosition = packet.GranulePos;
                    if (_block.Synthesis(packet) == 0)
                    {
                        var oldSamples = _dspState.SynthesisPcmOut(null, null);
                        _dspState.SynthesisBlockIn(_block);
                        _sampleTrack += _dspState.SynthesisPcmOut(null, null) - oldSamples;
                        _bitTrack += packet.Bytes * 8;

                        if (granulePosition != -1 && packet.EOS == 0)
                        {
                            var link = _seekable ? _currentLink : 0;
                            var samples = _dspState.SynthesisPcmOut(null, null);
                            granulePosition -= samples;

                            for (var i = 0; i < link; i++)
                            {
                                granulePosition += _pcmLengths[i];
                            }

                            _pcmOffset = granulePosition;
                        }

                        return 1;
                    }
                }
            }

            if (read == 0)
            {
                return 0;
            }

            if (GetNextPage(page, -1) < 0)
            {
                return 0;
            }

            _bitTrack += page.HeaderLength * 8;

            if (_decodeReady && _currentSerialNumber != page.SerialNumber)
            {
                DecodeClear();
            }

            if (!_decodeReady)
            {
                int link;

                if (_seekable)
                {
                    _currentSerialNumber = page.SerialNumber;

                    for (link = 0; link < _links; link++)
                    {
                        if (_serialNumbers[link] == _currentSerialNumber)
                        {
                            break;
                        }
                    }

                    if (link == _links)
                    {
                        return -1;
                    }

                    _currentLink = link;
                    _streamState.Initialize(_currentSerialNumber);
                    _streamState.Reset();
                }
                else
                {
                    var serialNumber = new int[1];
                    var result = FetchHeaders(_info[0], _comments[0], serialNumber, page);
                    _currentSerialNumber = serialNumber[0];
                    if (result != 0)
                    {
                        return result;
                    }

                    _currentLink++;
                }

                var decodeReadyResult = MakeDecodeReady();
                if (decodeReadyResult != 0)
                {
                    return decodeReadyResult;
                }
            }

            _streamState.PageIn(page);
        }
    }

    private static int FSeek(Stream stream, long offset, SeekOrigin origin)
    {
        if (!stream.CanSeek)
        {
            return -1;
        }

        try
        {
            stream.Seek(offset, origin);
            return 0;
        }
        catch
        {
            return -1;
        }
    }

    private static long Tell(Stream stream)
    {
        return stream.CanSeek ? stream.Position : 0;
    }

    private static int Clip(int value, int min, int max)
    {
        if (value > max)
        {
            return max;
        }

        return value < min ? min : value;
    }
}
