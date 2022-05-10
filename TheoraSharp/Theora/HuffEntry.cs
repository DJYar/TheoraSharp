namespace TheoraSharp.Theora;

using Buffer = TheoraSharp.Ogg.Buffer;

internal class HuffEntry
{
    internal HuffEntry[] Child = new HuffEntry[2];
    internal HuffEntry Previous;
    internal HuffEntry Next;
    internal int       Value;
    internal int       Frequency;

    internal HuffEntry Copy() 
    {
        HuffEntry huffDst;
        huffDst = new HuffEntry();
        huffDst.Value = Value;
        if (Value < 0) {
            huffDst.Child[0] = Child[0].Copy();
            huffDst.Child[1] = Child[1].Copy();
        }
        return huffDst;
    }

    internal int Read(int depth, Buffer opb) 
    {
        int bit;
        int ret;

        bit = opb.ReadB(1);
        if(bit < 0) {
            return (int)Result.BadHeader;
        }
        else if(bit == 0) {
            if (++depth > 32) 
                return (int)Result.BadHeader;

            Child[0] = new HuffEntry();
            ret = Child[0].Read(depth, opb);
            if (ret < 0) 
                return ret;

            Child[1] = new HuffEntry();
            ret = Child[1].Read(depth, opb);
            if (ret < 0) 
                return ret;

            Value = -1;
        } 
        else {
            Child[0] = null;
            Child[1] = null;
            Value = opb.ReadB(5);
            if (Value < 0) 
                return (int)Result.BadHeader;
        }
        return 0;
    }
}