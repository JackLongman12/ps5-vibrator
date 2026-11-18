using System.Text.Json.Serialization;

namespace DualSenseStudio.Core.Haptics;

public enum Channel
{
    Left = 0,
    Right = 1,
    LeftTrigger = 2,
    RightTrigger = 3,
}

/// <summary>
/// A vibration pattern: four channels sampled at a fixed rate (default 100 Hz), values 0-255.
/// Left = heavy rumble side, Right = light/sharp side, triggers = adaptive trigger vibration amplitude.
/// </summary>
public sealed class Pattern
{
    public const int DefaultSampleRate = 100;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Untitled";
    public string Category { get; set; } = "Custom";
    public string Description { get; set; } = "";
    public int SampleRate { get; set; } = DefaultSampleRate;
    public byte[] Left { get; set; } = [];
    public byte[] Right { get; set; } = [];
    public byte[] LeftTrigger { get; set; } = [];
    public byte[] RightTrigger { get; set; } = [];
    /// <summary>Frequency (Hz) used when the trigger channels make the triggers vibrate.</summary>
    public int TriggerFrequency { get; set; } = 35;
    public DateTime Created { get; set; } = DateTime.Now;
    public DateTime Modified { get; set; } = DateTime.Now;

    [JsonIgnore] public int Length => Left.Length;
    [JsonIgnore] public double DurationSeconds => SampleRate <= 0 ? 0 : (double)Length / SampleRate;
    [JsonIgnore] public bool HasTriggerData => LeftTrigger.Any(b => b > 0) || RightTrigger.Any(b => b > 0);

    public static Pattern Create(int samples, string name = "Untitled", int sampleRate = DefaultSampleRate) => new()
    {
        Name = name,
        SampleRate = sampleRate,
        Left = new byte[samples],
        Right = new byte[samples],
        LeftTrigger = new byte[samples],
        RightTrigger = new byte[samples],
    };

    public byte[] this[Channel c] => c switch
    {
        Channel.Left => Left,
        Channel.Right => Right,
        Channel.LeftTrigger => LeftTrigger,
        _ => RightTrigger,
    };

    public void SetChannel(Channel c, byte[] data)
    {
        switch (c)
        {
            case Channel.Left: Left = data; break;
            case Channel.Right: Right = data; break;
            case Channel.LeftTrigger: LeftTrigger = data; break;
            default: RightTrigger = data; break;
        }
    }

    public const int MinSampleRate = 10;
    public const int MaxSampleRate = 1000;
    /// <summary>Longest pattern accepted from disk (10 minutes).</summary>
    public const double MaxDurationSeconds = 600;
    public const int MaxNameLength = 100;
    public const int MaxDescriptionLength = 500;

    /// <summary>True when <paramref name="id"/> is a 32-char lowercase hex id (safe to use as a file name).</summary>
    public static bool IsValidId(string? id) =>
        id is { Length: 32 } && id.All(ch => ch is >= '0' and <= '9' or >= 'a' and <= 'f');

    /// <summary>
    /// Makes a pattern loaded from an untrusted file safe to use: valid id, bounded sizes and text,
    /// sane sample rate and trigger frequency, and all channels the same length as Left.
    /// Returns false if the pattern is unusable.
    /// </summary>
    public bool Sanitize()
    {
        Left ??= [];
        Right ??= [];
        LeftTrigger ??= [];
        RightTrigger ??= [];
        if (!IsValidId(Id)) Id = Guid.NewGuid().ToString("N");
        SampleRate = Math.Clamp(SampleRate, MinSampleRate, MaxSampleRate);
        int maxSamples = (int)(MaxDurationSeconds * SampleRate);
        if (Left.Length == 0 || Left.Length > maxSamples) return false;
        Name = Clip(string.IsNullOrWhiteSpace(Name) ? "Untitled" : Name.Trim(), MaxNameLength);
        Category = Clip(Category ?? "Custom", 40);
        Description = Clip(Description ?? "", MaxDescriptionLength);
        TriggerFrequency = Math.Clamp(TriggerFrequency, 1, 255);
        Normalize();
        return true;
    }

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max];

    /// <summary>Makes all channels the same length as Left (repairs older / hand-edited files).</summary>
    public void Normalize()
    {
        int n = Left.Length;
        if (Right.Length != n) Right = Resize(Right, n);
        if (LeftTrigger.Length != n) LeftTrigger = Resize(LeftTrigger, n);
        if (RightTrigger.Length != n) RightTrigger = Resize(RightTrigger, n);
        if (SampleRate <= 0) SampleRate = DefaultSampleRate;
    }

    private static byte[] Resize(byte[] a, int n)
    {
        var r = new byte[n];
        Array.Copy(a, r, Math.Min(a.Length, n));
        return r;
    }

    public Pattern Clone(bool newId = false)
    {
        var p = (Pattern)MemberwiseClone();
        p.Left = (byte[])Left.Clone();
        p.Right = (byte[])Right.Clone();
        p.LeftTrigger = (byte[])LeftTrigger.Clone();
        p.RightTrigger = (byte[])RightTrigger.Clone();
        if (newId) p.Id = Guid.NewGuid().ToString("N");
        return p;
    }

    public override string ToString() => Name;
}
