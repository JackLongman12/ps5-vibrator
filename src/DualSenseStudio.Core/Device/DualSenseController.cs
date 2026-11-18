using System.Diagnostics;
using DualSenseStudio.Core.Protocol;
using HidSharp;

namespace DualSenseStudio.Core.Device;

/// <summary>One open DualSense (USB or Bluetooth) over raw HID.</summary>
public sealed class DualSenseController : IDisposable
{
    public const int SonyVendorId = 0x054C;
    public const int DualSenseProductId = 0x0CE6;
    public const int DualSenseEdgeProductId = 0x0DF2;

    private readonly HidDevice _device;
    private readonly HidStream _stream;
    private readonly object _inputLock = new();
    private readonly object _writeLock = new();
    private readonly InputState _input = new();
    private readonly byte[] _report;
    private readonly byte[] _common = new byte[OutputBuilder.CommonLength];
    private readonly byte[] _lastCommon = new byte[OutputBuilder.CommonLength];
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private Thread? _reader;
    private volatile bool _running;
    private bool _lightbarReleased;
    private bool _hasWritten;
    private byte _sequence;
    private double _lastWriteAt;
    private double _lastReportAt;
    private long _reportCount;
    private int _simpleReports;
    private bool _retriedFullMode;

    public bool IsBluetooth { get; }
    public bool IsEdge { get; }
    /// <summary>True when the firmware supports the stronger "v2" rumble emulation.</summary>
    public bool SupportsRumbleV2 { get; private set; }
    public string FirmwareText { get; private set; } = "unknown";
    public string DevicePath => _device.DevicePath;
    public long ReportCount => Interlocked.Read(ref _reportCount);
    public double SecondsSinceLastReport => _clock.Elapsed.TotalSeconds - Volatile.Read(ref _lastReportAt);

    /// <summary>Raised (on the reader thread) when the device stops responding.</summary>
    public event Action<DualSenseController>? Disconnected;

    private DualSenseController(HidDevice device, HidStream stream)
    {
        _device = device;
        _stream = stream;
        IsEdge = device.ProductID == DualSenseEdgeProductId;
        IsBluetooth = device.GetMaxInputReportLength() > 64;
        int outLen = Math.Max(device.GetMaxOutputReportLength(),
            IsBluetooth ? OutputBuilder.BtReportLength : OutputBuilder.UsbReportLength);
        _report = new byte[outLen];
        ReadFeatures();
    }

    public static bool IsDualSense(HidDevice d) =>
        d.VendorID == SonyVendorId &&
        (d.ProductID == DualSenseProductId || d.ProductID == DualSenseEdgeProductId);

    public static DualSenseController? TryOpen(HidDevice device, out string? error)
    {
        error = null;
        try
        {
            if (!device.TryOpen(out HidStream stream))
            {
                error = "Could not open the controller. Another app (Steam, DS4Windows, DSX) may be holding it exclusively.";
                return null;
            }
            stream.ReadTimeout = 1000;
            stream.WriteTimeout = 1000;
            var c = new DualSenseController(device, stream);
            c.Start();
            return c;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }

    private void ReadFeatures()
    {
        int featLen = 64;
        try { featLen = Math.Max(_device.GetMaxFeatureReportLength(), 64); } catch { }

        // Reading the calibration report (0x05) switches Bluetooth controllers into full report mode (0x31).
        try
        {
            var buf = new byte[featLen];
            buf[0] = 0x05;
            _stream.GetFeature(buf);
        }
        catch { /* not fatal */ }

        // Firmware info (0x20): update version at bytes 44-45 decides the rumble flavour (see Linux hid-playstation).
        try
        {
            var buf = new byte[featLen];
            buf[0] = 0x20;
            _stream.GetFeature(buf);
            uint fw = BitConverter.ToUInt32(buf, 28);
            ushort update = BitConverter.ToUInt16(buf, 44);
            FirmwareText = $"fw 0x{fw:X8}, update {update >> 8}.{update & 0xFF:D2}";
            SupportsRumbleV2 = IsEdge || update >= 0x0215;
        }
        catch
        {
            SupportsRumbleV2 = IsEdge;
        }
    }

    private void Start()
    {
        _running = true;
        _reader = new Thread(ReadLoop) { IsBackground = true, Name = "DualSense input", Priority = ThreadPriority.AboveNormal };
        _reader.Start();
    }

    private void ReadLoop()
    {
        var buf = new byte[Math.Max(_device.GetMaxInputReportLength(), 64)];
        while (_running)
        {
            int n;
            try
            {
                n = _stream.Read(buf, 0, buf.Length);
            }
            catch (TimeoutException)
            {
                continue;
            }
            catch (Exception)
            {
                if (_running)
                {
                    _running = false;
                    Disconnected?.Invoke(this);
                }
                return;
            }
            if (n <= 0) continue;

            bool parsed;
            lock (_inputLock)
                parsed = InputParser.TryParse(buf.AsSpan(0, n), IsBluetooth, _input);
            if (!parsed) continue;

            Interlocked.Increment(ref _reportCount);
            Volatile.Write(ref _lastReportAt, _clock.Elapsed.TotalSeconds);

            if (IsBluetooth && buf[0] == 0x01 && !_retriedFullMode && ++_simpleReports > 250)
            {
                _retriedFullMode = true;
                ReadFeatures();
            }
        }
    }

    public InputState GetInput()
    {
        lock (_inputLock) return _input.Clone();
    }

    /// <summary>Sends the state if it changed (or as a periodic keep-alive). Returns false on I/O failure.</summary>
    public bool Write(OutputState state)
    {
        if (!_running) return false;
        lock (_writeLock)
        {
            OutputBuilder.BuildCommon(state, _common, !_lightbarReleased);
            double now = _clock.Elapsed.TotalSeconds;
            if (_hasWritten && _common.AsSpan().SequenceEqual(_lastCommon) && now - _lastWriteAt < 0.5)
                return true;

            OutputBuilder.Assemble(_common, IsBluetooth, _sequence, _report);
            _sequence = (byte)((_sequence + 1) & 0x0F);
            try
            {
                _stream.Write(_report, 0, _report.Length);
            }
            catch (TimeoutException)
            {
                return true;
            }
            catch (Exception)
            {
                return false;
            }
            _common.CopyTo(_lastCommon, 0);
            _lastWriteAt = now;
            _hasWritten = true;
            _lightbarReleased = true;
            return true;
        }
    }

    public void Dispose()
    {
        if (_running)
        {
            // Leave the controller quiet: motors off, triggers released.
            try
            {
                var quiet = new OutputState { UseRumbleV2 = SupportsRumbleV2, LightB = 64 };
                _hasWritten = false;
                Write(quiet);
            }
            catch { }
        }
        _running = false;
        try { _stream.Dispose(); } catch { }
        if (_reader != null && Thread.CurrentThread != _reader)
            _reader.Join(1500);
    }
}
