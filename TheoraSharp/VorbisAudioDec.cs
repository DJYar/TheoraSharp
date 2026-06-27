using TheoraSharp.Ogg;
using VorbisBlock = TheoraSharp.Vorbis.Block;
using VorbisComment = TheoraSharp.Vorbis.Comment;
using VorbisDspState = TheoraSharp.Vorbis.DspState;
using VorbisInfo = TheoraSharp.Vorbis.Info;

namespace TheoraSharp;

public class VorbisAudioDec : IAudioDecoder
{
    private readonly VorbisInfo _info = new();
    private readonly VorbisComment _comment = new();
    private readonly VorbisDspState _dspState = new();
    private readonly VorbisBlock _block;
    private readonly float[][][] _pcm = new float[1][][];
    private readonly List<DecodedAudioChunk> _audioChunks = new();
    private readonly List<DecodedAudioChunk> _lastAudioChunks = new();
    private int[] _pcmIndexes = Array.Empty<int>();
    private float[] _lastInterleavedSamples = Array.Empty<float>();
    private int _packetIndex;

    public VorbisAudioDec()
    {
        _info.Initialize();
        _comment.Initialize();
        _block = new VorbisBlock(_dspState);
    }

    public int Channels => _info.Channels;
    public int SampleRate => _info.Rate;
    public long TotalSamples { get; private set; }
    public IReadOnlyList<DecodedAudioChunk> AudioChunks => _audioChunks;
    public IReadOnlyList<DecodedAudioChunk> LastAudioChunks => _lastAudioChunks;

    public T[] GetData<T>()
    {
        if (typeof(T) != typeof(float) || _lastInterleavedSamples.Length == 0)
        {
            return null;
        }

        return _lastInterleavedSamples as T[];
    }

    public bool ReadPacket(PacketContext packetContext)
    {
        _lastAudioChunks.Clear();
        _lastInterleavedSamples = Array.Empty<float>();

        if (_packetIndex < 3)
        {
            if (_info.SynthesisHeaderIn(_comment, packetContext) < 0)
            {
                throw new Exception("does not contain Vorbis audio data.");
            }

            if (_packetIndex == 2)
            {
                _dspState.SynthesisInit(_info);
                _block.Initialize(_dspState);
                _pcmIndexes = new int[_info.Channels];
            }

            _packetIndex++;
            return false;
        }

        if (IsHeader(packetContext))
        {
            _packetIndex++;
            return false;
        }

        if (_block.Synthesis(packetContext) != 0)
        {
            throw new Exception("Error Decoding Vorbis.");
        }

        _dspState.SynthesisBlockIn(_block);
        DecodeAvailablePcm(packetContext);

        _packetIndex++;
        return _lastAudioChunks.Count != 0;
    }

    private void DecodeAvailablePcm(PacketContext packetContext)
    {
        while (true)
        {
            var sampleCount = _dspState.SynthesisPcmOut(_pcm, _pcmIndexes);
            if (sampleCount <= 0)
            {
                return;
            }

            var chunk = CopyPcm(packetContext, sampleCount);
            _lastInterleavedSamples = chunk.Samples;
            _lastAudioChunks.Add(chunk);
            _audioChunks.Add(chunk);

            _dspState.SynthesisRead(sampleCount);
            TotalSamples += sampleCount;
        }
    }

    private DecodedAudioChunk CopyPcm(PacketContext packetContext, int sampleCount)
    {
        var channels = _info.Channels;
        var samples = new float[sampleCount * channels];
        var pcm = _pcm[0];
        var dst = 0;

        for (var sample = 0; sample < sampleCount; sample++)
        {
            for (var channel = 0; channel < channels; channel++)
            {
                samples[dst++] = pcm[channel][_pcmIndexes[channel] + sample];
            }
        }

        return new DecodedAudioChunk(
            samples,
            channels,
            _info.Rate,
            sampleCount,
            TotalSamples,
            packetContext.GranulePos,
            packetContext.PacketNo);
    }

    private static bool IsHeader(PacketContext packetContext)
    {
        return packetContext.Bytes >= 1 && (packetContext.PacketBase[packetContext.PacketPos] & 0x01) == 0x01;
    }
}
