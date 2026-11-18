using System.Text.Json;
using Android.Content;
using DualSenseStudio.Core.Device;
using DualSenseStudio.Core.Haptics;
using DualSenseStudio.Core.Live;
using DualSenseStudio.Core.Recording;
using DualSenseStudio.Core.Storage;
using DualSenseStudio.Droid.Transports;

namespace DualSenseStudio.Droid;

public enum ControllerKind { None, Usb, Bluetooth }

/// <summary>Process-wide services (one engine for the whole app, like the Windows AppHost).</summary>
public static class AppHost
{
    private static bool _initialized;

    public static AndroidTransportProvider Transports { get; private set; } = null!;
    public static ControllerManager Controllers { get; private set; } = null!;
    public static HapticsEngine Engine { get; private set; } = null!;
    public static Recorder Recorder { get; } = new();
    public static TouchpadSource Touchpad { get; } = new();
    public static MotionSource Motion { get; } = new();
    public static PatternStore Store { get; private set; } = null!;
    public static Playback Playback { get; private set; } = null!;
    public static AppSettings Settings { get; private set; } = null!;
    public static string DataDir { get; private set; } = "";

    /// <summary>Raised on the UI thread after a pattern was saved outside the Library page.</summary>
    public static event Action? LibraryChanged;
    public static void NotifyLibraryChanged() => LibraryChanged?.Invoke();

    public static void Init(Context context)
    {
        if (_initialized) return;
        _initialized = true;
        DataDir = context.FilesDir!.AbsolutePath;
        Settings = AppSettings.Load(System.IO.Path.Combine(DataDir, "settings.json"));
        // Same layout and *.dspattern.json format as %AppData%\DualSenseStudio\patterns on Windows.
        Store = new PatternStore(System.IO.Path.Combine(DataDir, "patterns"));
        Transports = new AndroidTransportProvider(context);
        Controllers = new ControllerManager(Transports);
        Engine = new HapticsEngine(Controllers)
        {
            Lights = Settings.Lights,
            MasterCap = Settings.MasterCap,
            RumbleMode = Settings.RumbleMode,
        };
        Playback = new Playback(Engine, Settings);
    }

    public static ControllerKind Kind => Controllers?.Current?.Transport switch
    {
        UsbHidTransport => ControllerKind.Usb,
        AndroidInputTransport => ControllerKind.Bluetooth,
        _ => ControllerKind.None,
    };

    /// <summary>Adaptive triggers, player LEDs, mute LED, touchpad, rumble mode: raw HID only (USB on Android).</summary>
    public static bool HasRawHid => Kind == ControllerKind.Usb;

    public static bool HasMotion => Kind switch
    {
        ControllerKind.Usb => true,
        ControllerKind.Bluetooth => Transports.BluetoothTransport?.HasMotion == true,
        _ => false,
    };
}

/// <summary>Owns "what is playing right now" and the shared player controls (port of the Windows Playback service).</summary>
public sealed class Playback(HapticsEngine engine, AppSettings settings)
{
    public IHapticSource? Active => engine.Source;
    public PatternSource? CurrentPattern => engine.Source as PatternSource;

    public double Speed
    {
        get => settings.Speed;
        set { settings.Speed = value; if (CurrentPattern is { } p) p.Speed = value; settings.Save(); }
    }

    public double Intensity
    {
        get => settings.Intensity;
        set { settings.Intensity = value; if (CurrentPattern is { } p) p.Intensity = value; settings.Save(); }
    }

    public double Balance
    {
        get => settings.Balance;
        set { settings.Balance = value; if (CurrentPattern is { } p) p.Balance = value; settings.Save(); }
    }

    public bool TriggerChannels
    {
        get => settings.PlayTriggerChannels;
        set { settings.PlayTriggerChannels = value; if (CurrentPattern is { } p) p.TriggersEnabled = value; settings.Save(); }
    }

    public PatternSource Play(Pattern pattern, bool loop = true)
    {
        var src = new PatternSource(pattern)
        {
            Speed = Speed,
            Intensity = Intensity,
            Balance = Balance,
            TriggersEnabled = TriggerChannels,
            Loop = loop,
        };
        SetSource(src);
        return src;
    }

    public void SetSource(IHapticSource? source) => engine.Source = source;

    public void Stop() => SetSource(null);
}

public sealed class AppSettings
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private string _path = "";

    public double Speed { get; set; } = 1.0;
    public double Intensity { get; set; } = 0.8;
    public double Balance { get; set; }
    public bool PlayTriggerChannels { get; set; } = true;
    public double MasterCap { get; set; } = 1.0;
    public RumbleMode RumbleMode { get; set; } = RumbleMode.Auto;
    public LightSettings Lights { get; set; } = new();

    public static AppSettings Load(string path)
    {
        AppSettings s;
        try
        {
            s = File.Exists(path) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json) ?? new() : new();
        }
        catch
        {
            s = new();
        }
        s._path = path;
        s.Lights ??= new();
        return s;
    }

    private Timer? _saveTimer;

    /// <summary>Saves shortly after the last change (sliders call this many times a second).</summary>
    public void Save()
    {
        _saveTimer ??= new Timer(_ => SaveNow());
        _saveTimer.Change(500, Timeout.Infinite);
    }

    public void SaveNow()
    {
        try
        {
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
            File.Move(tmp, _path, overwrite: true);
        }
        catch { /* settings are best-effort */ }
    }
}
