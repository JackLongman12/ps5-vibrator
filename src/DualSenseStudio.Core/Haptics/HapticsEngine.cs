using System.Diagnostics;
using DualSenseStudio.Core.Device;
using DualSenseStudio.Core.Protocol;

namespace DualSenseStudio.Core.Haptics;

public enum RumbleMode { Auto, V2, Legacy }
public enum LightbarMode { Off, Static, FollowVibration, Rainbow, Battery }
public enum PlayerLedMode { Off, IntensityMeter, Battery, Custom }

public sealed class LightSettings
{
    public LightbarMode Mode { get; set; } = LightbarMode.FollowVibration;
    public byte R { get; set; } = 40;
    public byte G { get; set; } = 110;
    public byte B { get; set; } = 255;
    /// <summary>0 = high, 1 = medium, 2 = low (player LEDs).</summary>
    public byte LedBrightness { get; set; }
    public PlayerLedMode PlayerLeds { get; set; } = PlayerLedMode.IntensityMeter;
    public byte CustomLedMask { get; set; } = 0b00100;
}

/// <summary>
/// The real-time loop (100 Hz). Each tick: read latest input, ask the active source for a frame,
/// apply safety caps, compose triggers + lights, and write one output report.
/// </summary>
public sealed class HapticsEngine : IDisposable
{
    public const int TickHz = 100;

    private readonly ControllerManager _controllers;
    private readonly OutputState _out = new();
    private readonly Thread _thread;
    private volatile bool _running = true;
    private volatile IHapticSource? _source;
    private volatile InputState _input = InputState.Empty;
    private Buttons _prevButtons;
    private HapticFrame _lastFrame;
    private long _ticks;

    public HapticsEngine(ControllerManager controllers)
    {
        _controllers = controllers;
        _thread = new Thread(Run) { IsBackground = true, Name = "Haptics engine", Priority = ThreadPriority.Highest };
        _thread.Start();
    }

    public IHapticSource? Source
    {
        get => _source;
        set => _source = value;
    }

    /// <summary>Global intensity ceiling 0..1 (safety).</summary>
    public double MasterCap { get; set; } = 1.0;

    /// <summary>When true, everything is forced silent regardless of source.</summary>
    public bool Muted { get; set; }

    public RumbleMode RumbleMode { get; set; } = RumbleMode.Auto;

    /// <summary>Static trigger effects (Trigger Lab). Pattern trigger vibration overrides these while active.</summary>
    public TriggerConfig LeftTrigger { get; set; } = TriggerConfig.Off;
    public TriggerConfig RightTrigger { get; set; } = TriggerConfig.Off;

    public LightSettings Lights { get; set; } = new();

    public InputState Input => _input;
    public HapticFrame LastFrame => _lastFrame;
    public long TickCount => Interlocked.Read(ref _ticks);

    /// <summary>Newly pressed buttons this tick. Raised on the engine thread.</summary>
    public event Action<Buttons, InputState>? ButtonsPressed;

    private void Run()
    {
        var sw = Stopwatch.StartNew();
        const double period = 1.0 / TickHz;
        double next = 0;
        double last = 0;
        while (_running)
        {
            double now = sw.Elapsed.TotalSeconds;
            if (now < next)
            {
                double ms = (next - now) * 1000;
                if (ms > 1.6) Thread.Sleep((int)(ms - 0.6));
                else Thread.Yield();
                continue;
            }
            next += period;
            if (now - next > 0.1) next = now + period; // fell far behind: resync instead of bursting
            double dt = now - last;
            last = now;
            try
            {
                Step(now, dt);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Engine tick failed: " + ex);
            }
            Interlocked.Increment(ref _ticks);
        }
    }

    private void Step(double now, double dt)
    {
        var controller = _controllers.Current;
        var input = controller?.GetInput() ?? InputState.Empty;
        _input = input;

        var pressed = input.Buttons & ~_prevButtons;
        _prevButtons = input.Buttons;
        if (pressed != Buttons.None) ButtonsPressed?.Invoke(pressed, input);

        var src = _source;
        HapticFrame f = default;
        if (src != null && !Muted)
            f = src.GetFrame(new HapticContext(now, dt, input));

        float cap = (float)Math.Clamp(MasterCap, 0, 1);
        f.Left = Math.Clamp(f.Left, 0, 1) * cap;
        f.Right = Math.Clamp(f.Right, 0, 1) * cap;
        f.LeftTrigger = Math.Clamp(f.LeftTrigger, 0, 1) * cap;
        f.RightTrigger = Math.Clamp(f.RightTrigger, 0, 1) * cap;
        if (Muted) f = default;
        _lastFrame = f;

        if (controller == null) return;

        _out.MotorLeft = ToByte(f.Left);
        _out.MotorRight = ToByte(f.Right);
        int freq = f.TriggerFrequency > 0 ? f.TriggerFrequency : 35;
        _out.LeftTrigger = f.LeftTrigger > 0.02f ? TriggerConfig.Vibrate(f.LeftTrigger, freq) : (Muted ? TriggerConfig.Off : LeftTrigger);
        _out.RightTrigger = f.RightTrigger > 0.02f ? TriggerConfig.Vibrate(f.RightTrigger, freq) : (Muted ? TriggerConfig.Off : RightTrigger);
        _out.UseRumbleV2 = RumbleMode switch
        {
            RumbleMode.V2 => true,
            RumbleMode.Legacy => false,
            _ => controller.SupportsRumbleV2,
        };
        ApplyLights(now, f, input);

        if (!controller.Write(_out))
            _controllers.ReportFailure(controller);
    }

    private void ApplyLights(double now, in HapticFrame f, InputState input)
    {
        var l = Lights;
        float k = f.Intensity;
        byte r, g, b;
        if (f.HasColor)
        {
            (r, g, b) = (f.R, f.G, f.B);
        }
        else
        {
            switch (l.Mode)
            {
                case LightbarMode.Off:
                    (r, g, b) = (0, 0, 0);
                    break;
                case LightbarMode.Static:
                    (r, g, b) = (l.R, l.G, l.B);
                    break;
                case LightbarMode.Rainbow:
                    (r, g, b) = Hsv((now * 0.08) % 1.0, 1, 1);
                    break;
                case LightbarMode.Battery:
                {
                    double p = Math.Clamp(input.BatteryPercent / 100.0, 0, 1);
                    (r, g, b) = ((byte)(255 * (1 - p)), (byte)(255 * p), 0);
                    break;
                }
                default:
                {
                    float m = 0.12f + 0.88f * k;
                    (r, g, b) = ((byte)(l.R * m), (byte)(l.G * m), (byte)(l.B * m));
                    break;
                }
            }
        }
        _out.LightR = r;
        _out.LightG = g;
        _out.LightB = b;
        _out.LedBrightness = l.LedBrightness;
        _out.MuteLed = f.MuteLed;

        byte leds = l.PlayerLeds switch
        {
            PlayerLedMode.IntensityMeter => Meter((int)MathF.Round(k * 5)),
            PlayerLedMode.Battery => Meter((int)Math.Ceiling(input.BatteryPercent / 20.0)),
            PlayerLedMode.Custom => (byte)(l.CustomLedMask & 0x1F),
            _ => 0,
        };
        _out.PlayerLeds = (byte)(leds | 0x20);
    }

    private static byte Meter(int n) => (byte)((1 << Math.Clamp(n, 0, 5)) - 1);

    private static byte ToByte(float v) => (byte)Math.Clamp((int)MathF.Round(v * 255f), 0, 255);

    private static (byte, byte, byte) Hsv(double h, double s, double v)
    {
        double c = v * s;
        double x = c * (1 - Math.Abs(h * 6 % 2 - 1));
        double m = v - c;
        (double r, double g, double b) = (int)(h * 6) switch
        {
            0 => (c, x, 0.0),
            1 => (x, c, 0.0),
            2 => (0.0, c, x),
            3 => (0.0, x, c),
            4 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        return ((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }

    public void Dispose()
    {
        _running = false;
        _thread.Join(500);
    }
}
