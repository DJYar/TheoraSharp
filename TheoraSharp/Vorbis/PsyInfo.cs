namespace TheoraSharp.Vorbis;

internal class PsyInfo
{
    public int AthP { get; set; }
    public int DecayP { get; set; }
    public int SmoothP { get; set; }
    public int NoiseFitP { get; set; }
    public int NoiseFitSubBlock { get; set; }
    public float NoiseFitThreshDb { get; set; }

    public float AthAtt { get; set; }

    public int ToneMaskP { get; set; }
    public float[] ToneAtt125Hz { get; } = new float[5];
    public float[] ToneAtt250Hz { get; } = new float[5];
    public float[] ToneAtt500Hz { get; } = new float[5];
    public float[] ToneAtt1000Hz { get; } = new float[5];
    public float[] ToneAtt2000Hz { get; } = new float[5];
    public float[] ToneAtt4000Hz { get; } = new float[5];
    public float[] ToneAtt8000Hz { get; } = new float[5];

    public int PeakAttP { get; set; }
    public float[] PeakAtt125Hz { get; } = new float[5];
    public float[] PeakAtt250Hz { get; } = new float[5];
    public float[] PeakAtt500Hz { get; } = new float[5];
    public float[] PeakAtt1000Hz { get; } = new float[5];
    public float[] PeakAtt2000Hz { get; } = new float[5];
    public float[] PeakAtt4000Hz { get; } = new float[5];
    public float[] PeakAtt8000Hz { get; } = new float[5];

    public int NoiseMaskP { get; set; }
    public float[] NoiseAtt125Hz { get; } = new float[5];
    public float[] NoiseAtt250Hz { get; } = new float[5];
    public float[] NoiseAtt500Hz { get; } = new float[5];
    public float[] NoiseAtt1000Hz { get; } = new float[5];
    public float[] NoiseAtt2000Hz { get; } = new float[5];
    public float[] NoiseAtt4000Hz { get; } = new float[5];
    public float[] NoiseAtt8000Hz { get; } = new float[5];

    public float MaxCurveDb { get; set; }

    public float AttackCoeff { get; set; }
    public float DecayCoeff { get; set; }

    public void Free()
    {
    }
}
