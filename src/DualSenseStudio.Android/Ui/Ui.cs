using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Views;
using Android.Widget;
using Color = Android.Graphics.Color;

namespace DualSenseStudio.Droid.Ui;

/// <summary>Small helpers for building the (code-only) UI with a consistent dark look.</summary>
public static class Ui
{
    public static readonly Color Bg = Color.ParseColor("#0F1115");
    public static readonly Color CardBg = Color.ParseColor("#1A1D24");
    public static readonly Color CardActive = Color.ParseColor("#22304D");
    public static readonly Color Accent = Color.ParseColor("#3D7BFF");
    public static readonly Color AccentOrange = Color.ParseColor("#FF8A3D");
    public static readonly Color Text1 = Color.ParseColor("#E8EBF0");
    public static readonly Color Muted = Color.ParseColor("#9AA3B2");
    public static readonly Color Warn = Color.ParseColor("#FFB020");
    public static readonly Color Danger = Color.ParseColor("#FF4D5E");
    public static readonly Color Ok = Color.ParseColor("#2ED3B7");
    public static readonly Color Chip = Color.ParseColor("#262A33");

    public static int Dp(Context c, float dp) => (int)(dp * c.Resources!.DisplayMetrics!.Density + 0.5f);

    public static GradientDrawable Rounded(Color fill, float radiusPx, Color? stroke = null, int strokePx = 0)
    {
        var d = new GradientDrawable();
        d.SetColor(fill);
        d.SetCornerRadius(radiusPx);
        if (stroke is { } s) d.SetStroke(strokePx, s);
        return d;
    }

    public static LinearLayout.LayoutParams Match(int height = ViewGroup.LayoutParams.WrapContent) =>
        new(ViewGroup.LayoutParams.MatchParent, height);

    public static LinearLayout.LayoutParams Wrap() =>
        new(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent);

    public static LinearLayout.LayoutParams Weight(float w = 1) => new(0, ViewGroup.LayoutParams.WrapContent, w);

    public static LinearLayout Column(Context c, int paddingDp = 0)
    {
        var l = new LinearLayout(c) { Orientation = Orientation.Vertical };
        if (paddingDp > 0) l.SetPadding(Dp(c, paddingDp), Dp(c, paddingDp), Dp(c, paddingDp), Dp(c, paddingDp));
        return l;
    }

    public static LinearLayout Row(Context c)
    {
        var l = new LinearLayout(c) { Orientation = Orientation.Horizontal };
        l.SetGravity(GravityFlags.CenterVertical);
        return l;
    }

    public static TextView Text(Context c, string text, float sp = 14, Color? color = null, bool bold = false)
    {
        var t = new TextView(c) { Text = text };
        t.SetTextSize(Android.Util.ComplexUnitType.Sp, sp);
        t.SetTextColor(color ?? Text1);
        if (bold) t.SetTypeface(Typeface.Create("sans-serif-medium", TypefaceStyle.Normal), TypefaceStyle.Normal);
        return t;
    }

    public static TextView Heading(Context c, string text)
    {
        var t = Text(c, text, 17, Text1, bold: true);
        t.SetPadding(0, Dp(c, 4), 0, Dp(c, 6));
        return t;
    }

    public static TextView Help(Context c, string text)
    {
        var t = Text(c, text, 13, Muted);
        t.SetPadding(0, 0, 0, Dp(c, 8));
        return t;
    }

    /// <summary>A rounded panel. Add children to the returned layout.</summary>
    public static LinearLayout Card(Context c, ViewGroup? parent = null)
    {
        var l = Column(c, 14);
        l.Background = Rounded(CardBg, Dp(c, 12));
        var lp = Match();
        lp.BottomMargin = Dp(c, 12);
        l.LayoutParameters = lp;
        parent?.AddView(l);
        return l;
    }

    /// <summary>An orange notice used to mark features that are not available with the current connection.</summary>
    public static TextView Banner(Context c, string text)
    {
        var t = Text(c, text, 13, Color.ParseColor("#1A1300"));
        t.Background = Rounded(Warn, Dp(c, 8));
        t.SetPadding(Dp(c, 10), Dp(c, 8), Dp(c, 10), Dp(c, 8));
        var lp = Match();
        lp.BottomMargin = Dp(c, 10);
        t.LayoutParameters = lp;
        return t;
    }

    public static Button Button(Context c, string text, Action onClick, bool primary = false)
    {
        var b = new Button(c) { Text = text };
        b.SetAllCaps(false);
        b.SetTextColor(primary ? Color.White : Text1);
        b.SetTextSize(Android.Util.ComplexUnitType.Sp, 14);
        b.Background = Rounded(primary ? Accent : Chip, Dp(c, 8));
        b.SetPadding(Dp(c, 14), Dp(c, 6), Dp(c, 14), Dp(c, 6));
        b.SetMinHeight(Dp(c, 40));
        b.SetMinimumHeight(Dp(c, 40));
        b.StateListAnimator = null;
        b.Click += (_, _) => onClick();
        var lp = Wrap();
        lp.RightMargin = Dp(c, 8);
        lp.BottomMargin = Dp(c, 6);
        b.LayoutParameters = lp;
        return b;
    }

    public static void SetPrimary(Button b, bool primary)
    {
        b.Background = Rounded(primary ? Accent : Chip, Dp(b.Context!, 8));
        b.SetTextColor(primary ? Color.White : Text1);
    }

    /// <summary>Wraps children onto several lines (buttons, chips).</summary>
    public static FlowLayout Flow(Context c) => new(c);

    public static CheckBox Check(Context c, string text, bool value, Action<bool> changed)
    {
        var cb = new CheckBox(c) { Text = text, Checked = value };
        cb.SetTextColor(Text1);
        cb.ButtonTintList = Android.Content.Res.ColorStateList.ValueOf(Accent);
        cb.CheckedChange += (_, e) => changed(e.IsChecked);
        return cb;
    }

    public static ProgressBar Meter(Context c, Color? color = null)
    {
        var p = new ProgressBar(c, null, Android.Resource.Attribute.ProgressBarStyleHorizontal) { Max = 1000, Indeterminate = false };
        p.ProgressTintList = Android.Content.Res.ColorStateList.ValueOf(color ?? Accent);
        p.LayoutParameters = Match(Dp(c, 10));
        return p;
    }

    public static void SetMeter(ProgressBar p, double v01) => p.Progress = (int)(Math.Clamp(v01, 0, 1) * 1000);

    public static EditText Edit(Context c, string text, string hint = "")
    {
        var e = new EditText(c) { Text = text, Hint = hint };
        e.SetTextColor(Text1);
        e.SetHintTextColor(Muted);
        e.SetSingleLine(true);
        e.BackgroundTintList = Android.Content.Res.ColorStateList.ValueOf(Muted);
        return e;
    }

    public static View Spacer(Context c, int dp)
    {
        var v = new View(c) { LayoutParameters = Match(Dp(c, dp)) };
        return v;
    }
}

/// <summary>Label + value + SeekBar on a continuous range.</summary>
public sealed class SliderRow : LinearLayout
{
    private readonly SeekBar _bar;
    private readonly TextView _value;
    private readonly double _min, _step;
    private readonly Func<double, string> _format;
    private bool _setting;

    public SliderRow(Context c, string label, double min, double max, double step, double value, Func<double, string> format, Action<double>? changed = null)
        : base(c)
    {
        Orientation = Orientation.Vertical;
        _min = min;
        _step = step;
        _format = format;
        var top = Ui.Row(c);
        top.AddView(Ui.Text(c, label, 13, Ui.Muted), Ui.Weight());
        _value = Ui.Text(c, "", 13, Ui.Text1);
        top.AddView(_value);
        AddView(top, Ui.Match());
        _bar = new SeekBar(c) { Max = (int)Math.Round((max - min) / step) };
        _bar.ProgressTintList = Android.Content.Res.ColorStateList.ValueOf(Ui.Accent);
        _bar.ThumbTintList = Android.Content.Res.ColorStateList.ValueOf(Ui.Accent);
        _bar.SetPadding(Ui.Dp(c, 4), Ui.Dp(c, 8), Ui.Dp(c, 4), Ui.Dp(c, 10));
        AddView(_bar, Ui.Match());
        Value = value;
        _bar.ProgressChanged += (_, e) =>
        {
            _value.Text = _format(Value);
            if (!_setting) Changed?.Invoke(Value);
        };
        if (changed != null) Changed += changed;
    }

    public event Action<double>? Changed;

    public double Value
    {
        get => _min + _bar.Progress * _step;
        set
        {
            _setting = true;
            _bar.Progress = (int)Math.Round((value - _min) / _step);
            _setting = false;
            _value.Text = _format(Value);
        }
    }

    public double Maximum
    {
        get => _min + _bar.Max * _step;
        set
        {
            double v = Value;
            _setting = true;
            _bar.Max = Math.Max(1, (int)Math.Round((value - _min) / _step));
            _setting = false;
            Value = Math.Min(v, value);
        }
    }
}

/// <summary>Single-choice chips.</summary>
public sealed class ChipGroup<T> : FlowLayout where T : notnull
{
    private readonly List<(T Value, Button Chip)> _chips = new();
    private T _selected;

    public ChipGroup(Context c, IEnumerable<(string Label, T Value)> options, T selected, Action<T>? changed = null) : base(c)
    {
        _selected = selected;
        foreach (var (label, value) in options)
        {
            var v = value;
            var b = Ui.Button(c, label, () => Select(v, raise: true));
            b.SetMinHeight(Ui.Dp(c, 34));
            b.SetMinimumHeight(Ui.Dp(c, 34));
            _chips.Add((value, b));
            AddView(b);
        }
        if (changed != null) Changed += changed;
        Refresh();
    }

    public event Action<T>? Changed;

    public T Value
    {
        get => _selected;
        set => Select(value, raise: false);
    }

    private void Select(T v, bool raise)
    {
        _selected = v;
        Refresh();
        if (raise) Changed?.Invoke(v);
    }

    private void Refresh()
    {
        foreach (var (value, chip) in _chips) Ui.SetPrimary(chip, EqualityComparer<T>.Default.Equals(value, _selected));
    }

    public void SetChipsEnabled(bool enabled)
    {
        foreach (var (_, chip) in _chips)
        {
            chip.Enabled = enabled;
            chip.Alpha = enabled ? 1f : 0.4f;
        }
    }
}

/// <summary>Lays children out left to right, wrapping to new lines.</summary>
public class FlowLayout : ViewGroup
{
    public FlowLayout(Context c) : base(c) { }

    protected override void OnMeasure(int widthMeasureSpec, int heightMeasureSpec)
    {
        int maxW = MeasureSpec.GetSize(widthMeasureSpec);
        if (MeasureSpec.GetMode(widthMeasureSpec) == MeasureSpecMode.Unspecified) maxW = int.MaxValue;
        int x = PaddingLeft, y = PaddingTop, lineH = 0, widest = 0;
        for (int i = 0; i < ChildCount; i++)
        {
            var ch = GetChildAt(i)!;
            if (ch.Visibility == ViewStates.Gone) continue;
            MeasureChild(ch, widthMeasureSpec, heightMeasureSpec);
            var (ml, mt, mr, mb) = Margins(ch);
            int w = ch.MeasuredWidth + ml + mr, h = ch.MeasuredHeight + mt + mb;
            if (x + w > maxW - PaddingRight && x > PaddingLeft)
            {
                x = PaddingLeft;
                y += lineH;
                lineH = 0;
            }
            x += w;
            lineH = Math.Max(lineH, h);
            widest = Math.Max(widest, x);
        }
        int totalH = y + lineH + PaddingBottom;
        int width = MeasureSpec.GetMode(widthMeasureSpec) == MeasureSpecMode.Exactly ? maxW : Math.Min(maxW, widest + PaddingRight);
        SetMeasuredDimension(width, ResolveSize(totalH, heightMeasureSpec));
    }

    protected override void OnLayout(bool changed, int l, int t, int r, int b)
    {
        int maxW = r - l;
        int x = PaddingLeft, y = PaddingTop, lineH = 0;
        for (int i = 0; i < ChildCount; i++)
        {
            var ch = GetChildAt(i)!;
            if (ch.Visibility == ViewStates.Gone) continue;
            var (ml, mt, mr, mb) = Margins(ch);
            int w = ch.MeasuredWidth + ml + mr, h = ch.MeasuredHeight + mt + mb;
            if (x + w > maxW - PaddingRight && x > PaddingLeft)
            {
                x = PaddingLeft;
                y += lineH;
                lineH = 0;
            }
            ch.Layout(x + ml, y + mt, x + ml + ch.MeasuredWidth, y + mt + ch.MeasuredHeight);
            x += w;
            lineH = Math.Max(lineH, h);
        }
    }

    private static (int, int, int, int) Margins(View v) =>
        v.LayoutParameters is MarginLayoutParams m ? (m.LeftMargin, m.TopMargin, m.RightMargin, m.BottomMargin) : (0, 0, 0, 0);

    protected override bool CheckLayoutParams(LayoutParams? p) => p is MarginLayoutParams;
    protected override LayoutParams GenerateDefaultLayoutParams() => new MarginLayoutParams(LayoutParams.WrapContent, LayoutParams.WrapContent);
    public override LayoutParams GenerateLayoutParams(Android.Util.IAttributeSet? attrs) => new MarginLayoutParams(Context, attrs);
    protected override LayoutParams GenerateLayoutParams(LayoutParams? p) => p is MarginLayoutParams m ? new MarginLayoutParams(m) : new MarginLayoutParams(p);
}
