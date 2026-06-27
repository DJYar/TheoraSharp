using OggBuffer = TheoraSharp.Ogg.Buffer;

namespace TheoraSharp.Vorbis;

internal abstract class FuncFloor
{
    public static readonly FuncFloor[] FloorP = { new Floor0(), new Floor1() };

    public abstract void Pack(object info, OggBuffer buffer);
    public abstract object Unpack(Info info, OggBuffer buffer);
    public abstract object Look(DspState dspState, InfoMode mode, object info);
    public abstract void FreeInfo(object info);
    public abstract void FreeLook(object look);
    public abstract void FreeState(object state);
    public abstract int Forward(Block block, object info, float[] input, float[] output, object state);
    public abstract object Inverse1(Block block, object look, object memo);
    public abstract int Inverse2(Block block, object look, object memo, float[] output);
}
