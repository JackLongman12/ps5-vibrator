namespace DualSenseStudio.Core.Protocol;

public enum MuteLed : byte { Off = 0, On = 1, Pulse = 2 }

/// <summary>Everything we can tell the controller to do in one output report.</summary>
public sealed class OutputState
{
    /// <summary>Heavy / low-frequency side (left grip).</summary>
    public byte MotorLeft;
    /// <summary>Light / high-frequency side (right grip).</summary>
    public byte MotorRight;

    public TriggerConfig LeftTrigger = TriggerConfig.Off;
    public TriggerConfig RightTrigger = TriggerConfig.Off;

    public byte LightR, LightG, LightB;
    /// <summary>Bits 0-4 = the five player LEDs (left to right). Bit 5 = change instantly.</summary>
    public byte PlayerLeds;
    /// <summary>0 = high, 1 = medium, 2 = low.</summary>
    public byte LedBrightness;
    public MuteLed MuteLed;

    /// <summary>Use the newer "compatible vibration v2" rumble path (firmware update version 2.21+).</summary>
    public bool UseRumbleV2 = true;
}

public static class OutputBuilder
{
    public const int CommonLength = 47;
    public const int UsbReportLength = 48;
    public const int BtReportLength = 78;

    /// <summary>Fills the 47-byte common block shared by USB and BT reports.</summary>
    public static void BuildCommon(OutputState s, Span<byte> c, bool releaseBootLightbar)
    {
        c[..CommonLength].Clear();

        byte flag0 = 0x04 | 0x08; // right + left trigger effects
        byte flag2 = 0;
        flag0 |= 0x02; // haptics select: classic rumble emulation
        if (s.UseRumbleV2) flag2 |= 0x04; // compatible vibration v2
        else flag0 |= 0x01;               // compatible vibration v1

        c[0] = flag0;
        c[1] = 0x01 | 0x04 | 0x10; // mic mute LED, lightbar, player LEDs
        c[2] = s.MotorRight;
        c[3] = s.MotorLeft;
        c[8] = (byte)s.MuteLed;

        s.RightTrigger.Encode(c.Slice(10, 11));
        s.LeftTrigger.Encode(c.Slice(21, 11));

        if (releaseBootLightbar)
        {
            flag2 |= 0x02;  // lightbar setup control enable
            c[41] = 0x02;   // light out: fade the default blue so we own the lightbar
        }
        c[38] = flag2;
        c[42] = s.LedBrightness;
        c[43] = s.PlayerLeds;
        c[44] = s.LightR;
        c[45] = s.LightG;
        c[46] = s.LightB;
    }

    /// <summary>Wraps a common block into a complete report for the transport.</summary>
    public static void Assemble(ReadOnlySpan<byte> common, bool bluetooth, byte sequence, Span<byte> report)
    {
        report.Clear();
        if (!bluetooth)
        {
            report[0] = 0x02;
            common[..CommonLength].CopyTo(report[1..]);
            return;
        }

        report[0] = 0x31;
        report[1] = (byte)((sequence & 0x0F) << 4);
        report[2] = 0x10;
        common[..CommonLength].CopyTo(report[3..]);
        uint crc = Crc32.Compute(0xA2, report[..74]);
        report[74] = (byte)crc;
        report[75] = (byte)(crc >> 8);
        report[76] = (byte)(crc >> 16);
        report[77] = (byte)(crc >> 24);
    }
}
