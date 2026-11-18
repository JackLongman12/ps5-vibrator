using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DualSenseStudio.Core.Haptics;
using DualSenseStudio.Core.Recording;

namespace DualSenseStudio.Views;

public partial class RecorderView : UserControl
{
    private static readonly Dictionary<RecordMapping, string> Help = new()
    {
        [RecordMapping.Pressure] = "How far you press is how hard it vibrates. Either trigger drives both sides.",
        [RecordMapping.DualChannel] = "L2 drives the left (heavy) side and R2 drives the right (sharp) side, so you can play two parts at once.",
        [RecordMapping.VelocityAccents] = "Speed matters: a fast stab makes a sharp spike, a slow squeeze makes a gentle swell with a light body underneath.",
        [RecordMapping.Zones] = "Pressure picks the texture: light = smooth hum, medium = pulse train that speeds up as you press, full = rough buzz.",
    };

    private readonly DispatcherTimer _timer;
    private RecordMapping _mapping = RecordMapping.Pressure;
    private double _length = 8;
    private bool _syncingLength;
    private Pattern? _take;
    private bool _saved;

    public RecorderView()
    {
        InitializeComponent();
        MappingHelp.Text = Help[_mapping];
        LengthText.Text = "8 s";
        AppHost.Recorder.Completed += p => Dispatcher.BeginInvoke(() => OnTakeCompleted(p));
        Editor.Edited += () => _saved = false;

        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += (_, _) => Tick();
        Loaded += (_, _) => _timer.Start();
        Unloaded += (_, _) => _timer.Stop();
    }

    private void OnMapping(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag } && Enum.TryParse<RecordMapping>(tag, out var m))
        {
            _mapping = m;
            if (MappingHelp != null) MappingHelp.Text = Help[m];
        }
    }

    private void OnLengthChip(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag } || LengthSlider == null) return;
        _length = double.Parse(tag, System.Globalization.CultureInfo.InvariantCulture);
        _syncingLength = true;
        if (_length > 0) LengthSlider.Value = _length;
        _syncingLength = false;
        LengthText.Text = _length > 0 ? $"{_length:0} s" : "free";
    }

    private void OnLengthSlider(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (LengthText == null || _syncingLength) return;
        _length = Math.Round(e.NewValue);
        LengthText.Text = $"{_length:0} s";
        foreach (var chip in LengthChips.Children.OfType<RadioButton>())
            chip.IsChecked = (string)chip.Tag == _length.ToString(System.Globalization.CultureInfo.InvariantCulture) ? true : false;
    }

    /// <summary>Start, or stop early (also bound to the controller's Create button).</summary>
    public void ToggleRecording()
    {
        var rec = AppHost.Recorder;
        switch (rec.State)
        {
            case RecorderState.Idle:
                Start();
                break;
            case RecorderState.CountIn:
                rec.Cancel();
                AppHost.Playback.Stop();
                break;
            default:
                rec.RequestStop();
                break;
        }
    }

    private void OnRecord(object sender, RoutedEventArgs e) => ToggleRecording();

    private void Start()
    {
        if (AppHost.Controllers.Current == null)
        {
            StateText.Text = "No controller";
            StateDetail.Text = "Connect your DualSense first.";
            return;
        }
        bool overdub = OverdubCheck.IsChecked == true && _take != null;
        var options = new RecorderOptions
        {
            Mapping = _mapping,
            LengthSeconds = _length,
            CountIn = CountInCheck.IsChecked == true,
            FaceButtonHits = HitsCheck.IsChecked == true,
            RecordTriggerVibration = TrigVibCheck.IsChecked == true,
            Curve = Math.Pow(2, CurveSlider.Value),
            TriggerFrequency = _take?.TriggerFrequency ?? 35,
        };
        AppHost.Recorder.Begin(options, overdub ? _take : null);
        AppHost.Playback.SetSource(AppHost.Recorder);
    }

    private void OnTakeCompleted(Pattern take)
    {
        if (AppHost.Playback.Active == AppHost.Recorder) AppHost.Playback.Stop();
        if (take.Length < 5)
        {
            StateText.Text = "Take too short";
            StateDetail.Text = "Record for at least a moment.";
            return;
        }
        bool wasOverdub = OverdubCheck.IsChecked == true && _take != null;
        if (wasOverdub && _take != null)
        {
            // Keep identity so a saved pattern is updated in place.
            take.Id = _take.Id;
            take.Created = _take.Created;
        }
        _take = take;
        _saved = false;
        Editor.Pattern = take;
        Editor.Visibility = Visibility.Visible;
        NoTakeText.Visibility = Visibility.Collapsed;
        SaveButton.IsEnabled = true;
        if (!wasOverdub || string.IsNullOrWhiteSpace(NameBox.Text)) NameBox.Text = take.Name;
        LiveView.Pattern = take;
        LiveView.MinDisplaySamples = 0;
        StateText.Text = "Take recorded";
        StateDetail.Text = $"{take.DurationSeconds:0.0} s. Edit it below, then save it.";
        if (AutoPlayCheck.IsChecked == true) AppHost.Playback.Play(take);
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (_take == null) return;
        _take.Name = string.IsNullOrWhiteSpace(NameBox.Text) ? _take.Name : NameBox.Text.Trim();
        _take.Category = "Recording";
        AppHost.Store.Save(_take);
        AppHost.NotifyLibraryChanged();
        _saved = true;
        StateDetail.Text = $"Saved \"{_take.Name}\" to My Patterns.";
    }

    private void Tick()
    {
        var rec = AppHost.Recorder;
        var input = AppHost.Engine.Input;
        L2Meter.Value = input.L2Value;
        R2Meter.Value = input.R2Value;

        switch (rec.State)
        {
            case RecorderState.CountIn:
                RecordButton.Content = "■  Cancel";
                StateText.Text = $"Get ready… {Math.Ceiling(-rec.Elapsed / 0.6):0}";
                StateDetail.Text = "Recording starts after the count-in.";
                TakeProgress.Value = 0;
                break;
            case RecorderState.Recording:
            {
                RecordButton.Content = "■  Stop";
                double target = rec.TargetSeconds;
                StateText.Text = target > 0
                    ? $"Recording  {rec.Elapsed:0.0} / {target:0.#} s"
                    : $"Recording  {rec.Elapsed:0.0} s (free)";
                StateDetail.Text = "Play the triggers. Press Stop (or Create) to finish early.";
                TakeProgress.Value = target > 0 ? Math.Clamp(rec.Elapsed / target, 0, 1) : (rec.Elapsed % 4) / 4;
                var snap = rec.Snapshot();
                LiveView.MinDisplaySamples = target > 0 ? (int)(target * Pattern.DefaultSampleRate) : 0;
                LiveView.Pattern = snap;
                break;
            }
            default:
                RecordButton.Content = "●  Record";
                TakeProgress.Value = 0;
                LiveView.Playhead = _take != null && AppHost.Playback.CurrentPattern?.Pattern == _take
                    ? AppHost.Playback.CurrentPattern.Progress : -1;
                break;
        }
        SaveButton.Content = _saved ? "Saved ✓" : "Save to My Patterns";
    }
}
