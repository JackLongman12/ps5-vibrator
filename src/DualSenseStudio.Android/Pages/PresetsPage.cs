using Android.App;
using Android.Views;
using Android.Widget;
using DualSenseStudio.Core.Haptics;
using DualSenseStudio.Droid.Ui;
using U = DualSenseStudio.Droid.Ui.Ui;

namespace DualSenseStudio.Droid.Pages;

public sealed class PresetsPage(Activity activity) : Page(activity)
{
    private readonly List<(PresetInfo Preset, LinearLayout Tile)> _tiles = new();
    private Pattern? _highlighted;
    private bool _first = true;

    public override string Title => "Presets";

    protected override View Build()
    {
        var c = Activity;
        var col = U.Column(c);
        col.AddView(U.Heading(c, "Presets"));
        col.AddView(U.Help(c, $"{PresetLibrary.All.Count} looping patterns. Tap one to play it, tap again to stop. Intensity, speed and balance are in the player bar below."));

        // Two tiles per row on phones in portrait; more on wide screens.
        int columns = Math.Max(2, (int)(c.Resources!.DisplayMetrics!.WidthPixels / c.Resources.DisplayMetrics.Density / 200));
        LinearLayout? row = null;
        for (int i = 0; i < PresetLibrary.All.Count; i++)
        {
            if (i % columns == 0)
            {
                row = U.Row(c);
                row.SetGravity(GravityFlags.Top);
                col.AddView(row, U.Match());
            }
            var preset = PresetLibrary.All[i];
            var tile = Tile(preset);
            var lp = U.Weight();
            lp.RightMargin = (i % columns) < columns - 1 ? U.Dp(c, 8) : 0;
            lp.BottomMargin = U.Dp(c, 8);
            row!.AddView(tile, lp);
            _tiles.Add((preset, tile));
        }
        // Pad the last row so tiles keep the same width.
        for (int k = PresetLibrary.All.Count % columns; k > 0 && k < columns; k++)
            row!.AddView(new Android.Views.View(c), U.Weight());
        return col;
    }

    private LinearLayout Tile(PresetInfo preset)
    {
        var c = Activity;
        var t = U.Column(c, 10);
        t.Background = U.Rounded(U.CardBg, U.Dp(c, 10));
        t.AddView(U.Text(c, preset.Name, 14, U.Text1, bold: true));
        t.AddView(U.Text(c, $"{preset.Pattern.DurationSeconds:0.#} s loop" + (preset.Pattern.HasTriggerData ? " · triggers" : ""), 11, U.Muted));
        var view = new PatternView(c) { Pattern = preset.Pattern, Compact = true };
        var lp = U.Match(U.Dp(c, 40));
        lp.TopMargin = U.Dp(c, 6);
        lp.BottomMargin = U.Dp(c, 6);
        t.AddView(view, lp);
        var desc = U.Text(c, preset.Description, 11.5f, U.Muted);
        desc.SetMaxLines(3);
        desc.Ellipsize = Android.Text.TextUtils.TruncateAt.End;
        t.AddView(desc);
        t.Clickable = true;
        t.Click += (_, _) =>
        {
            if (AppHost.Playback.CurrentPattern?.Pattern == preset.Pattern) AppHost.Playback.Stop();
            else AppHost.Playback.Play(preset.Pattern);
            Tick();
        };
        return t;
    }

    public override void Tick()
    {
        var active = AppHost.Playback.CurrentPattern?.Pattern;
        if (active == _highlighted && !_first) return;
        _first = false;
        _highlighted = active;
        foreach (var (preset, tile) in _tiles)
        {
            bool on = preset.Pattern == active;
            tile.Background = on
                ? U.Rounded(U.CardActive, U.Dp(Activity, 10), U.Accent, U.Dp(Activity, 2))
                : U.Rounded(U.CardBg, U.Dp(Activity, 10));
        }
    }
}
