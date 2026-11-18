using Android.Hardware;
using Android.Hardware.Lights;
using Android.OS;
using Android.Views;
using DualSenseStudio.Core.Device;
using DualSenseStudio.Core.Protocol;
using Axis = Android.Views.Axis;
using Environment = System.Environment;

namespace DualSenseStudio.Droid.Transports;

/// <summary>
/// Bluetooth fallback. Android apps cannot open a Bluetooth HID device raw, so this transport speaks the
/// USB report format to the Core but is backed by Android's own DualSense driver:
/// <list type="bullet">
/// <item>input reports are built from key / motion events (sticks, L2/R2 pressure, buttons) and, when present, the
/// controller's motion sensors;</item>
/// <item>output reports are decoded: rumble goes to <c>InputDevice.VibratorManager</c>, the lightbar colour to
/// <c>InputDevice.LightsManager</c> (both Android 12+).</item>
/// </list>
/// Adaptive triggers, player LEDs, the touchpad and the mute LED cannot be reached this way and are ignored.
/// </summary>
public sealed class AndroidInputTransport : Java.Lang.Object, IControllerTransport, ISensorEventListener
{
    private const int ReportLength = 64;

    private readonly object _lock = new();
    private readonly SemaphoreSlim _changed = new(0, 1);
    private readonly InputDevice _device;
    private readonly byte[] _state = new byte[ReportLength];
    private readonly Vibrator? _left, _right;
    private readonly VibratorManager? _vibrators;
    private readonly LightsManager.LightsSession? _lightsSession;
    private readonly Light? _lightbar;
    private readonly SensorManager? _sensors;
    private readonly HandlerThread? _sensorThread;
    private byte _lastLeft, _lastRight;
    private int _lastColor = -1;
    private long _lastRumbleAt;
    private long _lastLightAt;
    private volatile bool _closed;
    private volatile bool _lost;

    public AndroidInputTransport(InputDevice device)
    {
        _device = device;
        DeviceId = device.Id;
        ProductId = device.ProductId;
        Path = $"Bluetooth (Android input) {device.Name}";

        // Neutral report: sticks centred, D-pad released (8), touch points inactive (bit 7 set).
        _state[0] = 0x01;
        _state[1] = _state[2] = _state[3] = _state[4] = 0x80;
        _state[1 + 7] = 0x08;
        _state[1 + 32] = 0x80;
        _state[1 + 36] = 0x80;

        if (OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            try
            {
                _vibrators = device.VibratorManager;
                var ids = _vibrators.GetVibratorIds();
                if (ids.Length >= 2)
                {
                    _left = _vibrators.GetVibrator(ids[0]);
                    _right = _vibrators.GetVibrator(ids[1]);
                }
                else if (ids.Length == 1) _left = _vibrators.GetVibrator(ids[0]);
                else _vibrators = null;
            }
            catch { _vibrators = null; }

            try
            {
                var lights = device.LightsManager;
                _lightbar = FindLightbar(lights);
                if (_lightbar != null) _lightsSession = lights.OpenSession();
            }
            catch { _lightbar = null; }

            try
            {
                _sensors = device.SensorManager;
                var accel = _sensors.GetDefaultSensor(SensorType.Accelerometer);
                var gyro = _sensors.GetDefaultSensor(SensorType.Gyroscope);
                if (accel != null || gyro != null)
                {
                    _sensorThread = new HandlerThread("DualSense sensors");
                    _sensorThread.Start();
                    var handler = new Handler(_sensorThread.Looper!);
                    if (accel != null) _sensors.RegisterListener(this, accel, SensorDelay.Game, handler);
                    if (gyro != null) _sensors.RegisterListener(this, gyro, SensorDelay.Game, handler);
                    HasMotion = accel != null;
                }
            }
            catch { HasMotion = false; }
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("android31.0")]
    private static Light? FindLightbar(LightsManager lights)
    {
        foreach (var l in lights.Lights)
            if (l.HasRgbControl) return l;
        foreach (var l in lights.Lights)
            if (l.Type == (int)LightType.Input) return l;
        return null;
    }

    public int DeviceId { get; }
    public string Path { get; }
    public int ProductId { get; }
    /// <summary>Reports use USB framing (we synthesise / decode them ourselves).</summary>
    public bool IsBluetooth => false;
    public int MaxInputReportLength => ReportLength;
    public int MaxOutputReportLength => OutputBuilder.UsbReportLength;
    public int MaxFeatureReportLength => 64;

    public bool HasRumble => _left != null;
    public bool HasLightbar => _lightbar != null;
    public bool HasMotion { get; }

    /// <summary>Called when Android removes the input device: the next read fails so the manager reconnects.</summary>
    public void MarkLost()
    {
        _lost = true;
        Signal();
    }

    // ---- input ---------------------------------------------------------------------

    /// <summary>Feeds a key event from the Activity. Returns true if it belonged to this controller.</summary>
    public bool OnKey(KeyEvent e)
    {
        if (e.DeviceId != DeviceId) return false;
        bool down = e.Action == KeyEventActions.Down;
        // Byte offsets in the USB report (after the report id): 8 = face buttons + D-pad, 9 = shoulders/menus, 10 = PS/touchpad.
        (int index, int bit) = e.KeyCode switch
        {
            Keycode.ButtonX => (8, 0x10),       // Square (west)
            Keycode.ButtonA => (8, 0x20),       // Cross (south)
            Keycode.ButtonB => (8, 0x40),       // Circle (east)
            Keycode.ButtonY => (8, 0x80),       // Triangle (north)
            Keycode.ButtonL1 => (9, 0x01),
            Keycode.ButtonR1 => (9, 0x02),
            Keycode.ButtonL2 => (9, 0x04),
            Keycode.ButtonR2 => (9, 0x08),
            Keycode.ButtonSelect => (9, 0x10),  // Create
            Keycode.ButtonStart => (9, 0x20),   // Options
            Keycode.ButtonThumbl => (9, 0x40),
            Keycode.ButtonThumbr => (9, 0x80),
            Keycode.ButtonMode => (10, 0x01),   // PS
            _ => (-1, 0),
        };
        if (index < 0)
        {
            // D-pad as keys (some layouts) instead of hat axes.
            var dpad = e.KeyCode switch
            {
                Keycode.DpadUp => 1,
                Keycode.DpadDown => 2,
                Keycode.DpadLeft => 4,
                Keycode.DpadRight => 8,
                _ => 0,
            };
            if (dpad == 0) return false;
            lock (_lock)
            {
                _dpadKeys = down ? _dpadKeys | dpad : _dpadKeys & ~dpad;
                UpdateHat();
            }
            Signal();
            return true;
        }
        lock (_lock)
        {
            if (down) _state[index] |= (byte)bit;
            else _state[index] &= (byte)~bit;
        }
        Signal();
        return true;
    }

    private int _dpadKeys;
    private float _hatX, _hatY;

    /// <summary>Feeds a joystick motion event. Returns true if it belonged to this controller.</summary>
    public bool OnMotion(MotionEvent e)
    {
        if (e.DeviceId != DeviceId) return false;
        lock (_lock)
        {
            _state[1] = Stick(e.GetAxisValue(Axis.X));
            _state[2] = Stick(e.GetAxisValue(Axis.Y));
            _state[3] = Stick(e.GetAxisValue(Axis.Z));
            _state[4] = Stick(e.GetAxisValue(Axis.Rz));
            float l2 = Math.Max(e.GetAxisValue(Axis.Ltrigger), e.GetAxisValue(Axis.Brake));
            float r2 = Math.Max(e.GetAxisValue(Axis.Rtrigger), e.GetAxisValue(Axis.Gas));
            _state[5] = Unit(l2);
            _state[6] = Unit(r2);
            _hatX = e.GetAxisValue(Axis.HatX);
            _hatY = e.GetAxisValue(Axis.HatY);
            UpdateHat();
        }
        Signal();
        return true;
    }

    private void UpdateHat()
    {
        bool up = _hatY < -0.5f || (_dpadKeys & 1) != 0;
        bool downd = _hatY > 0.5f || (_dpadKeys & 2) != 0;
        bool left = _hatX < -0.5f || (_dpadKeys & 4) != 0;
        bool right = _hatX > 0.5f || (_dpadKeys & 8) != 0;
        int hat = (up, right, downd, left) switch
        {
            (true, false, false, false) => 0,
            (true, true, false, false) => 1,
            (false, true, false, false) => 2,
            (false, true, true, false) => 3,
            (false, false, true, false) => 4,
            (false, false, true, true) => 5,
            (false, false, false, true) => 6,
            (true, false, false, true) => 7,
            _ => 8,
        };
        _state[8] = (byte)((_state[8] & 0xF0) | hat);
    }

    private static byte Stick(float v) => (byte)Math.Clamp((int)MathF.Round((v + 1f) * 127.5f), 0, 255);
    private static byte Unit(float v) => (byte)Math.Clamp((int)MathF.Round(v * 255f), 0, 255);

    public void OnSensorChanged(SensorEvent? e)
    {
        if (e?.Values == null || e.Sensor == null || e.Values.Count < 3) return;
        lock (_lock)
        {
            if (e.Sensor.Type == SensorType.Accelerometer)
            {
                // m/s^2 -> DualSense counts (~8192 per g). Android reports the controller's axes in its own frame;
                // this maps them onto the DualSense layout well enough for tilt/shake play.
                const double perMs2 = 8192.0 / 9.80665;
                WriteShort(1 + 21, e.Values[0] * perMs2);
                WriteShort(1 + 23, e.Values[1] * perMs2);
                WriteShort(1 + 25, e.Values[2] * perMs2);
            }
            else if (e.Sensor.Type == SensorType.Gyroscope)
            {
                // rad/s -> ~1024 counts per rad/s (approximate, only displayed).
                WriteShort(1 + 15, e.Values[0] * 1024);
                WriteShort(1 + 17, e.Values[1] * 1024);
                WriteShort(1 + 19, e.Values[2] * 1024);
            }
        }
        Signal();
    }

    private void WriteShort(int offset, double value)
    {
        short v = (short)Math.Clamp(Math.Round(value), short.MinValue, short.MaxValue);
        _state[offset] = (byte)v;
        _state[offset + 1] = (byte)(v >> 8);
    }

    public void OnAccuracyChanged(Sensor? sensor, SensorStatus accuracy) { }

    private void Signal()
    {
        try { if (_changed.CurrentCount == 0) _changed.Release(); }
        catch (SemaphoreFullException) { }
    }

    public int ReadInputReport(byte[] buffer)
    {
        if (_closed) throw new ObjectDisposedException(nameof(AndroidInputTransport));
        // Wake on new input, but also every 100 ms so battery state stays fresh.
        _changed.Wait(100);
        if (_closed) throw new ObjectDisposedException(nameof(AndroidInputTransport));
        if (_lost) throw new IOException("Controller disconnected.");
        lock (_lock)
        {
            UpdateBattery();
            int n = Math.Min(buffer.Length, ReportLength);
            Array.Copy(_state, buffer, n);
            return n;
        }
    }

    private long _batteryCheckedAt;

    private void UpdateBattery()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(31)) return;
        long now = Environment.TickCount64;
        if (now - _batteryCheckedAt < 5000) return;
        _batteryCheckedAt = now;
        try
        {
            var b = _device.BatteryState;
            if (!b.IsPresent) return;
            float cap = b.Capacity; // 0..1, NaN if unknown
            int level = float.IsNaN(cap) ? 5 : Math.Clamp((int)(cap * 10), 0, 10);
            int charge = b.Status switch
            {
                (int)Android.Hardware.BatteryStatus.Full => 2,
                (int)Android.Hardware.BatteryStatus.Charging => 1,
                _ => 0,
            };
            _state[1 + 52] = (byte)((charge << 4) | (level & 0x0F));
        }
        catch { }
    }

    // ---- output --------------------------------------------------------------------

    public void WriteOutputReport(byte[] report)
    {
        if (_closed) throw new ObjectDisposedException(nameof(AndroidInputTransport));
        if (_lost) throw new IOException("Controller disconnected.");
        if (report.Length < 1 + OutputBuilder.CommonLength || report[0] != 0x02) return;
        // Common block starts at byte 1 (see OutputBuilder.BuildCommon).
        byte right = report[1 + 2], left = report[1 + 3];
        Rumble(left, right);
        int color = unchecked((int)0xFF000000) | (report[1 + 44] << 16) | (report[1 + 45] << 8) | report[1 + 46];
        SetLightbar(color);
    }

    private void Rumble(byte left, byte right)
    {
        if (_vibrators == null || !OperatingSystem.IsAndroidVersionAtLeast(31)) return;
        long now = Environment.TickCount64;
        // The engine re-sends unchanged state every 0.5 s; one-shots last 1 s, so refresh those to keep going.
        if (left == _lastLeft && right == _lastRight && now - _lastRumbleAt < 400) return;
        // Changes arrive at up to 100 Hz; 50 Hz is plenty through Android's vibrator service. Stopping is never delayed.
        bool stop = left == 0 && right == 0;
        if (!stop && now - _lastRumbleAt < 20) return;
        _lastLeft = left;
        _lastRight = right;
        _lastRumbleAt = now;
        try
        {
            if (left == 0 && right == 0)
            {
                _vibrators.Cancel();
                return;
            }
            var combo = CombinedVibration.StartParallel();
            if (_right != null)
            {
                AddMotor(combo, _left!, left);
                AddMotor(combo, _right, right);
            }
            else AddMotor(combo, _left!, Math.Max(left, right));
            _vibrators.Vibrate(combo.Combine());
        }
        catch { }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("android31.0")]
    private static void AddMotor(CombinedVibration.ParallelCombination combo, Vibrator v, byte amplitude)
    {
        if (amplitude == 0) return;
        int amp = v.HasAmplitudeControl ? amplitude : 255;
        combo.AddVibrator(v.Id, VibrationEffect.CreateOneShot(1000, amp)!);
    }

    private void SetLightbar(int argb)
    {
        if (_lightsSession == null || _lightbar == null || argb == _lastColor || !OperatingSystem.IsAndroidVersionAtLeast(31)) return;
        // "Follow vibration" changes the colour every tick; ~20 Hz is enough, and the engine's 0.5 s keep-alive delivers the final colour.
        long now = Environment.TickCount64;
        if (now - _lastLightAt < 50) return;
        _lastLightAt = now;
        _lastColor = argb;
        try
        {
            var state = new LightState.Builder().SetColor(new Android.Graphics.Color(argb)).Build();
            var request = new LightsRequest.Builder().AddLight(_lightbar, state).Build();
            _lightsSession.RequestLights(request);
        }
        catch { }
    }

    public void GetFeatureReport(byte[] buffer) =>
        throw new NotSupportedException("Feature reports are not available through Android's input stack.");

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_closed)
        {
            _closed = true;
            Signal();
            try { if (OperatingSystem.IsAndroidVersionAtLeast(31)) _vibrators?.Cancel(); } catch { }
            try { if (OperatingSystem.IsAndroidVersionAtLeast(31)) _lightsSession?.Close(); } catch { }
            try { _sensors?.UnregisterListener(this); } catch { }
            try { _sensorThread?.QuitSafely(); } catch { }
        }
        base.Dispose(disposing);
    }
}
