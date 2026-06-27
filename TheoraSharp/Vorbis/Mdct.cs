namespace TheoraSharp.Vorbis;

internal class Mdct
{
    private int _n;
    private int _log2N;

    private float[] _trig;
    private int[] _bitRev;

    private float _scale;

    private float[] _x = new float[1024];
    private float[] _w = new float[1024];

    public void Init(int n)
    {
        _bitRev = new int[n / 4];
        _trig = new float[n + n / 4];

        _log2N = (int)Math.Round(Math.Log(n) / Math.Log(2));
        _n = n;

        var ae = 0;
        var ao = 1;
        var be = ae + n / 2;
        var bo = be + 1;
        var ce = be + n / 2;
        var co = ce + 1;

        for (var i = 0; i < n / 4; i++)
        {
            _trig[ae + i * 2] = (float)Math.Cos(Math.PI / n * (4 * i));
            _trig[ao + i * 2] = (float)-Math.Sin(Math.PI / n * (4 * i));
            _trig[be + i * 2] = (float)Math.Cos(Math.PI / (2 * n) * (2 * i + 1));
            _trig[bo + i * 2] = (float)Math.Sin(Math.PI / (2 * n) * (2 * i + 1));
        }

        for (var i = 0; i < n / 8; i++)
        {
            _trig[ce + i * 2] = (float)Math.Cos(Math.PI / n * (4 * i + 2));
            _trig[co + i * 2] = (float)-Math.Sin(Math.PI / n * (4 * i + 2));
        }

        var mask = (1 << (_log2N - 1)) - 1;
        var msb = 1 << (_log2N - 2);
        for (var i = 0; i < n / 8; i++)
        {
            var acc = 0;
            for (var j = 0; msb >> j != 0; j++)
            {
                if (((msb >> j) & i) != 0)
                {
                    acc |= 1 << j;
                }
            }

            _bitRev[i * 2] = (~acc) & mask;
            _bitRev[i * 2 + 1] = acc;
        }

        _scale = 4.0f / n;
    }

    public void Clear()
    {
    }

    public void Forward(float[] input, float[] output)
    {
    }

    public void Backward(float[] input, float[] output)
    {
        lock (this)
        {
            if (_x.Length < _n / 2)
            {
                _x = new float[_n / 2];
            }

            if (_w.Length < _n / 2)
            {
                _w = new float[_n / 2];
            }

            var x = _x;
            var w = _w;
            var n2 = _n >> 1;
            var n4 = _n >> 2;
            var n8 = _n >> 3;

            {
                var inputOffset = 1;
                var xOffset = 0;
                var a = n2;

                int i;
                for (i = 0; i < n8; i++)
                {
                    a -= 2;
                    x[xOffset++] = -input[inputOffset + 2] * _trig[a + 1] - input[inputOffset] * _trig[a];
                    x[xOffset++] = input[inputOffset] * _trig[a + 1] - input[inputOffset + 2] * _trig[a];
                    inputOffset += 4;
                }

                inputOffset = n2 - 4;
                for (i = 0; i < n8; i++)
                {
                    a -= 2;
                    x[xOffset++] = input[inputOffset] * _trig[a + 1] + input[inputOffset + 2] * _trig[a];
                    x[xOffset++] = input[inputOffset] * _trig[a] - input[inputOffset + 2] * _trig[a + 1];
                    inputOffset -= 4;
                }
            }

            var kernelOutput = MdctKernel(x, w, _n, n2, n4, n8);
            var kernelOffset = 0;

            {
                var b = n2;
                var output1 = n4;
                var output2 = output1 - 1;
                var output3 = n4 + n2;
                var output4 = output3 - 1;

                for (var i = 0; i < n4; i++)
                {
                    var temp1 = kernelOutput[kernelOffset] * _trig[b + 1] - kernelOutput[kernelOffset + 1] * _trig[b];
                    var temp2 = -(kernelOutput[kernelOffset] * _trig[b] + kernelOutput[kernelOffset + 1] * _trig[b + 1]);

                    output[output1] = -temp1;
                    output[output2] = temp1;
                    output[output3] = temp2;
                    output[output4] = temp2;

                    output1++;
                    output2--;
                    output3++;
                    output4--;
                    kernelOffset += 2;
                    b += 2;
                }
            }
        }
    }

    private float[] MdctKernel(float[] x, float[] w, int n, int n2, int n4, int n8)
    {
        var xA = n4;
        var xB = 0;
        var w2 = n4;
        var a = n2;

        for (var i = 0; i < n4;)
        {
            var x0 = x[xA] - x[xB];
            w[w2 + i] = x[xA++] + x[xB++];

            var x1 = x[xA] - x[xB];
            a -= 4;

            w[i++] = x0 * _trig[a] + x1 * _trig[a + 1];
            w[i] = x1 * _trig[a] - x0 * _trig[a + 1];

            w[w2 + i] = x[xA++] + x[xB++];
            i++;
        }

        for (var i = 0; i < _log2N - 3; i++)
        {
            var k0 = n >> (i + 2);
            var k1 = 1 << (i + 3);
            var wBase = n2 - 2;

            a = 0;
            float[] temp;

            for (var r = 0; r < (k0 >> 2); r++)
            {
                var w1 = wBase;
                w2 = w1 - (k0 >> 1);
                var ae = _trig[a];
                var ao = _trig[a + 1];
                wBase -= 2;

                k0++;
                for (var s = 0; s < (2 << i); s++)
                {
                    var wb = w[w1] - w[w2];
                    x[w1] = w[w1] + w[w2];

                    var wa = w[++w1] - w[++w2];
                    x[w1] = w[w1] + w[w2];

                    x[w2] = wa * ae - wb * ao;
                    x[w2 - 1] = wb * ae + wa * ao;

                    w1 -= k0;
                    w2 -= k0;
                }

                k0--;
                a += k1;
            }

            temp = w;
            w = x;
            x = temp;
        }

        {
            var c = n;
            var bit = 0;
            var x1 = 0;
            var x2 = n2 - 1;

            for (var i = 0; i < n8; i++)
            {
                var t1 = _bitRev[bit++];
                var t2 = _bitRev[bit++];

                var wA = w[t1] - w[t2 + 1];
                var wB = w[t1 - 1] + w[t2];
                var wC = w[t1] + w[t2 + 1];
                var wD = w[t1 - 1] - w[t2];

                var wAce = wA * _trig[c];
                var wBce = wB * _trig[c++];
                var wAco = wA * _trig[c];
                var wBco = wB * _trig[c++];

                x[x1++] = (wC + wAco + wBce) * 0.5f;
                x[x2--] = (-wD + wBco - wAce) * 0.5f;
                x[x1++] = (wD + wBco - wAce) * 0.5f;
                x[x2--] = (wC - wAco - wBce) * 0.5f;
            }
        }

        return x;
    }
}
