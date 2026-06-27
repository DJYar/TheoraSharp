using OggBuffer = TheoraSharp.Ogg.Buffer;
using PacketContext = TheoraSharp.Ogg.PacketContext;

namespace TheoraSharp.Vorbis;

public class Info
{
    private const int OvEBadPacket = -136;
    private const int OvENotAudio = -135;

    private const int ViTimeB = 1;
    private const int ViFloorB = 2;
    private const int ViResB = 3;
    private const int ViMapB = 1;
    private const int ViWindowB = 1;

    private static readonly byte[] Vorbis =
    {
        (byte)'v',
        (byte)'o',
        (byte)'r',
        (byte)'b',
        (byte)'i',
        (byte)'s'
    };

    public int Version { get; set; }
    public int Channels { get; set; }
    public int Rate { get; set; }

    public int BitrateUpper { get; set; }
    public int BitrateNominal { get; set; }
    public int BitrateLower { get; set; }

    public int[] BlockSizes { get; set; } = new int[2];

    public int Modes { get; set; }
    public int Maps { get; set; }
    public int Times { get; set; }
    public int Floors { get; set; }
    public int Residues { get; set; }
    public int Books { get; set; }
    public int Psys { get; set; }

    internal InfoMode[] ModeParam { get; set; }

    internal int[] MapType { get; set; }
    internal object[] MapParam { get; set; }

    public int[] TimeType { get; set; }
    public object[] TimeParam { get; set; }

    public int[] FloorType { get; set; }
    public object[] FloorParam { get; set; }

    public int[] ResidueType { get; set; }
    public object[] ResidueParam { get; set; }

    internal StaticCodeBook[] BookParam { get; set; }

    internal PsyInfo[] PsyParam { get; set; } = new PsyInfo[64];

    internal int Envelopes { get; set; }
    internal float PreechoThresh { get; set; }
    internal float PreechoClamp { get; set; }

    public void Initialize()
    {
        Rate = 0;
    }

    public void Clear()
    {
        if (ModeParam != null)
        {
            for (var i = 0; i < Modes && i < ModeParam.Length; i++)
            {
                ModeParam[i] = null;
            }
        }

        ModeParam = null;

        if (MapParam != null && MapType != null)
        {
            for (var i = 0; i < Maps && i < MapParam.Length && i < MapType.Length; i++)
            {
                FuncMapping.MappingP[MapType[i]].FreeInfo(MapParam[i]);
            }
        }

        MapParam = null;

        if (TimeParam != null && TimeType != null)
        {
            for (var i = 0; i < Times && i < TimeParam.Length && i < TimeType.Length; i++)
            {
                FuncTime.TimeP[TimeType[i]].FreeInfo(TimeParam[i]);
            }
        }

        TimeParam = null;

        if (FloorParam != null && FloorType != null)
        {
            for (var i = 0; i < Floors && i < FloorParam.Length && i < FloorType.Length; i++)
            {
                FuncFloor.FloorP[FloorType[i]].FreeInfo(FloorParam[i]);
            }
        }

        FloorParam = null;

        if (ResidueParam != null && ResidueType != null)
        {
            for (var i = 0; i < Residues && i < ResidueParam.Length && i < ResidueType.Length; i++)
            {
                FuncResidue.ResidueP[ResidueType[i]].FreeInfo(ResidueParam[i]);
            }
        }

        ResidueParam = null;

        if (BookParam != null)
        {
            for (var i = 0; i < Books && i < BookParam.Length; i++)
            {
                BookParam[i]?.Clear();
                BookParam[i] = null;
            }
        }

        BookParam = null;

        if (PsyParam != null)
        {
            for (var i = 0; i < Psys && i < PsyParam.Length; i++)
            {
                PsyParam[i]?.Free();
                PsyParam[i] = null;
            }
        }
    }

    internal int UnpackInfo(OggBuffer buffer)
    {
        Version = buffer.Read(32);
        if (Version != 0)
        {
            return -1;
        }

        Channels = buffer.Read(8);
        Rate = buffer.Read(32);

        BitrateUpper = buffer.Read(32);
        BitrateNominal = buffer.Read(32);
        BitrateLower = buffer.Read(32);

        BlockSizes[0] = 1 << buffer.Read(4);
        BlockSizes[1] = 1 << buffer.Read(4);

        if (Rate < 1 ||
            Channels < 1 ||
            BlockSizes[0] < 8 ||
            BlockSizes[1] < BlockSizes[0] ||
            buffer.Read(1) != 1)
        {
            Clear();
            return -1;
        }

        return 0;
    }

    internal int UnpackBooks(OggBuffer buffer)
    {
        Books = buffer.Read(8) + 1;

        if (BookParam == null || BookParam.Length != Books)
        {
            BookParam = new StaticCodeBook[Books];
        }

        for (var i = 0; i < Books; i++)
        {
            BookParam[i] = new StaticCodeBook();
            if (BookParam[i].Unpack(buffer) != 0)
            {
                Clear();
                return -1;
            }
        }

        Times = buffer.Read(6) + 1;
        if (TimeType == null || TimeType.Length != Times)
        {
            TimeType = new int[Times];
        }

        if (TimeParam == null || TimeParam.Length != Times)
        {
            TimeParam = new object[Times];
        }

        for (var i = 0; i < Times; i++)
        {
            TimeType[i] = buffer.Read(16);
            if (TimeType[i] < 0 || TimeType[i] >= ViTimeB)
            {
                Clear();
                return -1;
            }

            TimeParam[i] = FuncTime.TimeP[TimeType[i]].Unpack(this, buffer);
            if (TimeParam[i] == null)
            {
                Clear();
                return -1;
            }
        }

        Floors = buffer.Read(6) + 1;
        if (FloorType == null || FloorType.Length != Floors)
        {
            FloorType = new int[Floors];
        }

        if (FloorParam == null || FloorParam.Length != Floors)
        {
            FloorParam = new object[Floors];
        }

        for (var i = 0; i < Floors; i++)
        {
            FloorType[i] = buffer.Read(16);
            if (FloorType[i] < 0 || FloorType[i] >= ViFloorB)
            {
                Clear();
                return -1;
            }

            FloorParam[i] = FuncFloor.FloorP[FloorType[i]].Unpack(this, buffer);
            if (FloorParam[i] == null)
            {
                Clear();
                return -1;
            }
        }

        Residues = buffer.Read(6) + 1;
        if (ResidueType == null || ResidueType.Length != Residues)
        {
            ResidueType = new int[Residues];
        }

        if (ResidueParam == null || ResidueParam.Length != Residues)
        {
            ResidueParam = new object[Residues];
        }

        for (var i = 0; i < Residues; i++)
        {
            ResidueType[i] = buffer.Read(16);
            if (ResidueType[i] < 0 || ResidueType[i] >= ViResB)
            {
                Clear();
                return -1;
            }

            ResidueParam[i] = FuncResidue.ResidueP[ResidueType[i]].Unpack(this, buffer);
            if (ResidueParam[i] == null)
            {
                Clear();
                return -1;
            }
        }

        Maps = buffer.Read(6) + 1;
        if (MapType == null || MapType.Length != Maps)
        {
            MapType = new int[Maps];
        }

        if (MapParam == null || MapParam.Length != Maps)
        {
            MapParam = new object[Maps];
        }

        for (var i = 0; i < Maps; i++)
        {
            MapType[i] = buffer.Read(16);
            if (MapType[i] < 0 || MapType[i] >= ViMapB)
            {
                Clear();
                return -1;
            }

            MapParam[i] = FuncMapping.MappingP[MapType[i]].Unpack(this, buffer);
            if (MapParam[i] == null)
            {
                Clear();
                return -1;
            }
        }

        Modes = buffer.Read(6) + 1;
        if (ModeParam == null || ModeParam.Length != Modes)
        {
            ModeParam = new InfoMode[Modes];
        }

        for (var i = 0; i < Modes; i++)
        {
            ModeParam[i] = new InfoMode
            {
                BlockFlag = buffer.Read(1),
                WindowType = buffer.Read(16),
                TransformType = buffer.Read(16),
                Mapping = buffer.Read(8)
            };

            if (ModeParam[i].WindowType >= ViWindowB ||
                ModeParam[i].TransformType >= ViWindowB ||
                ModeParam[i].Mapping >= Maps)
            {
                Clear();
                return -1;
            }
        }

        if (buffer.Read(1) != 1)
        {
            Clear();
            return -1;
        }

        return 0;
    }

    public int SynthesisHeaderIn(Comment comment, PacketContext packet)
    {
        var buffer = new OggBuffer();

        if (packet == null)
        {
            return -1;
        }

        buffer.ReadInit(packet.PacketBase, packet.PacketPos, packet.Bytes);

        var header = new byte[6];
        var packetType = buffer.Read(8);
        buffer.Read(header, 6);

        if (header[0] != 'v' ||
            header[1] != 'o' ||
            header[2] != 'r' ||
            header[3] != 'b' ||
            header[4] != 'i' ||
            header[5] != 's')
        {
            return -1;
        }

        switch (packetType)
        {
            case 0x01:
                if (packet.BOS == 0 || Rate != 0)
                {
                    return -1;
                }

                return UnpackInfo(buffer);
            case 0x03:
                if (Rate == 0)
                {
                    return -1;
                }

                return comment.Unpack(buffer);
            case 0x05:
                if (Rate == 0 || comment.Vendor == null)
                {
                    return -1;
                }

                return UnpackBooks(buffer);
            default:
                return -1;
        }
    }

    internal int PackInfo(OggBuffer buffer)
    {
        buffer.Write(0x01, 8);
        buffer.Write(Vorbis);

        buffer.Write(0x00, 32);
        buffer.Write(unchecked((uint)Channels), 8);
        buffer.Write(unchecked((uint)Rate), 32);

        buffer.Write(unchecked((uint)BitrateUpper), 32);
        buffer.Write(unchecked((uint)BitrateNominal), 32);
        buffer.Write(unchecked((uint)BitrateLower), 32);

        buffer.Write(unchecked((uint)ILog2(BlockSizes[0])), 4);
        buffer.Write(unchecked((uint)ILog2(BlockSizes[1])), 4);
        buffer.Write(1, 1);
        return 0;
    }

    internal int PackBooks(OggBuffer buffer)
    {
        buffer.Write(0x05, 8);
        buffer.Write(Vorbis);

        buffer.Write(unchecked((uint)(Books - 1)), 8);
        for (var i = 0; i < Books; i++)
        {
            if (BookParam[i].Pack(buffer) != 0)
            {
                return -1;
            }
        }

        buffer.Write(unchecked((uint)(Times - 1)), 6);
        for (var i = 0; i < Times; i++)
        {
            buffer.Write(unchecked((uint)TimeType[i]), 16);
            FuncTime.TimeP[TimeType[i]].Pack(TimeParam[i], buffer);
        }

        buffer.Write(unchecked((uint)(Floors - 1)), 6);
        for (var i = 0; i < Floors; i++)
        {
            buffer.Write(unchecked((uint)FloorType[i]), 16);
            FuncFloor.FloorP[FloorType[i]].Pack(FloorParam[i], buffer);
        }

        buffer.Write(unchecked((uint)(Residues - 1)), 6);
        for (var i = 0; i < Residues; i++)
        {
            buffer.Write(unchecked((uint)ResidueType[i]), 16);
            FuncResidue.ResidueP[ResidueType[i]].Pack(ResidueParam[i], buffer);
        }

        buffer.Write(unchecked((uint)(Maps - 1)), 6);
        for (var i = 0; i < Maps; i++)
        {
            buffer.Write(unchecked((uint)MapType[i]), 16);
            FuncMapping.MappingP[MapType[i]].Pack(this, MapParam[i], buffer);
        }

        buffer.Write(unchecked((uint)(Modes - 1)), 6);
        for (var i = 0; i < Modes; i++)
        {
            buffer.Write(unchecked((uint)ModeParam[i].BlockFlag), 1);
            buffer.Write(unchecked((uint)ModeParam[i].WindowType), 16);
            buffer.Write(unchecked((uint)ModeParam[i].TransformType), 16);
            buffer.Write(unchecked((uint)ModeParam[i].Mapping), 8);
        }

        buffer.Write(1, 1);
        return 0;
    }

    public int BlockSize(PacketContext packet)
    {
        var buffer = new OggBuffer();
        int mode;

        buffer.ReadInit(packet.PacketBase, packet.PacketPos, packet.Bytes);

        if (buffer.Read(1) != 0)
        {
            return OvENotAudio;
        }

        var modeBits = 0;
        var value = Modes;
        while (value > 1)
        {
            modeBits++;
            value = (int)((uint)value >> 1);
        }

        mode = buffer.Read(modeBits);
        if (mode < 0 || mode >= Modes)
        {
            return OvEBadPacket;
        }

        return BlockSizes[ModeParam[mode].BlockFlag];
    }

    private static int ILog2(int value)
    {
        var result = 0;
        while (value > 1)
        {
            result++;
            value = (int)((uint)value >> 1);
        }

        return result;
    }

    public override string ToString()
    {
        return "version:" + Version +
               ", channels:" + Channels +
               ", rate:" + Rate +
               ", bitrate:" + BitrateUpper + "," +
               BitrateNominal + "," +
               BitrateLower;
    }
}
