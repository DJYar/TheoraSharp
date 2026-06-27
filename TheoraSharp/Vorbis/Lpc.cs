namespace TheoraSharp.Vorbis;

internal class Lpc
{
    private readonly Drft _fft = new();

    private int _ln;
    private int _m;

    public static float LpcFromData(float[] data, float[] lpc, int n, int m)
    {
        var autocorrelation = new float[m + 1];

        var j = m + 1;
        while (j-- != 0)
        {
            var value = 0.0f;
            for (var i = j; i < n; i++)
            {
                value += data[i] * data[i - j];
            }

            autocorrelation[j] = value;
        }

        var error = autocorrelation[0];
        for (var i = 0; i < m; i++)
        {
            var reflection = -autocorrelation[i + 1];

            if (error == 0)
            {
                for (var k = 0; k < m; k++)
                {
                    lpc[k] = 0.0f;
                }

                return 0;
            }

            for (j = 0; j < i; j++)
            {
                reflection -= lpc[j] * autocorrelation[i - j];
            }

            reflection /= error;
            lpc[i] = reflection;

            for (j = 0; j < i / 2; j++)
            {
                var temp = lpc[j];
                lpc[j] += reflection * lpc[i - 1 - j];
                lpc[i - 1 - j] += reflection * temp;
            }

            if ((i & 1) != 0)
            {
                lpc[j] += lpc[j] * reflection;
            }

            error *= 1.0f - reflection * reflection;
        }

        return error;
    }

    public float LpcFromCurve(float[] curve, float[] lpc)
    {
        var n = _ln;
        var work = new float[n + n];
        var scale = 0.5f / n;

        for (var i = 0; i < n; i++)
        {
            work[i * 2] = curve[i] * scale;
            work[i * 2 + 1] = 0;
        }

        work[n * 2 - 1] = curve[n - 1] * scale;

        n *= 2;
        _fft.Backward(work);

        for (int i = 0, j = n / 2; i < n / 2;)
        {
            (work[i], work[j]) = (work[j], work[i]);
            i++;
            j++;
        }

        return LpcFromData(work, lpc, n, _m);
    }

    public void Init(int mapped, int m)
    {
        _ln = mapped;
        _m = m;
        _fft.Initialize(mapped * 2);
    }

    public void Clear()
    {
        _fft.Clear();
    }

    public void LpcToCurve(float[] curve, float[] lpc, float amp)
    {
        for (var i = 0; i < _ln * 2; i++)
        {
            curve[i] = 0.0f;
        }

        if (amp == 0)
        {
            return;
        }

        for (var i = 0; i < _m; i++)
        {
            curve[i * 2 + 1] = lpc[i] / (4 * amp);
            curve[i * 2 + 2] = -lpc[i] / (4 * amp);
        }

        _fft.Backward(curve);

        var l2 = _ln * 2;
        var unit = 1.0f / amp;
        curve[0] = 1.0f / (curve[0] * 2 + unit);
        for (var i = 1; i < _ln; i++)
        {
            var real = curve[i] + curve[l2 - i];
            var imag = curve[i] - curve[l2 - i];
            curve[i] = 1.0f / FastHypot(real + unit, imag);
        }
    }

    private static float FastHypot(float a, float b)
    {
        return (float)Math.Sqrt(a * a + b * b);
    }
}
