using OggBuffer = TheoraSharp.Ogg.Buffer;

namespace TheoraSharp.Vorbis;

internal abstract class FuncTime
{
    public static readonly FuncTime[] TimeP = { new Time0() };

    public abstract void Pack(object info, OggBuffer buffer);
    public abstract object Unpack(Info info, OggBuffer buffer);
    public abstract object Look(DspState dspState, InfoMode mode, object info);
    public abstract void FreeInfo(object info);
    public abstract void FreeLook(object look);
    public abstract int Forward(Block block, object info);
    public abstract int Inverse(Block block, object info, float[] input, float[] output);
}

internal sealed class Time0 : FuncTime
{
    public override void Pack(object info, OggBuffer buffer)
    {
    }

    public override object Unpack(Info info, OggBuffer buffer)
    {
        return string.Empty;
    }

    public override object Look(DspState dspState, InfoMode mode, object info)
    {
        return string.Empty;
    }

    public override void FreeInfo(object info)
    {
    }

    public override void FreeLook(object look)
    {
    }

    public override int Forward(Block block, object info)
    {
        return 0;
    }

    public override int Inverse(Block block, object info, float[] input, float[] output)
    {
        return 0;
    }
}
