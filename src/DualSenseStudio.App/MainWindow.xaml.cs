using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;
using DualSenseStudio.Core.Haptics;
using DualSenseStudio.Core.Protocol;
using DualSenseStudio.Core.Recording;
using DualSenseStudio.Services;
using DualSenseStudio.Views;

namespace DualSenseStudio;

public partial class MainWindow : Window
{
    private const int PanicHotkeyId = 0xD5;

    private readonly Dictionary<string, FrameworkElement> _pages = new();
    private readonly DispatcherTimer _uiTimer;
    private readonly DispatcherTimer _slowTimer;
    private readonly PresetsView _presets = new();
    private readonly RecorderView _recorder = new();
    private bool _loadingSliders = true;
    private bool _warnDismissed;
    private string? _toast;
    private DateTime _toastUntil;
    private DateTime _lastUpdateCheck = DateTime.MinValue;
    private Pattern? _lastPlayed;

    public MainWindow()
    {
        InitializeComponent();

        _pages["presets"] = _presets;
        _pages["recorder"] = _recorder;
        _pages["library"] = new LibraryView();
        _pages["triggers"] = new TriggerLabView();
        _pages["live"] = new LivePlayView();
        _pages["lights"] = new LightsView();
        _pages["monitor"] = new MonitorView();
        _pages["settings"] = new SettingsView();
        Page.Content = _pages["presets"];
        RestorePlacement();

        _loadingSliders = true;
        IntensitySlider.Value = AppHost.Playback.Intensity;
        SpeedSlider.Value = Math.Log2(AppHost.Playback.Speed);
        BalanceSlider.Value = AppHost.Playback.Balance;
        TriggerToggle.IsChecked = AppHost.Playback.TriggerChannels;
        SpeedText.Text = $"{AppHost.Playback.Speed:0.00}×";
        _loadingSliders = false;

        VersionText.Text = AppHost.Updates.IsInstalled ? $"v{AppHost.Updates.CurrentVersion}" : "dev build (not installed)";

        AppHost.Playback.Changed += OnPlaybackChanged;
        AppHost.Engine.ButtonsPressed += (pressed, _) => Dispatcher.BeginInvoke(() => OnControllerButtons(pressed));
        AppHost.Controllers.ConnectionChanged += () => Dispatcher.BeginInvoke(UpdateConnection);
        AppHost.Updates.Changed += () => Dispatcher.BeginInvoke(UpdateUpdateBanner);

        _uiTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        _uiTimer.Tick += (_, _) => UiTick();
        _uiTimer.Start();

        _slowTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _slowTimer.Tick += (_, _) => SlowTick();
        _slowTimer.Start();

        Loaded += (_, _) =>
        {
            Navigate(AppHost.Settings.LastPage);
            UpdateConnection();
            UpdateUpdateBanner();
            SlowTick();
        };
        Closing += (_, _) =>
        {
            SavePlacement();
            AppHost.Settings.Save();
        };
        PreviewKeyDown += (_, e) =>
        {
            // Esc stops everything, unless you're typing in a text box.
            if (e.Key == System.Windows.Input.Key.Escape && e.OriginalSource is not TextBox and not PasswordBox)
            {
                PanicStop("Stopped (Esc)");
                e.Handled = true;
            }
        };
    }

    /// <summary>Called when the app is launched a second time.</summary>
    public void BringToFront()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show();
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void RestorePlacement()
    {
        var s = AppHost.Settings;
        if (s.WindowWidth is { } w && s.WindowHeight is { } h && s.WindowLeft is { } l && s.WindowTop is { } t)
        {
            // Only restore if the window would still be visible (monitor setups change).
            var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            var rect = new Rect(l, t, Math.Max(MinWidth, w), Math.Max(MinHeight, h));
            if (screen.IntersectsWith(new Rect(rect.Left + 50, rect.Top, Math.Max(1, rect.Width - 100), 40)))
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = rect.Left;
                Top = rect.Top;
                Width = rect.Width;
                Height = rect.Height;
            }
        }
        if (s.WindowMaximized) WindowState = WindowState.Maximized;
    }

    private void SavePlacement()
    {
        var s = AppHost.Settings;
        var r = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        if (r.Width > 0 && r.Height > 0)
        {
            s.WindowLeft = r.Left;
            s.WindowTop = r.Top;
            s.WindowWidth = r.Width;
            s.WindowHeight = r.Height;
        }
        s.WindowMaximized = WindowState == WindowState.Maximized;
    }

    // ---- navigation ---------------------------------------------------------------

    private void OnNav(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string key }) Navigate(key);
    }

    public void Navigate(string key)
    {
        if (!_pages.TryGetValue(key, out var page)) return;
        var button = Nav.Children.OfType<RadioButton>().FirstOrDefault(b => (string)b.Tag == key);
        if (button != null && button.IsChecked != true) button.IsChecked = true; // re-enters via Checked
        Page.Content = page;
        AppHost.Settings.LastPage = key;
    }

    // ---- hotkey -------------------------------------------------------------------

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        NativeMethods.RegisterHotKey(handle, PanicHotkeyId, NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT, 0x53 /* S */);
        HwndSource.FromHwnd(handle)?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == PanicHotkeyId)
        {
            PanicStop("Stopped by Ctrl+Alt+S");
            handled = true;
        }
        return IntPtr.Zero;
    }

    protected override void OnClosed(EventArgs e)
    {
        NativeMethods.UnregisterHotKey(new WindowInteropHelper(this).Handle, PanicHotkeyId);
        base.OnClosed(e);
    }

    // ---- stop / shortcuts ---------------------------------------------------------

    public void PanicStop(string reason)
    {
        if (AppHost.Recorder.State != RecorderState.Idle) AppHost.Recorder.Cancel();
        AppHost.Playback.Stop();
        ShowToast(reason);
    }

    private void OnStop(object sender, RoutedEventArgs e) => PanicStop("Stopped");

    private void OnControllerButtons(Buttons pressed)
    {
        if (pressed.HasFlag(Buttons.Mute))
        {
            PanicStop("Stopped from the controller (mute button)");
            return;
        }
        if (!AppHost.Settings.ControllerShortcuts) return;

        if (pressed.HasFlag(Buttons.Create))
        {
            Navigate("recorder");
            _recorder.ToggleRecording();
            return;
        }
        if (AppHost.Recorder.State != RecorderState.Idle) return;

        if (pressed.HasFlag(Buttons.Options))
        {
            if (AppHost.Playback.Active != null) AppHost.Playback.Stop();
            else AppHost.Playback.Play(_lastPlayed ?? PresetLibrary.All[0].Pattern);
        }
        if (pressed.HasFlag(Buttons.DPadRight)) _presets.PlayRelative(+1);
        if (pressed.HasFlag(Buttons.DPadLeft)) _presets.PlayRelative(-1);
        if (pressed.HasFlag(Buttons.DPadUp)) IntensitySlider.Value = Math.Min(1, IntensitySlider.Value + 0.1);
        if (pressed.HasFlag(Buttons.DPadDown)) IntensitySlider.Value = Math.Max(0, IntensitySlider.Value - 0.1);
    }

    private void OnPlaybackChanged()
    {
        var src = AppHost.Playback.Active;
        NowName.Text = src?.DisplayName ?? "Nothing";
        if (src is PatternSource ps)
        {
            _lastPlayed = ps.Pattern;
            NowView.Pattern = ps.Pattern;
        }
        else NowView.Pattern = null;
        _presets.HighlightActive();
    }

    // ---- player sliders -----------------------------------------------------------

    private void OnIntensity(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_loadingSliders) AppHost.Playback.Intensity = e.NewValue;
    }

    private void OnSpeed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        double speed = Math.Pow(2, e.NewValue);
        // Snap near 1× so it's easy to get back to normal.
        if (Math.Abs(e.NewValue) < 0.06) speed = 1;
        if (SpeedText != null) SpeedText.Text = $"{speed:0.00}×";
        if (!_loadingSliders) AppHost.Playback.Speed = speed;
    }

    private void OnBalance(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        double v = Math.Abs(e.NewValue) < 0.05 ? 0 : e.NewValue;
        if (!_loadingSliders) AppHost.Playback.Balance = v;
    }

    private void OnTriggerToggle(object sender, RoutedEventArgs e) =>
        AppHost.Playback.TriggerChannels = TriggerToggle.IsChecked == true;

    // ---- periodic UI --------------------------------------------------------------

    private void UiTick()
    {
        var f = AppHost.Engine.LastFrame;
        OutL.Value = f.Left;
        OutR.Value = f.Right;
        NowView.Playhead = AppHost.Playback.CurrentPattern?.Progress ?? -1;

        if (AppHost.Playback.CurrentPattern is { Finished: true })
            AppHost.Playback.Stop();

        int autoStop = AppHost.Settings.AutoStopMinutes;
        if (autoStop > 0 && AppHost.Playback.StartedAt is { } started &&
            AppHost.Recorder.State == RecorderState.Idle &&
            DateTime.Now - started > TimeSpan.FromMinutes(autoStop))
        {
            PanicStop($"Auto-stopped after {autoStop} min (change in Settings)");
        }

        if (_toast != null && DateTime.Now > _toastUntil)
        {
            _toast = null;
            RefreshWarnBanner();
        }
    }

    private void SlowTick()
    {
        UpdateConnection();
        RefreshWarnBanner();

        if (AppHost.Settings.CheckForUpdates && AppHost.Updates.IsInstalled &&
            DateTime.Now - _lastUpdateCheck > TimeSpan.FromMinutes(20))
        {
            _lastUpdateCheck = DateTime.Now;
            _ = AppHost.Updates.CheckAsync(AppHost.Settings.AutoDownloadUpdates);
        }
    }

    private void UpdateConnection()
    {
        var c = AppHost.Controllers.Current;
        if (c == null)
        {
            ConnDot.Fill = (System.Windows.Media.Brush)FindResource("DangerBrush");
            ConnText.Text = "No controller";
            ConnDetail.Text = AppHost.Controllers.LastError ?? "Turn on your DualSense (PS button) or plug it in by USB.";
            BatteryBar.Visibility = Visibility.Collapsed;
            return;
        }
        var input = AppHost.Engine.Input;
        bool stale = c.SecondsSinceLastReport > 3;
        ConnDot.Fill = (System.Windows.Media.Brush)FindResource(stale ? "WarningBrush" : "SuccessBrush");
        ConnText.Text = (c.IsEdge ? "DualSense Edge" : "DualSense") + (c.IsBluetooth ? " · Bluetooth" : " · USB");
        string battery = input.BatteryPercent > 0 || input.FullReport
            ? $"Battery {input.BatteryPercent}%{(input.Charging ? " (charging)" : input.BatteryFull ? " (full)" : "")}"
            : "Battery: waiting for data";
        ConnDetail.Text = stale ? "Connected, but no input received recently." : battery;
        BatteryBar.Visibility = input.FullReport ? Visibility.Visible : Visibility.Collapsed;
        BatteryBar.Value = input.BatteryPercent / 100.0;
        bool low = input.FullReport && !input.Charging && input.BatteryPercent <= 15;
        BatteryBar.Foreground = (System.Windows.Media.Brush)FindResource(low ? "DangerBrush" : "SuccessBrush");
        if (low && !stale) ConnDetail.Text = $"Battery low ({input.BatteryPercent}%): plug in to keep playing";
    }

    public void ShowToast(string text)
    {
        _toast = text;
        _toastUntil = DateTime.Now.AddSeconds(4);
        RefreshWarnBanner();
    }

    private void RefreshWarnBanner()
    {
        if (_toast != null)
        {
            WarnText.Text = _toast;
            WarnBanner.Visibility = Visibility.Visible;
            return;
        }
        List<string> conflicts = _warnDismissed ? [] : ConflictDetector.Check();
        if (conflicts.Count > 0)
        {
            WarnText.Text = string.Join("\n", conflicts);
            WarnBanner.Visibility = Visibility.Visible;
        }
        else WarnBanner.Visibility = Visibility.Collapsed;
    }

    private void OnDismissWarn(object sender, RoutedEventArgs e)
    {
        if (_toast != null) _toast = null;
        else _warnDismissed = true;
        RefreshWarnBanner();
    }

    // ---- updates ------------------------------------------------------------------

    private void UpdateUpdateBanner()
    {
        var u = AppHost.Updates;
        VersionText.Text = u.IsInstalled ? $"v{u.CurrentVersion}" : "dev build (not installed)";
        switch (u.Status)
        {
            case UpdateStatus.ReadyToRestart:
                UpdateText.Text = $"Update v{u.AvailableVersion} is downloaded and ready.";
                UpdateAction.Content = "Restart & update";
                UpdateAction.Visibility = Visibility.Visible;
                UpdateBanner.Visibility = Visibility.Visible;
                break;
            case UpdateStatus.Downloading:
                UpdateText.Text = $"Downloading update v{u.AvailableVersion}… {u.DownloadProgress}%";
                UpdateAction.Visibility = Visibility.Collapsed;
                UpdateBanner.Visibility = Visibility.Visible;
                break;
            case UpdateStatus.Idle when u.HasPendingDownload:
                UpdateText.Text = $"Update v{u.AvailableVersion} is available.";
                UpdateAction.Content = "Download";
                UpdateAction.Visibility = Visibility.Visible;
                UpdateBanner.Visibility = Visibility.Visible;
                break;
            default:
                UpdateBanner.Visibility = Visibility.Collapsed;
                break;
        }
    }

    private async void OnUpdateAction(object sender, RoutedEventArgs e)
    {
        var u = AppHost.Updates;
        if (u.Status == UpdateStatus.ReadyToRestart)
        {
            AppHost.Playback.Stop();
            AppHost.Settings.Save();
            u.ApplyAndRestart();
        }
        else await u.DownloadAsync();
    }
}
