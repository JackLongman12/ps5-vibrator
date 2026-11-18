using Android.App;
using Android.Views;
using Android.Widget;
using DualSenseStudio.Core.Haptics;
using DualSenseStudio.Core.Recording;
using DualSenseStudio.Droid.Ui;
using U = DualSenseStudio.Droid.Ui.Ui;

namespace DualSenseStudio.Droid.Pages;

public sealed class RecorderPage(Activity activity) : Page(activity)
{
    private static readonly Dictionary<RecordMapping, string> Help = new()
    {
        [RecordMapping.Pressure] = "How far you press is how hard it vibrates. Either trigger drives both sides.",
        [RecordMapping.DualChannel] = "L2 drives the left (heavy) side and R2 drives the right (sharp) side, so you can play two parts at once.",
        [RecordMapping.VelocityAccents] = "Speed matters: a fast stab makes a sharp spike, a slow squeeze makes a gentle swell with a light body underneath.",
        [RecordMapping.Zones] = "Pressure picks the texture: light = smooth hum, medium = pulse train that speeds up as you press, full = rough buzz.",
    };

    private RecordMapping _mapping = RecordMapping.Pressure;
    private double _length = 8;
    private Pattern? _take;
    private bool _saved;
    private bool _countIn = true, _hits = true, _trigVib, _overdub, _autoPlay = true;

    private TextView _mappingHelp = null!, _state = null!, _detail = null!, _noTake = null!, _btNote = null!;
    private Button _record = null!, _save = null!;
    private ProgressBar _progress = null!, _l2 = null!, _r2 = null!;
    private PatternView _live = null!;
    private PatternEditorView _editor = null!;
    private LinearLayout _takeCard = null!;
    private EditText _name = null!;
    private SliderRow _lengthSlider = null!, _curve = null!;
    private ChipGroup<double> _lengthChips = null!;
    private CheckBox _trigVibCheck = null!;

    public override string Title => "Recorder";

    protected override View Build()
    {
        var c = Activity;
        var col = U.Column(c);
        col.AddView(U.Heading(c, "Trigger recorder"));
        col.AddView(U.Help(c, "Play L2 / R2 like an instrument and record a loop. You feel what is being captured while you record."));

        var setup = U.Card(c, col);
        setup.AddView(U.Text(c, "Mapping", 14, U.Text1, bold: true));
        setup.AddView(new ChipGroup<RecordMapping>(c,
            [("Pressure", RecordMapping.Pressure), ("Dual channel", RecordMapping.DualChannel), ("Velocity accents", RecordMapping.VelocityAccents), ("Zones", RecordMapping.Zones)],
            _mapping, m => { _mapping = m; _mappingHelp.Text = Help[m]; }));
        _mappingHelp = U.Help(c, Help[_mapping]);
        setup.AddView(_mappingHelp);

        setup.AddView(U.Text(c, "Take length", 14, U.Text1, bold: true));
        _lengthChips = new ChipGroup<double>(c, [("4 s", 4), ("8 s", 8), ("16 s", 16), ("32 s", 32), ("Free", 0)], 8, v =>
        {
            _length = v;
            if (v > 0) _lengthSlider.Value = v;
            Show(_lengthSlider, v > 0);
        });
        setup.AddView(_lengthChips);
        _lengthSlider = new SliderRow(c, "Fixed length", 1, 60, 1, 8, v => $"{v:0} s", v =>
        {
            _length = v;
            _lengthChips.Value = v is 4 or 8 or 16 or 32 ? v : -1;
        });
        setup.AddView(_lengthSlider);
        setup.AddView(U.Help(c, $"Free: record until you press Stop (up to {Recorder.MaxFreeSeconds:0} s)."));

        setup.AddView(U.Check(c, "Count-in (3 beats)", _countIn, v => _countIn = v));
        setup.AddView(U.Check(c, "Face buttons add hits (✕ ○ □ △)", _hits, v => _hits = v));
        _trigVibCheck = U.Check(c, "Also record trigger vibration (L2/R2 channels)", _trigVib, v => _trigVib = v);
        setup.AddView(_trigVibCheck);
        setup.AddView(U.Check(c, "Overdub onto the current take", _overdub, v => _overdub = v));
        setup.AddView(U.Check(c, "Play the take when finished", _autoPlay, v => _autoPlay = v));
        _curve = new SliderRow(c, "Response curve", -1.5, 1.5, 0.05, 0, v => v switch
        {
            < -0.05 => $"sensitive ({Math.Pow(2, v):0.00})",
            > 0.05 => $"firm ({Math.Pow(2, v):0.00})",
            _ => "linear",
        });
        setup.AddView(_curve);

        var rec = U.Card(c, col);
        _btNote = U.Banner(c, "Bluetooth: trigger pressure is read through Android's gamepad driver, so recording works. Trigger vibration playback needs USB.");
        rec.AddView(_btNote);
        _record = U.Button(c, "●  Record", ToggleRecording, primary: true);
        rec.AddView(_record);
        _state = U.Text(c, "Ready", 16, U.Text1, bold: true);
        rec.AddView(_state);
        _detail = U.Help(c, "Press Record (or Create on the controller), then play the triggers.");
        rec.AddView(_detail);
        _progress = U.Meter(c, U.Danger);
        rec.AddView(_progress);
        rec.AddView(U.Spacer(c, 8));
        var meters = U.Row(c);
        meters.AddView(U.Text(c, "L2 ", 12, U.Muted));
        _l2 = U.Meter(c);
        meters.AddView(_l2, U.Weight());
        meters.AddView(U.Text(c, "   R2 ", 12, U.Muted));
        _r2 = U.Meter(c, U.AccentOrange);
        meters.AddView(_r2, U.Weight());
        rec.AddView(meters, U.Match());
        _live = new PatternView(c);
        var lp = U.Match(U.Dp(c, 110));
        lp.TopMargin = U.Dp(c, 10);
        rec.AddView(_live, lp);

        _takeCard = U.Card(c, col);
        _takeCard.AddView(U.Text(c, "Take", 14, U.Text1, bold: true));
        _noTake = U.Help(c, "No take yet.");
        _takeCard.AddView(_noTake);
        var saveRow = U.Row(c);
        _name = U.Edit(c, "", "Name");
        saveRow.AddView(_name, U.Weight());
        _save = U.Button(c, "Save to My Patterns", OnSave, primary: true);
        saveRow.AddView(_save);
        _takeCard.AddView(saveRow, U.Match());
        _editor = new PatternEditorView(c);
        _editor.Edited += () => _saved = false;
        _takeCard.AddView(_editor, U.Match());
        Show(_editor, false);
        Show(saveRow, false);
        _save.Tag = saveRow;

        AppHost.Recorder.Completed += OnCompletedFromEngine;
        AppHost.Engine.ButtonsPressed += OnButtonsFromEngine;
        return col;
    }

    public bool IsVisible { get; set; }

    // Both run on the engine thread.
    private void OnCompletedFromEngine(Pattern p) => Activity.RunOnUiThread(() => OnTakeCompleted(p));

    private void OnButtonsFromEngine(Core.Protocol.Buttons pressed, Core.Protocol.InputState _)
    {
        if ((pressed & Core.Protocol.Buttons.Create) != 0) Activity.RunOnUiThread(() => { if (IsVisible) ToggleRecording(); });
    }

    public override void Detach()
    {
        AppHost.Recorder.Completed -= OnCompletedFromEngine;
        AppHost.Engine.ButtonsPressed -= OnButtonsFromEngine;
    }

    public override void Refresh()
    {
        bool bt = AppHost.Kind == ControllerKind.Bluetooth;
        Show(_btNote, bt);
        _trigVibCheck.Enabled = !bt;
        _trigVibCheck.Alpha = bt ? 0.5f : 1f;
    }

    private void ToggleRecording()
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

    private void Start()
    {
        if (AppHost.Controllers.Current == null)
        {
            _state.Text = "No controller";
            _detail.Text = "Connect your DualSense first.";
            return;
        }
        bool overdub = _overdub && _take != null;
        var options = new RecorderOptions
        {
            Mapping = _mapping,
            LengthSeconds = _length,
            CountIn = _countIn,
            FaceButtonHits = _hits,
            RecordTriggerVibration = _trigVib && AppHost.HasRawHid,
            Curve = Math.Pow(2, _curve.Value),
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
            _state.Text = "Take too short";
            _detail.Text = "Record for at least a moment.";
            return;
        }
        bool wasOverdub = _overdub && _take != null;
        if (wasOverdub && _take != null)
        {
            take.Id = _take.Id;
            take.Created = _take.Created;
        }
        _take = take;
        _saved = false;
        _editor.Pattern = take;
        Show(_editor, true);
        Show((View)_save.Tag!, true);
        Show(_noTake, false);
        if (!wasOverdub || string.IsNullOrWhiteSpace(_name.Text)) _name.Text = take.Name;
        _live.Pattern = take;
        _live.MinDisplaySamples = 0;
        _state.Text = "Take recorded";
        _detail.Text = $"{take.DurationSeconds:0.0} s. Edit it below, then save it.";
        if (_autoPlay) AppHost.Playback.Play(take);
    }

    private void OnSave()
    {
        if (_take == null) return;
        var name = _name.Text?.Trim();
        if (!string.IsNullOrEmpty(name)) _take.Name = name;
        _take.Category = "Recording";
        try
        {
            AppHost.Store.Save(_take);
        }
        catch (Exception ex)
        {
            _detail.Text = "Could not save: " + ex.Message;
            return;
        }
        AppHost.NotifyLibraryChanged();
        _saved = true;
        _detail.Text = $"Saved \"{_take.Name}\" to My Patterns.";
    }

    public override void Tick()
    {
        var rec = AppHost.Recorder;
        var input = AppHost.Engine.Input;
        U.SetMeter(_l2, input.L2Value);
        U.SetMeter(_r2, input.R2Value);

        switch (rec.State)
        {
            case RecorderState.CountIn:
                _record.Text = "■  Cancel";
                _state.Text = $"Get ready… {Math.Ceiling(-rec.Elapsed / 0.6):0}";
                _detail.Text = "Recording starts after the count-in.";
                U.SetMeter(_progress, 0);
                break;
            case RecorderState.Recording:
            {
                _record.Text = "■  Stop";
                double target = rec.TargetSeconds;
                _state.Text = target > 0
                    ? $"Recording  {rec.Elapsed:0.0} / {target:0.#} s"
                    : $"Recording  {rec.Elapsed:0.0} s (free)";
                _detail.Text = "Play the triggers. Press Stop (or Create) to finish early.";
                U.SetMeter(_progress, target > 0 ? rec.Elapsed / target : (rec.Elapsed % 4) / 4);
                _live.MinDisplaySamples = target > 0 ? (int)(target * Pattern.DefaultSampleRate) : 0;
                _live.Pattern = rec.Snapshot();
                break;
            }
            default:
                _record.Text = "●  Record";
                U.SetMeter(_progress, 0);
                _live.Playhead = _take != null && AppHost.Playback.CurrentPattern?.Pattern == _take
                    ? AppHost.Playback.CurrentPattern.Progress : -1;
                break;
        }
        _save.Text = _saved ? "Saved ✓" : "Save";
        _editor.Tick();
    }
}
