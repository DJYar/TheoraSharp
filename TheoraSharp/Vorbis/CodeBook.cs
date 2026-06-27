using OggBuffer = TheoraSharp.Ogg.Buffer;

namespace TheoraSharp.Vorbis;

internal class CodeBook
{
    private int[] _decodeVsTemp = new int[15];

    public int Dim { get; set; }
    public int Entries { get; set; }
    public StaticCodeBook StaticBook { get; set; } = new();
    public float[] ValueList { get; set; }
    public int[] CodeList { get; set; }
    public DecodeAux DecodeTree { get; set; }

    public int Encode(int entry, OggBuffer buffer)
    {
        buffer.Write(unchecked((uint)CodeList[entry]), StaticBook.LengthList[entry]);
        return StaticBook.LengthList[entry];
    }

    public int ErrorV(float[] values)
    {
        var best = Best(values, 1);
        for (var k = 0; k < Dim; k++)
        {
            values[k] = ValueList[best * Dim + k];
        }

        return best;
    }

    public int EncodeV(int best, float[] values, OggBuffer buffer)
    {
        for (var k = 0; k < Dim; k++)
        {
            values[k] = ValueList[best * Dim + k];
        }

        return Encode(best, buffer);
    }

    public int EncodeVs(float[] values, OggBuffer buffer, int step, int addMul)
    {
        var best = BestError(values, step, addMul);
        return Encode(best, buffer);
    }

    public int DecodeVsAdd(float[] values, int offset, OggBuffer buffer, int n)
    {
        lock (this)
        {
            var step = n / Dim;
            if (_decodeVsTemp.Length < step)
            {
                _decodeVsTemp = new int[step];
            }

            int i;
            for (i = 0; i < step; i++)
            {
                var entry = Decode(buffer);
                if (entry == -1)
                {
                    return -1;
                }

                _decodeVsTemp[i] = entry * Dim;
            }

            var output = 0;
            for (i = 0; i < Dim; i++, output += step)
            {
                for (var j = 0; j < step; j++)
                {
                    values[offset + output + j] += ValueList[_decodeVsTemp[j] + i];
                }
            }

            return 0;
        }
    }

    public int DecodeVAdd(float[] values, int offset, OggBuffer buffer, int n)
    {
        int i;

        if (Dim > 8)
        {
            for (i = 0; i < n;)
            {
                var entry = Decode(buffer);
                if (entry == -1)
                {
                    return -1;
                }

                var valueOffset = entry * Dim;
                for (var j = 0; j < Dim;)
                {
                    values[offset + i++] += ValueList[valueOffset + j++];
                }
            }
        }
        else
        {
            for (i = 0; i < n;)
            {
                var entry = Decode(buffer);
                if (entry == -1)
                {
                    return -1;
                }

                var valueOffset = entry * Dim;
                var j = 0;
                switch (Dim)
                {
                    case 8:
                        values[offset + i++] += ValueList[valueOffset + j++];
                        goto case 7;
                    case 7:
                        values[offset + i++] += ValueList[valueOffset + j++];
                        goto case 6;
                    case 6:
                        values[offset + i++] += ValueList[valueOffset + j++];
                        goto case 5;
                    case 5:
                        values[offset + i++] += ValueList[valueOffset + j++];
                        goto case 4;
                    case 4:
                        values[offset + i++] += ValueList[valueOffset + j++];
                        goto case 3;
                    case 3:
                        values[offset + i++] += ValueList[valueOffset + j++];
                        goto case 2;
                    case 2:
                        values[offset + i++] += ValueList[valueOffset + j++];
                        goto case 1;
                    case 1:
                        values[offset + i++] += ValueList[valueOffset + j++];
                        break;
                }
            }
        }

        return 0;
    }

    public int DecodeVSet(float[] values, int offset, OggBuffer buffer, int n)
    {
        for (var i = 0; i < n;)
        {
            var entry = Decode(buffer);
            if (entry == -1)
            {
                return -1;
            }

            var valueOffset = entry * Dim;
            for (var j = 0; j < Dim;)
            {
                values[offset + i++] = ValueList[valueOffset + j++];
            }
        }

        return 0;
    }

    public int DecodeVvAdd(float[][] values, int offset, int channels, OggBuffer buffer, int n)
    {
        var channelPointer = 0;

        for (var i = offset / channels; i < (offset + n) / channels;)
        {
            var entry = Decode(buffer);
            if (entry == -1)
            {
                return -1;
            }

            var valueOffset = entry * Dim;
            for (var j = 0; j < Dim; j++)
            {
                values[channelPointer++][i] += ValueList[valueOffset + j];
                if (channelPointer == channels)
                {
                    channelPointer = 0;
                    i++;
                }
            }
        }

        return 0;
    }

    public int Decode(OggBuffer buffer)
    {
        var pointer = 0;
        var tree = DecodeTree;
        var look = buffer.Look(tree.TabN);

        if (look >= 0)
        {
            pointer = tree.Tab[look];
            buffer.Adv(tree.TabL[look]);
            if (pointer <= 0)
            {
                return -pointer;
            }
        }

        do
        {
            switch (buffer.Read1())
            {
                case 0:
                    pointer = tree.Ptr0[pointer];
                    break;
                case 1:
                    pointer = tree.Ptr1[pointer];
                    break;
                case -1:
                default:
                    return -1;
            }
        }
        while (pointer > 0);

        return -pointer;
    }

    public int DecodeVs(float[] values, int index, OggBuffer buffer, int step, int addMul)
    {
        var entry = Decode(buffer);
        if (entry == -1)
        {
            return -1;
        }

        switch (addMul)
        {
            case -1:
                for (int i = 0, output = 0; i < Dim; i++, output += step)
                {
                    values[index + output] = ValueList[entry * Dim + i];
                }

                break;
            case 0:
                for (int i = 0, output = 0; i < Dim; i++, output += step)
                {
                    values[index + output] += ValueList[entry * Dim + i];
                }

                break;
            case 1:
                for (int i = 0, output = 0; i < Dim; i++, output += step)
                {
                    values[index + output] *= ValueList[entry * Dim + i];
                }

                break;
        }

        return entry;
    }

    public int Best(float[] values, int step)
    {
        var nearestTree = StaticBook.NearestTree;
        var threshTree = StaticBook.ThreshTree;
        var pointer = 0;

        if (threshTree != null)
        {
            var index = 0;
            for (int k = 0, output = step * (Dim - 1); k < Dim; k++, output -= step)
            {
                int i;
                for (i = 0; i < threshTree.ThreshValues - 1; i++)
                {
                    if (values[output] < threshTree.QuantThresh[i])
                    {
                        break;
                    }
                }

                index = index * threshTree.QuantValues + threshTree.QuantMap[i];
            }

            if (StaticBook.LengthList[index] > 0)
            {
                return index;
            }
        }

        if (nearestTree != null)
        {
            while (true)
            {
                var distance = 0f;
                var p = nearestTree.P[pointer];
                var q = nearestTree.Q[pointer];
                for (int k = 0, output = 0; k < Dim; k++, output += step)
                {
                    distance += (ValueList[p + k] - ValueList[q + k]) *
                                (values[output] - (ValueList[p + k] + ValueList[q + k]) * 0.5f);
                }

                pointer = distance > 0 ? -nearestTree.Ptr0[pointer] : -nearestTree.Ptr1[pointer];
                if (pointer <= 0)
                {
                    break;
                }
            }

            return -pointer;
        }

        var bestIndex = -1;
        var best = 0f;
        var entryOffset = 0;
        for (var i = 0; i < Entries; i++)
        {
            if (StaticBook.LengthList[i] > 0)
            {
                var current = Distance(Dim, ValueList, entryOffset, values, step);
                if (bestIndex == -1 || current < best)
                {
                    best = current;
                    bestIndex = i;
                }
            }

            entryOffset += Dim;
        }

        return bestIndex;
    }

    public int BestError(float[] values, int step, int addMul)
    {
        var best = Best(values, step);
        switch (addMul)
        {
            case 0:
                for (int i = 0, output = 0; i < Dim; i++, output += step)
                {
                    values[output] -= ValueList[best * Dim + i];
                }

                break;
            case 1:
                for (int i = 0, output = 0; i < Dim; i++, output += step)
                {
                    var value = ValueList[best * Dim + i];
                    values[output] = value == 0 ? 0 : values[output] / value;
                }

                break;
        }

        return best;
    }

    public void Clear()
    {
    }

    public int InitDecode(StaticCodeBook staticBook)
    {
        StaticBook = staticBook;
        Entries = staticBook.Entries;
        Dim = staticBook.Dim;
        ValueList = staticBook.Unquantize();

        DecodeTree = MakeDecodeTree();
        if (DecodeTree == null)
        {
            Clear();
            return -1;
        }

        return 0;
    }

    public DecodeAux MakeDecodeTree()
    {
        var top = 0;
        var tree = new DecodeAux();
        var ptr0 = tree.Ptr0 = new int[Entries * 2];
        var ptr1 = tree.Ptr1 = new int[Entries * 2];
        var codeList = MakeWords(StaticBook.LengthList, StaticBook.Entries);

        if (codeList == null)
        {
            return null;
        }

        tree.Aux = Entries * 2;

        for (var i = 0; i < Entries; i++)
        {
            if (StaticBook.LengthList[i] <= 0)
            {
                continue;
            }

            var pointer = 0;
            int j;
            for (j = 0; j < StaticBook.LengthList[i] - 1; j++)
            {
                var bit = ((uint)codeList[i] >> j) & 1;
                if (bit == 0)
                {
                    if (ptr0[pointer] == 0)
                    {
                        ptr0[pointer] = ++top;
                    }

                    pointer = ptr0[pointer];
                }
                else
                {
                    if (ptr1[pointer] == 0)
                    {
                        ptr1[pointer] = ++top;
                    }

                    pointer = ptr1[pointer];
                }
            }

            if ((((uint)codeList[i] >> j) & 1) == 0)
            {
                ptr0[pointer] = -i;
            }
            else
            {
                ptr1[pointer] = -i;
            }
        }

        tree.TabN = ILog(Entries) - 4;
        if (tree.TabN < 5)
        {
            tree.TabN = 5;
        }

        var n = 1 << tree.TabN;
        tree.Tab = new int[n];
        tree.TabL = new int[n];
        for (var i = 0; i < n; i++)
        {
            var pointer = 0;
            int j;
            for (j = 0; j < tree.TabN && (pointer > 0 || j == 0); j++)
            {
                pointer = (i & (1 << j)) != 0 ? ptr1[pointer] : ptr0[pointer];
            }

            tree.Tab[i] = pointer;
            tree.TabL[i] = j;
        }

        return tree;
    }

    public static int[] MakeWords(int[] lengths, int n)
    {
        var marker = new int[33];
        var result = new int[n];

        for (var i = 0; i < n; i++)
        {
            var length = lengths[i];
            if (length <= 0)
            {
                continue;
            }

            var entry = marker[length];
            if (length < 32 && ((uint)entry >> length) != 0)
            {
                return null;
            }

            result[i] = entry;

            for (var j = length; j > 0; j--)
            {
                if ((marker[j] & 1) != 0)
                {
                    if (j == 1)
                    {
                        marker[1]++;
                    }
                    else
                    {
                        marker[j] = marker[j - 1] << 1;
                    }

                    break;
                }

                marker[j]++;
            }

            for (var j = length + 1; j < 33; j++)
            {
                if (((uint)marker[j] >> 1) == entry)
                {
                    entry = marker[j];
                    marker[j] = marker[j - 1] << 1;
                }
                else
                {
                    break;
                }
            }
        }

        for (var i = 0; i < n; i++)
        {
            var temp = 0;
            for (var j = 0; j < lengths[i]; j++)
            {
                temp <<= 1;
                temp |= (int)(((uint)result[i] >> j) & 1);
            }

            result[i] = temp;
        }

        return result;
    }

    private static float Distance(int elementCount, float[] reference, int index, float[] values, int step)
    {
        var accumulator = 0f;
        for (var i = 0; i < elementCount; i++)
        {
            var value = reference[index + i] - values[i * step];
            accumulator += value * value;
        }

        return accumulator;
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

internal class DecodeAux
{
    public int[] Tab { get; set; }
    public int[] TabL { get; set; }
    public int TabN { get; set; }
    public int[] Ptr0 { get; set; }
    public int[] Ptr1 { get; set; }
    public int Aux { get; set; }
}
