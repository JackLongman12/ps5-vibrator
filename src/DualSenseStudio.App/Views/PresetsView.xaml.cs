using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DualSenseStudio.Controls;
using DualSenseStudio.Core.Haptics;

namespace DualSenseStudio.Views;

public partial class PresetsView : UserControl
{
    private readonly List<(PresetInfo Preset, Button Tile)> _tiles = new();

    public PresetsView()
    {
        InitializeComponent();
        foreach (var preset in PresetLibrary.All)
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = preset.Name, FontWeight = FontWeights.SemiBold, FontSize = 14 });
            stack.Children.Add(new TextBlock
            {
                Text = $"{preset.Pattern.DurationSeconds:0.#} s loop" + (preset.Pattern.HasTriggerData ? " · triggers" : ""),
                Style = (Style)FindResource("Muted"),
                FontSize = 11,
                Margin = new Thickness(0, 1, 0, 8),
            });
            stack.Children.Add(new PatternView { Pattern = preset.Pattern, Height = 46, Compact = true, ShowGrid = false, IsHitTestVisible = false });
            stack.Children.Add(new TextBlock
            {
                Text = preset.Description,
                Style = (Style)FindResource("Muted"),
                FontSize = 11.5,
                Margin = new Thickness(0, 8, 0, 0),
                Height = 32,
            });
            var tile = new Button { Style = (Style)FindResource("Tile"), Content = stack };
            var p = preset;
            tile.Click += (_, _) => Toggle(p);
            Tiles.Children.Add(tile);
            _tiles.Add((preset, tile));
        }
    }

    private static void Toggle(PresetInfo p)
    {
        if (AppHost.Playback.CurrentPattern?.Pattern == p.Pattern) AppHost.Playback.Stop();
        else AppHost.Playback.Play(p.Pattern);
    }

    /// <summary>Plays the next / previous preset (controller D-pad).</summary>
    public void PlayRelative(int delta)
    {
        var all = PresetLibrary.All;
        int idx = -1;
        for (int i = 0; i < all.Count; i++)
            if (AppHost.Playback.CurrentPattern?.Pattern == all[i].Pattern) idx = i;
        int next = idx < 0 ? (delta > 0 ? 0 : all.Count - 1) : ((idx + delta) % all.Count + all.Count) % all.Count;
        AppHost.Playback.Play(all[next].Pattern);
    }

    public void HighlightActive()
    {
        var active = AppHost.Playback.CurrentPattern?.Pattern;
        foreach (var (preset, tile) in _tiles)
            tile.Tag = preset.Pattern == active ? "active" : null;
    }
}
