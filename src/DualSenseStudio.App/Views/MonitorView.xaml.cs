using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DualSenseStudio.Core.Protocol;

namespace DualSenseStudio.Views;

public partial class MonitorView : UserControl
{
    private static readonly (Buttons Flag, string Label)[] ButtonList =
    [
        (Buttons.Cross, "✕"), (Buttons.Circle, "○"), (Buttons.Square, "□"), (Buttons.Triangle, "△"),
        (Buttons.DPadUp, "▲"), (Buttons.DPadDown, "▼"), (Buttons.DPadLeft, "◀"), (Buttons.DPadRight, "▶"),
        (Buttons.L1, "L1"), (Buttons.R1, "R1"), (Buttons.L2, "L2"), (Buttons.R2, "R2"), (Buttons.L3, "L3"), (Buttons.R3, "R3"),
        (Buttons.Create, "Create"), (Buttons.Options, "Options"), (Buttons.PS, "PS"), (Buttons.Touchpad, "Pad click"), (Buttons.Mute, "Mute"),
    ];

    private readonly DispatcherTimer _timer;
    private readonly List<(Buttons Flag, Border Chip)> _chips = new();
    private readonly Brush _on, _off;
    private long _lastReports;
    private DateTime _lastRateAt = DateTime.Now;
    private double _rate;

    public MonitorView()
    {
        InitializeComponent();
        _on = (Brush)FindResource("AccentBrush");
        _off = (Brush)FindResource("ChipBrush");
        foreach (var (flag, label) in ButtonList)
        {
            var chip = new Border
            {
                Background = _off,
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(0, 0, 6, 6),
                Child = new TextBlock
                {
                    Text = label,
                    FontWeight = flag is Buttons.Cross or Buttons.Circle or Buttons.Square or Buttons.Triangle ? FontWeights.Bold : FontWeights.Normal,
                    // Face buttons in their PlayStation symbol colours.
                    Foreground = (Brush)FindResource(flag switch
                    {
                        Buttons.Cross => "CrossBrush",
                        Buttons.Circle => "CircleBrush",
                        Buttons.Square => "SquareBrush",
                        Buttons.Triangle => "TriangleBrush",
                        _ => "TextBrush",
                    }),
                },
            };
            ButtonsPanel.Children.Add(chip);
            _chips.Add((flag, chip));
        }
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += (_, _) => Tick();
        Loaded += (_, _) => _timer.Start();
        Unloaded += (_, _) => _timer.Stop();
    }

    private void Tick()
    {
        var s = AppHost.Engine.Input;
        L2Bar.Value = s.L2Value;
        R2Bar.Value = s.R2Value;
        L2Text.Text = s.L2.ToString();
        R2Text.Text = s.R2.ToString();
        PlaceStick(LeftStick, LeftDot, s.LeftX, s.LeftY);
        PlaceStick(RightStick, RightDot, s.RightX, s.RightY);
        foreach (var (flag, chip) in _chips) chip.Background = s.IsPressed(flag) ? _on : _off;

        PlaceTouch(T0, s.Touch0);
        PlaceTouch(T1, s.Touch1);
        TouchText.Text = $"#1 {(s.Touch0.Active ? $"{s.Touch0.X},{s.Touch0.Y}" : "-")}    #2 {(s.Touch1.Active ? $"{s.Touch1.X},{s.Touch1.Y}" : "-")}";
        GyroText.Text = $"gyro   x{s.GyroX,7} y{s.GyroY,7} z{s.GyroZ,7}";
        AccelText.Text = $"accel  x{s.AccelX,7} y{s.AccelY,7} z{s.AccelZ,7}";

        var c = AppHost.Controllers.Current;
        if (c == null)
        {
            ConnInfo.Text = "not connected";
            return;
        }
        var now = DateTime.Now;
        if ((now - _lastRateAt).TotalSeconds >= 1)
        {
            long reports = c.ReportCount;
            _rate = (reports - _lastReports) / (now - _lastRateAt).TotalSeconds;
            _lastReports = reports;
            _lastRateAt = now;
        }
        ConnInfo.Text =
            $"link      {(c.IsBluetooth ? "Bluetooth" : "USB")}{(c.IsEdge ? " (Edge)" : "")}\n" +
            $"reports   {_rate:0} Hz ({(s.FullReport ? "full" : "simple, sensors unavailable")})\n" +
            $"firmware  {c.FirmwareText}\n" +
            $"rumble    {(c.SupportsRumbleV2 ? "v2" : "v1 (legacy)")}, mode {AppHost.Engine.RumbleMode}\n" +
            $"engine    {AppHost.Engine.TickCount} ticks";
    }

    private static void PlaceStick(Canvas area, FrameworkElement dot, byte x, byte y)
    {
        double w = 108, h = 108;
        Canvas.SetLeft(dot, x / 255.0 * (w - dot.Width));
        Canvas.SetTop(dot, y / 255.0 * (h - dot.Height));
    }

    private void PlaceTouch(FrameworkElement dot, TouchPoint p)
    {
        dot.Visibility = p.Active ? Visibility.Visible : Visibility.Collapsed;
        if (!p.Active) return;
        Canvas.SetLeft(dot, p.X / (double)TouchPoint.MaxX * TouchCanvas.ActualWidth - dot.Width / 2);
        Canvas.SetTop(dot, p.Y / (double)TouchPoint.MaxY * TouchCanvas.ActualHeight - dot.Height / 2);
    }
}
