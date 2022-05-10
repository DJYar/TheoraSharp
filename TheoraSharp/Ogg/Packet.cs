namespace TheoraSharp.Ogg;

public class Packet
{
    public byte[] PacketBase { get; set; }
    public int PacketPos { get; set; }
    public int Bytes { get; set; }
    public int BOS { get; set; }
    public int EOS { get; set; }

    public long GranulePos { get; set; }

    public long PacketNo { get; set; }
}