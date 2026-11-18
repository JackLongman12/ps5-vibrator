using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DualSenseStudio.Core.Haptics;

namespace DualSenseStudio.Controls;

/// <summary>
/// Draws a pattern's channels as a waveform with a playhead. When <see cref="Editable"/> is on,
/// left-drag paints values onto <see cref="PaintChannel"/> and right-drag erases.
/// </summary>
public sealed class PatternView : FrameworkElement
{
    private static readonly Brush Bg = Freeze(new SolidColorBrush(Color.FromRgb(0x0A, 0x11, 0x20)));
    private static readonly Pen GridPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255))), 1));
    private static readonly Pen PlayheadPen = Freeze(new Pen(Brushes.White, 1.5));
    private static readonly Brush LeftFill = Freeze(new SolidColorBrush(Color.FromArgb(105, 0x5B, 0x93, 0xFF)));
    private static readonly Pen LeftPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0x5B, 0x93, 0xFF))), 1.5));
    private static readonly Pen RightPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x4D, 0x6A))), 1.5));
    private static readonly Pen LtPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0xF0, 0x6E, 0xB4))), 1.2) { DashStyle = DashStyles.Dash });
    private static readonly Pen RtPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0x1F, 0xC7, 0xA6))), 1.2) { DashStyle = DashStyles.Dash });
    private static readonly Brush LabelBrush = Freeze(new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)));
    private static readonly Typeface LabelFace = new("Segoe UI");

    private Pattern? _pattern;
    private bool _painting;
    private int _lastPaintIndex = -1;

    public Pattern? Pattern
    {
        get => _pattern;
        set { _pattern = value; InvalidateVisual(); }
    }

    /// <summary>Playhead position 0..1, or negative to hide.</summary>
    public double Playhead
    {
        get => _playhead;
        set { if (Math.Abs(_playhead - value) > 0.0005) { _playhead = value; InvalidateVisual(); } }
    }
    private double _playhead = -1;

    /// <summary>If &gt; pattern length, the view is scaled to this many samples (live recording into a fixed length).</summary>
    public int MinDisplaySamples { get; set; }

    public bool Editable { get; set; }
    public Channel PaintChannel { get; set; } = Channel.Left;
    public bool ShowGrid { get; set; } = true;
    public bool Compact { get; set; }

    /// <summary>Raised before a paint stroke modifies the pattern (for undo).</summary>
    public event Action? StrokeStarted;
    /// <summary>Raised after a paint stroke finishes.</summary>
    public event Action? StrokeFinished;

    public PatternView()
    {
        ClipToBounds = true;
        SnapsToDevicePixels = true;
    }

    private static T Freeze<T>(T f) where T : Freezable
    {
        f.Freeze();
        return f;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        var rect = new Rect(0, 0, w, h);
        dc.DrawRoundedRectangle(Bg, null, rect, 8, 8);

        var p = _pattern;
        if (p == null || p.Length == 0)
        {
            if (!Compact)
            {
                var ft = Text("No pattern", 12);
                dc.DrawText(ft, new Point((w - ft.Width) / 2, (h - ft.Height) / 2));
            }
            return;
        }

        int n = Math.Max(p.Length, MinDisplaySamples);
        double pad = Compact ? 2 : 6;
        double top = pad, bottom = h - pad - (Compact ? 0 : 14);
        double plotH = Math.Max(1, bottom - top);

        if (ShowGrid && !Compact)
        {
            double secs = (double)n / p.SampleRate;
            double step = secs > 30 ? 5 : secs > 10 ? 2 : secs > 4 ? 1 : 0.5;
            for (double s = 0; s <= secs + 0.0001; s += step)
            {
                double x = s * p.SampleRate / n * w;
                dc.DrawLine(GridPen, new Point(x, top), new Point(x, bottom));
                var ft = Text(s.ToString(step < 1 ? "0.0" : "0", CultureInfo.InvariantCulture) + "s", 10);
                dc.DrawText(ft, new Point(Math.Min(x + 2, w - ft.Width - 2), bottom + 1));
            }
            dc.DrawLine(GridPen, new Point(0, top + plotH / 2), new Point(w, top + plotH / 2));
        }

        DrawChannel(dc, p.Left, n, w, top, plotH, LeftPen, LeftFill);
        DrawChannel(dc, p.Right, n, w, top, plotH, RightPen, null);
        if (p.LeftTrigger.Any(b => b > 0)) DrawChannel(dc, p.LeftTrigger, n, w, top, plotH, LtPen, null);
        if (p.RightTrigger.Any(b => b > 0)) DrawChannel(dc, p.RightTrigger, n, w, top, plotH, RtPen, null);

        if (_playhead >= 0)
        {
            double x = _playhead * p.Length / n * w;
            dc.DrawLine(PlayheadPen, new Point(x, 0), new Point(x, h));
        }
    }

    private static void DrawChannel(DrawingContext dc, byte[] data, int n, double w, double top, double plotH, Pen pen, Brush? fill)
    {
        if (data.Length == 0) return;
        int pixels = Math.Max(2, (int)w);
        int count = Math.Min(data.Length, n);
        double samplesPerPx = (double)n / pixels;

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            bool started = false;
            double lastX = 0;
            // Downsample with per-pixel max so short spikes stay visible.
            for (int px = 0; px < pixels; px++)
            {
                int a = (int)(px * samplesPerPx);
                if (a >= count) break;
                int b = Math.Min(count, Math.Max(a + 1, (int)((px + 1) * samplesPerPx)));
                byte v = 0;
                for (int i = a; i < b; i++) v = Math.Max(v, data[i]);
                double x = (double)px / pixels * w;
                double y = top + plotH * (1 - v / 255.0);
                if (!started)
                {
                    ctx.BeginFigure(new Point(x, fill != null ? top + plotH : y), fill != null, false);
                    if (fill != null) ctx.LineTo(new Point(x, y), true, false);
                    started = true;
                }
                else ctx.LineTo(new Point(x, y), true, false);
                lastX = x;
            }
            if (started && fill != null) ctx.LineTo(new Point(lastX, top + plotH), false, false);
        }
        geo.Freeze();
        dc.DrawGeometry(fill, pen, geo);
    }

    private FormattedText Text(string s, double size) =>
        new(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, LabelFace, size, LabelBrush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

    // ---- painting -----------------------------------------------------------------

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (!Editable || _pattern == null || _pattern.Length == 0) return;
        if (e.ChangedButton is not (MouseButton.Left or MouseButton.Right)) return;
        StrokeStarted?.Invoke();
        _painting = true;
        _lastPaintIndex = -1;
        CaptureMouse();
        PaintAt(e.GetPosition(this), e.ChangedButton == MouseButton.Right);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_painting) return;
        bool erase = e.RightButton == MouseButtonState.Pressed && e.LeftButton != MouseButtonState.Pressed;
        PaintAt(e.GetPosition(this), erase);
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (!_painting) return;
        _painting = false;
        ReleaseMouseCapture();
        StrokeFinished?.Invoke();
    }

    private void PaintAt(Point pos, bool erase)
    {
        var p = _pattern!;
        double w = ActualWidth, h = ActualHeight;
        int n = Math.Max(p.Length, MinDisplaySamples);
        double pad = Compact ? 2 : 6;
        double top = pad, bottom = h - pad - (Compact ? 0 : 14);
        int idx = Math.Clamp((int)(pos.X / w * n), 0, p.Length - 1);
        double v = erase ? 0 : Math.Clamp(1 - (pos.Y - top) / (bottom - top), 0, 1);
        byte value = (byte)Math.Round(v * 255);
        var data = p[PaintChannel];

        if (_lastPaintIndex < 0) data[idx] = value;
        else
        {
            int a = Math.Min(_lastPaintIndex, idx), b = Math.Max(_lastPaintIndex, idx);
            byte va = data[_lastPaintIndex];
            for (int i = a; i <= b; i++)
            {
                double t = b == a ? 1 : (double)(i - _lastPaintIndex) / (idx - _lastPaintIndex);
                data[i] = (byte)Math.Round(va + (value - va) * Math.Clamp(t, 0, 1));
            }
        }
        data[idx] = value;
        _lastPaintIndex = idx;
        InvalidateVisual();
    }
}
