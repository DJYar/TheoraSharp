using Buffer = TheoraSharp.Ogg.Buffer;

namespace TheoraSharp.Theora;

interface ExtractMVectorComponent
{
    int Extract(Buffer opb);
}