using Android.App;
using Android.Content;
using Android.Hardware.Input;
using Android.Hardware.Usb;
using Android.OS;
using Android.Views;
using DualSenseStudio.Core.Device;

namespace DualSenseStudio.Droid.Transports;

/// <summary>
/// Opens a DualSense on Android. USB (raw HID, full feature set) wins; otherwise a Bluetooth controller
/// handled by Android's own driver is used through <see cref="AndroidInputTransport"/>.
/// </summary>
public sealed class AndroidTransportProvider : Java.Lang.Object, IControllerTransportProvider, InputManager.IInputDeviceListener
{
    public const string UsbPermissionAction = "io.github.jacklongman12.dualsensestudio.USB_PERMISSION";

    private readonly Context _context;
    private readonly UsbManager _usb;
    private readonly InputManager _input;
    private readonly UsbReceiver _receiver;
    private readonly HashSet<string> _asked = new();
    private readonly object _lock = new();
    private AndroidInputTransport? _bt;

    public AndroidTransportProvider(Context context)
    {
        _context = context.ApplicationContext ?? context;
        _usb = (UsbManager)_context.GetSystemService(Context.UsbService)!;
        _input = (InputManager)_context.GetSystemService(Context.InputService)!;
        _receiver = new UsbReceiver(this);

        var filter = new IntentFilter(UsbPermissionAction);
        filter.AddAction(UsbManager.ActionUsbDeviceDetached);
        filter.AddAction(UsbManager.ActionUsbDeviceAttached);
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
            _context.RegisterReceiver(_receiver, filter, ReceiverFlags.NotExported);
        else
            _context.RegisterReceiver(_receiver, filter);

        _input.RegisterInputDeviceListener(this, new Handler(Looper.MainLooper!));
    }

    public event Action? DevicesChanged;

    /// <summary>The Bluetooth fallback transport currently open, so the Activity can feed it input events.</summary>
    public AndroidInputTransport? BluetoothTransport
    {
        get { lock (_lock) return _bt; }
    }

    /// <summary>True while a DualSense is plugged in by USB but the user has not granted access yet.</summary>
    public bool WaitingForUsbPermission { get; private set; }

    public static bool IsDualSense(int vendorId, int productId) =>
        vendorId == DualSenseController.SonyVendorId &&
        (productId == DualSenseController.DualSenseProductId || productId == DualSenseController.DualSenseEdgeProductId);

    public IControllerTransport? TryOpen(out string? error)
    {
        error = null;
        WaitingForUsbPermission = false;

        var usbDevices = _usb.DeviceList?.Values.Where(d => IsDualSense(d.VendorId, d.ProductId)).ToList() ?? [];
        foreach (var d in usbDevices)
        {
            if (!_usb.HasPermission(d))
            {
                WaitingForUsbPermission = true;
                error = "Allow DualSense Studio to access the controller in the USB prompt.";
                RequestPermission(d, force: false);
                continue;
            }
            var t = UsbHidTransport.TryOpen(_usb, d, out error);
            if (t != null) return t;
        }
        // A USB-connected controller also shows up as an Android input device; don't fall back to it.
        if (usbDevices.Count > 0) return null;

        foreach (var id in InputDevice.GetDeviceIds() ?? [])
        {
            var dev = InputDevice.GetDevice(id);
            if (dev == null || !IsDualSense(dev.VendorId, dev.ProductId)) continue;
            if ((dev.Sources & InputSourceType.Joystick) != InputSourceType.Joystick) continue; // skip touchpad / motion nodes
            var t = new AndroidInputTransport(dev);
            lock (_lock) _bt = t;
            return t;
        }
        if (!OperatingSystem.IsAndroidVersionAtLeast(31))
            error = "Bluetooth needs Android 12 or newer for rumble and lights. Use a USB cable (OTG) instead.";
        return null;
    }

    /// <summary>Shows the USB access prompt again (e.g. after the user tapped Deny).</summary>
    public void RequestUsbPermissionAgain()
    {
        foreach (var d in _usb.DeviceList?.Values ?? Enumerable.Empty<UsbDevice>())
            if (IsDualSense(d.VendorId, d.ProductId) && !_usb.HasPermission(d))
                RequestPermission(d, force: true);
    }

    private void RequestPermission(UsbDevice d, bool force)
    {
        lock (_lock)
        {
            if (!force && !_asked.Add(d.DeviceName)) return;
            _asked.Add(d.DeviceName);
        }
        var intent = new Intent(UsbPermissionAction);
        intent.SetPackage(_context.PackageName);
        // Android 12+ requires an explicit mutability flag; the system adds the device + result extras, so it must be mutable.
        var flags = OperatingSystem.IsAndroidVersionAtLeast(31) ? PendingIntentFlags.Mutable : 0;
        var pi = PendingIntent.GetBroadcast(_context, 0, intent, flags);
        _usb.RequestPermission(d, pi);
    }

    private void OnUsbEvent(Intent intent)
    {
        if (intent.Action == UsbManager.ActionUsbDeviceDetached)
        {
            var dev = GetDevice(intent);
            if (dev != null) lock (_lock) _asked.Remove(dev.DeviceName);
        }
        DevicesChanged?.Invoke();
    }

    private static UsbDevice? GetDevice(Intent intent) =>
        OperatingSystem.IsAndroidVersionAtLeast(33)
            ? (UsbDevice?)intent.GetParcelableExtra(UsbManager.ExtraDevice, Java.Lang.Class.FromType(typeof(UsbDevice)))
#pragma warning disable CA1422
            : (UsbDevice?)intent.GetParcelableExtra(UsbManager.ExtraDevice);
#pragma warning restore CA1422

    /// <summary>Called by the Activity when Android launches it for a newly attached DualSense.</summary>
    public void OnUsbAttachedIntent() => DevicesChanged?.Invoke();

    public void OnInputDeviceAdded(int deviceId) => DevicesChanged?.Invoke();

    public void OnInputDeviceChanged(int deviceId) { }

    public void OnInputDeviceRemoved(int deviceId)
    {
        lock (_lock)
        {
            if (_bt != null && _bt.DeviceId == deviceId)
            {
                _bt.MarkLost();
                _bt = null;
            }
        }
        DevicesChanged?.Invoke();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try { _context.UnregisterReceiver(_receiver); } catch { }
            try { _input.UnregisterInputDeviceListener(this); } catch { }
        }
        base.Dispose(disposing);
    }

    private sealed class UsbReceiver(AndroidTransportProvider owner) : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent)
        {
            if (intent != null) owner.OnUsbEvent(intent);
        }
    }
}
