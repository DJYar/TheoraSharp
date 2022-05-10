namespace TheoraSharp.Theora;

public class Version
{
    public const int VERSION_MAJOR = 3;
    public const int VERSION_MINOR = 2;
    public const int VERSION_SUB = 0;
    public const int VERSION = (VERSION_MAJOR << 16) + (VERSION_MINOR << 8) + (VERSION_SUB);
    public static readonly string VENDOR_STRING = "Xiph.Org libTheora I 20040317 3 2 0";
}