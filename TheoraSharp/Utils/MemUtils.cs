using System.Text;

namespace TheoraSharp.Utils;

public class MemUtils
{
    private static char[] bytes = { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'a', 'b', 'c', 'd', 'e', 'f' };

    public static int Compare(byte[] mem1, byte[] mem2, int len)
    {
        for (int i = 0; i < len; i++)
        {
            if (mem1[i] != mem2[i])
            {
                if (mem1[i] < mem2[i])
                    return -i;
                else
                    return i;
            }
        }

        return 0;
    }

    public static void Set(byte[] mem, int offset, int val, int len)
    {
        len += offset;

        for (int i = offset; i < len; i++)
        {
            mem[i] = (byte)val;
        }
    }

    public static void Set(short[] mem, int offset, int val, int len)
    {
        len += offset;

        for (int i = offset; i < len; i++)
        {
            mem[i] = (short)val;
        }
    }

    public static void Set(int[] mem, int offset, int val, int len)
    {
        len += offset;

        for (int i = offset; i < len; i++)
        {
            mem[i] = (int)val;
        }
    }

    public static void Set(Object[] mem, int offset, Object val, int len)
    {
        len += offset;

        for (int i = offset; i < len; i++)
        {
            mem[i] = val;
        }
    }

    public static void Set<T>(T[] mem, int offset, T val, int len)
    {
        len += offset;

        for (int i = offset; i < len; i++)
        {
            mem[i] = val;
        }
    }

    /* check if a given arr starts with the given pattern */
    public static bool StartsWith(byte[] arr, int offset, int len, byte[] pattern)
    {
        int length = pattern.Length;
        int i;

        if (len < length)
            return false;

        for (i = 0; i < length; i++)
            if (arr[offset + i] != pattern[i])
                break;

        return i == length;
    }

    public static void Dump(byte[] mem, int start, int len)
    {
        int i, j;
        var str = new StringBuilder(50);
        var chars = new StringBuilder(18);
        String vis = Encoding.Default.GetString(mem, start, len);

        i = j = 0;
        while (i < len)
        {
            int b = ((int)mem[i + start]);
            if (b < 0) b += 256;

            if (b > 0x20 && b < 0x7f)
                chars.Append(vis[i]);
            else
                chars.Append(".");

            str.Append(bytes[b / 16]);
            str.Append(bytes[b % 16]);
            str.Append(" ");

            j++;
            i++;

            if (j == 16 || i == len)
            {
                str.Clear();
                chars.Clear();
                j = 0;
            }
        }
    }
}