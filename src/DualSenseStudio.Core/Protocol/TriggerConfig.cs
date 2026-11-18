namespace DualSenseStudio.Core.Protocol;

public enum TriggerMode
{
    Off,
    /// <summary>Constant resistance from a start zone to the end of travel.</summary>
    Resistance,
    /// <summary>Resistance that ramps between two strengths across a zone range.</summary>
    Slope,
    /// <summary>A "snap" between two zones, like a trigger break.</summary>
    Weapon,
    /// <summary>The trigger motor itself vibrates from a start zone onward.</summary>
    Vibration,
}

/// <summary>
/// Adaptive trigger effect. Zones run 0 (released) .. 9 (fully pressed); strengths 1..8.
/// Encoding follows the community-documented DualSense trigger effect format.
/// </summary>
public sealed record TriggerConfig(
    TriggerMode Mode,
    int Start = 0,
    int End = 9,
    int Strength = 4,
    int EndStrength = 8,
    int Frequency = 30)
{
    public static readonly TriggerConfig Off = new(TriggerMode.Off);

    public static TriggerConfig Vibrate(float amplitude01, int frequency)
    {
        int amp = Math.Clamp((int)MathF.Round(amplitude01 * 8f), 0, 8);
        return amp == 0 ? Off : new TriggerConfig(TriggerMode.Vibration, Start: 0, Strength: amp, Frequency: frequency);
    }

    /// <summary>Writes the 11-byte effect block.</summary>
    public void Encode(Span<byte> dst)
    {
        dst[..11].Clear();
        switch (Mode)
        {
            case TriggerMode.Resistance:
            {
                int start = Math.Clamp(Start, 0, 9);
                int s = Math.Clamp(Strength, 0, 8);
                if (s == 0) { EncodeOff(dst); return; }
                Span<int> strengths = stackalloc int[10];
                for (int i = start; i < 10; i++) strengths[i] = s;
                EncodeMultiFeedback(dst, strengths);
                return;
            }
            case TriggerMode.Slope:
            {
                int start = Math.Clamp(Start, 0, 8);
                int end = Math.Clamp(End, start + 1, 9);
                int s0 = Math.Clamp(Strength, 1, 8);
                int s1 = Math.Clamp(EndStrength, 1, 8);
                Span<int> strengths = stackalloc int[10];
                for (int i = start; i < 10; i++)
                {
                    float t = i >= end ? 1f : (i - start) / (float)(end - start);
                    strengths[i] = (int)MathF.Round(s0 + (s1 - s0) * t);
                }
                EncodeMultiFeedback(dst, strengths);
                return;
            }
            case TriggerMode.Weapon:
            {
                int start = Math.Clamp(Start, 2, 7);
                int end = Math.Clamp(End, start + 1, 8);
                int s = Math.Clamp(Strength, 0, 8);
                if (s == 0) { EncodeOff(dst); return; }
                ushort zones = (ushort)((1 << start) | (1 << end));
                dst[0] = 0x25;
                dst[1] = (byte)(zones & 0xFF);
                dst[2] = (byte)(zones >> 8);
                dst[3] = (byte)(s - 1);
                return;
            }
            case TriggerMode.Vibration:
            {
                int start = Math.Clamp(Start, 0, 9);
                int amp = Math.Clamp(Strength, 0, 8);
                int freq = Math.Clamp(Frequency, 0, 255);
                if (amp == 0 || freq == 0) { EncodeOff(dst); return; }
                uint ampZones = 0;
                ushort active = 0;
                for (int i = start; i < 10; i++)
                {
                    ampZones |= (uint)((amp - 1) & 0x07) << (3 * i);
                    active |= (ushort)(1 << i);
                }
                dst[0] = 0x26;
                dst[1] = (byte)(active & 0xFF);
                dst[2] = (byte)(active >> 8);
                dst[3] = (byte)(ampZones & 0xFF);
                dst[4] = (byte)((ampZones >> 8) & 0xFF);
                dst[5] = (byte)((ampZones >> 16) & 0xFF);
                dst[6] = (byte)((ampZones >> 24) & 0xFF);
                dst[9] = (byte)freq;
                return;
            }
            default:
                EncodeOff(dst);
                return;
        }
    }

    private static void EncodeOff(Span<byte> dst)
    {
        dst[..11].Clear();
        dst[0] = 0x05;
    }

    private static void EncodeMultiFeedback(Span<byte> dst, ReadOnlySpan<int> strengths)
    {
        uint force = 0;
        ushort active = 0;
        for (int i = 0; i < 10; i++)
        {
            int s = Math.Clamp(strengths[i], 0, 8);
            if (s <= 0) continue;
            force |= (uint)((s - 1) & 0x07) << (3 * i);
            active |= (ushort)(1 << i);
        }
        if (active == 0) { EncodeOff(dst); return; }
        dst[0] = 0x21;
        dst[1] = (byte)(active & 0xFF);
        dst[2] = (byte)(active >> 8);
        dst[3] = (byte)(force & 0xFF);
        dst[4] = (byte)((force >> 8) & 0xFF);
        dst[5] = (byte)((force >> 16) & 0xFF);
        dst[6] = (byte)((force >> 24) & 0xFF);
    }
}
