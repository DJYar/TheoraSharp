using TheoraSharp.Ogg;

namespace TheoraSharp;

public class OggStream
{
    public StreamState Stream { get; init; }
    public int Serial { get; init; }
    
    public bool AtBeginning;
    public IDecoder Decoder;
    
    public OggStream(int serial)
    {
        Serial = serial;
        
        Stream = new StreamState();
        Stream.Initialize(serial);
        Stream.Reset();
        
        AtBeginning = true;
    }
}
