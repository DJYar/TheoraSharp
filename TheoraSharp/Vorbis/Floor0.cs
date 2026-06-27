using OggBuffer = TheoraSharp.Ogg.Buffer;

namespace TheoraSharp.Vorbis;

internal sealed class Floor0 : FuncFloor
{
    private float[] _lsp;

    public override void Pack(object infoObject, OggBuffer buffer)
    {
        var info = (InfoFloor0)infoObject;
        buffer.Write(unchecked((uint)info.Order), 8);
        buffer.Write(unchecked((uint)info.Rate), 16);
        buffer.Write(unchecked((uint)info.BarkMap), 16);
        buffer.Write(unchecked((uint)info.AmpBits), 6);
        buffer.Write(unchecked((uint)info.AmpDb), 8);
        buffer.Write(unchecked((uint)(info.NumBooks - 1)), 4);

        for (var j = 0; j < info.NumBooks; j++)
        {
            buffer.Write(unchecked((uint)info.Books[j]), 8);
        }
    }

    public override object Unpack(Info info, OggBuffer buffer)
    {
        var floorInfo = new InfoFloor0
        {
            Order = buffer.Read(8),
            Rate = buffer.Read(16),
            BarkMap = buffer.Read(16),
            AmpBits = buffer.Read(6),
            AmpDb = buffer.Read(8),
            NumBooks = buffer.Read(4) + 1
        };

        if (floorInfo.Order < 1 ||
            floorInfo.Rate < 1 ||
            floorInfo.BarkMap < 1 ||
            floorInfo.NumBooks < 1)
        {
            return null;
        }

        for (var j = 0; j < floorInfo.NumBooks; j++)
        {
            floorInfo.Books[j] = buffer.Read(8);
            if (floorInfo.Books[j] < 0 || floorInfo.Books[j] >= info.Books)
            {
                return null;
            }
        }

        return floorInfo;
    }

    public override object Look(DspState dspState, InfoMode mode, object infoObject)
    {
        var info = dspState.Vi;
        var floorInfo = (InfoFloor0)infoObject;
        var look = new LookFloor0
        {
            M = floorInfo.Order,
            N = info.BlockSizes[mode.BlockFlag] / 2,
            Ln = floorInfo.BarkMap,
            Vi = floorInfo
        };

        look.LpcLook.Init(look.Ln, look.M);

        var scale = look.Ln / ToBark(floorInfo.Rate / 2.0f);
        look.LinearMap = new int[look.N];
        for (var j = 0; j < look.N; j++)
        {
            var value = (int)Math.Floor(ToBark(floorInfo.Rate / 2.0f / look.N * j) * scale);
            if (value >= look.Ln)
            {
                value = look.Ln;
            }

            look.LinearMap[j] = value;
        }

        return look;
    }

    public override void FreeInfo(object info)
    {
    }

    public override void FreeLook(object look)
    {
    }

    public override void FreeState(object state)
    {
    }

    public override int Forward(Block block, object info, float[] input, float[] output, object state)
    {
        return 0;
    }

    public int Inverse(Block block, object lookObject, float[] output)
    {
        var look = (LookFloor0)lookObject;
        var info = look.Vi;
        var ampRaw = block.Opb.Read(info.AmpBits);
        if (ampRaw > 0)
        {
            var maxValue = (1 << info.AmpBits) - 1;
            var amp = (float)ampRaw / maxValue * info.AmpDb;
            var bookNumber = block.Opb.Read(ILog(info.NumBooks));

            if (bookNumber != -1 && bookNumber < info.NumBooks)
            {
                lock (this)
                {
                    if (_lsp == null || _lsp.Length < look.M)
                    {
                        _lsp = new float[look.M];
                    }
                    else
                    {
                        Array.Clear(_lsp, 0, look.M);
                    }

                    var book = block.Vd.FullBooks[info.Books[bookNumber]];
                    var last = 0.0f;

                    for (var j = 0; j < look.M; j++)
                    {
                        output[j] = 0.0f;
                    }

                    for (var j = 0; j < look.M; j += book.Dim)
                    {
                        if (book.DecodeVs(_lsp, j, block.Opb, 1, -1) == -1)
                        {
                            for (var k = 0; k < look.N; k++)
                            {
                                output[k] = 0.0f;
                            }

                            return 0;
                        }
                    }

                    for (var j = 0; j < look.M;)
                    {
                        for (var k = 0; k < book.Dim; k++, j++)
                        {
                            _lsp[j] += last;
                        }

                        last = _lsp[j - 1];
                    }

                    Lsp.LspToCurve(output, look.LinearMap, look.N, look.Ln, _lsp, look.M, amp, info.AmpDb);
                    return 1;
                }
            }
        }

        return 0;
    }

    public override object Inverse1(Block block, object lookObject, object memo)
    {
        var look = (LookFloor0)lookObject;
        var info = look.Vi;
        var lsp = memo as float[];

        var ampRaw = block.Opb.Read(info.AmpBits);
        if (ampRaw > 0)
        {
            var maxValue = (1 << info.AmpBits) - 1;
            var amp = (float)ampRaw / maxValue * info.AmpDb;
            var bookNumber = block.Opb.Read(ILog(info.NumBooks));

            if (bookNumber != -1 && bookNumber < info.NumBooks)
            {
                var book = block.Vd.FullBooks[info.Books[bookNumber]];
                var last = 0.0f;

                if (lsp == null || lsp.Length < look.M + 1)
                {
                    lsp = new float[look.M + 1];
                }
                else
                {
                    Array.Clear(lsp, 0, lsp.Length);
                }

                for (var j = 0; j < look.M; j += book.Dim)
                {
                    if (book.DecodeVSet(lsp, j, block.Opb, book.Dim) == -1)
                    {
                        return null;
                    }
                }

                for (var j = 0; j < look.M;)
                {
                    for (var k = 0; k < book.Dim; k++, j++)
                    {
                        lsp[j] += last;
                    }

                    last = lsp[j - 1];
                }

                lsp[look.M] = amp;
                return lsp;
            }
        }

        return null;
    }

    public override int Inverse2(Block block, object lookObject, object memo, float[] output)
    {
        var look = (LookFloor0)lookObject;
        var info = look.Vi;

        if (memo != null)
        {
            var lsp = (float[])memo;
            var amp = lsp[look.M];
            Lsp.LspToCurve(output, look.LinearMap, look.N, look.Ln, lsp, look.M, amp, info.AmpDb);
            return 1;
        }

        for (var j = 0; j < look.N; j++)
        {
            output[j] = 0.0f;
        }

        return 0;
    }

    private static float ToBark(float value)
    {
        return (float)(13.1 * Math.Atan(0.00074 * value) +
                       2.24 * Math.Atan(value * value * 1.85e-8) +
                       1e-4 * value);
    }

    private static int ILog(int value)
    {
        var result = 0;
        while (value != 0)
        {
            result++;
            value = (int)((uint)value >> 1);
        }

        return result;
    }

    internal static float FromDb(float value)
    {
        return (float)Math.Exp(value * 0.11512925);
    }

    internal static void LspToLpc(float[] lsp, float[] lpc, int m)
    {
        var m2 = m / 2;
        var odd = new float[m2];
        var even = new float[m2];
        var ae = new float[m2 + 1];
        var ao = new float[m2 + 1];
        var be = new float[m2];
        var bo = new float[m2];

        for (var i = 0; i < m2; i++)
        {
            odd[i] = (float)(-2.0 * Math.Cos(lsp[i * 2]));
            even[i] = (float)(-2.0 * Math.Cos(lsp[i * 2 + 1]));
        }

        int j;
        for (j = 0; j < m2; j++)
        {
            ae[j] = 0.0f;
            ao[j] = 1.0f;
            be[j] = 0.0f;
            bo[j] = 1.0f;
        }

        ao[j] = 1.0f;
        ae[j] = 1.0f;

        for (var i = 1; i < m + 1; i++)
        {
            var a = 0.0f;
            var b = 0.0f;
            for (j = 0; j < m2; j++)
            {
                var temp = odd[j] * ao[j] + ae[j];
                ae[j] = ao[j];
                ao[j] = a;
                a += temp;

                temp = even[j] * bo[j] + be[j];
                be[j] = bo[j];
                bo[j] = b;
                b += temp;
            }

            lpc[i - 1] = (a + ao[j] + b - ae[j]) / 2;
            ao[j] = a;
            ae[j] = b;
        }
    }

    internal static void LpcToCurve(float[] curve, float[] lpc, float amp, LookFloor0 look)
    {
        var lcurve = new float[Math.Max(look.Ln * 2, look.M * 2 + 2)];

        if (amp == 0)
        {
            for (var j = 0; j < look.N; j++)
            {
                curve[j] = 0.0f;
            }

            return;
        }

        look.LpcLook.LpcToCurve(lcurve, lpc, amp);
        for (var i = 0; i < look.N; i++)
        {
            curve[i] = lcurve[look.LinearMap[i]];
        }
    }
}

internal class InfoFloor0
{
    public int Order { get; set; }
    public int Rate { get; set; }
    public int BarkMap { get; set; }
    public int AmpBits { get; set; }
    public int AmpDb { get; set; }
    public int NumBooks { get; set; }
    public int[] Books { get; } = new int[16];
}

internal class LookFloor0
{
    public int N { get; set; }
    public int Ln { get; set; }
    public int M { get; set; }
    public int[] LinearMap { get; set; }
    public InfoFloor0 Vi { get; set; }
    public Lpc LpcLook { get; } = new();
}

internal class EchStateFloor0
{
    public int[] CodeWords { get; set; }
    public float[] Curve { get; set; }
    public long FrameNo { get; set; }
    public long Codes { get; set; }
}
