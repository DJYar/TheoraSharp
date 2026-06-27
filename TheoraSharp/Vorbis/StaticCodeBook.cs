using OggBuffer = TheoraSharp.Ogg.Buffer;

namespace TheoraSharp.Vorbis;

internal class StaticCodeBook
{
    private const int VqFExp = 10;
    private const int VqFMan = 21;
    private const int VqFExpBias = 768;

    public int Dim { get; set; }
    public int Entries { get; set; }
    public int[] LengthList { get; set; }
    public int MapType { get; set; }
    public int QMin { get; set; }
    public int QDelta { get; set; }
    public int QQuant { get; set; }
    public int QSequenceP { get; set; }
    public int[] QuantList { get; set; }
    public EncodeAuxNearestMatch NearestTree { get; set; }
    public EncodeAuxThreshMatch ThreshTree { get; set; }

    public StaticCodeBook()
    {
    }

    public StaticCodeBook(
        int dim,
        int entries,
        int[] lengthList,
        int mapType,
        int qMin,
        int qDelta,
        int qQuant,
        int qSequenceP,
        int[] quantList,
        object nearestTree,
        object threshTree)
    {
        Dim = dim;
        Entries = entries;
        LengthList = lengthList;
        MapType = mapType;
        QMin = qMin;
        QDelta = qDelta;
        QQuant = qQuant;
        QSequenceP = qSequenceP;
        QuantList = quantList;
        NearestTree = nearestTree as EncodeAuxNearestMatch;
        ThreshTree = threshTree as EncodeAuxThreshMatch;
    }

    public int Pack(OggBuffer buffer)
    {
        var ordered = false;

        buffer.Write(0x564342, 24);
        buffer.Write((uint)Dim, 16);
        buffer.Write((uint)Entries, 24);

        int i;
        for (i = 1; i < Entries; i++)
        {
            if (LengthList[i] < LengthList[i - 1])
            {
                break;
            }
        }

        if (i == Entries)
        {
            ordered = true;
        }

        if (ordered)
        {
            var count = 0;
            buffer.Write(1, 1);
            buffer.Write((uint)(LengthList[0] - 1), 5);

            for (i = 1; i < Entries; i++)
            {
                var current = LengthList[i];
                var last = LengthList[i - 1];
                if (current <= last)
                {
                    continue;
                }

                for (var j = last; j < current; j++)
                {
                    buffer.Write((uint)(i - count), ILog(Entries - count));
                    count = i;
                }
            }

            buffer.Write((uint)(i - count), ILog(Entries - count));
        }
        else
        {
            buffer.Write(0, 1);

            for (i = 0; i < Entries; i++)
            {
                if (LengthList[i] == 0)
                {
                    break;
                }
            }

            if (i == Entries)
            {
                buffer.Write(0, 1);
                for (i = 0; i < Entries; i++)
                {
                    buffer.Write((uint)(LengthList[i] - 1), 5);
                }
            }
            else
            {
                buffer.Write(1, 1);
                for (i = 0; i < Entries; i++)
                {
                    if (LengthList[i] == 0)
                    {
                        buffer.Write(0, 1);
                    }
                    else
                    {
                        buffer.Write(1, 1);
                        buffer.Write((uint)(LengthList[i] - 1), 5);
                    }
                }
            }
        }

        buffer.Write((uint)MapType, 4);
        switch (MapType)
        {
            case 0:
                break;
            case 1:
            case 2:
            {
                if (QuantList == null)
                {
                    return -1;
                }

                buffer.Write(unchecked((uint)QMin), 32);
                buffer.Write(unchecked((uint)QDelta), 32);
                buffer.Write((uint)(QQuant - 1), 4);
                buffer.Write((uint)QSequenceP, 1);

                var quantValues = MapType == 1 ? MapType1QuantValues() : Entries * Dim;
                for (i = 0; i < quantValues; i++)
                {
                    buffer.Write((uint)Math.Abs(QuantList[i]), QQuant);
                }

                break;
            }
            default:
                return -1;
        }

        return 0;
    }

    public int Unpack(OggBuffer buffer)
    {
        if (buffer.Read(24) != 0x564342)
        {
            Clear();
            return -1;
        }

        Dim = buffer.Read(16);
        Entries = buffer.Read(24);
        if (Entries == -1)
        {
            Clear();
            return -1;
        }

        switch (buffer.Read(1))
        {
            case 0:
                LengthList = new int[Entries];
                if (buffer.Read(1) != 0)
                {
                    for (var i = 0; i < Entries; i++)
                    {
                        if (buffer.Read(1) != 0)
                        {
                            var number = buffer.Read(5);
                            if (number == -1)
                            {
                                Clear();
                                return -1;
                            }

                            LengthList[i] = number + 1;
                        }
                        else
                        {
                            LengthList[i] = 0;
                        }
                    }
                }
                else
                {
                    for (var i = 0; i < Entries; i++)
                    {
                        var number = buffer.Read(5);
                        if (number == -1)
                        {
                            Clear();
                            return -1;
                        }

                        LengthList[i] = number + 1;
                    }
                }

                break;
            case 1:
            {
                var length = buffer.Read(5) + 1;
                LengthList = new int[Entries];

                for (var i = 0; i < Entries;)
                {
                    var number = buffer.Read(ILog(Entries - i));
                    if (number == -1)
                    {
                        Clear();
                        return -1;
                    }

                    for (var j = 0; j < number; j++, i++)
                    {
                        LengthList[i] = length;
                    }

                    length++;
                }

                break;
            }
            default:
                return -1;
        }

        switch (MapType = buffer.Read(4))
        {
            case 0:
                break;
            case 1:
            case 2:
            {
                QMin = buffer.Read(32);
                QDelta = buffer.Read(32);
                QQuant = buffer.Read(4) + 1;
                QSequenceP = buffer.Read(1);

                var quantValues = MapType == 1 ? MapType1QuantValues() : Entries * Dim;
                QuantList = new int[quantValues];
                for (var i = 0; i < quantValues; i++)
                {
                    QuantList[i] = buffer.Read(QQuant);
                }

                if (QuantList[quantValues - 1] == -1)
                {
                    Clear();
                    return -1;
                }

                break;
            }
            default:
                Clear();
                return -1;
        }

        return 0;
    }

    public void Clear()
    {
    }

    public float[] Unquantize()
    {
        if (MapType != 1 && MapType != 2)
        {
            return null;
        }

        var minDelta = Float32Unpack(QMin);
        var delta = Float32Unpack(QDelta);
        var result = new float[Entries * Dim];

        switch (MapType)
        {
            case 1:
            {
                var quantValues = MapType1QuantValues();
                for (var j = 0; j < Entries; j++)
                {
                    var last = 0f;
                    var indexDivisor = 1;
                    for (var k = 0; k < Dim; k++)
                    {
                        var index = (j / indexDivisor) % quantValues;
                        var value = Math.Abs(QuantList[index]) * delta + minDelta + last;
                        if (QSequenceP != 0)
                        {
                            last = value;
                        }

                        result[j * Dim + k] = value;
                        indexDivisor *= quantValues;
                    }
                }

                break;
            }
            case 2:
                for (var j = 0; j < Entries; j++)
                {
                    var last = 0f;
                    for (var k = 0; k < Dim; k++)
                    {
                        var value = Math.Abs(QuantList[j * Dim + k]) * delta + minDelta + last;
                        if (QSequenceP != 0)
                        {
                            last = value;
                        }

                        result[j * Dim + k] = value;
                    }
                }

                break;
        }

        return result;
    }

    public int MapType1QuantValues()
    {
        var values = (int)Math.Floor(Math.Pow(Entries, 1.0 / Dim));

        while (true)
        {
            var accumulator = 1;
            var nextAccumulator = 1;
            for (var i = 0; i < Dim; i++)
            {
                accumulator *= values;
                nextAccumulator *= values + 1;
            }

            if (accumulator <= Entries && nextAccumulator > Entries)
            {
                return values;
            }

            if (accumulator > Entries)
            {
                values--;
            }
            else
            {
                values++;
            }
        }
    }

    public static long Float32Pack(float value)
    {
        var sign = 0;
        if (value < 0)
        {
            sign = unchecked((int)0x80000000);
            value = -value;
        }

        var exponent = (int)Math.Floor(Math.Log(value) / Math.Log(2));
        var mantissa = (int)Math.Round(Math.Pow(value, VqFMan - 1 - exponent));
        exponent = (exponent + VqFExpBias) << VqFMan;
        return sign | exponent | mantissa;
    }

    public static float Float32Unpack(int value)
    {
        var mantissa = value & 0x1fffff;
        var exponent = (value & 0x7fe00000) >> VqFMan;
        if ((value & unchecked((int)0x80000000)) != 0)
        {
            mantissa = -mantissa;
        }

        return LdExp(mantissa, exponent - (VqFMan - 1) - VqFExpBias);
    }

    private static float LdExp(float value, int exponent)
    {
        return (float)(value * Math.Pow(2, exponent));
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
}
