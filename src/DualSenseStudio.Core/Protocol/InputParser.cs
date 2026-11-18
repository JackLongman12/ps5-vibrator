using System.Buffers.Binary;

namespace DualSenseStudio.Core.Protocol;

/// <summary>
/// Parses DualSense input reports.
/// USB: report 0x01 (64 bytes). Bluetooth: report 0x31 (78 bytes), or the reduced
/// report 0x01 that BT controllers send until feature report 0x05 has been read.
/// Layout follows the Linux hid-playstation driver.
/// </summary>
public static class InputParser
{
    public static bool TryParse(ReadOnlySpan<byte> r, bool bluetooth, InputState s)
    {
        if (r.Length < 10) return false;

        if (!bluetooth && r[0] == 0x01 && r.Length >= 1 + 53)
        {
            ParseFull(r, 1, s);
            return true;
        }
        if (bluetooth && r[0] == 0x31 && r.Length >= 2 + 53)
        {
            ParseFull(r, 2, s);
            return true;
        }
        if (bluetooth && r[0] == 0x01)
        {
            ParseSimple(r, s);
            return true;
        }
        return false;
    }

    private static void ParseFull(ReadOnlySpan<byte> r, int b, InputState s)
    {
        s.LeftX = r[b + 0];
        s.LeftY = r[b + 1];
        s.RightX = r[b + 2];
        s.RightY = r[b + 3];
        s.L2 = r[b + 4];
        s.R2 = r[b + 5];
        s.Buttons = DecodeButtons(r[b + 7], r[b + 8], r[b + 9]);

        s.GyroX = BinaryPrimitives.ReadInt16LittleEndian(r[(b + 15)..]);
        s.GyroY = BinaryPrimitives.ReadInt16LittleEndian(r[(b + 17)..]);
        s.GyroZ = BinaryPrimitives.ReadInt16LittleEndian(r[(b + 19)..]);
        s.AccelX = BinaryPrimitives.ReadInt16LittleEndian(r[(b + 21)..]);
        s.AccelY = BinaryPrimitives.ReadInt16LittleEndian(r[(b + 23)..]);
        s.AccelZ = BinaryPrimitives.ReadInt16LittleEndian(r[(b + 25)..]);

        s.Touch0 = ParseTouch(r[(b + 32)..]);
        s.Touch1 = ParseTouch(r[(b + 36)..]);

        byte status = r[b + 52];
        int level = status & 0x0F;
        int charge = (status >> 4) & 0x0F;
        s.BatteryFull = charge == 2;
        s.Charging = charge == 1;
        s.BatteryPercent = s.BatteryFull ? 100 : Math.Min(level * 10 + 5, 100);
        s.FullReport = true;
    }

    private static void ParseSimple(ReadOnlySpan<byte> r, InputState s)
    {
        s.LeftX = r[1];
        s.LeftY = r[2];
        s.RightX = r[3];
        s.RightY = r[4];
        s.Buttons = DecodeButtons(r[5], r[6], (byte)(r[7] & 0x03));
        s.L2 = r[8];
        s.R2 = r[9];
        s.FullReport = false;
    }

    private static TouchPoint ParseTouch(ReadOnlySpan<byte> p)
    {
        bool active = (p[0] & 0x80) == 0;
        byte id = (byte)(p[0] & 0x7F);
        int x = p[1] | ((p[2] & 0x0F) << 8);
        int y = ((p[2] & 0xF0) >> 4) | (p[3] << 4);
        return new TouchPoint(active, id, x, y);
    }

    private static Buttons DecodeButtons(byte b0, byte b1, byte b2)
    {
        Buttons btn = Buttons.None;
        switch (b0 & 0x0F)
        {
            case 0: btn |= Buttons.DPadUp; break;
            case 1: btn |= Buttons.DPadUp | Buttons.DPadRight; break;
            case 2: btn |= Buttons.DPadRight; break;
            case 3: btn |= Buttons.DPadDown | Buttons.DPadRight; break;
            case 4: btn |= Buttons.DPadDown; break;
            case 5: btn |= Buttons.DPadDown | Buttons.DPadLeft; break;
            case 6: btn |= Buttons.DPadLeft; break;
            case 7: btn |= Buttons.DPadUp | Buttons.DPadLeft; break;
        }
        if ((b0 & 0x10) != 0) btn |= Buttons.Square;
        if ((b0 & 0x20) != 0) btn |= Buttons.Cross;
        if ((b0 & 0x40) != 0) btn |= Buttons.Circle;
        if ((b0 & 0x80) != 0) btn |= Buttons.Triangle;
        if ((b1 & 0x01) != 0) btn |= Buttons.L1;
        if ((b1 & 0x02) != 0) btn |= Buttons.R1;
        if ((b1 & 0x04) != 0) btn |= Buttons.L2;
        if ((b1 & 0x08) != 0) btn |= Buttons.R2;
        if ((b1 & 0x10) != 0) btn |= Buttons.Create;
        if ((b1 & 0x20) != 0) btn |= Buttons.Options;
        if ((b1 & 0x40) != 0) btn |= Buttons.L3;
        if ((b1 & 0x80) != 0) btn |= Buttons.R3;
        if ((b2 & 0x01) != 0) btn |= Buttons.PS;
        if ((b2 & 0x02) != 0) btn |= Buttons.Touchpad;
        if ((b2 & 0x04) != 0) btn |= Buttons.Mute;
        return btn;
    }
}
