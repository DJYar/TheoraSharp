namespace TheoraSharp.Vorbis;

internal class AllocChain
{
    public object Ptr { get; set; }
    public AllocChain Next { get; set; }
}
