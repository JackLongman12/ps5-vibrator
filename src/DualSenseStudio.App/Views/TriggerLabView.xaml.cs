using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DualSenseStudio.Core.Protocol;

namespace DualSenseStudio.Views;

public partial class TriggerLabView : UserControl
{
    private readonly DispatcherTimer _timer;
    private bool _syncing;

    public TriggerLabView()
    {
        InitializeComponent();
        LeftPanel.Changed += c => OnPanelChanged(c, left: true);
        RightPanel.Changed += c => OnPanelChanged(c, left: false);
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += (_, _) =>
        {
            var input = AppHost.Engine.Input;
            LeftPanel.PressValue = input.L2Value;
            RightPanel.PressValue = input.R2Value;
        };
        Loaded += (_, _) => _timer.Start();
        Unloaded += (_, _) => _timer.Stop();
    }

    private void OnPanelChanged(TriggerConfig c, bool left)
    {
        if (_syncing) return;
        if (left) AppHost.Engine.LeftTrigger = c;
        else AppHost.Engine.RightTrigger = c;
        if (LinkCheck.IsChecked == true)
        {
            _syncing = true;
            (left ? RightPanel : LeftPanel).Load(c);
            _syncing = false;
            AppHost.Engine.LeftTrigger = c;
            AppHost.Engine.RightTrigger = c;
        }
    }

    private void OnQuick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag }) return;
        TriggerConfig c = tag switch
        {
            "soft" => new(TriggerMode.Resistance, Start: 1, Strength: 2),
            "heavy" => new(TriggerMode.Resistance, Start: 0, Strength: 8),
            "bow" => new(TriggerMode.Slope, Start: 0, End: 9, Strength: 1, EndStrength: 8),
            "brake" => new(TriggerMode.Slope, Start: 2, End: 7, Strength: 3, EndStrength: 8),
            "click" => new(TriggerMode.Weapon, Start: 4, End: 6, Strength: 8),
            "buzz" => new(TriggerMode.Vibration, Start: 2, Strength: 8, Frequency: 12),
            "rumble" => new(TriggerMode.Vibration, Start: 0, Strength: 6, Frequency: 60),
            _ => TriggerConfig.Off,
        };
        _syncing = true;
        LeftPanel.Load(c);
        RightPanel.Load(c);
        _syncing = false;
        AppHost.Engine.LeftTrigger = c;
        AppHost.Engine.RightTrigger = c;
    }
}
