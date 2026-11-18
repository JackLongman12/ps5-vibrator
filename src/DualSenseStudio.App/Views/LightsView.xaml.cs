using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using DualSenseStudio.Core.Haptics;

namespace DualSenseStudio.Views;

public partial class LightsView : UserControl
{
    private static readonly Color[] SwatchColors =
    [
        Color.FromRgb(40, 110, 255), Color.FromRgb(0, 200, 255), Color.FromRgb(46, 211, 183), Color.FromRgb(48, 220, 90),
        Color.FromRgb(255, 210, 0), Color.FromRgb(255, 120, 20), Color.FromRgb(255, 40, 60), Color.FromRgb(255, 60, 200),
        Color.FromRgb(150, 70, 255), Color.FromRgb(255, 255, 255),
    ];

    private readonly List<ToggleButton> _ledToggles = new();
    private bool _loading = true;

    private static LightSettings L => AppHost.Engine.Lights;

    public LightsView()
    {
        InitializeComponent();

        foreach (var c in SwatchColors)
        {
            var b = new Button
            {
                Width = 30, Height = 30, Margin = new Thickness(0, 0, 8, 6), Cursor = System.Windows.Input.Cursors.Hand,
                Background = new SolidColorBrush(c),
                Template = SwatchTemplate(),
            };
            var color = c;
            b.Click += (_, _) => SetColor(color.R, color.G, color.B);
            Swatches.Children.Add(b);
        }

        for (int i = 0; i < 5; i++)
        {
            var t = new ToggleButton { Style = (Style)FindResource("Chip"), Content = $"{i + 1}", Width = 40 };
            int bit = i;
            t.IsChecked = (L.CustomLedMask & (1 << bit)) != 0;
            t.Click += (_, _) =>
            {
                byte mask = 0;
                for (int k = 0; k < _ledToggles.Count; k++) if (_ledToggles[k].IsChecked == true) mask |= (byte)(1 << k);
                L.CustomLedMask = mask;
                Save();
            };
            _ledToggles.Add(t);
            CustomLeds.Children.Add(t);
        }

        Check(ModeChips, L.Mode.ToString());
        Check(LedChips, L.PlayerLeds.ToString());
        Check(BrightChips, L.LedBrightness.ToString());
        RSlider.Value = L.R;
        GSlider.Value = L.G;
        BSlider.Value = L.B;
        _loading = false;
        UpdatePreview();
        CustomLeds.Visibility = L.PlayerLeds == PlayerLedMode.Custom ? Visibility.Visible : Visibility.Collapsed;
    }

    private static ControlTemplate SwatchTemplate()
    {
        var t = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(15));
        border.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        t.VisualTree = border;
        return t;
    }

    private static void Check(Panel panel, string tag)
    {
        foreach (var rb in panel.Children.OfType<RadioButton>())
            rb.IsChecked = (string)rb.Tag == tag;
    }

    private static void Save() => AppHost.Settings.Save();

    private void SetColor(byte r, byte g, byte b)
    {
        _loading = true;
        RSlider.Value = r;
        GSlider.Value = g;
        BSlider.Value = b;
        _loading = false;
        OnRgb(this, new RoutedPropertyChangedEventArgs<double>(0, 0));
        if (L.Mode is LightbarMode.Off or LightbarMode.Rainbow or LightbarMode.Battery)
            Check(ModeChips, LightbarMode.Static.ToString());
    }

    private void OnMode(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton { Tag: string tag }) return;
        if (Enum.TryParse<LightbarMode>(tag, out var m)) { L.Mode = m; Save(); }
    }

    private void OnLedMode(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag }) return;
        if (Enum.TryParse<PlayerLedMode>(tag, out var m))
        {
            if (CustomLeds != null) CustomLeds.Visibility = m == PlayerLedMode.Custom ? Visibility.Visible : Visibility.Collapsed;
            if (_loading) return;
            L.PlayerLeds = m;
            Save();
        }
    }

    private void OnBrightness(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton { Tag: string tag }) return;
        L.LedBrightness = byte.Parse(tag);
        Save();
    }

    private void OnRgb(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading || BSlider == null) return;
        L.R = (byte)RSlider.Value;
        L.G = (byte)GSlider.Value;
        L.B = (byte)BSlider.Value;
        UpdatePreview();
        Save();
    }

    private void UpdatePreview()
    {
        if (Preview == null) return;
        Preview.Background = new SolidColorBrush(Color.FromRgb((byte)RSlider.Value, (byte)GSlider.Value, (byte)BSlider.Value));
    }
}
