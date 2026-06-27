namespace TheoraSharp;

public sealed class DecodedAudioChunk
{
    public DecodedAudioChunk(
        float[] samples,
        int channels,
        int sampleRate,
        int sampleCount,
        long startSample,
        long granulePosition,
        long packetNumber)
    {
        Samples = samples;
        Channels = channels;
        SampleRate = sampleRate;
        SampleCount = sampleCount;
        StartSample = startSample;
        GranulePosition = granulePosition;
        PacketNumber = packetNumber;
    }

    public float[] Samples { get; }
    public int Channels { get; }
    public int SampleRate { get; }
    public int SampleCount { get; }
    public long StartSample { get; }
    public long GranulePosition { get; }
    public long PacketNumber { get; }

    public double StartTimeSeconds => SampleRate > 0 ? (double)StartSample / SampleRate : 0;
    public double DurationSeconds => SampleRate > 0 ? (double)SampleCount / SampleRate : 0;
}
