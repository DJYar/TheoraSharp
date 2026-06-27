using OggBuffer = TheoraSharp.Ogg.Buffer;

namespace TheoraSharp.Vorbis;

internal abstract class FuncResidue
{
    public static readonly FuncResidue[] ResidueP = { new Residue0(), new Residue1(), new Residue2() };

    public abstract void Pack(object residue, OggBuffer buffer);
    public abstract object Unpack(Info info, OggBuffer buffer);
    public abstract object Look(DspState dspState, InfoMode mode, object residue);
    public abstract void FreeInfo(object info);
    public abstract void FreeLook(object look);
    public abstract int Forward(Block block, object look, float[][] input, int channels);
    public abstract int Inverse(Block block, object look, float[][] input, int[] nonzero, int channels);
}

internal sealed class Residue1 : Residue0
{
    public override int Forward(Block block, object look, float[][] input, int channels)
    {
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

        return used != 0 ? Inverse01(block, look, input, used, 1) : 0;
    }
}

internal sealed class Residue2 : Residue0
{
    public override int Forward(Block block, object look, float[][] input, int channels)
    {
        return 0;
    }

    public override int Inverse(Block block, object look, float[][] input, int[] nonzero, int channels)
    {
        var i = 0;
        for (; i < channels; i++)
        {
            if (nonzero[i] != 0)
            {
                break;
            }
        }

        return i == channels ? 0 : Inverse2(block, look, input, channels);
    }
}
