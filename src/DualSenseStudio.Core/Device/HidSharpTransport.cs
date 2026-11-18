using HidSharp;

namespace DualSenseStudio.Core.Device;

/// <summary>Raw HID through HidSharp (Windows, also Linux/macOS).</summary>
public sealed class HidSharpTransport : IControllerTransport
{
    private readonly HidDevice _device;
    private readonly HidStream _stream;

    private HidSharpTransport(HidDevice device, HidStream stream)
    {
        _device = device;
        _stream = stream;
        MaxInputReportLength = SafeLength(device.GetMaxInputReportLength);
        MaxOutputReportLength = SafeLength(device.GetMaxOutputReportLength);
        MaxFeatureReportLength = SafeLength(device.GetMaxFeatureReportLength);
        IsBluetooth = MaxInputReportLength > 64;
    }

    public string Path => _device.DevicePath;
    public int ProductId => _device.ProductID;
    public bool IsBluetooth { get; }
    public int MaxInputReportLength { get; }
    public int MaxOutputReportLength { get; }
    public int MaxFeatureReportLength { get; }

    public static bool IsDualSense(HidDevice d) =>
        d.VendorID == DualSenseController.SonyVendorId &&
        (d.ProductID == DualSenseController.DualSenseProductId || d.ProductID == DualSenseController.DualSenseEdgeProductId);

    public static HidSharpTransport? TryOpen(HidDevice device, out string? error)
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
            return new HidSharpTransport(device, stream);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }

    private static int SafeLength(Func<int> f)
    {
        try { return f(); } catch { return 0; }
    }

    public int ReadInputReport(byte[] buffer)
    {
        try
        {
            return _stream.Read(buffer, 0, buffer.Length);
        }
        catch (TimeoutException)
        {
            return 0;
        }
    }

    public void WriteOutputReport(byte[] report) => _stream.Write(report, 0, report.Length);

    public void GetFeatureReport(byte[] buffer) => _stream.GetFeature(buffer);

    public void Dispose() => _stream.Dispose();
}

/// <summary>Enumerates DualSense controllers with <see cref="DeviceList.Local"/>, preferring USB.</summary>
public sealed class HidSharpTransportProvider : IControllerTransportProvider
{
    public HidSharpTransportProvider()
    {
        DeviceList.Local.Changed += OnChanged;
    }

    public event Action? DevicesChanged;

    private void OnChanged(object? sender, DeviceListChangedEventArgs e) => DevicesChanged?.Invoke();

    public IControllerTransport? TryOpen(out string? error)
    {
        error = null;
        var candidates = DeviceList.Local.GetHidDevices()
            .Where(HidSharpTransport.IsDualSense)
            .OrderBy(d => d.GetMaxInputReportLength() > 64 ? 1 : 0) // prefer USB
            .ToList();
        foreach (var d in candidates)
        {
            var t = HidSharpTransport.TryOpen(d, out error);
            if (t != null)
            {
                error = null;
                return t;
            }
        }
        return null;
    }

    public void Dispose() => DeviceList.Local.Changed -= OnChanged;
}
