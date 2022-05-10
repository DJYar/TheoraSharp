using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace TheoraSharp.Theora;

public class YUVBuffer
{
    internal bool gotNewPixels = true;
    
    public int y_width;
    public int y_height;
    public int y_stride;
    public int uv_width;
    public int uv_height;
    public int uv_stride;
    public short[] data;
    public int y_offset;
    public int u_offset;
    public int v_offset;
    private int pix_size;
    //private uint[] pixels;
    private int crop_x;
    private int crop_y;
    private int crop_w;
    private int crop_h;

    public YUVFormat Format
    {
        get
        {
            if (uv_height < y_height)
                return YUVFormat.Yuv420;

            if (uv_width == y_width)
                return YUVFormat.Yuv444;

            return YUVFormat.Yuv422;
        }
    }

    private uint[] rgb24Cache;

    public uint[] GetPixelsRgb24()
    {
        PrepareRGBData(0, 0, y_width, y_height);
        return rgb24Cache;
    }

    private uint[] PrepareRGBData(int x, int y, int width, int height)
    {
        lock (this)
        {
            if (!gotNewPixels)
            {
                return rgb24Cache;
            }

            int size = width * height;

            try
            {
                if (size != pix_size)
                {
                    rgb24Cache = new uint[size];
                    pix_size = size;
                }

                /* rely on the buffer size being set correctly, and the only allowed
                 video formats being Theora's video formats */
                if (uv_height < y_height)
                    YUV420toRGB(x, y, width, height);
                else if (uv_width == y_width)
                    YUV444toRGB(x, y, width, height);
                else
                    YUV422toRGB(x, y, width, height);
            }
            catch (Exception t)
            {
                /* ignore */
            }

            gotNewPixels = false;
        }

        return rgb24Cache;
    }

    private void YUV420toRGB(int x, int y, int width, int height)
    {
        /*
         * this modified version of the original YUVtoRGB was
         * provided by Ilan and Yaniv Ben Hagai.
         *
         * additional thanks to Gumboot for helping with making this
         * code perform better.
         */

        // Set up starting values for YUV pointers
        int YPtr = y_offset + x + y * (y_stride);
        int YPtr2 = YPtr + y_stride;
        int UPtr = u_offset + x / 2 + (y / 2) * (uv_stride);
        int VPtr = v_offset + x / 2 + (y / 2) * (uv_stride);
        int RGBPtr = 0;
        int RGBPtr2 = width;
        int width2 = width / 2;
        int height2 = height / 2;

        // Set the line step for the Y and UV planes and YPtr2
        int YStep = y_stride * 2 - (width2) * 2;
        int UVStep = uv_stride - (width2);
        int RGBStep = width;

        for (int i = 0; i < height2; i++)
        {
            for (int j = 0; j < width2; j++)
            {
                int D, E, r, g, b, t1, t2, t3, t4;

                D = data[UPtr++];
                E = data[VPtr++];

                t1 = 298 * (data[YPtr] - 16);
                t2 = 409 * E - 409 * 128 + 128;
                t3 = (100 * D) + (208 * E) - 100 * 128 - 208 * 128 - 128;
                t4 = 516 * D - 516 * 128 + 128;

                r = (t1 + t2);
                g = (t1 - t3);
                b = (t1 + t4);

                // retrieve data for next pixel now, hide latency?
                t1 = 298 * (data[YPtr + 1] - 16);

                // pack pixel
                rgb24Cache[RGBPtr] =
                    (uint)((clamp65280(r) << 8) | clamp65280(g) | (clamp65280(b) >> 8) | 0xff000000);

                r = (t1 + t2);
                g = (t1 - t3);
                b = (t1 + t4);

                // retrieve data for next pixel now, hide latency?
                t1 = 298 * (data[YPtr2] - 16);

                // pack pixel
                rgb24Cache[RGBPtr + 1] =
                    (uint)((clamp65280(r) << 8) | clamp65280(g) | (clamp65280(b) >> 8) | 0xff000000);


                r = (t1 + t2);
                g = (t1 - t3);
                b = (t1 + t4);

                // retrieve data for next pixel now, hide latency?
                t1 = 298 * (data[YPtr2 + 1] - 16);

                // pack pixel
                rgb24Cache[RGBPtr2] =
                    (uint)((clamp65280(r) << 8) | clamp65280(g) | (clamp65280(b) >> 8) | 0xff000000);


                r = (t1 + t2);
                g = (t1 - t3);
                b = (t1 + t4);

                // pack pixel
                rgb24Cache[RGBPtr2 + 1] =
                    (uint)((clamp65280(r) << 8) | clamp65280(g) | (clamp65280(b) >> 8) | 0xff000000);
                YPtr += 2;
                YPtr2 += 2;
                RGBPtr += 2;
                RGBPtr2 += 2;
            }

            // Increment the various pointers
            YPtr += YStep;
            YPtr2 += YStep;
            UPtr += UVStep;
            VPtr += UVStep;
            RGBPtr += RGBStep;
            RGBPtr2 += RGBStep;
        }
    }

    // kept for reference
    /*private static final int clamp255(int val) {
        return ((~(val>>31)) & 255 & (val | ((255-val)>>31)));
    }*/

    private static int clamp65280(int val)
    {
        /* 65280 == 255 << 8 == 0x0000FF00 */
        /* This function is just like clamp255, but only acting on the top
        24 bits (bottom 8 are zero'd).  This allows val, initially scaled
        to 65536, to be clamped without shifting, thereby saving one shift.
        (RGB packing must be aware that the info is in the second-lowest
        byte.) */
        return ((~(val >> 31)) & 65280 & (val | ((65280 - val) >> 31)));
    }

    private void YUV444toRGB(int x, int y, int width, int height)
    {
        for (int j = 0; j < height; j++)
        {
            for (int i = 0; i < width; i++)
            {
                int D, E, r, g, b, t1, t2, t3, t4, p;
                p = x + i + (j + y) * y_stride;

                D = data[u_offset + p];
                E = data[v_offset + p];

                t1 = 298 * (data[y_offset + p] - 16);
                t2 = 409 * E - 409 * 128 + 128;
                t3 = (100 * D) + (208 * E) - 100 * 128 - 208 * 128 - 128;
                t4 = 516 * D - 516 * 128 + 128;

                r = (t1 + t2);
                g = (t1 - t3);
                b = (t1 + t4);

                // pack pixel
                rgb24Cache[i + j * width] =
                    (uint)((clamp65280(r) << 8) | clamp65280(g) | (clamp65280(b) >> 8) | 0xff000000);
            }
        }
    }

    private void YUV422toRGB(int x, int y, int width, int height)
    {
        int x2 = x / 2;
        int width2 = width / 2;
        for (int j = 0; j < height; j++)
        {
            for (int i = 0; i < width2; i++)
            {
                int D, E, r, g, b, t1, t2, t3, t4, p;
                p = x2 + i + (y + j) * uv_stride;

                D = data[u_offset + p];
                E = data[v_offset + p];

                p = y_offset + 2 * (x2 + i) + (y + j) * y_stride;
                t1 = 298 * (data[p] - 16);
                t2 = 409 * E - 409 * 128 + 128;
                t3 = (100 * D) + (208 * E) - 100 * 128 - 208 * 128 - 128;
                t4 = 516 * D - 516 * 128 + 128;

                r = (t1 + t2);
                g = (t1 - t3);
                b = (t1 + t4);

                p++;
                t1 = 298 * (data[p] - 16);

                // pack pixel
                p = 2 * i + j * width;
                rgb24Cache[p] =
                    (uint)((clamp65280(r) << 8) | clamp65280(g) | (clamp65280(b) >> 8) | 0xff000000);

                r = (t1 + t2);
                g = (t1 - t3);
                b = (t1 + t4);
                p++;

                // pack pixel
                rgb24Cache[p] =
                    (uint)((clamp65280(r) << 8) | clamp65280(g) | (clamp65280(b) >> 8) | 0xff000000);
            }
        }
    }

    internal void newPixels()
    {
        gotNewPixels = true;
    }
}