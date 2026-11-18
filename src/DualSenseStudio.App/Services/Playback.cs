using DualSenseStudio.Core.Haptics;

namespace DualSenseStudio.Services;

/// <summary>Owns "what is playing right now" and the shared player controls.</summary>
public sealed class Playback
{
    private readonly HapticsEngine _engine;
    private readonly AppSettings _settings;

    public Playback(HapticsEngine engine, AppSettings settings)
    {
        _engine = engine;
        _settings = settings;
    }

    public IHapticSource? Active => _engine.Source;
    public PatternSource? CurrentPattern => _engine.Source as PatternSource;
    public DateTime? StartedAt { get; private set; }

    /// <summary>Raised on the UI thread whenever the active source changes.</summary>
    public event Action? Changed;

    public double Speed
    {
        get => _settings.Speed;
        set { _settings.Speed = value; if (CurrentPattern is { } p) p.Speed = value; }
    }

    public double Intensity
    {
        get => _settings.Intensity;
        set { _settings.Intensity = value; if (CurrentPattern is { } p) p.Intensity = value; }
    }

    public double Balance
    {
        get => _settings.Balance;
        set { _settings.Balance = value; if (CurrentPattern is { } p) p.Balance = value; }
    }

    public bool TriggerChannels
    {
        get => _settings.PlayTriggerChannels;
        set { _settings.PlayTriggerChannels = value; if (CurrentPattern is { } p) p.TriggersEnabled = value; }
    }

    public PatternSource Play(Pattern pattern, bool loop = true)
    {
        var src = new PatternSource(pattern)
        {
            Speed = Speed,
            Intensity = Intensity,
            Balance = Balance,
            TriggersEnabled = TriggerChannels,
            Loop = loop,
        };
        SetSource(src);
        return src;
    }

    public void SetSource(IHapticSource? source)
    {
        _engine.Source = source;
        StartedAt = source == null ? null : DateTime.Now;
        Changed?.Invoke();
    }

    public void Stop() => SetSource(null);
}
