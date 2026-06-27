namespace TheoraSharp.Vorbis;

public class DspState
{
    private const float Pi = 3.1415926539f;
    private const int ViTransformB = 1;
    private const int ViWindowB = 1;

    internal int AnalysisP { get; set; }
    internal Info Vi { get; set; }
    internal int ModeBits { get; set; }

    internal float[][] Pcm { get; set; }
    internal int PcmStorage { get; set; }
    internal int PcmCurrent { get; set; }
    internal int PcmReturned { get; set; }

    internal float[] Multipliers { get; set; }
    internal int EnvelopeStorage { get; set; }
    internal int EnvelopeCurrent { get; set; }

    internal int EofFlag { get; set; }

    internal int LW { get; set; }
    internal int W { get; set; }
    internal int NW { get; set; }
    internal int CenterW { get; set; }

    internal long GranulePos { get; set; }
    internal long Sequence { get; set; }

    internal long GlueBits { get; set; }
    internal long TimeBits { get; set; }
    internal long FloorBits { get; set; }
    internal long ResBits { get; set; }

    internal float[][][][][] Window { get; set; }
    internal object[][] Transform { get; set; }
    internal CodeBook[] FullBooks { get; set; }
    internal object[] Mode { get; set; }

    internal byte[] Header { get; set; }
    internal byte[] Header1 { get; set; }
    internal byte[] Header2 { get; set; }

    public DspState()
    {
        InitializeLookupStorage();
    }

    private void InitializeLookupStorage()
    {
        Transform = new object[2][];
        Window = new float[2][][][][];

        Window[0] = new float[2][][][];
        Window[0][0] = new float[2][][];
        Window[0][1] = new float[2][][];
        Window[0][0][0] = new float[2][];
        Window[0][0][1] = new float[2][];
        Window[0][1][0] = new float[2][];
        Window[0][1][1] = new float[2][];

        Window[1] = new float[2][][][];
        Window[1][0] = new float[2][][];
        Window[1][1] = new float[2][][];
        Window[1][0][0] = new float[2][];
        Window[1][0][1] = new float[2][];
        Window[1][1][0] = new float[2][];
        Window[1][1][1] = new float[2][];
    }

    internal DspState(Info info)
        : this()
    {
        Init(info, false);
        PcmReturned = CenterW;
        CenterW -= info.BlockSizes[W] / 4 + info.BlockSizes[LW] / 4;
        GranulePos = -1;
        Sequence = -1;
    }

    private static int ILog2(int value)
    {
        var result = 0;
        while (value > 1)
        {
            result++;
            value = (int)((uint)value >> 1);
        }

        return result;
    }

    internal static float[] WindowCurve(int type, int window, int left, int right)
    {
        var result = new float[window];
        switch (type)
        {
            case 0:
                var leftBegin = window / 4 - left / 2;
                var rightBegin = window - window / 4 - right / 2;

                for (var i = 0; i < left; i++)
                {
                    var x = (float)((i + 0.5) / left * Pi / 2.0);
                    x = (float)Math.Sin(x);
                    x *= x;
                    x *= Pi / 2.0f;
                    x = (float)Math.Sin(x);
                    result[i + leftBegin] = x;
                }

                for (var i = leftBegin + left; i < rightBegin; i++)
                {
                    result[i] = 1.0f;
                }

                for (var i = 0; i < right; i++)
                {
                    var x = (float)((right - i - 0.5) / right * Pi / 2.0);
                    x = (float)Math.Sin(x);
                    x *= x;
                    x *= Pi / 2.0f;
                    x = (float)Math.Sin(x);
                    result[i + rightBegin] = x;
                }

                break;
            default:
                return null;
        }

        return result;
    }

    internal int Init(Info info, bool encode)
    {
        Vi = info;
        ModeBits = ILog2(info.Modes);

        Transform[0] = new object[ViTransformB];
        Transform[1] = new object[ViTransformB];

        Transform[0][0] = new Mdct();
        Transform[1][0] = new Mdct();
        ((Mdct)Transform[0][0]).Init(info.BlockSizes[0]);
        ((Mdct)Transform[1][0]).Init(info.BlockSizes[1]);

        Window[0][0][0] = new float[ViWindowB][];
        Window[0][0][1] = Window[0][0][0];
        Window[0][1][0] = Window[0][0][0];
        Window[0][1][1] = Window[0][0][0];
        Window[1][0][0] = new float[ViWindowB][];
        Window[1][0][1] = new float[ViWindowB][];
        Window[1][1][0] = new float[ViWindowB][];
        Window[1][1][1] = new float[ViWindowB][];

        for (var i = 0; i < ViWindowB; i++)
        {
            Window[0][0][0][i] = WindowCurve(i, info.BlockSizes[0], info.BlockSizes[0] / 2, info.BlockSizes[0] / 2);
            Window[1][0][0][i] = WindowCurve(i, info.BlockSizes[1], info.BlockSizes[0] / 2, info.BlockSizes[0] / 2);
            Window[1][0][1][i] = WindowCurve(i, info.BlockSizes[1], info.BlockSizes[0] / 2, info.BlockSizes[1] / 2);
            Window[1][1][0][i] = WindowCurve(i, info.BlockSizes[1], info.BlockSizes[1] / 2, info.BlockSizes[0] / 2);
            Window[1][1][1][i] = WindowCurve(i, info.BlockSizes[1], info.BlockSizes[1] / 2, info.BlockSizes[1] / 2);
        }

        FullBooks = new CodeBook[info.Books];
        for (var i = 0; i < info.Books; i++)
        {
            FullBooks[i] = new CodeBook();
            FullBooks[i].InitDecode(info.BookParam[i]);
        }

        PcmStorage = 8192;
        Pcm = new float[info.Channels][];
        for (var i = 0; i < info.Channels; i++)
        {
            Pcm[i] = new float[PcmStorage];
        }

        LW = 0;
        W = 0;
        CenterW = info.BlockSizes[1] / 2;
        PcmCurrent = CenterW;

        Mode = new object[info.Modes];
        for (var i = 0; i < info.Modes; i++)
        {
            var mapNumber = info.ModeParam[i].Mapping;
            var mapType = info.MapType[mapNumber];
            Mode[i] = FuncMapping.MappingP[mapType].Look(this, info.ModeParam[i], info.MapParam[mapNumber]);
        }

        return 0;
    }

    public int SynthesisInit(Info info)
    {
        Init(info, false);
        PcmReturned = CenterW;
        CenterW -= info.BlockSizes[W] / 4 + info.BlockSizes[LW] / 4;
        GranulePos = -1;
        Sequence = -1;
        return 0;
    }

    public int SynthesisBlockIn(Block block)
    {
        if (CenterW > Vi.BlockSizes[1] / 2 && PcmReturned > 8192)
        {
            var shiftPcm = CenterW - Vi.BlockSizes[1] / 2;
            shiftPcm = PcmReturned < shiftPcm ? PcmReturned : shiftPcm;

            PcmCurrent -= shiftPcm;
            CenterW -= shiftPcm;
            PcmReturned -= shiftPcm;
            if (shiftPcm != 0)
            {
                for (var i = 0; i < Vi.Channels; i++)
                {
                    Array.Copy(Pcm[i], shiftPcm, Pcm[i], 0, PcmCurrent);
                }
            }
        }

        LW = W;
        W = block.W;
        NW = -1;

        GlueBits += block.GlueBits;
        TimeBits += block.TimeBits;
        FloorBits += block.FloorBits;
        ResBits += block.ResBits;

        if (Sequence + 1 != block.Sequence)
        {
            GranulePos = -1;
        }

        Sequence = block.Sequence;

        var sizeW = Vi.BlockSizes[W];
        var centerW = CenterW + Vi.BlockSizes[LW] / 4 + sizeW / 4;
        var beginW = centerW - sizeW / 2;
        var endW = beginW + sizeW;
        var beginSl = 0;
        var endSl = 0;

        if (endW > PcmStorage)
        {
            PcmStorage = endW + Vi.BlockSizes[1];
            for (var i = 0; i < Vi.Channels; i++)
            {
                var expanded = new float[PcmStorage];
                Array.Copy(Pcm[i], 0, expanded, 0, Pcm[i].Length);
                Pcm[i] = expanded;
            }
        }

        switch (W)
        {
            case 0:
                beginSl = 0;
                endSl = Vi.BlockSizes[0] / 2;
                break;
            case 1:
                beginSl = Vi.BlockSizes[1] / 4 - Vi.BlockSizes[LW] / 4;
                endSl = beginSl + Vi.BlockSizes[LW] / 2;
                break;
        }

        for (var j = 0; j < Vi.Channels; j++)
        {
            var pcmOffset = beginW;
            var i = 0;
            for (i = beginSl; i < endSl; i++)
            {
                Pcm[j][pcmOffset + i] += block.Pcm[j][i];
            }

            for (; i < sizeW; i++)
            {
                Pcm[j][pcmOffset + i] = block.Pcm[j][i];
            }
        }

        if (GranulePos == -1)
        {
            GranulePos = block.GranulePos;
        }
        else
        {
            GranulePos += centerW - CenterW;
            if (block.GranulePos != -1 && GranulePos != block.GranulePos)
            {
                if (GranulePos > block.GranulePos && block.EofFlag != 0)
                {
                    centerW -= (int)(GranulePos - block.GranulePos);
                }

                GranulePos = block.GranulePos;
            }
        }

        CenterW = centerW;
        PcmCurrent = endW;
        if (block.EofFlag != 0)
        {
            EofFlag = 1;
        }

        return 0;
    }

    public int SynthesisPcmOut(float[][][] pcm, int[] index)
    {
        if (PcmReturned < CenterW)
        {
            if (pcm != null)
            {
                for (var i = 0; i < Vi.Channels; i++)
                {
                    index[i] = PcmReturned;
                }

                pcm[0] = Pcm;
            }

            return CenterW - PcmReturned;
        }

        return 0;
    }

    public int SynthesisRead(int samples)
    {
        if (samples != 0 && PcmReturned + samples > CenterW)
        {
            return -1;
        }

        PcmReturned += samples;
        return 0;
    }

    public void Clear()
    {
        if (Vi != null && Mode != null)
        {
            for (var i = 0; i < Vi.Modes && i < Mode.Length; i++)
            {
                var mapNumber = Vi.ModeParam[i].Mapping;
                var mapType = Vi.MapType[mapNumber];
                FuncMapping.MappingP[mapType].FreeLook(Mode[i]);
            }
        }

        Pcm = null;
        Multipliers = null;
        Window = null;
        Transform = null;
        FullBooks = null;
        Mode = null;
        Header = null;
        Header1 = null;
        Header2 = null;
        Vi = null;
        InitializeLookupStorage();
    }
}
