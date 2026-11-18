using Android.Content;
using Android.Widget;
using DualSenseStudio.Core.Haptics;

namespace DualSenseStudio.Droid.Ui;

/// <summary>Waveform + post-edit tools + undo, shared by the Recorder and the Library (port of the WPF PatternEditor).</summary>
public sealed class PatternEditorView : LinearLayout
{
    private readonly Stack<Pattern> _undo = new();
    private readonly PatternView _view;
    private readonly TextView _info;
    private readonly Button _undoButton, _playButton, _eraseButton;
    private readonly SliderRow _trimStart, _trimEnd, _length, _bpm, _smooth, _gain, _seam, _freq;
    private readonly ChipGroup<int> _steps;
    private Pattern? _pattern;
    private bool _loading;

    public PatternEditorView(Context c) : base(c)
    {
        Orientation = Orientation.Vertical;

        _info = Ui.Text(c, "", 12, Ui.Muted);
        AddView(_info);

        _view = new PatternView(c) { Editable = true };
        AddView(_view, Ui.Match(Ui.Dp(c, 170)));
        _view.StrokeStarted += PushUndo;
        _view.StrokeFinished += RaiseEdited;

        AddView(Ui.Help(c, "Drag on the waveform to draw on the selected channel."));
        var paint = new ChipGroup<Channel>(c,
            [("Left (heavy)", Channel.Left), ("Right (sharp)", Channel.Right), ("L2 vibration", Channel.LeftTrigger), ("R2 vibration", Channel.RightTrigger)],
            Channel.Left, ch => _view.PaintChannel = ch);
        AddView(paint);

        var actions = Ui.Flow(c);
        _playButton = Ui.Button(c, "▶  Play loop", OnPlay, primary: true);
        _undoButton = Ui.Button(c, "Undo", OnUndo);
        _eraseButton = Ui.Button(c, "Eraser: off", () =>
        {
            _view.Erase = !_view.Erase;
            _eraseButton!.Text = _view.Erase ? "Eraser: on" : "Eraser: off";
        });
        actions.AddView(_playButton);
        actions.AddView(_undoButton);
        actions.AddView(_eraseButton);
        actions.AddView(Ui.Button(c, "Clear channel", () => Apply(p =>
        {
            var copy = p.Clone();
            copy.SetChannel(_view.PaintChannel, new byte[p.Length]);
            return copy;
        })));
        AddView(actions);

        // Trim
        var trim = Section(c, "Trim");
        _trimStart = new SliderRow(c, "Start", 0, 10, 0.01, 0, v => $"{v:0.00} s", _ => FixTrim(start: true));
        _trimEnd = new SliderRow(c, "End", 0, 10, 0.01, 10, v => $"{v:0.00} s", _ => FixTrim(start: false));
        trim.AddView(_trimStart);
        trim.AddView(_trimEnd);
        trim.AddView(Ui.Button(c, "Trim to selection", () => Apply(p => PatternTools.Trim(p, _trimStart.Value, _trimEnd.Value))));

        // Stretch
        var stretch = Section(c, "Loop length (time-stretch)");
        _length = new SliderRow(c, "New length", 0.2, 30, 0.1, 4, v => $"{v:0.0} s");
        stretch.AddView(_length);
        stretch.AddView(Ui.Button(c, "Stretch", () => Apply(p => PatternTools.Stretch(p, _length.Value))));

        // Quantize
        var quant = Section(c, "Quantize to BPM");
        _bpm = new SliderRow(c, "Tempo", 40, 240, 1, 120, v => $"{v:0} BPM");
        quant.AddView(_bpm);
        _steps = new ChipGroup<int>(c, [("1 / beat", 1), ("2", 2), ("4", 4), ("8", 8)], 2);
        quant.AddView(_steps);
        quant.AddView(Ui.Button(c, "Quantize", () => Apply(p => PatternTools.Quantize(p, _bpm.Value, _steps.Value))));

        // Shape
        var shape = Section(c, "Shape");
        _smooth = new SliderRow(c, "Smooth window", 10, 400, 5, 60, v => $"{v:0} ms");
        shape.AddView(_smooth);
        shape.AddView(Ui.Button(c, "Smooth", () => Apply(p => PatternTools.Smooth(p, _smooth.Value))));
        _gain = new SliderRow(c, "Gain", 0.1, 3, 0.05, 1.2, v => $"×{v:0.00}");
        shape.AddView(_gain);
        var gainRow = Ui.Flow(c);
        gainRow.AddView(Ui.Button(c, "Apply gain", () => Apply(p => PatternTools.Gain(p, _gain.Value))));
        gainRow.AddView(Ui.Button(c, "Normalize", () => Apply(PatternTools.Normalize)));
        shape.AddView(gainRow);
        _seam = new SliderRow(c, "Seam crossfade", 10, 500, 5, 80, v => $"{v:0} ms");
        shape.AddView(_seam);
        var seamRow = Ui.Flow(c);
        seamRow.AddView(Ui.Button(c, "Crossfade seam", () => Apply(p => PatternTools.CrossfadeSeam(p, _seam.Value))));
        seamRow.AddView(Ui.Button(c, "Reverse", () => Apply(PatternTools.Reverse)));
        seamRow.AddView(Ui.Button(c, "Swap sides", () => Apply(PatternTools.SwapSides)));
        shape.AddView(seamRow);

        // Trigger vibration frequency
        var trig = Section(c, "Trigger vibration");
        _freq = new SliderRow(c, "Frequency", 5, 120, 1, 35, v => $"{v:0} Hz", v =>
        {
            if (_loading || _pattern == null) return;
            _pattern.TriggerFrequency = (int)v;
            Edited?.Invoke();
        });
        trig.AddView(_freq);
        trig.AddView(Ui.Help(c, "Used when the L2 / R2 channels make the triggers vibrate (USB only on Android)."));

        Refresh();
    }

    private LinearLayout Section(Context c, string title)
    {
        var col = Ui.Column(c);
        col.SetPadding(0, Ui.Dp(c, 10), 0, 0);
        col.AddView(Ui.Text(c, title, 14, Ui.Text1, bold: true));
        AddView(col, Ui.Match());
        return col;
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
            Refresh();
        }
    }

    private bool IsPlayingThis => _pattern != null && AppHost.Playback.CurrentPattern?.Pattern == _pattern;

    private void Refresh()
    {
        _loading = true;
        _view.Pattern = _pattern;
        _undoButton.Enabled = _undo.Count > 0;
        _undoButton.Alpha = _undo.Count > 0 ? 1f : 0.4f;
        if (_pattern != null)
        {
            double d = Math.Max(0.02, _pattern.DurationSeconds);
            _trimStart.Maximum = d;
            _trimEnd.Maximum = d;
            _trimStart.Value = 0;
            _trimEnd.Value = d;
            _length.Value = Math.Clamp(d, 0.2, 30);
            _freq.Value = _pattern.TriggerFrequency;
            _info.Text = $"{_pattern.DurationSeconds:0.00} s · {_pattern.Length} samples @ {_pattern.SampleRate} Hz";
        }
        else _info.Text = "";
        _loading = false;
        _view.Invalidate();
    }

    private void FixTrim(bool start)
    {
        if (_loading) return;
        if (_trimEnd.Value < _trimStart.Value + 0.05)
        {
            if (start) _trimEnd.Value = Math.Min(_trimEnd.Maximum, _trimStart.Value + 0.05);
            else _trimStart.Value = Math.Max(0, _trimEnd.Value - 0.05);
        }
    }

    /// <summary>Called by the page's UI timer.</summary>
    public void Tick()
    {
        bool playing = IsPlayingThis;
        _view.Playhead = playing ? AppHost.Playback.CurrentPattern!.Progress : -1;
        _playButton.Text = playing ? "■  Stop" : "▶  Play loop";
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
        _undoButton.Enabled = true;
        _undoButton.Alpha = 1f;
    }

    private void RaiseEdited()
    {
        if (_pattern != null) _pattern.Modified = DateTime.Now;
        _view.Invalidate();
        Edited?.Invoke();
    }

    /// <summary>Applies a tool by copying the result into the current pattern (keeps identity for playback / library).</summary>
    private void Apply(Func<Pattern, Pattern> tool)
    {
        if (_pattern == null) return;
        PushUndo();
        CopyInto(tool(_pattern), _pattern);
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

    private void OnUndo()
    {
        if (_pattern == null || _undo.Count == 0) return;
        CopyInto(_undo.Pop(), _pattern);
        RestartIfPlaying();
        Refresh();
        Edited?.Invoke();
    }

    private void OnPlay()
    {
        if (_pattern == null) return;
        if (IsPlayingThis) AppHost.Playback.Stop();
        else AppHost.Playback.Play(_pattern);
    }
}
