using TheoraSharp.Ogg;

namespace TheoraSharp;

public class OggStream
{
    public int serialno;
    public StreamState os;
    public bool bos;
    public IDecoder decoder;

    public OggStream(int serial)
    {
        serialno = serial;
        os = new StreamState();
        os.init(serial);
        os.reset();
        bos = true;
    }
}