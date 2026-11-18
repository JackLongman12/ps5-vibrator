using Android.App;
using Android.Content;
using Android.Views;
using Android.Widget;
using DualSenseStudio.Core.Protocol;
using DualSenseStudio.Droid.Ui;
using U = DualSenseStudio.Droid.Ui.Ui;

namespace DualSenseStudio.Droid.Pages;

public sealed class TriggerLabPage(Activity activity) : Page(activity)
{
    private TriggerPanelView _left = null!, _right = null!;
    private TextView _banner = null!;
    private LinearLayout _body = null!;
    private bool _link;
    private bool _syncing;

    public override string Title => "Triggers";

    protected override View Build()
    {
        var c = Activity;
        var col = U.Column(c);
        col.AddView(U.Heading(c, "Trigger Lab"));
        col.AddView(U.Help(c, "Adaptive trigger effects: resistance, slopes, a click/break and vibration. Press L2 / R2 to feel them."));
        _banner = U.Banner(c, "Not available over Bluetooth. Android's controller driver has no access to the adaptive triggers. Connect the DualSense with a USB cable (OTG) to use the Trigger Lab.");
        col.AddView(_banner);

        _body = U.Column(c);
        col.AddView(_body, U.Match());

        var quick = U.Card(c, _body);
        quick.AddView(U.Text(c, "Quick effects", 14, U.Text1, bold: true));
        var flow = U.Flow(c);
        foreach (var (label, cfg) in new (string, TriggerConfig)[]
                 {
                     ("Off", TriggerConfig.Off),
                     ("Soft", new(TriggerMode.Resistance, Start: 1, Strength: 2)),
                     ("Heavy", new(TriggerMode.Resistance, Start: 0, Strength: 8)),
                     ("Bow", new(TriggerMode.Slope, Start: 0, End: 9, Strength: 1, EndStrength: 8)),
                     ("Brake", new(TriggerMode.Slope, Start: 2, End: 7, Strength: 3, EndStrength: 8)),
                     ("Click", new(TriggerMode.Weapon, Start: 4, End: 6, Strength: 8)),
                     ("Buzz", new(TriggerMode.Vibration, Start: 2, Strength: 8, Frequency: 12)),
                     ("Rumble", new(TriggerMode.Vibration, Start: 0, Strength: 6, Frequency: 60)),
                 })
        {
            var config = cfg;
            flow.AddView(U.Button(c, label, () => ApplyBoth(config)));
        }
        quick.AddView(flow);
        quick.AddView(U.Check(c, "Link L2 and R2 (edit both at once)", _link, v => _link = v));

        _left = new TriggerPanelView(c, "L2");
        _right = new TriggerPanelView(c, "R2");
        _left.Load(AppHost.Engine.LeftTrigger);
        _right.Load(AppHost.Engine.RightTrigger);
        _left.Changed += cfg => OnPanelChanged(cfg, left: true);
        _right.Changed += cfg => OnPanelChanged(cfg, left: false);
        U.Card(c, _body).AddView(_left);
        U.Card(c, _body).AddView(_right);
        return col;
    }

    public override void Refresh()
    {
        bool bt = AppHost.Kind == ControllerKind.Bluetooth;
        Show(_banner, bt);
        _body.Alpha = bt ? 0.45f : 1f;
        SetEnabled(_body, !bt);
    }

    private static void SetEnabled(View v, bool enabled)
    {
        v.Enabled = enabled;
        if (v is ViewGroup g)
            for (int i = 0; i < g.ChildCount; i++) SetEnabled(g.GetChildAt(i)!, enabled);
    }

    private void ApplyBoth(TriggerConfig c)
    {
        _syncing = true;
        _left.Load(c);
        _right.Load(c);
        _syncing = false;
        AppHost.Engine.LeftTrigger = c;
        AppHost.Engine.RightTrigger = c;
    }

    private void OnPanelChanged(TriggerConfig c, bool left)
    {
        if (_syncing) return;
        if (left) AppHost.Engine.LeftTrigger = c;
        else AppHost.Engine.RightTrigger = c;
        if (_link)
        {
            _syncing = true;
            (left ? _right : _left).Load(c);
            _syncing = false;
            AppHost.Engine.LeftTrigger = c;
            AppHost.Engine.RightTrigger = c;
        }
    }

    public override void Tick()
    {
        var input = AppHost.Engine.Input;
        _left.Press = input.L2Value;
        _right.Press = input.R2Value;
    }
}

/// <summary>Editor for one trigger's effect (port of the WPF TriggerPanel).</summary>
public sealed class TriggerPanelView : LinearLayout
{
    private static readonly Dictionary<TriggerMode, string> HelpText = new()
    {
        [TriggerMode.Off] = "No resistance. The trigger moves freely.",
        [TriggerMode.Resistance] = "Constant push-back from the start zone to the end of travel.",
        [TriggerMode.Slope] = "Resistance ramps from one strength to another, like drawing a bow or pressing a brake.",
        [TriggerMode.Weapon] = "Firm until a break point, then it gives way with a click.",
        [TriggerMode.Vibration] = "The trigger motor buzzes from the start zone onward.",
    };

    private readonly ChipGroup<TriggerMode> _mode;
    private readonly TextView _help;
    private readonly SliderRow _start, _end, _strength, _endStrength, _freq;
    private readonly ProgressBar _press;
    private bool _loading;

    public TriggerPanelView(Context c, string side) : base(c)
    {
        Orientation = Orientation.Vertical;
        AddView(U.Text(c, side, 16, U.Text1, bold: true));
        _mode = new ChipGroup<TriggerMode>(c,
            [("Off", TriggerMode.Off), ("Resistance", TriggerMode.Resistance), ("Slope", TriggerMode.Slope), ("Click", TriggerMode.Weapon), ("Vibration", TriggerMode.Vibration)],
            TriggerMode.Off, OnMode);
        AddView(_mode);
        _help = U.Help(c, "");
        AddView(_help);
        _start = Slider(c, "Start zone", 0, 9, 0, v => $"{v:0}");
        _end = Slider(c, "End zone", 1, 9, 9, v => $"{v:0}");
        _strength = Slider(c, "Strength", 1, 8, 4, v => $"{v:0}");
        _endStrength = Slider(c, "End strength", 1, 8, 8, v => $"{v:0}");
        _freq = Slider(c, "Frequency", 1, 120, 30, v => $"{v:0} Hz");
        AddView(U.Text(c, "Press", 12, U.Muted));
        _press = U.Meter(c);
        AddView(_press);
        UpdateVisibility();
    }

    private SliderRow Slider(Context c, string label, double min, double max, double value, Func<double, string> fmt)
    {
        var s = new SliderRow(c, label, min, max, 1, value, fmt, _ => Emit());
        AddView(s, U.Match());
        return s;
    }

    public event Action<TriggerConfig>? Changed;

    public double Press { set => U.SetMeter(_press, value); }

    public TriggerConfig Config => new(_mode.Value,
        Start: (int)_start.Value, End: (int)_end.Value,
        Strength: (int)_strength.Value, EndStrength: (int)_endStrength.Value,
        Frequency: (int)_freq.Value);

    public void Load(TriggerConfig c)
    {
        _loading = true;
        _mode.Value = c.Mode;
        _start.Value = c.Start;
        _end.Value = c.End;
        _strength.Value = Math.Max(1, c.Strength);
        _endStrength.Value = Math.Max(1, c.EndStrength);
        _freq.Value = Math.Max(1, c.Frequency);
        _loading = false;
        UpdateVisibility();
    }

    private void OnMode(TriggerMode m)
    {
        if (m == TriggerMode.Weapon)
        {
            _loading = true;
            if (_start.Value < 2) _start.Value = 3;
            if (_end.Value > 8 || _end.Value <= _start.Value) _end.Value = Math.Min(8, _start.Value + 2);
            _loading = false;
        }
        UpdateVisibility();
        Emit();
    }

    private void Emit()
    {
        if (_loading) return;
        Changed?.Invoke(Config);
    }

    private void UpdateVisibility()
    {
        var m = _mode.Value;
        _help.Text = HelpText[m];
        _start.Visibility = m == TriggerMode.Off ? ViewStates.Gone : ViewStates.Visible;
        _end.Visibility = m is TriggerMode.Slope or TriggerMode.Weapon ? ViewStates.Visible : ViewStates.Gone;
        _strength.Visibility = m == TriggerMode.Off ? ViewStates.Gone : ViewStates.Visible;
        _endStrength.Visibility = m == TriggerMode.Slope ? ViewStates.Visible : ViewStates.Gone;
        _freq.Visibility = m == TriggerMode.Vibration ? ViewStates.Visible : ViewStates.Gone;
    }
}
