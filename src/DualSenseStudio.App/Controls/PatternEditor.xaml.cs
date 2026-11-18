using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DualSenseStudio.Core.Haptics;

namespace DualSenseStudio.Controls;

/// <summary>Waveform + post-edit tools + undo, shared by the Recorder and the Library.</summary>
public partial class PatternEditor : UserControl
{
    private readonly Stack<Pattern> _undo = new();
    private readonly DispatcherTimer _timer;
    private Pattern? _pattern;
    private bool _loading;
    private bool _ready;

    public PatternEditor()
    {
        InitializeComponent();
        _ready = true;
        View.StrokeStarted += () => PushUndo();
        View.StrokeFinished += () => RaiseEdited();
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += (_, _) => UpdatePlayhead();
        Loaded += (_, _) => _timer.Start();
        Unloaded += (_, _) => _timer.Stop();
        UpdateLabels();
    }

    /// <summary>Raised after any change to the pattern (paint or tool).</summary>
    public event Action? Edited;

    public Pattern? Pattern
    {
        get => _pattern;
        set
        {
            _pattern = value;
            _undo.Clear();
            UndoButton.IsEnabled = false;
            Refresh();
        }
    }

    private bool IsPlayingThis => _pattern != null && AppHost.Playback.CurrentPattern?.Pattern == _pattern;

    private void Refresh()
    {
        _loading = true;
        View.Pattern = _pattern;
        IsEnabled = _pattern != null;
        if (_pattern != null)
        {
            double d = Math.Max(0.02, _pattern.DurationSeconds);
            TrimStart.Maximum = d;
            TrimEnd.Maximum = d;
            TrimStart.Value = 0;
            TrimEnd.Value = d;
            LengthSlider.Value = Math.Clamp(d, LengthSlider.Minimum, LengthSlider.Maximum);
            FreqSlider.Value = _pattern.TriggerFrequency;
            InfoText.Text = $"{_pattern.DurationSeconds:0.00} s · {_pattern.Length} samples @ {_pattern.SampleRate} Hz";
        }
        else InfoText.Text = "";
        _loading = false;
        UpdateLabels();
        View.InvalidateVisual();
    }

    private void UpdatePlayhead()
    {
        bool playing = IsPlayingThis;
        View.Playhead = playing ? AppHost.Playback.CurrentPattern!.Progress : -1;
        PlayButton.Content = playing ? "■  Stop" : "▶  Play loop";
    }

    private void UpdateLabels()
    {
        if (!_ready) return;
        TrimStartText.Text = $"{TrimStart.Value:0.00}s";
        TrimEndText.Text = $"{TrimEnd.Value:0.00}s";
        LengthText.Text = $"{LengthSlider.Value:0.0}s";
        BpmText.Text = $"{BpmSlider.Value:0}";
        SmoothText.Text = $"{SmoothSlider.Value:0} ms";
        GainText.Text = $"×{GainSlider.Value:0.00}";
        SeamText.Text = $"{SeamSlider.Value:0} ms";
        FreqText.Text = $"{FreqSlider.Value:0} Hz";
    }

    private void PushUndo()
    {
        if (_pattern == null) return;
        _undo.Push(_pattern.Clone());
        if (_undo.Count > 50)
        {
            var keep = _undo.Take(50).Reverse().ToList();
            _undo.Clear();
            foreach (var p in keep) _undo.Push(p);
        }
        UndoButton.IsEnabled = true;
    }

    private void RaiseEdited()
    {
        if (_pattern != null) _pattern.Modified = DateTime.Now;
        View.InvalidateVisual();
        Edited?.Invoke();
    }

    /// <summary>Applies a tool by copying the result's data into the current pattern (keeps identity for playback/library).</summary>
    private void Apply(Func<Pattern, Pattern> tool)
    {
        if (_pattern == null) return;
        PushUndo();
        var result = tool(_pattern);
        CopyInto(result, _pattern);
        RestartIfPlaying();
        Refresh();
        Edited?.Invoke();
    }

    private static void CopyInto(Pattern from, Pattern to)
    {
        to.Left = from.Left;
        to.Right = from.Right;
        to.LeftTrigger = from.LeftTrigger;
        to.RightTrigger = from.RightTrigger;
        to.SampleRate = from.SampleRate;
        to.TriggerFrequency = from.TriggerFrequency;
        to.Modified = DateTime.Now;
    }

    private void RestartIfPlaying()
    {
        if (IsPlayingThis) AppHost.Playback.Play(_pattern!);
    }

    // ---- handlers -----------------------------------------------------------------

    private void OnPaintChannel(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag } && Enum.TryParse<Channel>(tag, out var ch))
            View.PaintChannel = ch;
    }

    private void OnUndo(object sender, RoutedEventArgs e)
    {
        if (_pattern == null || _undo.Count == 0) return;
        CopyInto(_undo.Pop(), _pattern);
        UndoButton.IsEnabled = _undo.Count > 0;
        RestartIfPlaying();
        Refresh();
        Edited?.Invoke();
    }

    private void OnPlay(object sender, RoutedEventArgs e)
    {
        if (_pattern == null) return;
        if (IsPlayingThis) AppHost.Playback.Stop();
        else AppHost.Playback.Play(_pattern);
    }

    private void OnTrimChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading || !_ready) return;
        if (TrimEnd.Value < TrimStart.Value + 0.05)
        {
            if (sender == TrimStart) TrimEnd.Value = Math.Min(TrimEnd.Maximum, TrimStart.Value + 0.05);
            else TrimStart.Value = Math.Max(0, TrimEnd.Value - 0.05);
        }
        UpdateLabels();
    }

    private void OnLengthChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateLabels();
    private void OnBpmChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateLabels();
    private void OnShapeChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateLabels();

    private void OnFreqChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateLabels();
        if (_loading || !_ready || _pattern == null) return;
        _pattern.TriggerFrequency = (int)FreqSlider.Value;
        Edited?.Invoke();
    }

    private void OnTrim(object sender, RoutedEventArgs e) => Apply(p => PatternTools.Trim(p, TrimStart.Value, TrimEnd.Value));
    private void OnStretch(object sender, RoutedEventArgs e) => Apply(p => PatternTools.Stretch(p, LengthSlider.Value));

    private void OnQuantize(object sender, RoutedEventArgs e)
    {
        int steps = Steps8.IsChecked == true ? 8 : Steps4.IsChecked == true ? 4 : Steps2.IsChecked == true ? 2 : 1;
        Apply(p => PatternTools.Quantize(p, BpmSlider.Value, steps));
    }

    private void OnSmooth(object sender, RoutedEventArgs e) => Apply(p => PatternTools.Smooth(p, SmoothSlider.Value));
    private void OnGain(object sender, RoutedEventArgs e) => Apply(p => PatternTools.Gain(p, GainSlider.Value));
    private void OnNormalize(object sender, RoutedEventArgs e) => Apply(PatternTools.Normalize);
    private void OnSeam(object sender, RoutedEventArgs e) => Apply(p => PatternTools.CrossfadeSeam(p, SeamSlider.Value));
    private void OnReverse(object sender, RoutedEventArgs e) => Apply(PatternTools.Reverse);
    private void OnSwap(object sender, RoutedEventArgs e) => Apply(PatternTools.SwapSides);

    private void OnClearChannel(object sender, RoutedEventArgs e) => Apply(p =>
    {
        var c = p.Clone();
        c.SetChannel(View.PaintChannel, new byte[p.Length]);
        return c;
    });
}
