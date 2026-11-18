using System.Windows;
using DualSenseStudio.Core.Device;
using DualSenseStudio.Core.Haptics;
using DualSenseStudio.Core.Live;
using DualSenseStudio.Core.Recording;
using DualSenseStudio.Core.Storage;
using DualSenseStudio.Services;

namespace DualSenseStudio;

/// <summary>Application-wide services (single instance each).</summary>
public static class AppHost
{
    public static AppSettings Settings { get; private set; } = null!;
    public static ControllerManager Controllers { get; private set; } = null!;
    public static HapticsEngine Engine { get; private set; } = null!;
    public static Playback Playback { get; private set; } = null!;
    public static PatternStore Store { get; private set; } = null!;
    public static UpdateService Updates { get; private set; } = null!;
    public static Recorder Recorder { get; } = new();
    public static TouchpadSource Touchpad { get; } = new();
    public static MotionSource Motion { get; } = new();
    public static AudioReactiveSource Audio { get; } = new();

    /// <summary>Raised on the UI thread after the user's pattern library changes.</summary>
    public static event Action? LibraryChanged;
    public static void NotifyLibraryChanged() => LibraryChanged?.Invoke();

    internal static void Start()
    {
        Settings = AppSettings.Load();
        Store = new PatternStore(AppPaths.PatternsDir);
        Controllers = new ControllerManager();
        Engine = new HapticsEngine(Controllers)
        {
            MasterCap = Settings.MasterCap,
            RumbleMode = Settings.RumbleMode,
            Lights = Settings.Lights,
        };
        Playback = new Playback(Engine, Settings);
        Updates = new UpdateService(Settings);
    }

    internal static void Shutdown()
    {
        try { Settings.Save(); } catch { }
        try { Audio.Dispose(); } catch { }
        try { Engine.Source = null; Engine.Dispose(); } catch { }
        try { Controllers.Dispose(); } catch { }
    }
}

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        NativeMethods.timeBeginPeriod(1);
        AppHost.Start();
        var main = new MainWindow();
        MainWindow = main;
        main.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppHost.Shutdown();
        NativeMethods.timeEndPeriod(1);
        base.OnExit(e);
    }
}
