using TheoraSharp.Ogg;

namespace TheoraSharp;

public interface IDecoder
{
    bool ReadPacket(Packet packet);
    T[] GetData<T>();
}