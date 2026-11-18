using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DualSenseStudio.Core.Haptics;
using DualSenseStudio.Core.Protocol;

namespace DualSenseStudio.Views;

public partial class LivePlayView : UserControl
{
    private readonly DispatcherTimer _timer;

    public LivePlayView()
    {
        InitializeComponent();
        TiltText.Text = "50°";
        AppHost.Playback.Changed += OnPlaybackChanged;
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += (_, _) => Tick();
        Loaded += (_, _) => { _timer.Start(); OnPlaybackChanged(); };
        Unloaded += (_, _) => _timer.Stop();
    }

    private static bool IsActive(IHapticSource s) => AppHost.Playback.Active == s;

    private void OnPlaybackChanged()
    {
        // Release the audio device whenever audio reactive is no longer the active source.
        if (!IsActive(AppHost.Audio) && AppHost.Audio.IsRunning) AppHost.Audio.Stop();
        TouchButton.Content = IsActive(AppHost.Touchpad) ? "■  Stop touchpad" : "▶  Start touchpad";
        MotionButton.Content = IsActive(AppHost.Motion) ? "■  Stop motion" : "▶  Start motion";
        AudioButton.Content = IsActive(AppHost.Audio) ? "■  Stop audio reactive" : "▶  Start audio reactive";
    }

    private static void Toggle(IHapticSource s)
    {
        if (IsActive(s)) AppHost.Playback.Stop();
        else AppHost.Playback.SetSource(s);
    }

    private void OnTouch(object sender, RoutedEventArgs e) => Toggle(AppHost.Touchpad);

    private void OnMotion(object sender, RoutedEventArgs e)
    {
        if (!IsActive(AppHost.Motion)) AppHost.Motion.Calibrate();
        Toggle(AppHost.Motion);
    }

    private void OnAudio(object sender, RoutedEventArgs e)
    {
        if (IsActive(AppHost.Audio))
        {
            AppHost.Playback.Stop();
            return;
        }
        AppHost.Audio.Start();
        AudioError.Text = AppHost.Audio.Error ?? "";
        if (AppHost.Audio.IsRunning) AppHost.Playback.SetSource(AppHost.Audio);
    }

    private void OnCalibrate(object sender, RoutedEventArgs e) => AppHost.Motion.Calibrate();

    private void OnTouchGain(object sender, RoutedPropertyChangedEventArgs<double> e) => AppHost.Touchpad.Gain = e.NewValue;
    private void OnMotionGain(object sender, RoutedPropertyChangedEventArgs<double> e) => AppHost.Motion.Gain = e.NewValue;
    private void OnShake(object sender, RoutedEventArgs e) => AppHost.Motion.ShakeBursts = ShakeCheck.IsChecked == true;

    private void OnTilt(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        AppHost.Motion.MaxTiltDegrees = e.NewValue;
        if (TiltText != null) TiltText.Text = $"{e.NewValue:0}°";
    }

    private void OnAudioParams(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (AudioGain == null || BassWeight == null || TrebleWeight == null) return;
        AppHost.Audio.Gain = AudioGain.Value;
        AppHost.Audio.BassWeight = BassWeight.Value;
        AppHost.Audio.TrebleWeight = TrebleWeight.Value;
    }

    private void Tick()
    {
        var input = AppHost.Engine.Input;
        PlaceFinger(Finger0, input.Touch0);
        PlaceFinger(Finger1, input.Touch1);

        var m = AppHost.Motion;
        if (IsActive(m))
        {
            PitchMeter.Value = Math.Clamp(Math.Abs(m.Pitch) / m.MaxTiltDegrees, 0, 1);
            RollView.Value = Math.Clamp(m.Roll / m.MaxTiltDegrees, -1, 1);
            MotionText.Text = $"pitch {m.Pitch:+0;-0}°  roll {m.Roll:+0;-0}°  shake {m.ShakeG:0.0} g";
        }
        else MotionText.Text = input.FullReport ? "Start motion to see tilt." : "Motion data not available yet.";

        BassMeter.Value = AppHost.Audio.BassLevel;
        TrebleMeter.Value = AppHost.Audio.TrebleLevel;
    }

    private void PlaceFinger(FrameworkElement dot, TouchPoint p)
    {
        if (!p.Active)
        {
            dot.Visibility = Visibility.Collapsed;
            return;
        }
        dot.Visibility = Visibility.Visible;
        double w = TouchCanvas.ActualWidth, h = TouchCanvas.ActualHeight;
        Canvas.SetLeft(dot, p.X / (double)TouchPoint.MaxX * w - dot.Width / 2);
        Canvas.SetTop(dot, p.Y / (double)TouchPoint.MaxY * h - dot.Height / 2);
    }
}
