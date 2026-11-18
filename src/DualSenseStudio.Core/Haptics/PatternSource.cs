namespace DualSenseStudio.Core.Haptics;

/// <summary>Plays a pattern, optionally looping, with live speed / intensity / balance control.</summary>
public sealed class PatternSource : IHapticSource
{
    private double _pos;

    public PatternSource(Pattern pattern)
    {
        Pattern = pattern;
    }

    public Pattern Pattern { get; }
    public string DisplayName => Pattern.Name;

    /// <summary>Playback rate multiplier (0.25 .. 4).</summary>
    public double Speed { get; set; } = 1.0;
    /// <summary>Gain 0..1.</summary>
    public double Intensity { get; set; } = 1.0;
    /// <summary>-1 = left only, 0 = both, +1 = right only.</summary>
    public double Balance { get; set; }
    public bool Loop { get; set; } = true;
    public bool TriggersEnabled { get; set; } = true;
    public bool Finished { get; private set; }

    /// <summary>Playhead position 0..1.</summary>
    public double Progress => Pattern.Length == 0 ? 0 : _pos / Pattern.Length;

    public void Restart()
    {
        _pos = 0;
        Finished = false;
    }

    public HapticFrame GetFrame(in HapticContext ctx)
    {
        int len = Pattern.Length;
        if (len == 0 || Finished) return default;

        float gain = (float)Math.Clamp(Intensity, 0, 1);
        float bal = (float)Math.Clamp(Balance, -1, 1);
        float gl = MathF.Min(1f, 1f - bal);
        float gr = MathF.Min(1f, 1f + bal);

        var f = new HapticFrame
        {
            Left = Sample(Pattern.Left, _pos, len) * gain * gl,
            Right = Sample(Pattern.Right, _pos, len) * gain * gr,
            TriggerFrequency = Pattern.TriggerFrequency,
        };
        if (TriggersEnabled)
        {
            f.LeftTrigger = Sample(Pattern.LeftTrigger, _pos, len) * gain;
            f.RightTrigger = Sample(Pattern.RightTrigger, _pos, len) * gain;
        }

        double dt = Math.Clamp(ctx.Dt, 0, 0.1);
        _pos += dt * Pattern.SampleRate * Math.Clamp(Speed, 0.05, 8);
        if (_pos >= len)
        {
            if (Loop) _pos %= len;
            else
            {
                _pos = len;
                Finished = true;
            }
        }
        return f;
    }

    private float Sample(byte[] data, double pos, int len)
    {
        if (data.Length < len) return 0;
        int i0 = (int)pos;
        if (i0 >= len) i0 = len - 1;
        int i1 = i0 + 1;
        if (i1 >= len) i1 = Loop ? 0 : len - 1;
        float t = (float)(pos - Math.Floor(pos));
        return (data[i0] + (data[i1] - data[i0]) * t) / 255f;
    }
}
