using TheoraSharp.Ogg;
using TheoraSharp.Theora;

namespace TheoraSharp;

public class TheoraDec : IVideoDecoder
{
    private Info _info = new();
    private Comment _comment = new();
    private State _state = new();
    private int _packedIdx = 0;
    private YUVBuffer _yuvBuffer = new();

    private List<uint[]> _framesRgb24 = new();
    public IReadOnlyList<uint[]> FramesRgb24 => _framesRgb24;

    public int Width => _info.width;
    public int Height => _info.height;
    public float Fps => 1.0f * _info.fps_numerator / _info.fps_denominator;

    public T[] GetData<T>()
    {
        if (typeof(T) != typeof(uint))
            return null;

        return FramesRgb24[^1] as T[];
    }

    public bool ReadPacket(PacketContext op)
    {
        uint[] pixels = null;
        if (_packedIdx < 3)
        {
            if (_info.decodeHeader(_comment, op) < 0)
            {
                throw new Exception("does not contain Theora video data.");
            }

            if (_packedIdx == 2)
            {
                _state.decodeInit(_info);
            }
        }
        else
        {
            var tst = _state.decodePacketin(op); 
            if (tst != 0)
            {
                throw new Exception("Error Decoding Theora.");
                return false;
            }

            if (_state.decodeYUVout(_yuvBuffer) != 0)
            {
                throw new Exception("Error getting the picture.");
                return false;
            }
            else
            {
                pixels = _yuvBuffer.GetPixelsRgb24();
                _framesRgb24.Add(pixels);
            }
        }

        _packedIdx++;
        return pixels != null;
    }
}