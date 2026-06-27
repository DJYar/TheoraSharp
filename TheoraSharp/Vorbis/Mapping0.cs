using OggBuffer = TheoraSharp.Ogg.Buffer;

namespace TheoraSharp.Vorbis;

internal class Mapping0 : FuncMapping
{
    private float[][] _pcmBundle;
    private int[] _zeroBundle;
    private int[] _nonZero;
    private object[] _floorMemo;

    public override void FreeInfo(object mapping)
    {
    }

    public override void FreeLook(object mapping)
    {
    }

    public override object Look(DspState dspState, InfoMode mode, object mapping)
    {
        var info = dspState.Vi;
        var look = new LookMapping0
        {
            Map = (InfoMapping0)mapping,
            Mode = mode
        };

        var mappingInfo = look.Map;

        look.TimeLook = new object[mappingInfo.SubMaps];
        look.FloorLook = new object[mappingInfo.SubMaps];
        look.ResidueLook = new object[mappingInfo.SubMaps];
        look.TimeFunc = new FuncTime[mappingInfo.SubMaps];
        look.FloorFunc = new FuncFloor[mappingInfo.SubMaps];
        look.ResidueFunc = new FuncResidue[mappingInfo.SubMaps];

        for (var i = 0; i < mappingInfo.SubMaps; i++)
        {
            var timeNumber = mappingInfo.TimeSubMap[i];
            var floorNumber = mappingInfo.FloorSubMap[i];
            var residueNumber = mappingInfo.ResidueSubMap[i];

            look.TimeFunc[i] = FuncTime.TimeP[info.TimeType[timeNumber]];
            look.TimeLook[i] = look.TimeFunc[i].Look(dspState, mode, info.TimeParam[timeNumber]);

            look.FloorFunc[i] = FuncFloor.FloorP[info.FloorType[floorNumber]];
            look.FloorLook[i] = look.FloorFunc[i].Look(dspState, mode, info.FloorParam[floorNumber]);

            look.ResidueFunc[i] = FuncResidue.ResidueP[info.ResidueType[residueNumber]];
            look.ResidueLook[i] = look.ResidueFunc[i].Look(dspState, mode, info.ResidueParam[residueNumber]);
        }

        look.Channels = info.Channels;
        return look;
    }

    public override void Pack(Info info, object mapping, OggBuffer buffer)
    {
        var mappingInfo = (InfoMapping0)mapping;

        if (mappingInfo.SubMaps > 1)
        {
            buffer.Write(1, 1);
            buffer.Write((uint)(mappingInfo.SubMaps - 1), 4);
        }
        else
        {
            buffer.Write(0, 1);
        }

        if (mappingInfo.CouplingSteps > 0)
        {
            buffer.Write(1, 1);
            buffer.Write((uint)(mappingInfo.CouplingSteps - 1), 8);
            for (var i = 0; i < mappingInfo.CouplingSteps; i++)
            {
                buffer.Write((uint)mappingInfo.CouplingMag[i], ILog2(info.Channels));
                buffer.Write((uint)mappingInfo.CouplingAng[i], ILog2(info.Channels));
            }
        }
        else
        {
            buffer.Write(0, 1);
        }

        buffer.Write(0, 2);

        if (mappingInfo.SubMaps > 1)
        {
            for (var i = 0; i < info.Channels; i++)
            {
                buffer.Write((uint)mappingInfo.ChMuxList[i], 4);
            }
        }

        for (var i = 0; i < mappingInfo.SubMaps; i++)
        {
            buffer.Write((uint)mappingInfo.TimeSubMap[i], 8);
            buffer.Write((uint)mappingInfo.FloorSubMap[i], 8);
            buffer.Write((uint)mappingInfo.ResidueSubMap[i], 8);
        }
    }

    public override object Unpack(Info info, OggBuffer buffer)
    {
        var mappingInfo = new InfoMapping0();

        mappingInfo.SubMaps = buffer.Read(1) != 0 ? buffer.Read(4) + 1 : 1;

        if (buffer.Read(1) != 0)
        {
            mappingInfo.CouplingSteps = buffer.Read(8) + 1;

            for (var i = 0; i < mappingInfo.CouplingSteps; i++)
            {
                var testMagnitude = mappingInfo.CouplingMag[i] = buffer.Read(ILog2(info.Channels));
                var testAngle = mappingInfo.CouplingAng[i] = buffer.Read(ILog2(info.Channels));

                if (testMagnitude < 0 ||
                    testAngle < 0 ||
                    testMagnitude == testAngle ||
                    testMagnitude >= info.Channels ||
                    testAngle >= info.Channels)
                {
                    mappingInfo.Free();
                    return null;
                }
            }
        }

        if (buffer.Read(2) > 0)
        {
            mappingInfo.Free();
            return null;
        }

        if (mappingInfo.SubMaps > 1)
        {
            for (var i = 0; i < info.Channels; i++)
            {
                mappingInfo.ChMuxList[i] = buffer.Read(4);
                if (mappingInfo.ChMuxList[i] >= mappingInfo.SubMaps)
                {
                    mappingInfo.Free();
                    return null;
                }
            }
        }

        for (var i = 0; i < mappingInfo.SubMaps; i++)
        {
            mappingInfo.TimeSubMap[i] = buffer.Read(8);
            if (mappingInfo.TimeSubMap[i] >= info.Times)
            {
                mappingInfo.Free();
                return null;
            }

            mappingInfo.FloorSubMap[i] = buffer.Read(8);
            if (mappingInfo.FloorSubMap[i] >= info.Floors)
            {
                mappingInfo.Free();
                return null;
            }

            mappingInfo.ResidueSubMap[i] = buffer.Read(8);
            if (mappingInfo.ResidueSubMap[i] >= info.Residues)
            {
                mappingInfo.Free();
                return null;
            }
        }

        return mappingInfo;
    }

    public override int Inverse(Block block, object lookObject)
    {
        var dspState = block.Vd;
        var info = dspState.Vi;
        var look = (LookMapping0)lookObject;
        var mappingInfo = look.Map;
        var mode = look.Mode;
        var n = block.PcmEnd = info.BlockSizes[block.W];
        var window = dspState.Window[block.W][block.LW][block.NW][mode.WindowType];

        if (_pcmBundle == null || _pcmBundle.Length < info.Channels)
        {
            _pcmBundle = new float[info.Channels][];
            _nonZero = new int[info.Channels];
            _zeroBundle = new int[info.Channels];
            _floorMemo = new object[info.Channels];
        }

        for (var i = 0; i < info.Channels; i++)
        {
            var pcm = block.Pcm[i];
            var subMap = mappingInfo.ChMuxList[i];

            _floorMemo[i] = look.FloorFunc[subMap].Inverse1(block, look.FloorLook[subMap], _floorMemo[i]);
            _nonZero[i] = _floorMemo[i] != null ? 1 : 0;

            for (var j = 0; j < n / 2; j++)
            {
                pcm[j] = 0;
            }
        }

        for (var i = 0; i < mappingInfo.CouplingSteps; i++)
        {
            if (_nonZero[mappingInfo.CouplingMag[i]] != 0 || _nonZero[mappingInfo.CouplingAng[i]] != 0)
            {
                _nonZero[mappingInfo.CouplingMag[i]] = 1;
                _nonZero[mappingInfo.CouplingAng[i]] = 1;
            }
        }

        for (var i = 0; i < mappingInfo.SubMaps; i++)
        {
            var channelsInBundle = 0;
            for (var j = 0; j < info.Channels; j++)
            {
                if (mappingInfo.ChMuxList[j] != i)
                {
                    continue;
                }

                _zeroBundle[channelsInBundle] = _nonZero[j] != 0 ? 1 : 0;
                _pcmBundle[channelsInBundle++] = block.Pcm[j];
            }

            look.ResidueFunc[i].Inverse(block, look.ResidueLook[i], _pcmBundle, _zeroBundle, channelsInBundle);
        }

        for (var i = mappingInfo.CouplingSteps - 1; i >= 0; i--)
        {
            var pcmMagnitude = block.Pcm[mappingInfo.CouplingMag[i]];
            var pcmAngle = block.Pcm[mappingInfo.CouplingAng[i]];

            for (var j = 0; j < n / 2; j++)
            {
                var magnitude = pcmMagnitude[j];
                var angle = pcmAngle[j];

                if (magnitude > 0)
                {
                    if (angle > 0)
                    {
                        pcmMagnitude[j] = magnitude;
                        pcmAngle[j] = magnitude - angle;
                    }
                    else
                    {
                        pcmAngle[j] = magnitude;
                        pcmMagnitude[j] = magnitude + angle;
                    }
                }
                else
                {
                    if (angle > 0)
                    {
                        pcmMagnitude[j] = magnitude;
                        pcmAngle[j] = magnitude + angle;
                    }
                    else
                    {
                        pcmAngle[j] = magnitude;
                        pcmMagnitude[j] = magnitude - angle;
                    }
                }
            }
        }

        for (var i = 0; i < info.Channels; i++)
        {
            var pcm = block.Pcm[i];
            var subMap = mappingInfo.ChMuxList[i];
            look.FloorFunc[subMap].Inverse2(block, look.FloorLook[subMap], _floorMemo[i], pcm);
        }

        for (var i = 0; i < info.Channels; i++)
        {
            var pcm = block.Pcm[i];
            ((Mdct)dspState.Transform[block.W][0]).Backward(pcm, pcm);
        }

        for (var i = 0; i < info.Channels; i++)
        {
            var pcm = block.Pcm[i];
            if (_nonZero[i] != 0)
            {
                for (var j = 0; j < n; j++)
                {
                    pcm[j] *= window[j];
                }
            }
            else
            {
                for (var j = 0; j < n; j++)
                {
                    pcm[j] = 0f;
                }
            }
        }

        return 0;
    }

    private static int ILog2(int value)
    {
        var result = 0;
        if (value > 0)
        {
            value--;
        }

        while (value > 0)
        {
            result++;
            value = (int)((uint)value >> 1);
        }

        return result;
    }
}

internal class InfoMapping0
{
    public int SubMaps { get; set; }
    public int[] ChMuxList { get; private set; } = new int[256];
    public int[] TimeSubMap { get; private set; } = new int[16];
    public int[] FloorSubMap { get; private set; } = new int[16];
    public int[] ResidueSubMap { get; private set; } = new int[16];
    public int[] PsySubMap { get; private set; } = new int[16];
    public int CouplingSteps { get; set; }
    public int[] CouplingMag { get; private set; } = new int[256];
    public int[] CouplingAng { get; private set; } = new int[256];

    public void Free()
    {
        ChMuxList = null;
        TimeSubMap = null;
        FloorSubMap = null;
        ResidueSubMap = null;
        PsySubMap = null;
        CouplingMag = null;
        CouplingAng = null;
    }
}

internal class LookMapping0
{
    public InfoMode Mode { get; set; }
    public InfoMapping0 Map { get; set; }
    public object[] TimeLook { get; set; }
    public object[] FloorLook { get; set; }
    public object[] FloorState { get; set; }
    public object[] ResidueLook { get; set; }
    public PsyLook[] PsyLook { get; set; }
    public FuncTime[] TimeFunc { get; set; }
    public FuncFloor[] FloorFunc { get; set; }
    public FuncResidue[] ResidueFunc { get; set; }
    public int Channels { get; set; }
    public float[][] Decay { get; set; }
    public int LastFrame { get; set; }
}
