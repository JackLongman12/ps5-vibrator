using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Hardware.Usb;
using Android.OS;
using Android.Views;
using Android.Widget;
using DualSenseStudio.Core.Protocol;
using DualSenseStudio.Droid.Pages;
using DualSenseStudio.Droid.Ui;
using U = DualSenseStudio.Droid.Ui.Ui;

namespace DualSenseStudio.Droid;

[Activity(
    Label = "DualSense Studio",
    Name = "io.github.jacklongman12.dualsensestudio.MainActivity",
    MainLauncher = true,
    Exported = true,
    LaunchMode = LaunchMode.SingleTop,
    // A gamepad connecting changes keyboard/navigation config; don't let that recreate the Activity.
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.ScreenLayout |
                           ConfigChanges.SmallestScreenSize | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden |
                           ConfigChanges.Navigation | ConfigChanges.UiMode)]
[IntentFilter([UsbManager.ActionUsbDeviceAttached])]
[MetaData(UsbManager.ActionUsbDeviceAttached, Resource = "@xml/usb_device_filter")]
public sealed class MainActivity : Activity
{
    private readonly Handler _handler = new(Looper.MainLooper!);
    private readonly List<(Page Page, Button Tab)> _tabs = new();
    private FrameLayout _content = null!;
    private Page? _page;
    private TextView _status = null!, _statusDetail = null!, _nowPlaying = null!;
    private View _statusDot = null!;
    private LinearLayout _playerControls = null!;
    private Button _usbButton = null!;
    private bool _resumed;
    private ControllerKind _lastKind = (ControllerKind)(-1);
    private string? _lastError;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        AppHost.Init(this);
        if (!OperatingSystem.IsAndroidVersionAtLeast(35))
        {
            // Android 15+ draws edge-to-edge; the root view's background shows behind the bars instead.
            Window!.SetStatusBarColor(U.Bg);
            Window.SetNavigationBarColor(U.Bg);
        }

        var root = U.Column(this);
        root.SetBackgroundColor(U.Bg);
        root.SetFitsSystemWindows(true);

        // Header: title + connection status.
        var header = U.Column(this);
        header.SetPadding(U.Dp(this, 14), U.Dp(this, 10), U.Dp(this, 14), U.Dp(this, 4));
        var titleRow = U.Row(this);
        titleRow.AddView(U.Text(this, "DualSense Studio", 20, U.Text1, bold: true), U.Weight());
        _usbButton = U.Button(this, "Allow USB", AppHost.Transports.RequestUsbPermissionAgain, primary: true);
        titleRow.AddView(_usbButton);
        header.AddView(titleRow, U.Match());
        var statusRow = U.Row(this);
        _statusDot = new View(this);
        var dotLp = new LinearLayout.LayoutParams(U.Dp(this, 10), U.Dp(this, 10)) { RightMargin = U.Dp(this, 8) };
        statusRow.AddView(_statusDot, dotLp);
        _status = U.Text(this, "", 13, U.Text1, bold: true);
        statusRow.AddView(_status);
        header.AddView(statusRow);
        _statusDetail = U.Text(this, "", 12, U.Muted);
        header.AddView(_statusDetail);
        root.AddView(header, U.Match());

        // Tabs.
        var tabScroll = new HorizontalScrollView(this) { HorizontalScrollBarEnabled = false };
        var tabRow = U.Row(this);
        tabRow.SetPadding(U.Dp(this, 10), U.Dp(this, 6), U.Dp(this, 10), 0);
        tabScroll.AddView(tabRow);
        root.AddView(tabScroll, U.Match());
        foreach (var page in new Page[]
                 {
                     new PresetsPage(this), new RecorderPage(this), new LibraryPage(this), new TriggerLabPage(this),
                     new LivePage(this), new LightsPage(this), new DevicePage(this),
                 })
        {
            var p = page;
            var tab = U.Button(this, page.Title, () => ShowPage(p));
            tabRow.AddView(tab);
            _tabs.Add((page, tab));
        }

        _content = new FrameLayout(this);
        root.AddView(_content, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1));

        root.AddView(BuildPlayerBar(), U.Match());
        SetContentView(root);

        AppHost.Controllers.ConnectionChanged += OnConnectionChanged;
        AppHost.Engine.ButtonsPressed += OnControllerButtons;
        ShowPage(_tabs[0].Page);
        UpdateConnection();
    }

    private View BuildPlayerBar()
    {
        var bar = U.Column(this, 10);
        bar.Background = U.Rounded(U.CardBg, 0);
        var row = U.Row(this);
        _nowPlaying = U.Text(this, "Nothing playing", 13, U.Text1);
        _nowPlaying.SetSingleLine(true);
        _nowPlaying.Ellipsize = Android.Text.TextUtils.TruncateAt.End;
        row.AddView(_nowPlaying, U.Weight());
        Button? toggle = null;
        toggle = U.Button(this, "Controls ▴", () =>
        {
            bool show = _playerControls.Visibility != ViewStates.Visible;
            _playerControls.Visibility = show ? ViewStates.Visible : ViewStates.Gone;
            toggle!.Text = show ? "Controls ▾" : "Controls ▴";
        });
        row.AddView(toggle);
        var stop = U.Button(this, "■ Stop", StopAll, primary: true);
        stop.Background = U.Rounded(U.Danger, U.Dp(this, 8));
        row.AddView(stop);
        bar.AddView(row, U.Match());

        _playerControls = U.Column(this);
        _playerControls.AddView(new SliderRow(this, "Intensity", 0, 1, 0.01, AppHost.Playback.Intensity, v => $"{v * 100:0}%", v => AppHost.Playback.Intensity = v));
        _playerControls.AddView(new SliderRow(this, "Speed", 0.25, 4, 0.05, AppHost.Playback.Speed, v => $"×{v:0.00}", v => AppHost.Playback.Speed = v));
        _playerControls.AddView(new SliderRow(this, "Balance", -1, 1, 0.05, AppHost.Playback.Balance, v => v switch
        {
            < -0.02 => $"left {-v * 100:0}%",
            > 0.02 => $"right {v * 100:0}%",
            _ => "centre",
        }, v => AppHost.Playback.Balance = v));
        _playerControls.AddView(U.Check(this, "Play trigger vibration channels (USB)", AppHost.Playback.TriggerChannels, v => AppHost.Playback.TriggerChannels = v));
        _playerControls.Visibility = ViewStates.Gone;
        bar.AddView(_playerControls, U.Match());
        return bar;
    }

    private void OnConnectionChanged() => RunOnUiThread(UpdateConnection);

    private void StopAll()
    {
        if (AppHost.Recorder.State != Core.Recording.RecorderState.Idle) AppHost.Recorder.Cancel();
        AppHost.Playback.Stop();
    }

    /// <summary>The mute button is a panic stop, like on Windows. Runs on the engine thread.</summary>
    private void OnControllerButtons(Buttons pressed, InputState _)
    {
        if ((pressed & Buttons.Mute) != 0) RunOnUiThread(StopAll);
    }

    private void ShowPage(Page page)
    {
        if (_page == page) return;
        _page?.OnHidden();
        if (_page is RecorderPage oldRec) oldRec.IsVisible = false;
        _page = page;
        if (page is RecorderPage rec) rec.IsVisible = true;
        _content.RemoveAllViews();
        _content.AddView(page.Root);
        foreach (var (p, tab) in _tabs) U.SetPrimary(tab, p == page);
        page.Refresh();
        page.Tick();
    }

    private void UpdateConnection()
    {
        var kind = AppHost.Kind;
        var cur = AppHost.Controllers.Current;
        (string text, string detail, Android.Graphics.Color color) = kind switch
        {
            ControllerKind.Usb => ("Connected by USB", "Full support: rumble, adaptive triggers, lights, touchpad and motion.", U.Ok),
            ControllerKind.Bluetooth => ("Connected over Bluetooth (limited)",
                "Rumble, lightbar colour and trigger pressure only. Adaptive triggers, touchpad and player LEDs need a USB cable.", U.Warn),
            _ => ("No controller",
                AppHost.Controllers.LastError ?? "Plug in the DualSense with a USB-C OTG cable (full support), or pair it over Bluetooth in Android settings.",
                U.Danger),
        };
        _status.Text = text + (cur?.IsEdge == true ? " · Edge" : "");
        _statusDetail.Text = detail;
        _statusDot.Background = U.Rounded(color, U.Dp(this, 5));
        _usbButton.Visibility = AppHost.Transports.WaitingForUsbPermission && kind == ControllerKind.None ? ViewStates.Visible : ViewStates.Gone;
        _lastKind = kind;
        _lastError = AppHost.Controllers.LastError;
        _page?.Refresh();
    }

    private void Tick()
    {
        if (!_resumed) return;
        try
        {
            if (AppHost.Kind != _lastKind || (AppHost.Kind == ControllerKind.None && AppHost.Controllers.LastError != _lastError))
                UpdateConnection();
            var src = AppHost.Playback.Active;
            _nowPlaying.Text = src == null ? "Nothing playing" : "▶ " + src.DisplayName;
            _page?.Tick();
        }
        finally
        {
            _handler.PostDelayed(Tick, 33);
        }
    }

    protected override void OnResume()
    {
        base.OnResume();
        _resumed = true;
        _handler.RemoveCallbacksAndMessages(null);
        _handler.Post(Tick);
        AppHost.Controllers.TryConnect();
    }

    protected override void OnPause()
    {
        _resumed = false;
        _page?.OnHidden();
        AppHost.Settings.SaveNow();
        base.OnPause();
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        if (intent?.Action == UsbManager.ActionUsbDeviceAttached) AppHost.Transports.OnUsbAttachedIntent();
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        foreach (var (p, _) in _tabs)
            if (p is LibraryPage lib) lib.OnActivityResult(requestCode, resultCode, data);
    }

    // Over Bluetooth the controller's input arrives as normal Android events; hand them to the transport
    // (and keep them away from UI navigation, so Circle doesn't act as Back while you play).
    public override bool DispatchKeyEvent(KeyEvent? e)
    {
        if (e != null && AppHost.Transports.BluetoothTransport?.OnKey(e) == true) return true;
        return base.DispatchKeyEvent(e);
    }

    public override bool DispatchGenericMotionEvent(MotionEvent? e)
    {
        if (e != null && AppHost.Transports.BluetoothTransport?.OnMotion(e) == true) return true;
        return base.DispatchGenericMotionEvent(e);
    }

    protected override void OnDestroy()
    {
        _handler.RemoveCallbacksAndMessages(null);
        AppHost.Engine.ButtonsPressed -= OnControllerButtons;
        AppHost.Controllers.ConnectionChanged -= OnConnectionChanged;
        foreach (var (p, _) in _tabs) p.Detach();
        if (IsFinishing) StopAll();
        base.OnDestroy();
    }
}
