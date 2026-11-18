using Android.Hardware.Usb;
using DualSenseStudio.Core.Device;

namespace DualSenseStudio.Droid.Transports;

/// <summary>
/// Raw HID over Android USB host: claims the DualSense's HID interface (detaching the kernel driver)
/// and talks to its interrupt endpoints directly. Same reports as Windows USB, so everything works.
/// </summary>
public sealed class UsbHidTransport : IControllerTransport
{
    private const int TimeoutMs = 1000;
    // HID class requests (USB HID 1.11, section 7.2).
    private const int RequestTypeClassInterfaceIn = 0xA1;
    private const int HidGetReport = 0x01;
    private const int ReportTypeFeature = 0x03;

    private readonly UsbDeviceConnection _connection;
    private readonly UsbInterface _interface;
    private readonly UsbEndpoint _in;
    private readonly UsbEndpoint _out;
    private readonly UsbManager _manager;
    private readonly string _deviceName;
    private volatile bool _closed;

    private UsbHidTransport(UsbManager manager, UsbDevice device, UsbDeviceConnection connection, UsbInterface intf, UsbEndpoint inEp, UsbEndpoint outEp)
    {
        _manager = manager;
        _deviceName = device.DeviceName;
        _connection = connection;
        _interface = intf;
        _in = inEp;
        _out = outEp;
        ProductId = device.ProductId;
        Path = $"USB {device.DeviceName}";
        MaxInputReportLength = Math.Max(64, inEp.MaxPacketSize);
        MaxOutputReportLength = Core.Protocol.OutputBuilder.UsbReportLength;
    }

    public string Path { get; }
    public int ProductId { get; }
    public bool IsBluetooth => false;
    public int MaxInputReportLength { get; }
    public int MaxOutputReportLength { get; }
    public int MaxFeatureReportLength => 64;

    public static UsbHidTransport? TryOpen(UsbManager manager, UsbDevice device, out string? error)
    {
        error = null;
        UsbInterface? hid = null;
        UsbEndpoint? inEp = null, outEp = null;
        for (int i = 0; i < device.InterfaceCount; i++)
        {
            var intf = device.GetInterface(i);
            if (intf.InterfaceClass != UsbClass.Hid) continue;
            for (int e = 0; e < intf.EndpointCount; e++)
            {
                var ep = intf.GetEndpoint(e);
                if (ep == null || ep.Type != UsbAddressing.XferInterrupt) continue;
                if (ep.Direction == UsbAddressing.In) inEp ??= ep;
                else outEp ??= ep;
            }
            if (inEp != null && outEp != null)
            {
                hid = intf;
                break;
            }
            inEp = outEp = null;
        }
        if (hid == null || inEp == null || outEp == null)
        {
            error = "This USB device has no usable HID interface.";
            return null;
        }

        var connection = manager.OpenDevice(device);
        if (connection == null)
        {
            error = "Android refused to open the controller over USB.";
            return null;
        }
        // force = true detaches the kernel's gamepad driver so we own the reports.
        if (!connection.ClaimInterface(hid, true))
        {
            connection.Close();
            error = "Could not claim the controller's HID interface. Another app may be using it.";
            return null;
        }
        return new UsbHidTransport(manager, device, connection, hid, inEp, outEp);
    }

    private bool StillAttached()
    {
        if (_closed) return false;
        try
        {
            return _manager.DeviceList?.ContainsKey(_deviceName) == true;
        }
        catch
        {
            return false;
        }
    }

    public int ReadInputReport(byte[] buffer)
    {
        if (_closed) throw new ObjectDisposedException(nameof(UsbHidTransport));
        int n = _connection.BulkTransfer(_in, buffer, buffer.Length, TimeoutMs);
        if (n >= 0) return n;
        // -1 means timeout or error; Android does not tell them apart.
        if (!StillAttached()) throw new IOException("Controller unplugged.");
        return 0;
    }

    public void WriteOutputReport(byte[] report)
    {
        if (_closed) throw new ObjectDisposedException(nameof(UsbHidTransport));
        int n = _connection.BulkTransfer(_out, report, report.Length, TimeoutMs);
        if (n >= 0) return;
        if (!StillAttached()) throw new IOException("Controller unplugged.");
        throw new TimeoutException();
    }

    public void GetFeatureReport(byte[] buffer)
    {
        if (_closed) throw new ObjectDisposedException(nameof(UsbHidTransport));
        int n = _connection.ControlTransfer(
            (UsbAddressing)RequestTypeClassInterfaceIn, HidGetReport,
            (ReportTypeFeature << 8) | buffer[0], _interface.Id,
            buffer, buffer.Length, TimeoutMs);
        if (n < 0) throw new IOException($"Feature report 0x{buffer[0]:X2} failed.");
    }

    public void Dispose()
    {
        if (_closed) return;
        _closed = true;
        try { _connection.ReleaseInterface(_interface); } catch { }
        try { _connection.Close(); } catch { }
    }
}
