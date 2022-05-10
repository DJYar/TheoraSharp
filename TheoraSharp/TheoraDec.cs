using TheoraSharp.Ogg;
using TheoraSharp.Theora;

namespace TheoraSharp;

public class TheoraDec : IDecoder
{
    private Info ti = new();
    private Comment tc = new();
    private State ts = new();
    private int packet = 0;
    private YUVBuffer yuv = new();

    private List<uint[]> framesRgb24 = new();
    public IReadOnlyList<uint[]> FramesRgb24 => framesRgb24;

    public int Width => ti.width;
    public int Height => ti.height;
    public float Fps => 1.0f * ti.fps_numerator / ti.fps_denominator;

    public T[] GetData<T>()
    {
        if (typeof(T) != typeof(uint))
            return null;

        return FramesRgb24[^1] as T[];
    }

    public bool ReadPacket(Packet op)
    {
        uint[] pixels = null;
        if (packet < 3)
        {
            if (ti.decodeHeader(tc, op) < 0)
            {
                throw new Exception("does not contain Theora video data.");
            }

            if (packet == 2)
            {
                ts.decodeInit(ti);
            }
        }
        else
        {
            var tst = ts.decodePacketin(op); 
            if (tst != 0)
            {
                throw new Exception("Error Decoding Theora.");
                return false;
            }

            if (ts.decodeYUVout(yuv) != 0)
            {
                throw new Exception("Error getting the picture.");
                return false;
            }
            else
            {
                pixels = yuv.GetPixelsRgb24();
                framesRgb24.Add(pixels);
            }
        }

        packet++;
        return pixels != null;
    }
}