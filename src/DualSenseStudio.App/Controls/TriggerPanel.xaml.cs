using System.Windows;
using System.Windows.Controls;
using DualSenseStudio.Core.Protocol;

namespace DualSenseStudio.Controls;

public partial class TriggerPanel : UserControl
{
    private static readonly Dictionary<TriggerMode, string> Help = new()
    {
        [TriggerMode.Off] = "No resistance. The trigger moves freely.",
        [TriggerMode.Resistance] = "Constant push-back from the start zone to the end of travel.",
        [TriggerMode.Slope] = "Resistance ramps from one strength to another, like drawing a bow or pressing a brake.",
        [TriggerMode.Weapon] = "Firm until a break point, then it gives way with a click.",
        [TriggerMode.Vibration] = "The trigger motor buzzes from the start zone onward.",
    };

    private TriggerMode _mode = TriggerMode.Off;
    private bool _loading;

    public TriggerPanel()
    {
        InitializeComponent();
        UpdateVisibility();
    }

    public string Side
    {
        get => Title.Text;
        set => Title.Text = value;
    }

    public event Action<TriggerConfig>? Changed;

    public double PressValue
    {
        set => PressMeter.Value = value;
    }

    public TriggerConfig Config => new(
        _mode,
        Start: (int)StartSlider.Value,
        End: (int)EndSlider.Value,
        Strength: (int)StrengthSlider.Value,
        EndStrength: (int)EndStrengthSlider.Value,
        Frequency: (int)FreqSlider.Value);

    public void Load(TriggerConfig c)
    {
        _loading = true;
        _mode = c.Mode;
        StartSlider.Value = c.Start;
        EndSlider.Value = c.End;
        StrengthSlider.Value = c.Strength;
        EndStrengthSlider.Value = c.EndStrength;
        FreqSlider.Value = c.Frequency;
        foreach (var rb in ModeChips.Children.OfType<RadioButton>())
            rb.IsChecked = (string)rb.Tag == c.Mode.ToString();
        _loading = false;
        UpdateVisibility();
    }

    private void OnMode(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag } && Enum.TryParse<TriggerMode>(tag, out var m))
        {
            _mode = m;
            if (m == TriggerMode.Weapon)
            {
                _loading = true;
                if (StartSlider.Value < 2) StartSlider.Value = 3;
                if (EndSlider.Value > 8 || EndSlider.Value <= StartSlider.Value) EndSlider.Value = Math.Min(8, StartSlider.Value + 2);
                _loading = false;
            }
            UpdateVisibility();
            Emit();
        }
    }

    private void OnValue(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (FreqText == null) return;
        UpdateLabels();
        Emit();
    }

    private void Emit()
    {
        if (_loading || FreqText == null) return;
        Changed?.Invoke(Config);
    }

    private void UpdateVisibility()
    {
        if (ModeHelp == null) return;
        ModeHelp.Text = Help[_mode];
        StartRow.Visibility = _mode == TriggerMode.Off ? Visibility.Collapsed : Visibility.Visible;
        EndRow.Visibility = _mode is TriggerMode.Slope or TriggerMode.Weapon ? Visibility.Visible : Visibility.Collapsed;
        StrengthRow.Visibility = _mode == TriggerMode.Off ? Visibility.Collapsed : Visibility.Visible;
        EndStrengthRow.Visibility = _mode == TriggerMode.Slope ? Visibility.Visible : Visibility.Collapsed;
        FreqRow.Visibility = _mode == TriggerMode.Vibration ? Visibility.Visible : Visibility.Collapsed;
        StrengthLabel.Text = _mode switch
        {
            TriggerMode.Slope => "Start strength",
            TriggerMode.Vibration => "Amplitude",
            _ => "Strength",
        };
        UpdateLabels();
    }

    private void UpdateLabels()
    {
        StartText.Text = $"{StartSlider.Value:0}";
        EndText.Text = $"{EndSlider.Value:0}";
        StrengthText.Text = $"{StrengthSlider.Value:0}";
        EndStrengthText.Text = $"{EndStrengthSlider.Value:0}";
        FreqText.Text = $"{FreqSlider.Value:0} Hz";
    }
}
