using HidSharp;

namespace DualSenseStudio.Core.Device;

/// <summary>Finds a DualSense, keeps it connected, and reconnects after unplug / BT drop.</summary>
public sealed class ControllerManager : IDisposable
{
    private readonly object _lock = new();
    private readonly Timer _timer;
    private DualSenseController? _current;
    private bool _disposed;

    public DualSenseController? Current => _current;
    public string? LastError { get; private set; }

    /// <summary>Raised on a background thread when a controller connects or disconnects.</summary>
    public event Action? ConnectionChanged;

    public ControllerManager()
    {
        DeviceList.Local.Changed += OnDeviceListChanged;
        _timer = new Timer(_ => TryConnect(), null, 0, 1500);
    }

    private void OnDeviceListChanged(object? sender, DeviceListChangedEventArgs e) => TryConnect();

    public void TryConnect()
    {
        bool changed = false;
        lock (_lock)
        {
            if (_disposed || _current != null) return;
            try
            {
                var candidates = DeviceList.Local.GetHidDevices()
                    .Where(DualSenseController.IsDualSense)
                    .OrderBy(d => d.GetMaxInputReportLength() > 64 ? 1 : 0) // prefer USB
                    .ToList();
                if (candidates.Count == 0)
                {
                    LastError = null;
                    return;
                }
                foreach (var d in candidates)
                {
                    var c = DualSenseController.TryOpen(d, out var err);
                    if (c == null)
                    {
                        LastError = err;
                        continue;
                    }
                    c.Disconnected += OnDisconnected;
                    _current = c;
                    LastError = null;
                    changed = true;
                    break;
                }
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
        }
        if (changed) ConnectionChanged?.Invoke();
    }

    private void OnDisconnected(DualSenseController c)
    {
        lock (_lock)
        {
            if (_current != c) return;
            _current = null;
        }
        c.Disconnected -= OnDisconnected;
        ThreadPool.QueueUserWorkItem(_ => c.Dispose());
        ConnectionChanged?.Invoke();
    }

    /// <summary>Called when a write fails: drop the device so we reconnect cleanly.</summary>
    public void ReportFailure(DualSenseController c) => OnDisconnected(c);

    public void Dispose()
    {
        DualSenseController? c;
        lock (_lock)
        {
            _disposed = true;
            c = _current;
            _current = null;
        }
        DeviceList.Local.Changed -= OnDeviceListChanged;
        _timer.Dispose();
        c?.Dispose();
    }
}
