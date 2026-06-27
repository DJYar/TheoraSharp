using OggBuffer = TheoraSharp.Ogg.Buffer;

namespace TheoraSharp.Vorbis;

internal class Residue0 : FuncResidue
{
    private static int[][][] _partWord = new int[2][][];

    public override void Pack(object residue, OggBuffer buffer)
    {
        var info = (InfoResidue0)residue;
        var acc = 0;

        buffer.Write(unchecked((uint)info.Begin), 24);
        buffer.Write(unchecked((uint)info.End), 24);
        buffer.Write(unchecked((uint)(info.Grouping - 1)), 24);
        buffer.Write(unchecked((uint)(info.Partitions - 1)), 6);
        buffer.Write(unchecked((uint)info.GroupBook), 8);

        for (var j = 0; j < info.Partitions; j++)
        {
            if (ILog(info.SecondStages[j]) > 3)
            {
                buffer.Write(unchecked((uint)info.SecondStages[j]), 3);
                buffer.Write(1, 1);
                buffer.Write(unchecked((uint)((uint)info.SecondStages[j] >> 3)), 5);
            }
            else
            {
                buffer.Write(unchecked((uint)info.SecondStages[j]), 4);
            }

            acc += ICount(info.SecondStages[j]);
        }

        for (var j = 0; j < acc; j++)
        {
            buffer.Write(unchecked((uint)info.BookList[j]), 8);
        }
    }

    public override object Unpack(Info info, OggBuffer buffer)
    {
        var acc = 0;
        var residueInfo = new InfoResidue0
        {
            Begin = buffer.Read(24),
            End = buffer.Read(24),
            Grouping = buffer.Read(24) + 1,
            Partitions = buffer.Read(6) + 1,
            GroupBook = buffer.Read(8)
        };

        for (var j = 0; j < residueInfo.Partitions; j++)
        {
            var cascade = buffer.Read(3);
            if (buffer.Read(1) != 0)
            {
                cascade |= buffer.Read(5) << 3;
            }

            residueInfo.SecondStages[j] = cascade;
            acc += ICount(cascade);
        }

        for (var j = 0; j < acc; j++)
        {
            residueInfo.BookList[j] = buffer.Read(8);
        }

        if (residueInfo.GroupBook >= info.Books)
        {
            FreeInfo(residueInfo);
            return null;
        }

        for (var j = 0; j < acc; j++)
        {
            if (residueInfo.BookList[j] >= info.Books)
            {
                FreeInfo(residueInfo);
                return null;
            }
        }

        return residueInfo;
    }

    public override object Look(DspState dspState, InfoMode mode, object residue)
    {
        var info = (InfoResidue0)residue;
        var look = new LookResidue0
        {
            Info = info,
            Map = mode.Mapping,
            Parts = info.Partitions,
            FullBooks = dspState.FullBooks,
            PhraseBook = dspState.FullBooks[info.GroupBook]
        };

        var acc = 0;
        var dim = look.PhraseBook.Dim;
        var maxStage = 0;

        look.PartBooks = new int[look.Parts][];
        for (var j = 0; j < look.Parts; j++)
        {
            var stages = ILog(info.SecondStages[j]);
            if (stages == 0)
            {
                continue;
            }

            if (stages > maxStage)
            {
                maxStage = stages;
            }

            look.PartBooks[j] = new int[stages];
            for (var k = 0; k < stages; k++)
            {
                if ((info.SecondStages[j] & (1 << k)) != 0)
                {
                    look.PartBooks[j][k] = info.BookList[acc++];
                }
            }
        }

        look.PartVals = (int)Math.Round(Math.Pow(look.Parts, dim), MidpointRounding.AwayFromZero);
        look.Stages = maxStage;
        look.DecodeMap = new int[look.PartVals][];
        for (var j = 0; j < look.PartVals; j++)
        {
            var value = j;
            var mult = look.PartVals / look.Parts;
            look.DecodeMap[j] = new int[dim];

            for (var k = 0; k < dim; k++)
            {
                var decoded = value / mult;
                value -= decoded * mult;
                mult /= look.Parts;
                look.DecodeMap[j][k] = decoded;
            }
        }

        return look;
    }

    public override void FreeInfo(object info)
    {
    }

    public override void FreeLook(object look)
    {
    }

    public override int Forward(Block block, object look, float[][] input, int channels)
    {
        throw new NotImplementedException("Vorbis Residue0.Forward is not implemented in the original JOrbis port.");
    }

    protected static int Inverse01(Block block, object lookObject, float[][] input, int channels, int decodePart)
    {
        lock (typeof(Residue0))
        {
            var look = (LookResidue0)lookObject;
            var info = look.Info;

            var samplesPerPartition = info.Grouping;
            var partitionsPerWord = look.PhraseBook.Dim;
            var n = info.End - info.Begin;

            var partValues = n / samplesPerPartition;
            var partWords = (partValues + partitionsPerWord - 1) / partitionsPerWord;

            if (_partWord.Length < channels)
            {
                _partWord = new int[channels][][];
                for (var j = 0; j < channels; j++)
                {
                    _partWord[j] = new int[partWords][];
                }
            }
            else
            {
                for (var j = 0; j < channels; j++)
                {
                    if (_partWord[j] == null || _partWord[j].Length < partWords)
                    {
                        _partWord[j] = new int[partWords][];
                    }
                }
            }

            for (var stage = 0; stage < look.Stages; stage++)
            {
                for (int i = 0, l = 0; i < partValues; l++)
                {
                    if (stage == 0)
                    {
                        for (var j = 0; j < channels; j++)
                        {
                            var temp = look.PhraseBook.Decode(block.Opb);
                            if (temp == -1)
                            {
                                return 0;
                            }

                            _partWord[j][l] = look.DecodeMap[temp];
                            if (_partWord[j][l] == null)
                            {
                                return 0;
                            }
                        }
                    }

                    for (var k = 0; k < partitionsPerWord && i < partValues; k++, i++)
                    {
                        for (var j = 0; j < channels; j++)
                        {
                            var offset = info.Begin + i * samplesPerPartition;
                            var partition = _partWord[j][l][k];
                            if ((info.SecondStages[partition] & (1 << stage)) == 0)
                            {
                                continue;
                            }

                            var stageBook = look.FullBooks[look.PartBooks[partition][stage]];
                            if (stageBook == null)
                            {
                                continue;
                            }

                            if (decodePart == 0)
                            {
                                if (stageBook.DecodeVsAdd(input[j], offset, block.Opb, samplesPerPartition) == -1)
                                {
                                    return 0;
                                }
                            }
                            else if (decodePart == 1)
                            {
                                if (stageBook.DecodeVAdd(input[j], offset, block.Opb, samplesPerPartition) == -1)
                                {
                                    return 0;
                                }
                            }
                        }
                    }
                }
            }

            return 0;
        }
    }

    protected static int Inverse2(Block block, object lookObject, float[][] input, int channels)
    {
        var look = (LookResidue0)lookObject;
        var info = look.Info;

        var samplesPerPartition = info.Grouping;
        var partitionsPerWord = look.PhraseBook.Dim;
        var n = info.End - info.Begin;

        var partValues = n / samplesPerPartition;
        var partWords = (partValues + partitionsPerWord - 1) / partitionsPerWord;

        var partWord = new int[partWords][];
        for (var stage = 0; stage < look.Stages; stage++)
        {
            for (int i = 0, l = 0; i < partValues; l++)
            {
                if (stage == 0)
                {
                    var temp = look.PhraseBook.Decode(block.Opb);
                    if (temp == -1)
                    {
                        return 0;
                    }

                    partWord[l] = look.DecodeMap[temp];
                    if (partWord[l] == null)
                    {
                        return 0;
                    }
                }

                for (var k = 0; k < partitionsPerWord && i < partValues; k++, i++)
                {
                    var offset = info.Begin + i * samplesPerPartition;
                    var partition = partWord[l][k];
                    if ((info.SecondStages[partition] & (1 << stage)) == 0)
                    {
                        continue;
                    }

                    var stageBook = look.FullBooks[look.PartBooks[partition][stage]];
                    if (stageBook == null)
                    {
                        continue;
                    }

                    if (stageBook.DecodeVvAdd(input, offset, channels, block.Opb, samplesPerPartition) == -1)
                    {
                        return 0;
                    }
                }
            }
        }

        return 0;
    }

    public override int Inverse(Block block, object look, float[][] input, int[] nonzero, int channels)
    {
        var used = 0;
        for (var i = 0; i < channels; i++)
        {
            if (nonzero[i] != 0)
            {
                input[used++] = input[i];
            }
        }

        return used != 0 ? Inverse01(block, look, input, used, 0) : 0;
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

    private static int ICount(int value)
    {
        var result = 0;
        while (value != 0)
        {
            result += value & 1;
            value = (int)((uint)value >> 1);
        }

        return result;
    }
}

internal class LookResidue0
{
    public InfoResidue0 Info { get; set; }
    public int Map { get; set; }
    public int Parts { get; set; }
    public int Stages { get; set; }
    public CodeBook[] FullBooks { get; set; }
    public CodeBook PhraseBook { get; set; }
    public int[][] PartBooks { get; set; }
    public int PartVals { get; set; }
    public int[][] DecodeMap { get; set; }
    public int PostBits { get; set; }
    public int PhraseBits { get; set; }
    public int Frames { get; set; }
}

internal class InfoResidue0
{
    public int Begin { get; set; }
    public int End { get; set; }
    public int Grouping { get; set; }
    public int Partitions { get; set; }
    public int GroupBook { get; set; }
    public int[] SecondStages { get; } = new int[64];
    public int[] BookList { get; } = new int[256];

    public float[] EntMax { get; } = new float[64];
    public float[] AmpMax { get; } = new float[64];
    public int[] SubGroup { get; } = new int[64];
    public int[] BLimit { get; } = new int[64];
}
