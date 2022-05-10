using Buffer = TheoraSharp.Ogg.Buffer;

namespace TheoraSharp.Theora;

internal class ExtractMVectorComponentA : ExtractMVectorComponent
{
    public int Extract(Buffer opb)
    {
        /* Get group to which coded component belongs */
        /*  Now extract the appropriate number of bits to identify the component */
        switch (opb.ReadB(3))
        {
            case 0:
                return 0;
            case 1:
                return 1;
            case 2:
                return -1;
            case 3:
                return 2 - (4 * opb.ReadB(1));
            case 4:
                return 3 - (6 * opb.ReadB(1));
            case 5:
                return (4 + opb.ReadB(2)) * -((opb.ReadB(1) << 1) - 1);
            case 6:
                return (8 + opb.ReadB(3)) * -((opb.ReadB(1) << 1) - 1);
            case 7:
                return (16 + opb.ReadB(4)) * -((opb.ReadB(1) << 1) - 1);
        }

        return 0;
    }
}