using DualSenseStudio.Core.Haptics;
using DualSenseStudio.Core.Protocol;

namespace DualSenseStudio.Core.Live;

/// <summary>
/// Touchpad as an XY pad. One finger: up/down = strength, left/right = texture (smooth to fast pulses).
/// Two fingers: first finger drives the left side, second finger the right side.
/// </summary>
public sealed class TouchpadSource : IHapticSource
{
    private double _phaseL, _phaseR;
    private float _l, _r;

    public string DisplayName => "Touchpad pad";
    public double Gain { get; set; } = 1.0;

    public HapticFrame GetFrame(in HapticContext ctx)
    {
        var a = ctx.Input.Touch0;
        var b = ctx.Input.Touch1;
        double dt = Math.Clamp(ctx.Dt, 0, 0.1);
        float targetL = 0, targetR = 0;

        if (a.Active && b.Active)
        {
            targetL = Finger(a, dt, ref _phaseL);
            targetR = Finger(b, dt, ref _phaseR);
        }
        else if (a.Active || b.Active)
        {
            var p = a.Active ? a : b;
            targetL = Finger(p, dt, ref _phaseL);
            targetR = targetL;
        }

        // Quick attack, short release so lifting a finger doesn't cut abruptly.
        _l = targetL > _l ? targetL : _l + (targetL - _l) * 0.35f;
        _r = targetR > _r ? targetR : _r + (targetR - _r) * 0.35f;
        float g = (float)Gain;
        return HapticFrame.Motors(_l * g, _r * g);
    }

    private static float Finger(TouchPoint p, double dt, ref double phase)
    {
        double strength = 1.0 - Math.Clamp(p.Y / (double)TouchPoint.MaxY, 0, 1);
        double texture = Math.Clamp(p.X / (double)TouchPoint.MaxX, 0, 1);
        if (texture < 0.2) return (float)strength;
        double hz = 2 + (texture - 0.2) / 0.8 * 18; // 2..20 Hz pulsing
        phase = (phase + hz * dt) % 1.0;
        return phase < 0.5 ? (float)strength : (float)(strength * 0.08);
    }
}

/// <summary>
/// Motion control. Tilt forward/back = strength, tilt left/right = balance, shake = burst.
/// Uses the accelerometer (gravity direction), relative to a calibrated neutral pose.
/// </summary>
public sealed class MotionSource : IHapticSource
{
    private double _neutralPitch, _neutralRoll;
    private double _burstUntil;
    private bool _calibrateNext = true;

    public string DisplayName => "Motion control";
    public double Gain { get; set; } = 1.0;
    public double MaxTiltDegrees { get; set; } = 50;
    public bool ShakeBursts { get; set; } = true;

    public double Pitch { get; private set; }
    public double Roll { get; private set; }
    public double ShakeG { get; private set; }

    public void Calibrate() => _calibrateNext = true;

    public HapticFrame GetFrame(in HapticContext ctx)
    {
        var s = ctx.Input;
        if (!s.FullReport) return default;
        double ax = s.AccelX, ay = s.AccelY, az = s.AccelZ;
        double mag = Math.Sqrt(ax * ax + ay * ay + az * az);
        if (mag < 1) return default;

        double pitch = Math.Asin(Math.Clamp(az / mag, -1, 1)) * 180 / Math.PI;
        double roll = Math.Asin(Math.Clamp(ax / mag, -1, 1)) * 180 / Math.PI;
        if (_calibrateNext)
        {
            _neutralPitch = pitch;
            _neutralRoll = roll;
            _calibrateNext = false;
        }
        Pitch = pitch - _neutralPitch;
        Roll = roll - _neutralRoll;

        // ~8192 counts per g on the DualSense accelerometer.
        ShakeG = Math.Abs(mag / 8192.0 - 1.0);
        if (ShakeBursts && ShakeG > 1.2) _burstUntil = ctx.Time + 0.3;

        double strength = Math.Clamp(Math.Abs(Pitch) / MaxTiltDegrees, 0, 1);
        double balance = Math.Clamp(Roll / MaxTiltDegrees, -1, 1);
        float l = (float)(strength * Math.Min(1, 1 - balance));
        float r = (float)(strength * Math.Min(1, 1 + balance));
        if (ctx.Time < _burstUntil) { l = 1; r = 1; }
        float g = (float)Gain;
        return HapticFrame.Motors(l * g, r * g);
    }
}

/// <summary>Plays motor values that something else (the UI, audio capture) pushes in.</summary>
public sealed class ManualSource(string name) : IHapticSource
{
    public string DisplayName { get; } = name;
    public volatile float Left;
    public volatile float Right;
    public HapticFrame GetFrame(in HapticContext ctx) => HapticFrame.Motors(Left, Right);
}
