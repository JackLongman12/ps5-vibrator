using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using DualSenseStudio.Core.Haptics;
using DualSenseStudio.Core.Protocol;
using DualSenseStudio.Droid.Ui;
using Color = Android.Graphics.Color;
using U = DualSenseStudio.Droid.Ui.Ui;
using View = Android.Views.View;

namespace DualSenseStudio.Droid.Pages;

public sealed class LivePage(Activity activity) : Page(activity)
{
    private Button _touchButton = null!, _motionButton = null!;
    private TextView _touchBanner = null!, _motionBanner = null!, _motionText = null!;
    private LinearLayout _touchCard = null!, _motionCard = null!;
    private TouchpadView _pad = null!;
    private ProgressBar _pitch = null!;
    private ProgressBar _roll = null!;

    public override string Title => "Live";

    protected override View Build()
    {
        var c = Activity;
        var col = U.Column(c);
        col.AddView(U.Heading(c, "Live Play"));
        col.AddView(U.Help(c, "Play the vibration in real time from the controller itself."));

        // Touchpad
        _touchCard = U.Card(c, col);
        _touchCard.AddView(U.Text(c, "Touchpad XY pad", 15, U.Text1, bold: true));
        _touchBanner = U.Banner(c, "Not available over Bluetooth: Android does not pass the DualSense touchpad's finger positions to apps. Use a USB cable (OTG).");
        _touchCard.AddView(_touchBanner);
        _touchCard.AddView(U.Help(c, "One finger: up/down = strength, left/right = texture (smooth → fast pulses). Two fingers: the first drives the left side, the second the right side."));
        _pad = new TouchpadView(c);
        _touchCard.AddView(_pad, U.Match(U.Dp(c, 120)));
        _touchButton = U.Button(c, "▶  Start touchpad", () => Toggle(AppHost.Touchpad), primary: true);
        _touchCard.AddView(_touchButton);
        _touchCard.AddView(new SliderRow(c, "Gain", 0.1, 1, 0.05, AppHost.Touchpad.Gain, v => $"{v * 100:0}%", v => AppHost.Touchpad.Gain = v));

        // Motion
        _motionCard = U.Card(c, col);
        _motionCard.AddView(U.Text(c, "Motion control", 15, U.Text1, bold: true));
        _motionBanner = U.Banner(c, "");
        _motionCard.AddView(_motionBanner);
        _motionCard.AddView(U.Help(c, "Tilt forward/back = strength, tilt left/right = balance, shake = burst. Hold the controller in a neutral pose and press Calibrate."));
        var mrow = U.Flow(c);
        _motionButton = U.Button(c, "▶  Start motion", () =>
        {
            if (AppHost.Playback.Active != AppHost.Motion) AppHost.Motion.Calibrate();
            Toggle(AppHost.Motion);
        }, primary: true);
        mrow.AddView(_motionButton);
        mrow.AddView(U.Button(c, "Calibrate", AppHost.Motion.Calibrate));
        _motionCard.AddView(mrow);
        _motionCard.AddView(new SliderRow(c, "Gain", 0.1, 1, 0.05, AppHost.Motion.Gain, v => $"{v * 100:0}%", v => AppHost.Motion.Gain = v));
        _motionCard.AddView(new SliderRow(c, "Full strength at tilt", 15, 90, 1, AppHost.Motion.MaxTiltDegrees, v => $"{v:0}°", v => AppHost.Motion.MaxTiltDegrees = v));
        _motionCard.AddView(U.Check(c, "Shake for a burst", AppHost.Motion.ShakeBursts, v => AppHost.Motion.ShakeBursts = v));
        _motionCard.AddView(U.Text(c, "Strength (pitch)", 12, U.Muted));
        _pitch = U.Meter(c);
        _motionCard.AddView(_pitch);
        _motionCard.AddView(U.Text(c, "Balance (roll, centre = both sides)", 12, U.Muted));
        _roll = U.Meter(c, U.AccentOrange);
        _motionCard.AddView(_roll);
        _motionText = U.Help(c, "");
        _motionCard.AddView(_motionText);

        // Audio reactive
        var audio = U.Card(c, col);
        audio.AddView(U.Text(c, "Audio reactive", 15, U.Text1, bold: true));
        audio.AddView(U.Banner(c, "Not available on Android yet. On Windows this follows the audio playing on the PC, and Android does not let apps capture other apps' audio without screen-capture permission. Unavailable over Bluetooth and USB."));
        return col;
    }

    private static void Toggle(IHapticSource s)
    {
        if (AppHost.Playback.Active == s) AppHost.Playback.Stop();
        else AppHost.Playback.SetSource(s);
    }

    public override void Refresh()
    {
        var kind = AppHost.Kind;
        bool touch = kind != ControllerKind.Bluetooth;
        Show(_touchBanner, !touch);
        _touchButton.Enabled = touch;
        _touchButton.Alpha = touch ? 1 : 0.4f;
        _pad.Alpha = touch ? 1 : 0.4f;
        if (!touch && AppHost.Playback.Active == AppHost.Touchpad) AppHost.Playback.Stop();

        bool motion = kind != ControllerKind.Bluetooth || AppHost.HasMotion;
        if (kind == ControllerKind.Bluetooth && AppHost.HasMotion)
        {
            _motionBanner.Text = "Experimental over Bluetooth: motion comes from Android's controller sensor API, so axes and scale may differ from USB.";
            Show(_motionBanner, true);
        }
        else if (kind == ControllerKind.Bluetooth)
        {
            _motionBanner.Text = "Not available over Bluetooth on this phone: Android does not expose the controller's motion sensors here. Use a USB cable (OTG).";
            Show(_motionBanner, true);
        }
        else Show(_motionBanner, false);
        _motionButton.Enabled = motion;
        _motionButton.Alpha = motion ? 1 : 0.4f;
        if (!motion && AppHost.Playback.Active == AppHost.Motion) AppHost.Playback.Stop();
    }

    public override void Tick()
    {
        var input = AppHost.Engine.Input;
        _pad.Update(input.Touch0, input.Touch1);
        _touchButton.Text = AppHost.Playback.Active == AppHost.Touchpad ? "■  Stop touchpad" : "▶  Start touchpad";
        var m = AppHost.Motion;
        bool active = AppHost.Playback.Active == m;
        _motionButton.Text = active ? "■  Stop motion" : "▶  Start motion";
        if (active)
        {
            U.SetMeter(_pitch, Math.Abs(m.Pitch) / m.MaxTiltDegrees);
            U.SetMeter(_roll, 0.5 + Math.Clamp(m.Roll / m.MaxTiltDegrees, -1, 1) / 2);
            _motionText.Text = $"pitch {m.Pitch:+0;-0}°  roll {m.Roll:+0;-0}°  shake {m.ShakeG:0.0} g";
        }
        else
        {
            U.SetMeter(_pitch, 0);
            U.SetMeter(_roll, 0.5);
            _motionText.Text = input.FullReport ? "Start motion to see tilt." : "Motion data not available yet.";
        }
    }
}

/// <summary>Shows where fingers are on the controller's touchpad.</summary>
public sealed class TouchpadView : View
{
    private readonly Paint _bg = new() { Color = Color.ParseColor("#14171E"), AntiAlias = true };
    private readonly Paint _border = new() { Color = Color.Argb(60, 255, 255, 255), AntiAlias = true, StrokeWidth = 2 };
    private readonly Paint _f0 = new() { Color = Color.ParseColor("#3D7BFF"), AntiAlias = true };
    private readonly Paint _f1 = new() { Color = Color.ParseColor("#FF8A3D"), AntiAlias = true };
    private TouchPoint _a, _b;

    public TouchpadView(Context c) : base(c)
    {
        _border.SetStyle(Paint.Style.Stroke);
    }

    public void Update(TouchPoint a, TouchPoint b)
    {
        if (a == _a && b == _b) return;
        _a = a;
        _b = b;
        Invalidate();
    }

    protected override void OnDraw(Canvas canvas)
    {
        float d = Resources!.DisplayMetrics!.Density;
        canvas.DrawRoundRect(0, 0, Width, Height, 10 * d, 10 * d, _bg);
        canvas.DrawRoundRect(1, 1, Width - 1, Height - 1, 10 * d, 10 * d, _border);
        Dot(canvas, _a, _f0, d);
        Dot(canvas, _b, _f1, d);
    }

    private void Dot(Canvas canvas, TouchPoint p, Paint paint, float d)
    {
        if (!p.Active) return;
        float x = p.X / (float)TouchPoint.MaxX * Width;
        float y = p.Y / (float)TouchPoint.MaxY * Height;
        canvas.DrawCircle(x, y, 9 * d, paint);
    }
}
