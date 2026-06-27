using OggBuffer = TheoraSharp.Ogg.Buffer;

namespace TheoraSharp.Vorbis;

internal abstract class FuncMapping
{
    public static readonly FuncMapping[] MappingP = { new Mapping0() };

    public abstract void Pack(Info info, object mapping, OggBuffer buffer);
    public abstract object Unpack(Info info, OggBuffer buffer);
    public abstract object Look(DspState dspState, InfoMode mode, object mapping);
    public abstract void FreeInfo(object mapping);
    public abstract void FreeLook(object mapping);
    public abstract int Inverse(Block block, object look);
}
