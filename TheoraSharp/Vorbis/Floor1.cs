using OggBuffer = TheoraSharp.Ogg.Buffer;

namespace TheoraSharp.Vorbis;

internal class Floor1 : FuncFloor
{
    private const int Floor1RangeDb = 140;
    private const int VifPosit = 63;

    public override void Pack(object infoObject, OggBuffer buffer)
    {
        var info = (InfoFloor1)infoObject;
        var count = 0;
        var maxPosition = info.PostList[1];
        var maxClass = -1;

        buffer.Write((uint)info.Partitions, 5);
        for (var j = 0; j < info.Partitions; j++)
        {
            buffer.Write((uint)info.PartitionClass[j], 4);
            if (maxClass < info.PartitionClass[j])
            {
                maxClass = info.PartitionClass[j];
            }
        }

        for (var j = 0; j < maxClass + 1; j++)
        {
            buffer.Write((uint)(info.ClassDim[j] - 1), 3);
            buffer.Write((uint)info.ClassSubs[j], 2);
            if (info.ClassSubs[j] != 0)
            {
                buffer.Write((uint)info.ClassBook[j], 8);
            }

            for (var k = 0; k < (1 << info.ClassSubs[j]); k++)
            {
                buffer.Write((uint)(info.ClassSubBook[j][k] + 1), 8);
            }
        }

        buffer.Write((uint)(info.Mult - 1), 2);
        var rangeBits = ILog2(maxPosition);
        buffer.Write((uint)rangeBits, 4);

        for (int j = 0, k = 0; j < info.Partitions; j++)
        {
            count += info.ClassDim[info.PartitionClass[j]];
            for (; k < count; k++)
            {
                buffer.Write((uint)info.PostList[k + 2], rangeBits);
            }
        }
    }

    public override object Unpack(Info info, OggBuffer buffer)
    {
        var count = 0;
        var maxClass = -1;
        var floorInfo = new InfoFloor1();

        floorInfo.Partitions = buffer.Read(5);
        for (var j = 0; j < floorInfo.Partitions; j++)
        {
            floorInfo.PartitionClass[j] = buffer.Read(4);
            if (maxClass < floorInfo.PartitionClass[j])
            {
                maxClass = floorInfo.PartitionClass[j];
            }
        }

        for (var j = 0; j < maxClass + 1; j++)
        {
            floorInfo.ClassDim[j] = buffer.Read(3) + 1;
            floorInfo.ClassSubs[j] = buffer.Read(2);
            if (floorInfo.ClassSubs[j] < 0)
            {
                floorInfo.Free();
                return null;
            }

            if (floorInfo.ClassSubs[j] != 0)
            {
                floorInfo.ClassBook[j] = buffer.Read(8);
            }

            if (floorInfo.ClassBook[j] < 0 || floorInfo.ClassBook[j] >= info.Books)
            {
                floorInfo.Free();
                return null;
            }

            for (var k = 0; k < (1 << floorInfo.ClassSubs[j]); k++)
            {
                floorInfo.ClassSubBook[j][k] = buffer.Read(8) - 1;
                if (floorInfo.ClassSubBook[j][k] < -1 || floorInfo.ClassSubBook[j][k] >= info.Books)
                {
                    floorInfo.Free();
                    return null;
                }
            }
        }

        floorInfo.Mult = buffer.Read(2) + 1;
        var rangeBits = buffer.Read(4);

        for (int j = 0, k = 0; j < floorInfo.Partitions; j++)
        {
            count += floorInfo.ClassDim[floorInfo.PartitionClass[j]];
            for (; k < count; k++)
            {
                var value = floorInfo.PostList[k + 2] = buffer.Read(rangeBits);
                if (value < 0 || value >= (1 << rangeBits))
                {
                    floorInfo.Free();
                    return null;
                }
            }
        }

        floorInfo.PostList[0] = 0;
        floorInfo.PostList[1] = 1 << rangeBits;
        return floorInfo;
    }

    public override object Look(DspState dspState, InfoMode mode, object infoObject)
    {
        var postCount = 0;
        var sortPointer = new int[VifPosit + 2];
        var info = (InfoFloor1)infoObject;
        var look = new LookFloor1
        {
            Info = info,
            N = info.PostList[1]
        };

        for (var j = 0; j < info.Partitions; j++)
        {
            postCount += info.ClassDim[info.PartitionClass[j]];
        }

        postCount += 2;
        look.Posts = postCount;

        for (var j = 0; j < postCount; j++)
        {
            sortPointer[j] = j;
        }

        for (var j = 0; j < postCount - 1; j++)
        {
            for (var k = j; k < postCount; k++)
            {
                if (info.PostList[sortPointer[j]] <= info.PostList[sortPointer[k]])
                {
                    continue;
                }

                var temp = sortPointer[k];
                sortPointer[k] = sortPointer[j];
                sortPointer[j] = temp;
            }
        }

        for (var j = 0; j < postCount; j++)
        {
            look.ForwardIndex[j] = sortPointer[j];
        }

        for (var j = 0; j < postCount; j++)
        {
            look.ReverseIndex[look.ForwardIndex[j]] = j;
        }

        for (var j = 0; j < postCount; j++)
        {
            look.SortedIndex[j] = info.PostList[look.ForwardIndex[j]];
        }

        look.QuantQ = info.Mult switch
        {
            1 => 256,
            2 => 128,
            3 => 86,
            4 => 64,
            _ => -1
        };

        for (var j = 0; j < postCount - 2; j++)
        {
            var lo = 0;
            var hi = 1;
            var lx = 0;
            var hx = look.N;
            var currentX = info.PostList[j + 2];

            for (var k = 0; k < j + 2; k++)
            {
                var x = info.PostList[k];
                if (x > lx && x < currentX)
                {
                    lo = k;
                    lx = x;
                }

                if (x < hx && x > currentX)
                {
                    hi = k;
                    hx = x;
                }
            }

            look.LoNeighbor[j] = lo;
            look.HiNeighbor[j] = hi;
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

    public override object Inverse1(Block block, object lookObject, object memo)
    {
        var look = (LookFloor1)lookObject;
        var info = look.Info;
        var books = block.Dsp.FullBooks;

        if (block.Opb.Read(1) != 1)
        {
            return null;
        }

        var fitValue = memo as int[];
        if (fitValue == null || fitValue.Length < look.Posts)
        {
            fitValue = new int[look.Posts];
        }
        else
        {
            Array.Clear(fitValue, 0, fitValue.Length);
        }

        fitValue[0] = block.Opb.Read(ILog(look.QuantQ - 1));
        fitValue[1] = block.Opb.Read(ILog(look.QuantQ - 1));

        for (int i = 0, j = 2; i < info.Partitions; i++)
        {
            var classNumber = info.PartitionClass[i];
            var classDimensions = info.ClassDim[classNumber];
            var classSubBits = info.ClassSubs[classNumber];
            var classSub = 1 << classSubBits;
            var classValue = 0;

            if (classSubBits != 0)
            {
                classValue = books[info.ClassBook[classNumber]].Decode(block.Opb);
                if (classValue == -1)
                {
                    return null;
                }
            }

            for (var k = 0; k < classDimensions; k++)
            {
                var book = info.ClassSubBook[classNumber][classValue & (classSub - 1)];
                classValue >>= classSubBits;

                if (book >= 0)
                {
                    fitValue[j + k] = books[book].Decode(block.Opb);
                    if (fitValue[j + k] == -1)
                    {
                        return null;
                    }
                }
                else
                {
                    fitValue[j + k] = 0;
                }
            }

            j += classDimensions;
        }

        for (var i = 2; i < look.Posts; i++)
        {
            var predicted = RenderPoint(
                info.PostList[look.LoNeighbor[i - 2]],
                info.PostList[look.HiNeighbor[i - 2]],
                fitValue[look.LoNeighbor[i - 2]],
                fitValue[look.HiNeighbor[i - 2]],
                info.PostList[i]);

            var highRoom = look.QuantQ - predicted;
            var lowRoom = predicted;
            var room = (highRoom < lowRoom ? highRoom : lowRoom) << 1;
            var value = fitValue[i];

            if (value != 0)
            {
                if (value >= room)
                {
                    value = highRoom > lowRoom
                        ? value - lowRoom
                        : -1 - (value - highRoom);
                }
                else
                {
                    if ((value & 1) != 0)
                    {
                        value = -((value + 1) >> 1);
                    }
                    else
                    {
                        value >>= 1;
                    }
                }

                fitValue[i] = value + predicted;
                fitValue[look.LoNeighbor[i - 2]] &= 0x7fff;
                fitValue[look.HiNeighbor[i - 2]] &= 0x7fff;
            }
            else
            {
                fitValue[i] = predicted | 0x8000;
            }
        }

        return fitValue;
    }

    public override int Inverse2(Block block, object lookObject, object memo, float[] output)
    {
        var look = (LookFloor1)lookObject;
        var info = look.Info;
        var n = block.Dsp.Vi.BlockSizes[block.Mode] / 2;

        if (memo != null)
        {
            var fitValue = (int[])memo;
            var highX = 0;
            var lowX = 0;
            var lowY = fitValue[0] * info.Mult;

            for (var j = 1; j < look.Posts; j++)
            {
                var current = look.ForwardIndex[j];
                var highY = fitValue[current] & 0x7fff;
                if (highY != fitValue[current])
                {
                    continue;
                }

                highY *= info.Mult;
                highX = info.PostList[current];

                RenderLine(lowX, highX, lowY, highY, output);

                lowX = highX;
                lowY = highY;
            }

            for (var j = highX; j < n; j++)
            {
                output[j] *= output[j - 1];
            }

            return 1;
        }

        for (var j = 0; j < n; j++)
        {
            output[j] = 0f;
        }

        return 0;
    }

    private static int RenderPoint(int x0, int x1, int y0, int y1, int x)
    {
        y0 &= 0x7fff;
        y1 &= 0x7fff;

        var dy = y1 - y0;
        var adx = x1 - x0;
        var ady = Math.Abs(dy);
        var error = ady * (x - x0);
        var offset = error / adx;

        return dy < 0 ? y0 - offset : y0 + offset;
    }

    private static void RenderLine(int x0, int x1, int y0, int y1, float[] data)
    {
        var dy = y1 - y0;
        var adx = x1 - x0;
        var ady = Math.Abs(dy);
        var basis = dy / adx;
        var sy = dy < 0 ? basis - 1 : basis + 1;
        var x = x0;
        var y = y0;
        var error = 0;

        ady -= Math.Abs(basis * adx);

        data[x] *= FloorFromDbLookup[y];
        while (++x < x1)
        {
            error += ady;
            if (error >= adx)
            {
                error -= adx;
                y += sy;
            }
            else
            {
                y += basis;
            }

            data[x] *= FloorFromDbLookup[y];
        }
    }

    private static int ILog(int value)
    {
        var result = 0;
        while (value != 0)
        {
            result++;
            value >>= 1;
        }

        return result;
    }

    private static int ILog2(int value)
    {
        var result = 0;
        while (value > 1)
        {
            result++;
            value >>= 1;
        }

        return result;
    }

    private static readonly float[] FloorFromDbLookup =
    {
        1.0649863e-07f, 1.1341951e-07f, 1.2079015e-07f, 1.2863978e-07f,
        1.3699951e-07f, 1.4590251e-07f, 1.5538408e-07f, 1.6548181e-07f,
        1.7623575e-07f, 1.8768855e-07f, 1.9988561e-07f, 2.128753e-07f,
        2.2670913e-07f, 2.4144197e-07f, 2.5713223e-07f, 2.7384213e-07f,
        2.9163793e-07f, 3.1059021e-07f, 3.3077411e-07f, 3.5226968e-07f,
        3.7516214e-07f, 3.9954229e-07f, 4.2550680e-07f, 4.5315863e-07f,
        4.8260743e-07f, 5.1396998e-07f, 5.4737065e-07f, 5.8294187e-07f,
        6.2082472e-07f, 6.6116941e-07f, 7.0413592e-07f, 7.4989464e-07f,
        7.9862701e-07f, 8.5052630e-07f, 9.0579828e-07f, 9.6466216e-07f,
        1.0273513e-06f, 1.0941144e-06f, 1.1652161e-06f, 1.2409384e-06f,
        1.3215816e-06f, 1.4074654e-06f, 1.4989305e-06f, 1.5963394e-06f,
        1.7000785e-06f, 1.8105592e-06f, 1.9282195e-06f, 2.0535261e-06f,
        2.1869758e-06f, 2.3290978e-06f, 2.4804557e-06f, 2.6416497e-06f,
        2.8133190e-06f, 2.9961443e-06f, 3.1908506e-06f, 3.3982101e-06f,
        3.6190449e-06f, 3.8542308e-06f, 4.1047004e-06f, 4.3714470e-06f,
        4.6555282e-06f, 4.9580707e-06f, 5.2802740e-06f, 5.6234160e-06f,
        5.9888572e-06f, 6.3780469e-06f, 6.7925283e-06f, 7.2339451e-06f,
        7.7040476e-06f, 8.2047000e-06f, 8.7378876e-06f, 9.3057248e-06f,
        9.9104632e-06f, 1.0554501e-05f, 1.1240392e-05f, 1.1970856e-05f,
        1.2748789e-05f, 1.3577278e-05f, 1.4459606e-05f, 1.5399272e-05f,
        1.6400004e-05f, 1.7465768e-05f, 1.8600792e-05f, 1.9809576e-05f,
        2.1096914e-05f, 2.2467911e-05f, 2.3928002e-05f, 2.5482978e-05f,
        2.7139006e-05f, 2.8902651e-05f, 3.0780908e-05f, 3.2781225e-05f,
        3.4911534e-05f, 3.7180282e-05f, 3.9596466e-05f, 4.2169667e-05f,
        4.4910090e-05f, 4.7828601e-05f, 5.0936773e-05f, 5.4246931e-05f,
        5.7772202e-05f, 6.1526565e-05f, 6.5524908e-05f, 6.9783085e-05f,
        7.4317983e-05f, 7.9147585e-05f, 8.4291040e-05f, 8.9768747e-05f,
        9.5602426e-05f, 0.00010181521f, 0.00010843174f, 0.00011547824f,
        0.00012298267f, 0.00013097477f, 0.00013948625f, 0.00014855085f,
        0.00015820453f, 0.00016848555f, 0.00017943469f, 0.00019109536f,
        0.00020351382f, 0.00021673929f, 0.00023082423f, 0.00024582449f,
        0.00026179955f, 0.00027881276f, 0.00029693158f, 0.00031622787f,
        0.00033677814f, 0.00035866388f, 0.00038197188f, 0.00040679456f,
        0.00043323036f, 0.00046138411f, 0.00049136745f, 0.00052329927f,
        0.00055730621f, 0.00059352311f, 0.00063209358f, 0.00067317058f,
        0.00071691700f, 0.00076350630f, 0.00081312324f, 0.00086596457f,
        0.00092223983f, 0.00098217216f, 0.0010459992f, 0.0011139742f,
        0.0011863665f, 0.0012634633f, 0.0013455702f, 0.0014330129f,
        0.0015261382f, 0.0016253153f, 0.0017309374f, 0.0018434235f,
        0.0019632195f, 0.0020908006f, 0.0022266726f, 0.0023713743f,
        0.0025254795f, 0.0026895994f, 0.0028643847f, 0.0030505286f,
        0.0032487691f, 0.0034598925f, 0.0036847358f, 0.0039241906f,
        0.0041792066f, 0.0044507950f, 0.0047400328f, 0.0050480668f,
        0.0053761186f, 0.0057254891f, 0.0060975636f, 0.0064938176f,
        0.0069158225f, 0.0073652516f, 0.0078438871f, 0.0083536271f,
        0.0088964928f, 0.009474637f, 0.010090352f, 0.010746080f,
        0.011444421f, 0.012188144f, 0.012980198f, 0.013823725f,
        0.014722068f, 0.015678791f, 0.016697687f, 0.017782797f,
        0.018938423f, 0.020169149f, 0.021479854f, 0.022875735f,
        0.024362330f, 0.025945531f, 0.027631618f, 0.029427276f,
        0.031339626f, 0.033376252f, 0.035545228f, 0.037855157f,
        0.040315199f, 0.042935108f, 0.045725273f, 0.048696758f,
        0.051861348f, 0.055231591f, 0.058820850f, 0.062643361f,
        0.066714279f, 0.071049749f, 0.075666962f, 0.080584227f,
        0.085821044f, 0.091398179f, 0.097337747f, 0.10366330f,
        0.11039993f, 0.11757434f, 0.12521498f, 0.13335215f,
        0.14201813f, 0.15124727f, 0.16107617f, 0.17154380f,
        0.18269168f, 0.19456402f, 0.20720788f, 0.22067342f,
        0.23501402f, 0.25028656f, 0.26655159f, 0.28387361f,
        0.30232132f, 0.32196786f, 0.34289114f, 0.36517414f,
        0.38890521f, 0.41417847f, 0.44109412f, 0.46975890f,
        0.50028648f, 0.53279791f, 0.56742212f, 0.60429640f,
        0.64356699f, 0.68538959f, 0.72993007f, 0.77736504f,
        0.82788260f, 0.88168307f, 0.9389798f, 1f
    };
}

internal class InfoFloor1
{
    private const int VifPosit = 63;
    private const int VifClass = 16;
    private const int VifParts = 31;

    public int Partitions { get; set; }
    public int[] PartitionClass { get; private set; } = new int[VifParts];
    public int[] ClassDim { get; private set; } = new int[VifClass];
    public int[] ClassSubs { get; private set; } = new int[VifClass];
    public int[] ClassBook { get; private set; } = new int[VifClass];
    public int[][] ClassSubBook { get; private set; }
    public int Mult { get; set; }
    public int[] PostList { get; private set; } = new int[VifPosit + 2];
    public float MaxOver { get; set; }
    public float MaxUnder { get; set; }
    public float MaxError { get; set; }
    public int TwoFitMinSize { get; set; }
    public int TwoFitMinUsed { get; set; }
    public int TwoFitWeight { get; set; }
    public float TwoFitAtten { get; set; }
    public int UnusedMinSize { get; set; }
    public int UnusedMinN { get; set; }
    public int N { get; set; }

    public InfoFloor1()
    {
        ClassSubBook = new int[VifClass][];
        for (var i = 0; i < ClassSubBook.Length; i++)
        {
            ClassSubBook[i] = new int[8];
        }
    }

    public void Free()
    {
        PartitionClass = null;
        ClassDim = null;
        ClassSubs = null;
        ClassBook = null;
        ClassSubBook = null;
        PostList = null;
    }

    public object CopyInfo()
    {
        var copy = new InfoFloor1
        {
            Partitions = Partitions,
            Mult = Mult,
            MaxOver = MaxOver,
            MaxUnder = MaxUnder,
            MaxError = MaxError,
            TwoFitMinSize = TwoFitMinSize,
            TwoFitMinUsed = TwoFitMinUsed,
            TwoFitWeight = TwoFitWeight,
            TwoFitAtten = TwoFitAtten,
            UnusedMinSize = UnusedMinSize,
            UnusedMinN = UnusedMinN,
            N = N
        };

        Array.Copy(PartitionClass, 0, copy.PartitionClass, 0, VifParts);
        Array.Copy(ClassDim, 0, copy.ClassDim, 0, VifClass);
        Array.Copy(ClassSubs, 0, copy.ClassSubs, 0, VifClass);
        Array.Copy(ClassBook, 0, copy.ClassBook, 0, VifClass);

        for (var j = 0; j < VifClass; j++)
        {
            Array.Copy(ClassSubBook[j], 0, copy.ClassSubBook[j], 0, 8);
        }

        Array.Copy(PostList, 0, copy.PostList, 0, VifPosit + 2);
        return copy;
    }
}

internal class LookFloor1
{
    private const int VifPosit = 63;

    public int[] SortedIndex { get; private set; } = new int[VifPosit + 2];
    public int[] ForwardIndex { get; private set; } = new int[VifPosit + 2];
    public int[] ReverseIndex { get; private set; } = new int[VifPosit + 2];
    public int[] HiNeighbor { get; private set; } = new int[VifPosit];
    public int[] LoNeighbor { get; private set; } = new int[VifPosit];
    public int Posts { get; set; }
    public int N { get; set; }
    public int QuantQ { get; set; }
    public InfoFloor1 Info { get; set; }
    public int PhraseBits { get; set; }
    public int PostBits { get; set; }
    public int Frames { get; set; }

    public void Free()
    {
        SortedIndex = null;
        ForwardIndex = null;
        ReverseIndex = null;
        HiNeighbor = null;
        LoNeighbor = null;
    }
}

internal class LsfitAcc
{
    public long X0 { get; set; }
    public long X1 { get; set; }
    public long Xa { get; set; }
    public long Ya { get; set; }
    public long X2A { get; set; }
    public long Y2A { get; set; }
    public long XyA { get; set; }
    public long N { get; set; }
    public long An { get; set; }
    public long Un { get; set; }
    public long EdgeY0 { get; set; }
    public long EdgeY1 { get; set; }
}

internal class EchStateFloor1
{
    public int[] CodeWords { get; set; }
    public float[] Curve { get; set; }
    public long FrameNumber { get; set; }
    public long Codes { get; set; }
}
