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

public interface IAudioDecoder : IDecoder
{
    int Channels { get; }
    int SampleRate { get; }
    long TotalSamples { get; }
    
    IReadOnlyList<DecodedAudioChunk> AudioChunks { get; }
    IReadOnlyList<DecodedAudioChunk> LastAudioChunks { get; }
}
