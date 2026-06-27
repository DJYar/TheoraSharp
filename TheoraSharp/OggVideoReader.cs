using TheoraSharp.Ogg;

namespace TheoraSharp;

public class OggVideoReader
{
    private const int DEFAULT_BUFFER_SIZE = 8192;

    private readonly int _bufferSize = DEFAULT_BUFFER_SIZE;
    private readonly Stream _inputStream;
    private IVideoDecoder _decoder;

    public int Width => _decoder?.Width ?? 0;
    public int Height => _decoder?.Height ?? 0;
    public float Fps => _decoder?.Fps ?? 0;
    
    public OggVideoReader(Stream input, int bufferSize = DEFAULT_BUFFER_SIZE)
    {
        _bufferSize = bufferSize;
        _inputStream = input;
    }

    public OggVideoReader(byte[] buffer, int bufferSize = DEFAULT_BUFFER_SIZE)
    {
        _bufferSize = bufferSize;
        _inputStream = new MemoryStream(buffer);
    }

    public OggVideoReader(string fileName, int bufferSize = DEFAULT_BUFFER_SIZE)
    {
        _bufferSize = bufferSize;
        _inputStream = File.OpenRead(fileName);
    }

    public IEnumerator<T[]> StartReading<T>(bool throwOnCorruptedPacket = true)
    {
        var page = new Page();
        var packet = new PacketContext();
        var streams = new List<OggStream>();
        var reader = new SyncState();
        
        while (true)
        {
            var index = reader.Buffer(_bufferSize);
            var read = _inputStream.Read(reader.Data, index, _bufferSize);
            if (read <= 0)
            {
                yield break;
            }

            reader.Wrote(read);

            while (true)
            {
                var result = reader.PageOut(page);
                if (result == 0)
                {
                    break; // need more data
                }

                if (result == -1)
                {
                    if (throwOnCorruptedPacket)
                    {
                        throw new Exception("Corrupted page data");
                    }
                    
                    continue;
                }

                OggStream currentStream = null;
                var serial = page.SerialNumber;
                foreach (var stream in streams)
                {
                    currentStream = stream;
                    if (currentStream.Serial == serial)
                    {
                        break;
                    }
                    currentStream = null;
                }

                if (currentStream == null)
                {
                    currentStream = new OggStream(serial);
                    streams.Add(currentStream);
                }

                result = currentStream.Stream.PageIn(page);
                if (result < 0)
                {
                    // error; stream version mismatch perhaps
                    throw new Exception("Error reading first page of Ogg bitstream data.");
                }

                while (true)
                {
                    result = currentStream.Stream.PacketOut(packet);
                    if (result == 0)
                    {
                        break; // need more data
                    }

                    if (result == -1)
                    {
                        // missing or corrupt data at this page position
                        // no reason to complain; already complained above
                    }
                    else
                    {
                        if (currentStream.AtBeginning)
                        {
                            SetupDecoder(currentStream, packet.PacketBase[packet.PacketPos + 1]);
                            currentStream.AtBeginning = false;
                        }

                        if (currentStream.Decoder == null)
                        {
                            continue;
                        }

                        if (currentStream.Decoder.ReadPacket(packet))
                        {
                            yield return currentStream.Decoder.GetData<T>();
                        }
                    }
                }
            }
        }
    }

    private void SetupDecoder(OggStream stream, byte streamType)
    {
        const byte VorbisAudio = 0x76;
        const byte SmokeVideo = 0x73;
        const byte TheoraAudio = 0x74;
        
        switch (streamType)
        {
            case VorbisAudio:
                // vorbis audio
                break;
            case SmokeVideo:
                // smoke video
                break;
            case TheoraAudio:
                // theora video
                _decoder ??= new TheoraDec();
                stream.Decoder ??= _decoder;
                break;
        }
    }
}
