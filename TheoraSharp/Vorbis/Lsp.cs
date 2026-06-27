namespace TheoraSharp.Vorbis;

internal static class Lsp
{
    private const float Pi = 3.1415926539f;

    public static void LspToCurve(float[] curve, int[] map, int n, int ln, float[] lsp, int m, float amp, float ampOffset)
    {
        var wdel = Pi / ln;
        for (var i = 0; i < m; i++)
        {
            lsp[i] = Lookup.CosLook(lsp[i]);
        }

        var m2 = m / 2 * 2;
        var index = 0;
        while (index < n)
        {
            var k = map[index];
            var p = 0.7071067812f;
            var q = 0.7071067812f;
            var w = Lookup.CosLook(wdel * k);

            for (var j = 0; j < m2; j += 2)
            {
                q *= lsp[j] - w;
                p *= lsp[j + 1] - w;
            }

            if ((m & 1) != 0)
            {
                q *= lsp[m - 1] - w;
                q *= q;
                p *= p * (1.0f - w * w);
            }
            else
            {
                q *= q * (1.0f + w);
                p *= p * (1.0f - w);
            }

            q = p + q;
            var hx = BitConverter.SingleToInt32Bits(q);
            var ix = 0x7fffffff & hx;
            var qexp = 0;

            if (ix < 0x7f800000 && ix != 0)
            {
                if (ix < 0x00800000)
                {
                    q *= 3.3554432e+07f;
                    hx = BitConverter.SingleToInt32Bits(q);
                    ix = 0x7fffffff & hx;
                    qexp = -25;
                }

                qexp += (int)((uint)ix >> 23) - 126;
                hx = (hx & unchecked((int)0x807fffff)) | 0x3f000000;
                q = BitConverter.Int32BitsToSingle(hx);
            }

            q = Lookup.FromDbLook(amp * Lookup.InvSqLook(q) * Lookup.InvSq2ExpLook(qexp + m) - ampOffset);

            do
            {
                curve[index++] *= q;
            }
            while (index < n && map[index] == k);
        }
    }
}
