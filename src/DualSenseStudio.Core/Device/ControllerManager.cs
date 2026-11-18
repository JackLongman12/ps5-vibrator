namespace DualSenseStudio.Core.Device;

/// <summary>Finds a DualSense, keeps it connected, and reconnects after unplug / BT drop.</summary>
public sealed class ControllerManager : IDisposable
{
    private readonly object _lock = new();
    private readonly IControllerTransportProvider _provider;
    private readonly Timer _timer;
    private DualSenseController? _current;
    private bool _disposed;

    public DualSenseController? Current => _current;
    public string? LastError { get; private set; }

    /// <summary>Raised on a background thread when a controller connects or disconnects.</summary>
    public event Action? ConnectionChanged;

    /// <summary>Uses HidSharp (Windows).</summary>
    public ControllerManager() : this(new HidSharpTransportProvider()) { }

    public ControllerManager(IControllerTransportProvider provider)
    {
        _provider = provider;
        _provider.DevicesChanged += TryConnect;
        _timer = new Timer(_ => TryConnect(), null, 0, 1500);
    }

    public void TryConnect()
    {
        bool changed = false;
        lock (_lock)
        {
            if (_disposed || _current != null) return;
            try
            {
                var t = _provider.TryOpen(out var err);
                if (t == null)
                {
                    LastError = err;
                    return;
                }
                var c = DualSenseController.TryOpen(t, out err);
                if (c == null)
                {
                    LastError = err;
                    return;
                }
                c.Disconnected += OnDisconnected;
                _current = c;
                LastError = null;
                changed = true;
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
        _provider.DevicesChanged -= TryConnect;
        _timer.Dispose();
        c?.Dispose();
        _provider.Dispose();
    }
}
