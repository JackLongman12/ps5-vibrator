using Android.Content;
using Android.Graphics;
using Android.Views;
using DualSenseStudio.Core.Haptics;
using Color = Android.Graphics.Color;

namespace DualSenseStudio.Droid.Ui;

/// <summary>
/// Draws a pattern's channels as a waveform with a playhead (port of the WPF PatternView).
/// When <see cref="Editable"/> is on, dragging a finger paints values onto <see cref="PaintChannel"/>.
/// </summary>
public sealed class PatternView : View
{
    private static readonly Color BgColor = Color.ParseColor("#14171E");
    private readonly Paint _bg = new() { Color = BgColor, AntiAlias = true };
    private readonly Paint _grid = new() { Color = Color.Argb(40, 255, 255, 255), StrokeWidth = 1 };
    private readonly Paint _playhead = new() { Color = Color.White, AntiAlias = true };
    private readonly Paint _leftFill = new() { Color = Color.Argb(110, 0x3D, 0x7B, 0xFF), AntiAlias = true };
    private readonly Paint _left = Stroke(Color.ParseColor("#3D7BFF"), 1.6f);
    private readonly Paint _right = Stroke(Color.ParseColor("#FF8A3D"), 1.6f);
    private readonly Paint _lt = Stroke(Color.ParseColor("#A66BFF"), 1.2f, dashed: true);
    private readonly Paint _rt = Stroke(Color.ParseColor("#2ED3B7"), 1.2f, dashed: true);
    private readonly Paint _label = new() { Color = Color.Argb(150, 255, 255, 255), AntiAlias = true };
    private readonly Android.Graphics.Path _path = new();
    private readonly float _density;
    private Pattern? _pattern;
    private double _playheadPos = -1;
    private bool _painting;
    private int _lastPaintIndex = -1;

    public PatternView(Context c) : base(c)
    {
        _density = c.Resources!.DisplayMetrics!.Density;
        foreach (var p in new[] { _left, _right, _lt, _rt }) p.StrokeWidth *= _density;
        _playhead.StrokeWidth = 1.5f * _density;
        _label.TextSize = 10 * _density;
    }

    private static Paint Stroke(Color c, float w, bool dashed = false)
    {
        var p = new Paint { Color = c, StrokeWidth = w, AntiAlias = true };
        p.SetStyle(Paint.Style.Stroke);
        p.StrokeJoin = Paint.Join.Round;
        if (dashed) p.SetPathEffect(new DashPathEffect([8f, 6f], 0));
        return p;
    }

    public Pattern? Pattern
    {
        get => _pattern;
        set { _pattern = value; Invalidate(); }
    }

    /// <summary>Playhead position 0..1, or negative to hide.</summary>
    public double Playhead
    {
        get => _playheadPos;
        set { if (Math.Abs(_playheadPos - value) > 0.0005) { _playheadPos = value; Invalidate(); } }
    }

    /// <summary>If larger than the pattern, the view is scaled to this many samples (live recording into a fixed length).</summary>
    public int MinDisplaySamples { get; set; }
    public bool Editable { get; set; }
    public Channel PaintChannel { get; set; } = Channel.Left;
    public bool Compact { get; set; }
    /// <summary>When painting, draw zero instead of following the finger height.</summary>
    public bool Erase { get; set; }

    public event Action? StrokeStarted;
    public event Action? StrokeFinished;

    private float Pad => (Compact ? 2 : 6) * _density;
    private float BottomInset => Compact ? 0 : 14 * _density;

    protected override void OnDraw(Canvas canvas)
    {
        float w = Width, h = Height;
        if (w <= 0 || h <= 0) return;
        canvas.DrawRoundRect(0, 0, w, h, 8 * _density, 8 * _density, _bg);
        var p = _pattern;
        if (p == null || p.Length == 0)
        {
            if (!Compact)
            {
                const string s = "No pattern";
                canvas.DrawText(s, (w - _label.MeasureText(s)) / 2, h / 2, _label);
            }
            return;
        }

        int n = Math.Max(p.Length, MinDisplaySamples);
        float top = Pad, bottom = h - Pad - BottomInset;
        float plotH = Math.Max(1, bottom - top);

        if (!Compact)
        {
            double secs = (double)n / p.SampleRate;
            double step = secs > 30 ? 5 : secs > 10 ? 2 : secs > 4 ? 1 : 0.5;
            for (double s = 0; s <= secs + 0.0001; s += step)
            {
                float x = (float)(s * p.SampleRate / n * w);
                canvas.DrawLine(x, top, x, bottom, _grid);
                string label = s.ToString(step < 1 ? "0.0" : "0", System.Globalization.CultureInfo.InvariantCulture) + "s";
                canvas.DrawText(label, Math.Min(x + 2 * _density, w - _label.MeasureText(label) - 2), h - 3 * _density, _label);
            }
            canvas.DrawLine(0, top + plotH / 2, w, top + plotH / 2, _grid);
        }

        DrawChannel(canvas, p.Left, n, w, top, plotH, _left, _leftFill);
        DrawChannel(canvas, p.Right, n, w, top, plotH, _right, null);
        if (p.LeftTrigger.Any(b => b > 0)) DrawChannel(canvas, p.LeftTrigger, n, w, top, plotH, _lt, null);
        if (p.RightTrigger.Any(b => b > 0)) DrawChannel(canvas, p.RightTrigger, n, w, top, plotH, _rt, null);

        if (_playheadPos >= 0)
        {
            float x = (float)(_playheadPos * p.Length / n * w);
            canvas.DrawLine(x, 0, x, h, _playhead);
        }
    }

    private void DrawChannel(Canvas canvas, byte[] data, int n, float w, float top, float plotH, Paint stroke, Paint? fill)
    {
        if (data.Length == 0) return;
        int pixels = Math.Max(2, (int)(w / _density));
        int count = Math.Min(data.Length, n);
        double samplesPerPx = (double)n / pixels;
        _path.Reset();
        bool started = false;
        float lastX = 0;
        // Downsample with per-column max so short spikes stay visible.
        for (int px = 0; px < pixels; px++)
        {
            int a = (int)(px * samplesPerPx);
            if (a >= count) break;
            int b = Math.Min(count, Math.Max(a + 1, (int)((px + 1) * samplesPerPx)));
            byte v = 0;
            for (int i = a; i < b; i++) v = Math.Max(v, data[i]);
            float x = (float)px / pixels * w;
            float y = top + plotH * (1 - v / 255f);
            if (!started)
            {
                _path.MoveTo(x, y);
                started = true;
            }
            else _path.LineTo(x, y);
            lastX = x;
        }
        if (!started) return;
        if (fill != null)
        {
            using var area = new Android.Graphics.Path(_path);
            area.LineTo(lastX, top + plotH);
            area.LineTo(0, top + plotH);
            area.Close();
            canvas.DrawPath(area, fill);
        }
        canvas.DrawPath(_path, stroke);
    }

    public override bool OnTouchEvent(MotionEvent? e)
    {
        if (e == null || !Editable || _pattern == null || _pattern.Length == 0) return base.OnTouchEvent(e);
        switch (e.ActionMasked)
        {
            case MotionEventActions.Down:
                Parent?.RequestDisallowInterceptTouchEvent(true); // keep the ScrollView from stealing the stroke
                StrokeStarted?.Invoke();
                _painting = true;
                _lastPaintIndex = -1;
                PaintAt(e.GetX(), e.GetY());
                return true;
            case MotionEventActions.Move when _painting:
                for (int i = 0; i < e.HistorySize; i++) PaintAt(e.GetHistoricalX(i), e.GetHistoricalY(i));
                PaintAt(e.GetX(), e.GetY());
                return true;
            case MotionEventActions.Up or MotionEventActions.Cancel when _painting:
                _painting = false;
                Parent?.RequestDisallowInterceptTouchEvent(false);
                StrokeFinished?.Invoke();
                return true;
        }
        return base.OnTouchEvent(e);
    }

    private void PaintAt(float px, float py)
    {
        var p = _pattern!;
        float w = Width, h = Height;
        int n = Math.Max(p.Length, MinDisplaySamples);
        float top = Pad, bottom = h - Pad - BottomInset;
        int idx = Math.Clamp((int)(px / w * n), 0, p.Length - 1);
        double v = Erase ? 0 : Math.Clamp(1 - (py - top) / (bottom - top), 0, 1);
        byte value = (byte)Math.Round(v * 255);
        var data = p[PaintChannel];
        if (_lastPaintIndex >= 0 && _lastPaintIndex != idx)
        {
            int a = Math.Min(_lastPaintIndex, idx), b = Math.Max(_lastPaintIndex, idx);
            byte va = data[_lastPaintIndex];
            for (int i = a; i <= b; i++)
            {
                double t = (double)(i - _lastPaintIndex) / (idx - _lastPaintIndex);
                data[i] = (byte)Math.Round(va + (value - va) * Math.Clamp(t, 0, 1));
            }
        }
        data[idx] = value;
        _lastPaintIndex = idx;
        Invalidate();
    }
}
