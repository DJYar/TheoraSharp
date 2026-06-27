using TheoraSharp.Ogg;

namespace TheoraSharp;

public interface IDecoder
{
    bool ReadPacket(PacketContext packetContext);
    T[] GetData<T>();
}

public interface IVideoDecoder : IDecoder
{
    int Width { get; }
    int Height { get; }
    float Fps { get; }
}