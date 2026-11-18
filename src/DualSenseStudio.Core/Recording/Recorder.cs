using DualSenseStudio.Core.Haptics;
using DualSenseStudio.Core.Protocol;

namespace DualSenseStudio.Core.Recording;

public enum RecordMapping
{
    /// <summary>How far you press = how hard it vibrates (both sides).</summary>
    Pressure,
    /// <summary>L2 drives the left side, R2 drives the right side.</summary>
    DualChannel,
    /// <summary>Fast presses make sharp spikes, slow presses make gentle swells.</summary>
    VelocityAccents,
    /// <summary>Light press = smooth hum, medium = pulse train, full = rough buzz.</summary>
    Zones,
}

public enum RecorderState { Idle, CountIn, Recording }

public sealed class RecorderOptions
{
    public RecordMapping Mapping { get; set; } = RecordMapping.Pressure;
    /// <summary>Fixed take length in seconds. 0 = free (stop manually, max <see cref="Recorder.MaxFreeSeconds"/>).</summary>
    public double LengthSeconds { get; set; } = 8;
    public bool CountIn { get; set; } = true;
    public bool FaceButtonHits { get; set; } = true;
    /// <summary>Also record trigger pressure into the trigger-vibration channels.</summary>
    public bool RecordTriggerVibration { get; set; }
    /// <summary>Response curve: &lt;1 = more sensitive to light presses, &gt;1 = needs firmer presses.</summary>
    public double Curve { get; set; } = 1.0;
    public int TriggerFrequency { get; set; } = 35;
}

/// <summary>
/// Records a performance on the triggers (and optionally face buttons) into a pattern.
/// While active it is the engine's source, so you feel what is being captured in real time.
/// Runs on the engine thread; the UI reads snapshots.
/// </summary>
public sealed class Recorder : IHapticSource
{
    public const double MaxFreeSeconds = 120;
    private const int Rate = Pattern.DefaultSampleRate;
    private const double CountInBeat = 0.6;
    private const int CountInBeats = 3;

    private readonly object _lock = new();
    private readonly List<byte> _l = new(), _r = new(), _lt = new(), _rt = new();
    private readonly List<Hit> _hits = new();
    private readonly Random _rng = new();
    private RecorderOptions _opt = new();
    private Pattern? _overdubBase;
    private double _startTime = double.NaN;
    private double _recordStart;
    private double _prevValue, _prevL, _prevR;
    private double _accent, _accentL, _accentR;
    private double _zonePhase;
    private float _buzz;
    private Buttons _prevButtons;
    private volatile RecorderState _state = RecorderState.Idle;
    private volatile bool _stopRequested;
    private HapticFrame _live;

    public string DisplayName => "Recorder";
    public RecorderState State => _state;
    public RecorderOptions Options => _opt;

    /// <summary>Seconds into the take (or count-in, negative).</summary>
    public double Elapsed { get; private set; }
    public double TargetSeconds => _overdubBase?.DurationSeconds ?? _opt.LengthSeconds;
    public HapticFrame LiveFrame => _live;

    /// <summary>Raised on the engine thread when a take finishes.</summary>
    public event Action<Pattern>? Completed;

    public void Begin(RecorderOptions options, Pattern? overdubOnto = null)
    {
        lock (_lock)
        {
            _opt = options;
            _overdubBase = overdubOnto?.Clone();
            _l.Clear(); _r.Clear(); _lt.Clear(); _rt.Clear();
            _hits.Clear();
            _startTime = double.NaN;
            _prevValue = _prevL = _prevR = 0;
            _accent = _accentL = _accentR = 0;
            _stopRequested = false;
            _prevButtons = Buttons.None;
            Elapsed = options.CountIn ? -CountInBeats * CountInBeat : 0;
            _state = options.CountIn ? RecorderState.CountIn : RecorderState.Recording;
        }
    }

    /// <summary>Finish the take now (free mode, or early stop).</summary>
    public void RequestStop() => _stopRequested = true;

    public void Cancel()
    {
        lock (_lock)
        {
            _state = RecorderState.Idle;
            _l.Clear(); _r.Clear(); _lt.Clear(); _rt.Clear();
        }
    }

    /// <summary>Copy of what has been captured so far, for live display.</summary>
    public Pattern Snapshot()
    {
        lock (_lock) return BuildPattern(false);
    }

    public HapticFrame GetFrame(in HapticContext ctx)
    {
        lock (_lock)
        {
            if (double.IsNaN(_startTime))
            {
                _startTime = ctx.Time;
                _recordStart = ctx.Time + (_opt.CountIn ? CountInBeats * CountInBeat : 0);
            }

            switch (_state)
            {
                case RecorderState.CountIn:
                {
                    Elapsed = ctx.Time - _recordStart;
                    if (Elapsed >= 0)
                    {
                        _state = RecorderState.Recording;
                        goto case RecorderState.Recording;
                    }
                    double sinceStart = ctx.Time - _startTime;
                    double beatPos = sinceStart % CountInBeat;
                    bool click = beatPos < 0.07;
                    _live = new HapticFrame
                    {
                        Left = click ? 0.6f : 0,
                        Right = click ? 0.6f : 0,
                        HasColor = true,
                        R = (byte)(click ? 255 : 60),
                        G = (byte)(click ? 170 : 40),
                        B = 0,
                        MuteLed = MuteLed.Pulse,
                    };
                    _prevButtons = ctx.Input.Buttons;
                    return _live;
                }
                case RecorderState.Recording:
                {
                    Elapsed = ctx.Time - _recordStart;
                    var f = Map(ctx);

                    // Overdub: you feel the existing take under your new layer.
                    var heard = f;
                    int expected = (int)(Elapsed * Rate);
                    if (_overdubBase != null && _overdubBase.Length > 0)
                    {
                        int i = Math.Clamp(expected, 0, _overdubBase.Length - 1);
                        heard.Left = MathF.Max(f.Left, _overdubBase.Left[i] / 255f);
                        heard.Right = MathF.Max(f.Right, _overdubBase.Right[i] / 255f);
                    }

                    // Append samples time-accurately (catches up if a tick was late).
                    while (_l.Count <= expected)
                    {
                        _l.Add(ToByte(f.Left));
                        _r.Add(ToByte(f.Right));
                        _lt.Add(ToByte(f.LeftTrigger));
                        _rt.Add(ToByte(f.RightTrigger));
                    }

                    double target = TargetSeconds > 0 ? TargetSeconds : MaxFreeSeconds;
                    if (_stopRequested || Elapsed >= target)
                    {
                        _state = RecorderState.Idle;
                        var p = BuildPattern(true);
                        _live = default;
                        ThreadPool.QueueUserWorkItem(_ => Completed?.Invoke(p));
                        return default;
                    }

                    heard.HasColor = true;
                    heard.R = 255;
                    heard.G = (byte)(30 * (1 - f.Intensity));
                    heard.B = (byte)(30 * (1 - f.Intensity));
                    heard.MuteLed = MuteLed.On;
                    _live = heard;
                    return heard;
                }
                default:
                    _live = default;
                    return default;
            }
        }
    }

    private HapticFrame Map(in HapticContext ctx)
    {
        var input = ctx.Input;
        double dt = Math.Clamp(ctx.Dt, 0.001, 0.1);
        double curve = Math.Clamp(_opt.Curve, 0.3, 3);
        double l2 = Math.Pow(input.L2Value, curve);
        double r2 = Math.Pow(input.R2Value, curve);
        double v = Math.Max(l2, r2);
        float left, right;

        switch (_opt.Mapping)
        {
            case RecordMapping.DualChannel:
                left = (float)l2;
                right = (float)r2;
                break;

            case RecordMapping.VelocityAccents:
            {
                double decay = Math.Pow(0.06, dt); // ~ -94% per second
                double vel = (v - _prevValue) / dt;
                _accent = Math.Max(_accent * decay, Math.Clamp(vel / 8.0, 0, 1));
                double velL = (l2 - _prevL) / dt, velR = (r2 - _prevR) / dt;
                _accentL = Math.Max(_accentL * decay, Math.Clamp(velL / 8.0, 0, 1));
                _accentR = Math.Max(_accentR * decay, Math.Clamp(velR / 8.0, 0, 1));
                left = (float)Math.Max(v * 0.3, Math.Max(_accentL, _accent * 0.6));
                right = (float)Math.Max(v * 0.2, Math.Max(_accentR, _accent));
                break;
            }

            case RecordMapping.Zones:
            {
                if (v < 0.05)
                {
                    left = right = 0;
                }
                else if (v < 0.4)
                {
                    // Smooth hum, gets stronger through the zone.
                    left = (float)(0.15 + v * 1.2);
                    right = 0.05f;
                }
                else if (v < 0.85)
                {
                    // Pulse train: rate rises with pressure (6..14 Hz).
                    double hz = 6 + (v - 0.4) / 0.45 * 8;
                    _zonePhase = (_zonePhase + hz * dt) % 1.0;
                    bool on = _zonePhase < 0.5;
                    left = on ? 0.75f : 0.1f;
                    right = on ? 0.6f : 0.05f;
                }
                else
                {
                    // Rough buzz: random texture at full strength.
                    _buzz = 0.65f + 0.35f * (float)_rng.NextDouble();
                    left = _buzz;
                    right = 1.0f - (_buzz - 0.65f);
                }
                break;
            }

            default:
                left = right = (float)v;
                break;
        }

        _prevValue = v;
        _prevL = l2;
        _prevR = r2;

        // Face-button hits are layered on top of any mapping.
        if (_opt.FaceButtonHits)
        {
            var pressed = input.Buttons & ~_prevButtons;
            double t = ctx.Time;
            if (pressed.HasFlag(Buttons.Cross)) _hits.Add(new Hit(t, 0.08, 1.0, 1.0, HitShape.Flat));
            if (pressed.HasFlag(Buttons.Square)) _hits.Add(new Hit(t, 0.15, 0.9, 0.0, HitShape.Decay));
            if (pressed.HasFlag(Buttons.Circle)) _hits.Add(new Hit(t, 0.05, 0.0, 0.85, HitShape.Flat));
            if (pressed.HasFlag(Buttons.Triangle)) _hits.Add(new Hit(t, 0.4, 0.8, 0.6, HitShape.Swell));
            if (pressed.HasFlag(Buttons.L1)) _hits.Add(new Hit(t, 0.04, 0.5, 0.0, HitShape.Flat));
            if (pressed.HasFlag(Buttons.R1)) _hits.Add(new Hit(t, 0.04, 0.0, 0.5, HitShape.Flat));
            _hits.RemoveAll(h => t > h.Start + h.Duration);
            foreach (var h in _hits)
            {
                float e = h.Envelope(t);
                left = MathF.Max(left, (float)(h.Left * e));
                right = MathF.Max(right, (float)(h.Right * e));
            }
        }
        _prevButtons = input.Buttons;

        var f = new HapticFrame
        {
            Left = Math.Clamp(left, 0, 1),
            Right = Math.Clamp(right, 0, 1),
            TriggerFrequency = _opt.TriggerFrequency,
        };
        if (_opt.RecordTriggerVibration)
        {
            f.LeftTrigger = (float)l2;
            f.RightTrigger = (float)r2;
        }
        return f;
    }

    private Pattern BuildPattern(bool final)
    {
        int n = _l.Count;
        var take = Pattern.Create(n, final ? $"Take {DateTime.Now:HH.mm.ss}" : "Recording");
        _l.CopyTo(take.Left);
        _r.CopyTo(take.Right);
        _lt.CopyTo(take.LeftTrigger);
        _rt.CopyTo(take.RightTrigger);
        take.TriggerFrequency = _opt.TriggerFrequency;
        take.Category = "Recording";

        if (_overdubBase == null) return take;

        // Pad / cut the new layer to the base length, then merge.
        var padded = Pattern.Create(_overdubBase.Length);
        Array.Copy(take.Left, padded.Left, Math.Min(n, padded.Length));
        Array.Copy(take.Right, padded.Right, Math.Min(n, padded.Length));
        Array.Copy(take.LeftTrigger, padded.LeftTrigger, Math.Min(n, padded.Length));
        Array.Copy(take.RightTrigger, padded.RightTrigger, Math.Min(n, padded.Length));
        var merged = PatternTools.Overdub(_overdubBase, padded);
        if (final) merged.Name = _overdubBase.Name;
        return merged;
    }

    private static byte ToByte(float v) => (byte)Math.Clamp((int)MathF.Round(v * 255f), 0, 255);

    private enum HitShape { Flat, Decay, Swell }

    private readonly record struct Hit(double Start, double Duration, double Left, double Right, HitShape Shape)
    {
        public float Envelope(double t)
        {
            double x = (t - Start) / Duration;
            if (x < 0 || x > 1) return 0;
            return Shape switch
            {
                HitShape.Decay => (float)(1 - x),
                HitShape.Swell => (float)Math.Sin(Math.PI * x),
                _ => 1f,
            };
        }
    }
}
