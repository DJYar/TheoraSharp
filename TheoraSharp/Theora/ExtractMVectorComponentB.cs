using Buffer = TheoraSharp.Ogg.Buffer;

namespace TheoraSharp.Theora;

internal class ExtractMVectorComponentB : ExtractMVectorComponent
{
    public int Extract(Buffer opb)
    {
        /* Get group to which coded component belongs */
        return (opb.ReadB(5)) * -((opb.ReadB(1) << 1) - 1);
    }
}