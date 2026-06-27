using OggBuffer = TheoraSharp.Ogg.Buffer;
using PacketContext = TheoraSharp.Ogg.PacketContext;

namespace TheoraSharp.Vorbis;

public class Block
{
    internal float[][] Pcm { get; set; } = Array.Empty<float[]>();
    internal OggBuffer Opb { get; } = new();

    internal int LW { get; set; }
    internal int W { get; set; }
    internal int NW { get; set; }
    internal int PcmEnd { get; set; }
    internal int Mode { get; set; }

    internal int EofFlag { get; set; }
    internal long GranulePos { get; set; }
    internal long Sequence { get; set; }
    internal DspState Dsp { get; private set; }

    internal long GlueBits { get; set; }
    internal long TimeBits { get; set; }
    internal long FloorBits { get; set; }
    internal long ResBits { get; set; }

    public Block(DspState dspState)
    {
        Dsp = dspState;
        if (dspState.AnalysisP != 0)
        {
            Opb.WriteInit();
        }
    }

    public void Initialize(DspState dspState)
    {
        Dsp = dspState;
    }

    public void Clear()
    {
        if (Dsp != null && Dsp.AnalysisP != 0)
        {
            Opb.WriteClear();
        }
    }

    public int Synthesis(PacketContext packet)
    {
        var info = Dsp.Vi;

        Opb.ReadInit(packet.PacketBase, packet.PacketPos, packet.Bytes);

        if (Opb.Read(1) != 0)
        {
            return -1;
        }

        var mode = Opb.Read(Dsp.ModeBits);
        if (mode == -1)
        {
            return -1;
        }

        Mode = mode;
        W = info.ModeParam[Mode].BlockFlag;
        if (W != 0)
        {
            LW = Opb.Read(1);
            NW = Opb.Read(1);
            if (NW == -1)
            {
                return -1;
            }
        }
        else
        {
            LW = 0;
            NW = 0;
        }

        GranulePos = packet.GranulePos;
        Sequence = packet.PacketNo - 3;
        EofFlag = packet.EOS;

        PcmEnd = info.BlockSizes[W];
        if (Pcm.Length < info.Channels)
        {
            Pcm = new float[info.Channels][];
        }

        for (var i = 0; i < info.Channels; i++)
        {
            if (Pcm[i] == null || Pcm[i].Length < PcmEnd)
            {
                Pcm[i] = new float[PcmEnd];
            }
            else
            {
                Array.Clear(Pcm[i], 0, PcmEnd);
            }
        }

        var type = info.MapType[info.ModeParam[Mode].Mapping];
        return FuncMapping.MappingP[type].Inverse(this, Dsp.Mode[Mode]);
    }
}
