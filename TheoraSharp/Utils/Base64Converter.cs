namespace TheoraSharp.Utils;

public class Base64Converter
{
    public static readonly char[] Alphabet =
    {
        'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', //  0 to  7
        'I', 'J', 'K', 'L', 'M', 'N', 'O', 'P', //  8 to 15
        'Q', 'R', 'S', 'T', 'U', 'V', 'W', 'X', // 16 to 23
        'Y', 'Z', 'a', 'b', 'c', 'd', 'e', 'f', // 24 to 31
        'g', 'h', 'i', 'j', 'k', 'l', 'm', 'n', // 32 to 39
        'o', 'p', 'q', 'r', 's', 't', 'u', 'v', // 40 to 47
        'w', 'x', 'y', 'z', '0', '1', '2', '3', // 48 to 55
        '4', '5', '6', '7', '8', '9', '+', '/' // 56 to 63
    }; 


    public static string Encode(byte[] octetString)
    {
        int bits24;
        int bits6;

        char[] res = new char[((octetString.Length - 1) / 3 + 1) * 4];

        int outIndex = 0;
        int i = 0;

        while ((i + 3) <= octetString.Length)
        {
            // store the octets
            bits24 = (octetString[i++] & 0xFF) << 16;
            bits24 |= (octetString[i++] & 0xFF) << 8;
            bits24 |= (octetString[i++] & 0xFF) << 0;

            bits6 = (bits24 & 0x00FC0000) >> 18;
            res[outIndex++] = Alphabet[bits6];
            bits6 = (bits24 & 0x0003F000) >> 12;
            res[outIndex++] = Alphabet[bits6];
            bits6 = (bits24 & 0x00000FC0) >> 6;
            res[outIndex++] = Alphabet[bits6];
            bits6 = (bits24 & 0x0000003F);
            res[outIndex++] = Alphabet[bits6];
        }

        if (octetString.Length - i == 2)
        {
            bits24 = (octetString[i] & 0xFF) << 16;
            bits24 |= (octetString[i + 1] & 0xFF) << 8;

            bits6 = (bits24 & 0x00FC0000) >> 18;
            res[outIndex++] = Alphabet[bits6];
            bits6 = (bits24 & 0x0003F000) >> 12;
            res[outIndex++] = Alphabet[bits6];
            bits6 = (bits24 & 0x00000FC0) >> 6;
            res[outIndex++] = Alphabet[bits6];

            res[outIndex++] = '=';
        }
        else if (octetString.Length - i == 1)
        {
            bits24 = (octetString[i] & 0xFF) << 16;

            bits6 = (bits24 & 0x00FC0000) >> 18;
            res[outIndex++] = Alphabet[bits6];
            bits6 = (bits24 & 0x0003F000) >> 12;
            res[outIndex++] = Alphabet[bits6];

            res[outIndex++] = '=';
            res[outIndex++] = '=';
        }

        return new string(res);
    }
}