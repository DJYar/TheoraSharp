namespace TheoraSharp.Theora;

public static class Constants
{
    public const int CURRENT_ENCODE_VERSION =  1;

/* Baseline dct height and width. */
    public const int BLOCK_HEIGHT_WIDTH = 8;
    public const int HFRAGPIXELS        = 8;
    public const int VFRAGPIXELS        = 8;

/* Baseline dct block size */
    public const int BLOCK_SIZE         = (BLOCK_HEIGHT_WIDTH * BLOCK_HEIGHT_WIDTH);

/* Border is for unrestricted mv's */
    public const int UMV_BORDER         = 16;
    public const int STRIDE_EXTRA       = (UMV_BORDER * 2);
    public const int Q_TABLE_SIZE       = 64;

    public const int BASE_FRAME         = 0;
    public const int NORMAL_FRAME       = 1;

    public const int MAX_MODES          = 8;
    public const int MODE_BITS          = 3;
    public const int MODE_METHODS       = 8;
    public const int MODE_METHOD_BITS   = 3;

    public static int[] dequant_index = {
        0,  1,  8,  16,  9,  2,  3, 10,
        17, 24, 32, 25, 18, 11,  4,  5,
        12, 19, 26, 33, 40, 48, 41, 34,
        27, 20, 13,  6,  7, 14, 21, 28,
        35, 42, 49, 56, 57, 50, 43, 36,
        29, 22, 15, 23, 30, 37, 44, 51,
        58, 59, 52, 45, 38, 31, 39, 46,
        53, 60, 61, 54, 47, 55, 62, 63
    };
}