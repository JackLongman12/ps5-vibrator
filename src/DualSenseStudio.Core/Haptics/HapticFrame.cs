using DualSenseStudio.Core.Protocol;

namespace DualSenseStudio.Core.Haptics;

/// <summary>What a source wants the controller to do during one engine tick. Values are 0..1.</summary>
public struct HapticFrame
{
    public float Left;
    public float Right;
    public float LeftTrigger;
    public float RightTrigger;
    public int TriggerFrequency;

    /// <summary>When set, overrides the configured lightbar colour for this tick.</summary>
    public bool HasColor;
    public byte R, G, B;
    public MuteLed MuteLed;

    public readonly float Intensity => MathF.Max(Left, Right);

    public static HapticFrame Motors(float left, float right) => new() { Left = left, Right = right };
}

public readonly record struct HapticContext(double Time, double Dt, InputState Input);

/// <summary>Anything that can drive the controller: a pattern loop, the recorder, the touchpad, audio...</summary>
public interface IHapticSource
{
    string DisplayName { get; }
    HapticFrame GetFrame(in HapticContext ctx);
}
