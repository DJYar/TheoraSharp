using System.Collections;
using TheoraSharp;
using TheoraSharp.Ogg;

namespace TheoraSharp;

public class OggVideoReader
{
    private static int BUFFSIZE = 8192;

    private Stream inputStream;

    private OggStream stream;
    private SyncState oy = new();
    private List<OggStream> streams = new();
    private TheoraDec decoder;

    public int Width => decoder?.Width ?? 0;
    public int Height => decoder?.Height ?? 0;
    public float Fps => decoder?.Fps ?? 0;

    public OggVideoReader(Stream inp)
    {
        inputStream = inp;
    }

    public OggVideoReader(byte[] buffer)
    {
        inputStream = new MemoryStream(buffer);
    }

    public OggVideoReader(string fileName)
    {
        inputStream = File.OpenRead(fileName);
    }

    public IEnumerator<T[]> StartReading<T>()
    {
        int res;

        var og = new Page();
        var op = new Packet();

        var stopping = false;
        while (!stopping)
        {
            int index = oy.buffer(BUFFSIZE);
            int read = inputStream.Read(oy.data, index, BUFFSIZE);
            if (read <= 0)
                yield break;

            oy.wrote(read);

            while (!stopping)
            {
                res = oy.pageout(og);
                if (res == 0)
                    break; // need more data

                if (res == -1)
                {
                    throw new Exception("Corrupted page data");
                }

                int serial = og.serialno();
                for (int i = 0; i < streams.Count; i++)
                {
                    stream = streams[i];
                    if (stream.serialno == serial)
                        break;
                    stream = null;
                }

                if (stream == null)
                {
                    stream = new OggStream(serial);
                    streams.Add(stream);
                }

                res = stream.os.pagein(og);
                if (res < 0)
                {
                    // error; stream version mismatch perhaps
                    throw new Exception("Error reading first page of Ogg bitstream data.");
                }

                while (!stopping)
                {
                    res = stream.os.packetout(op);
                    if (res == 0)
                        break; // need more data

                    if (res == -1)
                    {
                        // missing or corrupt data at this page position
                        // no reason to complain; already complained above
                    }
                    else
                    {
                        if (stream.bos)
                        {
                            // typefind
                            if (op.PacketBase[op.PacketPos + 1] == 0x76)
                            {
                                // vorbis audio
                            }
                            else if (op.PacketBase[op.PacketPos + 1] == 0x73)
                            {
                                // smoke video
                            }
                            else if (op.PacketBase[op.PacketPos + 1] == 0x74)
                            {
                                // theora video
                                decoder ??= new TheoraDec();
                                stream.decoder ??= decoder;
                            }

                            stream.bos = false;
                        }

                        if (stream.decoder != null)
                        {
                            if (stream.decoder.ReadPacket(op))
                                yield return stream.decoder.GetData<T>();
                        }
                    }
                }
            }
        }
    }
}