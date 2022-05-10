namespace TheoraSharp.Ogg;

internal class Buffer
{
    private const int BufferIncrement = 256;

    private static uint[] mask = {
        0x00000000,0x00000001,0x00000003,0x00000007,0x0000000f,
        0x0000001f,0x0000003f,0x0000007f,0x000000ff,0x000001ff,
        0x000003ff,0x000007ff,0x00000fff,0x00001fff,0x00003fff,
        0x00007fff,0x0000ffff,0x0001ffff,0x0003ffff,0x0007ffff,
        0x000fffff,0x001fffff,0x003fffff,0x007fffff,0x00ffffff,
        0x01ffffff,0x03ffffff,0x07ffffff,0x0fffffff,0x1fffffff,
        0x3fffffff,0x7fffffff,0xffffffff
    };

    private int ptr;
    private byte[] buffer;
    private int endbit;
    private int endbyte;
    private int storage;

    public void WriteInit()
    {
        buffer = new byte[BufferIncrement]; 
        ptr = 0;
        buffer[0] = (byte)'\0';
        storage = BufferIncrement;
    }

    public void Write(byte[] buf)
    {
        foreach (var b in buf)
        {
            if(b == 0)
                break;
            
            Write(b,8);
        }
    }

    public void Read(byte[] buf, int length)
    {
        var i = 0;
        while (length-- != 0){
            buf[i++] = (byte)Read(8);
        }
    }

    void Reset()
    {
        ptr = 0;
        buffer[0] = (byte)'\0';
        endbit = 0;
        endbyte = 0;
    }

    public void WriteClear()
    {
        buffer = null;
    }

    public void ReadInit(byte[] buf, int length)
    {
        ReadInit(buf, 0, length);
    }

    public void ReadInit(byte[] buf, int start, int length)
    {
        ptr = start;
        buffer = buf;
        endbit = 0;
        endbyte = 0;
        storage = length;
    }

    public void Write(uint value, int bits)
    {
        if (endbyte+4 >= storage)
        {
            var foo = new byte[storage+BufferIncrement];
            Array.Copy(buffer, 0, foo, 0, storage);
            buffer = foo;
            storage += BufferIncrement;
        }

        value &= mask[bits];
        bits += endbit;
        buffer[ptr] |= (byte)(value << endbit);

        if (bits >= 8)
        {
            buffer[ptr+1]=(byte)(value>>(8-endbit));
            if (bits >= 16)
            {
                buffer[ptr+2]=(byte)(value>>(16-endbit));  
                if (bits >= 24)
                {
                    buffer[ptr+3] = (byte)(value>>(24-endbit));  
                    if (bits >= 32)
                    {
                        if (endbit>0)
                            buffer[ptr+4] = (byte)(value>>(32-endbit));
                        else
                            buffer[ptr+4] = 0;
                    }
                }
            }
        }

        endbyte += bits/8;
        ptr += bits/8;
        endbit = bits&7;
    }

    public int Look(int bits)
    {
        var m = mask[bits];
        
        bits+=endbit;

        if(endbyte+4>=storage){
            if(endbyte+(bits-1)/8>=storage)return(-1);
        }
  
        var ret = (uint)(((buffer[ptr])&0xff)>>endbit);
        if (bits > 8)
        {
            ret |= (uint)(((buffer[ptr+1])&0xff)<<(8-endbit));
            if (bits > 16)
            {
                ret |= (uint)(((buffer[ptr+2])&0xff)<<(16-endbit));
                if (bits > 24)
                {
                    ret |= (uint)(((buffer[ptr+3])&0xff)<<(24-endbit));
                    if (bits > 32 && endbit != 0)
                    {
                        ret|=(uint)(((buffer[ptr+4])&0xff)<<(32-endbit));
                    }
                }
            }
        }
        return (int)(m&ret);
    }

    public int Look1()
    {
        if (endbyte >= storage)
            return(-1);
        
        return((buffer[ptr]>>endbit)&1);
    }

    public void Adv(int bits)
    {
        bits += endbit;
        ptr += bits/8;
        endbyte += bits/8;
        endbit = bits&7;
    }

    public void Adv1()
    {
        ++endbit;
        if (endbit > 7)
        {
            endbit=0;
            ptr++;
            endbyte++;
        }
    }

    public int Read(int bits)
    {
        uint ret;
        uint m=mask[bits];

        bits+=endbit;

        if(endbyte+4>=storage){
            ret=unchecked((uint)-1);
            if(endbyte+(bits-1)/8>=storage){
                ptr+=bits/8;
                endbyte+=bits/8;
                endbit=bits&7;
                return (int)(ret);
            }
        }

        ret = (uint)(((buffer[ptr])&0xff) >> endbit);
        if(bits>8){
            ret |= (uint)(((buffer[ptr+1])&0xff)<<(8-endbit));
            if(bits>16){
                ret|=(uint)(((buffer[ptr+2])&0xff)<<(16-endbit));
                if(bits>24){
                    ret|=(uint)(((buffer[ptr+3])&0xff)<<(24-endbit));
                    if(bits>32 && endbit!=0){
                        ret|=(uint)(((buffer[ptr+4])&0xff)<<(32-endbit));
                    }
                }
            }
        }
        ret &= m;

        ptr += bits/8;
        endbyte += bits/8;
        endbit = bits&7;
        return (int)(ret);
    }

    public int ReadB(int bits)
    {
        uint ret;
        int m=32-bits;

        bits+=endbit;

        if(endbyte+4>=storage){
            /* not the main path */
            ret=unchecked((uint)-1);
            if(endbyte*8+bits>storage*8) {
                ptr+=bits/8;
                endbyte+=bits/8;
                endbit=bits&7;
                return unchecked((int)ret);
            }
        }
      
        ret=(uint)((buffer[ptr]&0xff)<<(24+endbit));
        if(bits>8){
            ret |= (uint)((buffer[ptr+1]&0xff)<<(16+endbit));
            if(bits>16){
                ret |= (uint)((buffer[ptr+2]&0xff)<<(8+endbit));
                if(bits>24){
                    ret |= (uint)((buffer[ptr+3]&0xff)<<(endbit));
                    if(bits>32 && (endbit != 0))
                        ret |= (uint)((buffer[ptr+4]&0xff)>>(8-endbit));
                }
            }
        }
        ret = (ret >> (m>>1)) >> ((m+1)>>1);
      
        ptr += bits/8;
        endbyte += bits/8;
        endbit = bits&7;
        return (int)ret;
    }

    public int Read1()
    {
        int ret;
        if(endbyte>=storage){
            ret=-1;
            endbit++;
            if (endbit>7)
            {
                endbit=0;
                ptr++;
                endbyte++;
            }
            return(ret);
        }

        ret=(buffer[ptr]>>endbit)&1;

        endbit++;
        if(endbit>7){
            endbit=0;
            ptr++;
            endbyte++;
        }
        return(ret);
    }

    public int Bytes()
    {
        return(endbyte+(endbit+7)/8);
    }

    public int Bits()
    {
        return(endbyte*8+endbit);
    }

    public byte[] GetBuffer()
    {
        return (buffer);
    }

    public static int iLog(uint v)
    {
        int ret=0;
        while(v>0){
            ret++;
            v >>= 1;
        }
        return(ret);
    }
}