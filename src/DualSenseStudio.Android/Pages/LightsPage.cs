using Android.App;
using Android.Views;
using Android.Widget;
using DualSenseStudio.Core.Haptics;
using DualSenseStudio.Droid.Ui;
using Color = Android.Graphics.Color;
using U = DualSenseStudio.Droid.Ui.Ui;
using View = Android.Views.View;

namespace DualSenseStudio.Droid.Pages;

public sealed class LightsPage(Activity activity) : Page(activity)
{
    private static readonly (byte R, byte G, byte B)[] Swatches =
    [
        (40, 110, 255), (0, 200, 255), (46, 211, 183), (48, 220, 90), (255, 210, 0),
        (255, 120, 20), (255, 40, 60), (255, 60, 200), (150, 70, 255), (255, 255, 255),
    ];

    private ChipGroup<LightbarMode> _mode = null!;
    private SliderRow _r = null!, _g = null!, _b = null!;
    private View _preview = null!;
    private TextView _btBanner = null!;
    private LinearLayout _ledCard = null!, _custom = null!;
    private readonly List<Button> _ledButtons = new();
    private bool _loading;

    private static LightSettings L => AppHost.Engine.Lights;
    private static void Save() => AppHost.Settings.Save();

    public override string Title => "Lights";

    protected override View Build()
    {
        var c = Activity;
        var col = U.Column(c);
        col.AddView(U.Heading(c, "Lights"));
        _btBanner = U.Banner(c, "Bluetooth: only the lightbar colour can be set (through Android's controller lights API, Android 12+). Player LEDs and LED brightness need USB.");
        col.AddView(_btBanner);

        var bar = U.Card(c, col);
        bar.AddView(U.Text(c, "Lightbar", 15, U.Text1, bold: true));
        _mode = new ChipGroup<LightbarMode>(c,
            [("Follow vibration", LightbarMode.FollowVibration), ("Static", LightbarMode.Static), ("Rainbow", LightbarMode.Rainbow), ("Battery", LightbarMode.Battery), ("Off", LightbarMode.Off)],
            L.Mode, m => { L.Mode = m; Save(); });
        bar.AddView(_mode);

        var swatches = U.Flow(c);
        foreach (var (r, g, b) in Swatches)
        {
            var v = new View(c) { Background = U.Rounded(Color.Rgb(r, g, b), U.Dp(c, 16), Color.Argb(60, 255, 255, 255), 2), Clickable = true };
            var lp = new ViewGroup.MarginLayoutParams(U.Dp(c, 32), U.Dp(c, 32)) { RightMargin = U.Dp(c, 8), BottomMargin = U.Dp(c, 8), TopMargin = U.Dp(c, 6) };
            v.LayoutParameters = lp;
            v.Click += (_, _) => SetColor(r, g, b);
            swatches.AddView(v);
        }
        bar.AddView(swatches);

        _preview = new View(c);
        bar.AddView(_preview, U.Match(U.Dp(c, 28)));
        _r = new SliderRow(c, "Red", 0, 255, 1, L.R, v => $"{v:0}", _ => OnRgb());
        _g = new SliderRow(c, "Green", 0, 255, 1, L.G, v => $"{v:0}", _ => OnRgb());
        _b = new SliderRow(c, "Blue", 0, 255, 1, L.B, v => $"{v:0}", _ => OnRgb());
        bar.AddView(_r);
        bar.AddView(_g);
        bar.AddView(_b);

        _ledCard = U.Card(c, col);
        _ledCard.AddView(U.Text(c, "Player LEDs", 15, U.Text1, bold: true));
        _ledCard.AddView(new ChipGroup<PlayerLedMode>(c,
            [("Intensity meter", PlayerLedMode.IntensityMeter), ("Battery", PlayerLedMode.Battery), ("Custom", PlayerLedMode.Custom), ("Off", PlayerLedMode.Off)],
            L.PlayerLeds, m => { L.PlayerLeds = m; Show(_custom, m == PlayerLedMode.Custom); Save(); }));
        _custom = U.Row(c);
        for (int i = 0; i < 5; i++)
        {
            int bit = i;
            var b = U.Button(c, $"{i + 1}", () =>
            {
                L.CustomLedMask ^= (byte)(1 << bit);
                UpdateLedButtons();
                Save();
            });
            _ledButtons.Add(b);
            _custom.AddView(b);
        }
        _ledCard.AddView(_custom);
        Show(_custom, L.PlayerLeds == PlayerLedMode.Custom);
        UpdateLedButtons();
        _ledCard.AddView(U.Text(c, "LED brightness", 13, U.Muted));
        _ledCard.AddView(new ChipGroup<byte>(c, [("High", (byte)0), ("Medium", (byte)1), ("Low", (byte)2)], L.LedBrightness, v => { L.LedBrightness = v; Save(); }));

        UpdatePreview();
        return col;
    }

    public override void Refresh()
    {
        bool bt = AppHost.Kind == ControllerKind.Bluetooth;
        Show(_btBanner, bt);
        _ledCard.Alpha = bt ? 0.45f : 1f;
    }

    private void UpdateLedButtons()
    {
        for (int i = 0; i < _ledButtons.Count; i++) U.SetPrimary(_ledButtons[i], (L.CustomLedMask & (1 << i)) != 0);
    }

    private void SetColor(byte r, byte g, byte b)
    {
        _loading = true;
        _r.Value = r;
        _g.Value = g;
        _b.Value = b;
        _loading = false;
        OnRgb();
        if (L.Mode is LightbarMode.Off or LightbarMode.Rainbow or LightbarMode.Battery)
        {
            L.Mode = LightbarMode.Static;
            _mode.Value = LightbarMode.Static;
        }
    }

    private void OnRgb()
    {
        if (_loading) return;
        L.R = (byte)_r.Value;
        L.G = (byte)_g.Value;
        L.B = (byte)_b.Value;
        UpdatePreview();
        Save();
    }

    private void UpdatePreview() =>
        _preview.Background = U.Rounded(Color.Rgb((int)_r.Value, (int)_g.Value, (int)_b.Value), U.Dp(Activity, 8));
}
