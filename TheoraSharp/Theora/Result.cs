namespace TheoraSharp.Theora;

public enum Result
{
    Fault = -1,
    EInval = -10,
    BadHeader = -20,
    NotFormat = -21,
    Version = -22,
    Impl = -23,
    BadPacket = -24,
    NewPacket = -25
}