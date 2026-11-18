using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DualSenseStudio.Core.Protocol;

namespace DualSenseStudio.Core.Tests;

public class Crc32Tests
{
    [Fact]
    public void MatchesStandardCheckValue()
    {
        // CRC-32 of "123456789" is 0xCBF43926; feed the first byte as the seed.
        Assert.Equal(0xCBF43926u, Crc32.Compute((byte)'1', Encoding.ASCII.GetBytes("23456789")));
    }
}

public class OutputBuilderTests
{
    private static byte[] Usb(OutputState s, bool release = false)
    {
        var common = new byte[OutputBuilder.CommonLength];
        OutputBuilder.BuildCommon(s, common, release);
        var report = new byte[OutputBuilder.UsbReportLength];
        OutputBuilder.Assemble(common, bluetooth: false, 0, report);
        return report;
    }

    [Fact]
    public void UsbReportLayout()
    {
        var r = Usb(new OutputState { MotorLeft = 10, MotorRight = 20, LightR = 1, LightG = 2, LightB = 3, PlayerLeds = 0x05, MuteLed = MuteLed.Pulse });
        Assert.Equal(0x02, r[0]);
        Assert.Equal(0x02 | 0x04 | 0x08, r[1]);      // haptics select + both trigger effects (v2 rumble)
        Assert.Equal(0x01 | 0x04 | 0x10, r[2]);      // mute LED, lightbar, player LEDs
        Assert.Equal(20, r[3]);                       // right motor
        Assert.Equal(10, r[4]);                       // left motor
        Assert.Equal(2, r[1 + 8]);                    // mute LED pulse
        Assert.Equal(0x05, r[1 + 10]);                // right trigger off
        Assert.Equal(0x05, r[1 + 21]);                // left trigger off
        Assert.Equal(0x04, r[1 + 38]);                // compatible vibration v2
        Assert.Equal(0x05, r[1 + 43]);
        Assert.Equal(new byte[] { 1, 2, 3 }, r[(1 + 44)..(1 + 47)]);
    }

    [Fact]
    public void LegacyRumbleUsesFlag0()
    {
        var r = Usb(new OutputState { UseRumbleV2 = false });
        Assert.Equal(0x01, r[1] & 0x01);
        Assert.Equal(0, r[1 + 38] & 0x04);
    }

    [Fact]
    public void FirstReportReleasesBootLightbar()
    {
        var r = Usb(new OutputState(), release: true);
        Assert.Equal(0x02, r[1 + 38] & 0x02);
        Assert.Equal(0x02, r[1 + 41]);
    }

    [Fact]
    public void BluetoothReportHasHeaderAndValidCrc()
    {
        var s = new OutputState { MotorLeft = 200, LightG = 99 };
        var common = new byte[OutputBuilder.CommonLength];
        OutputBuilder.BuildCommon(s, common, false);
        var report = new byte[547]; // Windows pads BT output reports to the max report length
        OutputBuilder.Assemble(common, bluetooth: true, sequence: 5, report);

        Assert.Equal(0x31, report[0]);
        Assert.Equal(5 << 4, report[1]);
        Assert.Equal(0x10, report[2]);
        Assert.Equal(200, report[3 + 3]);
        Assert.Equal(99, report[3 + 45]);
        uint crc = BitConverter.ToUInt32(report, 74);
        Assert.Equal(Crc32.Compute(0xA2, report.AsSpan(0, 74)), crc);
        Assert.All(report[78..], b => Assert.Equal(0, b));
    }
}

public class TriggerConfigTests
{
    private static byte[] Encode(TriggerConfig c)
    {
        var b = new byte[11];
        c.Encode(b);
        return b;
    }

    [Fact]
    public void Off() => Assert.Equal(new byte[] { 0x05, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, Encode(TriggerConfig.Off));

    [Fact]
    public void FullResistance() =>
        Assert.Equal(new byte[] { 0x21, 0xFF, 0x03, 0xFF, 0xFF, 0xFF, 0x3F, 0, 0, 0, 0 },
            Encode(new TriggerConfig(TriggerMode.Resistance, Start: 0, Strength: 8)));

    [Fact]
    public void Weapon() =>
        Assert.Equal(new byte[] { 0x25, 0x48, 0x00, 0x07, 0, 0, 0, 0, 0, 0, 0 },
            Encode(new TriggerConfig(TriggerMode.Weapon, Start: 3, End: 6, Strength: 8)));

    [Fact]
    public void Vibration() =>
        Assert.Equal(new byte[] { 0x26, 0xFF, 0x03, 0xFF, 0xFF, 0xFF, 0x3F, 0, 0, 40, 0 },
            Encode(new TriggerConfig(TriggerMode.Vibration, Start: 0, Strength: 8, Frequency: 40)));

    [Fact]
    public void SlopeRampsStrength()
    {
        var b = Encode(new TriggerConfig(TriggerMode.Slope, Start: 0, End: 9, Strength: 1, EndStrength: 8));
        Assert.Equal(0x21, b[0]);
        uint force = BitConverter.ToUInt32(b, 3);
        Assert.Equal(0u, force & 0x7);              // zone 0 -> strength 1
        Assert.Equal(7u, (force >> 27) & 0x7);      // zone 9 -> strength 8
    }

    [Theory]
    [InlineData(-50, 99, 99, 99, 9999)]
    [InlineData(0, 0, 0, 0, 0)]
    public void OutOfRangeValuesNeverThrowAndStayWellFormed(int start, int end, int strength, int endStrength, int freq)
    {
        foreach (TriggerMode mode in Enum.GetValues<TriggerMode>())
        {
            var b = Encode(new TriggerConfig(mode, start, end, strength, endStrength, freq));
            Assert.Contains(b[0], new byte[] { 0x05, 0x21, 0x25, 0x26 });
        }
    }

    [Fact]
    public void VibrateHelperTurnsOffAtZero() => Assert.Equal(TriggerConfig.Off, TriggerConfig.Vibrate(0, 40));

    [Fact]
    public void RoundTripsThroughJson()
    {
        var opts = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
        var c = new TriggerConfig(TriggerMode.Slope, 2, 7, 3, 8, 30);
        Assert.Equal(c, JsonSerializer.Deserialize<TriggerConfig>(JsonSerializer.Serialize(c, opts), opts));
    }
}

public class InputParserTests
{
    private static byte[] UsbReport()
    {
        var r = new byte[64];
        r[0] = 0x01;
        r[1] = 10;            // LX
        r[2] = 20;            // LY
        r[5] = 100;           // L2
        r[6] = 200;           // R2
        r[8] = 0x08 | 0x20;   // hat neutral + cross
        r[9] = 0x20;          // options
        r[10] = 0x04;         // mute
        BitConverter.GetBytes((short)8192).CopyTo(r, 1 + 23); // accel Y
        // touch 0: active id 1 at (1000, 500); touch 1 inactive
        r[1 + 32] = 0x01;
        r[1 + 33] = 1000 & 0xFF;
        r[1 + 34] = (byte)(((1000 >> 8) & 0x0F) | ((500 & 0x0F) << 4));
        r[1 + 35] = 500 >> 4;
        r[1 + 36] = 0x80;
        r[1 + 52] = 0x15;     // charging, level 5
        return r;
    }

    [Fact]
    public void ParsesUsbFullReport()
    {
        var s = new InputState();
        Assert.True(InputParser.TryParse(UsbReport(), bluetooth: false, s));
        Assert.Equal(10, s.LeftX);
        Assert.Equal(20, s.LeftY);
        Assert.Equal(100, s.L2);
        Assert.Equal(200, s.R2);
        Assert.Equal(Buttons.Cross | Buttons.Options | Buttons.Mute, s.Buttons);
        Assert.Equal(8192, s.AccelY);
        Assert.True(s.Touch0.Active);
        Assert.Equal(1, s.Touch0.Id);
        Assert.Equal(1000, s.Touch0.X);
        Assert.Equal(500, s.Touch0.Y);
        Assert.False(s.Touch1.Active);
        Assert.True(s.Charging);
        Assert.Equal(55, s.BatteryPercent);
        Assert.True(s.FullReport);
    }

    [Fact]
    public void ParsesBluetoothFullReportWithOffset()
    {
        var usb = UsbReport();
        var bt = new byte[78];
        bt[0] = 0x31;
        Array.Copy(usb, 1, bt, 2, 63);
        var s = new InputState();
        Assert.True(InputParser.TryParse(bt, bluetooth: true, s));
        Assert.Equal(100, s.L2);
        Assert.Equal(200, s.R2);
        Assert.Equal(1000, s.Touch0.X);
    }

    [Fact]
    public void ParsesBluetoothSimpleReport()
    {
        var r = new byte[78];
        r[0] = 0x01;
        r[5] = 0x01;  // hat up-right
        r[8] = 77;
        r[9] = 88;
        var s = new InputState();
        Assert.True(InputParser.TryParse(r, bluetooth: true, s));
        Assert.Equal(77, s.L2);
        Assert.Equal(88, s.R2);
        Assert.Equal(Buttons.DPadUp | Buttons.DPadRight, s.Buttons);
        Assert.False(s.FullReport);
    }

    [Fact]
    public void RejectsShortOrUnknownReports()
    {
        var s = new InputState();
        Assert.False(InputParser.TryParse(new byte[5], false, s));
        var unknown = new byte[64];
        unknown[0] = 0x99;
        Assert.False(InputParser.TryParse(unknown, false, s));
        var truncatedUsb = new byte[20];
        truncatedUsb[0] = 0x01;
        Assert.False(InputParser.TryParse(truncatedUsb, false, s));
    }
}
