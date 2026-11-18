using System.Text.Json;
using DualSenseStudio.Core.Haptics;
using DualSenseStudio.Core.Protocol;
using DualSenseStudio.Core.Recording;
using DualSenseStudio.Core.Storage;

namespace DualSenseStudio.Core.Tests;

public class PatternSanitizeTests
{
    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef", true)]
    [InlineData("0123456789ABCDEF0123456789ABCDEF", false)]
    [InlineData("..\\..\\..\\Windows\\evil", false)]
    [InlineData("../x", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IdValidation(string? id, bool valid) => Assert.Equal(valid, Pattern.IsValidId(id));

    [Fact]
    public void RepairsHostileFields()
    {
        var p = Pattern.Create(10);
        p.Id = "..\\..\\evil";
        p.SampleRate = 0;
        p.Name = new string('x', 5000);
        p.TriggerFrequency = 9999;
        p.Right = new byte[3];
        p.LeftTrigger = null!;
        Assert.True(p.Sanitize());
        Assert.True(Pattern.IsValidId(p.Id));
        Assert.Equal(Pattern.MinSampleRate, p.SampleRate);
        Assert.Equal(Pattern.MaxNameLength, p.Name.Length);
        Assert.Equal(255, p.TriggerFrequency);
        Assert.Equal(10, p.Right.Length);
        Assert.Equal(10, p.LeftTrigger.Length);
    }

    [Fact]
    public void RejectsEmptyAndOverlongPatterns()
    {
        Assert.False(Pattern.Create(0).Sanitize());
        Assert.False(Pattern.Create((int)(Pattern.MaxDurationSeconds * 100) + 1).Sanitize());
    }
}

public sealed class PatternStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dss-tests-" + Guid.NewGuid().ToString("N"));
    private readonly PatternStore _store;

    public PatternStoreTests() => _store = new PatternStore(Path.Combine(_dir, "patterns"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void SaveAndLoadRoundTrip()
    {
        var p = PresetLibrary.All[0].Pattern.Clone(newId: true);
        p.Name = "Round trip";
        _store.Save(p);
        var loaded = Assert.Single(_store.LoadAll());
        Assert.Equal(p.Id, loaded.Id);
        Assert.Equal(p.Left, loaded.Left);
        Assert.Equal("Round trip", loaded.Name);
    }

    [Fact]
    public void PathForRefusesTraversalIds()
    {
        var p = Pattern.Create(5);
        p.Id = "..\\..\\escape";
        Assert.Throws<InvalidOperationException>(() => _store.PathFor(p));
        Assert.Throws<InvalidOperationException>(() => _store.Save(p));
    }

    [Fact]
    public void HostileFileInLibraryCannotWriteOutsideIt()
    {
        var evil = Pattern.Create(5);
        evil.Id = "..\\..\\pwned";
        File.WriteAllText(Path.Combine(_store.Directory, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" + PatternStore.Extension),
            JsonSerializer.Serialize(evil));

        var loaded = Assert.Single(_store.LoadAll());
        Assert.True(Pattern.IsValidId(loaded.Id));
        _store.Save(loaded);
        Assert.False(File.Exists(Path.Combine(_dir, "pwned" + PatternStore.Extension)));
        Assert.All(Directory.GetFiles(_dir, "*", SearchOption.AllDirectories),
            f => Assert.StartsWith(_store.Directory, f));
    }

    [Fact]
    public void RefusesOversizedAndMalformedFiles()
    {
        var big = Path.Combine(_dir, "big.json");
        using (var fs = File.Create(big)) fs.SetLength(PatternStore.MaxFileBytes + 1);
        Assert.Null(PatternStore.TryLoad(big));

        var junk = Path.Combine(_dir, "junk.json");
        File.WriteAllText(junk, "{ not json");
        Assert.Null(PatternStore.TryLoad(junk));

        var deep = Path.Combine(_dir, "deep.json");
        File.WriteAllText(deep, new string('[', 5000) + new string(']', 5000));
        Assert.Null(PatternStore.TryLoad(deep));
    }

    [Fact]
    public void ImportAlwaysGetsFreshId()
    {
        var p = PresetLibrary.All[1].Pattern.Clone(newId: true);
        var file = Path.Combine(_dir, "share" + PatternStore.Extension);
        PatternStore.Export(p, file);
        var imported = _store.Import(file);
        Assert.NotNull(imported);
        Assert.NotEqual(p.Id, imported!.Id);
        Assert.Equal("Imported", imported.Category);
    }
}

public class PatternToolsTests
{
    private static Pattern Ramp(int n)
    {
        var p = Pattern.Create(n);
        for (int i = 0; i < n; i++) p.Left[i] = p.Right[i] = (byte)(i * 255 / Math.Max(1, n - 1));
        return p;
    }

    [Fact]
    public void StretchChangesLoopLength() => Assert.Equal(200, PatternTools.Stretch(Ramp(100), 2.0).Length);

    [Fact]
    public void TrimKeepsSelection()
    {
        var t = PatternTools.Trim(Ramp(100), 0.2, 0.5);
        Assert.Equal(30, t.Length);
        Assert.Equal(Ramp(100).Left[20], t.Left[0]);
    }

    [Fact]
    public void CrossfadeMakesSeamContinuous()
    {
        var p = PatternTools.CrossfadeSeam(Ramp(100), 200);
        Assert.True(Math.Abs(p.Left[^1] - p.Left[0]) < 30, $"seam jump {p.Left[^1]} -> {p.Left[0]}");
    }

    [Fact]
    public void QuantizeHoldsPeakPerStep()
    {
        var p = Pattern.Create(100);
        p.Left[3] = 200; // single spike inside the first 0.25 s step at 120 BPM / 2 steps per beat
        var q = PatternTools.Quantize(p, 120, 2);
        Assert.Equal(200, q.Left[0]);
        Assert.Equal(0, q.Left[30]);
    }

    [Fact]
    public void OverdubKeepsLouderSample()
    {
        var a = Pattern.Create(4);
        var b = Pattern.Create(4);
        a.Left[0] = 100; b.Left[0] = 50;
        a.Left[1] = 10; b.Left[1] = 90;
        var m = PatternTools.Overdub(a, b);
        Assert.Equal(new byte[] { 100, 90, 0, 0 }, m.Left);
    }

    [Fact]
    public void ToolsNeverReturnMismatchedChannels()
    {
        var p = Ramp(57);
        foreach (var r in new[]
                 {
                     PatternTools.Smooth(p, 80), PatternTools.Gain(p, 1.7), PatternTools.Normalize(p),
                     PatternTools.Reverse(p), PatternTools.SwapSides(p), PatternTools.Quantize(p, 90, 4),
                 })
        {
            Assert.Equal(r.Left.Length, r.Right.Length);
            Assert.Equal(r.Left.Length, r.LeftTrigger.Length);
            Assert.Equal(r.Left.Length, r.RightTrigger.Length);
        }
    }
}

public class PresetAndPlaybackTests
{
    [Fact]
    public void PresetsAreWellFormed()
    {
        var all = PresetLibrary.All;
        Assert.Equal(18, all.Count);
        Assert.Equal(all.Count, all.Select(p => p.Name).Distinct().Count());
        foreach (var p in all)
        {
            Assert.True(p.Pattern.Length > 0, p.Name);
            Assert.Equal(p.Pattern.Left.Length, p.Pattern.Right.Length);
            Assert.True(p.Pattern.Left.Concat(p.Pattern.Right).Concat(p.Pattern.LeftTrigger).Any(b => b > 0), p.Name);
        }
    }

    [Fact]
    public void PatternSourceLoopsAndStops()
    {
        var p = Pattern.Create(100);
        var looping = new PatternSource(p);
        looping.GetFrame(new HapticContext(0, 0.05, InputState.Empty));
        Assert.Equal(0.05 * 100 / 100, looping.Progress, 3);
        for (int i = 0; i < 30; i++) looping.GetFrame(new HapticContext(0, 0.05, InputState.Empty));
        Assert.False(looping.Finished);

        var once = new PatternSource(p) { Loop = false };
        for (int i = 0; i < 30; i++) once.GetFrame(new HapticContext(0, 0.05, InputState.Empty));
        Assert.True(once.Finished);
    }

    [Fact]
    public void IntensityAndBalanceScaleOutput()
    {
        var p = Pattern.Create(10);
        Array.Fill(p.Left, (byte)255);
        Array.Fill(p.Right, (byte)255);
        var src = new PatternSource(p) { Intensity = 0.5, Balance = 1 };
        var f = src.GetFrame(new HapticContext(0, 0.01, InputState.Empty));
        Assert.Equal(0f, f.Left, 3);
        Assert.Equal(0.5f, f.Right, 3);
    }
}

public class RecorderTests
{
    [Fact]
    public void FixedLengthTakeCapturesTriggerPressure()
    {
        var rec = new Recorder();
        Pattern? take = null;
        using var done = new ManualResetEventSlim();
        rec.Completed += p => { take = p; done.Set(); };
        rec.Begin(new RecorderOptions { Mapping = RecordMapping.Pressure, LengthSeconds = 0.5, CountIn = false, FaceButtonHits = false });

        var input = new InputState { R2 = 255, FullReport = true };
        for (int i = 0; i < 80 && rec.State != RecorderState.Idle; i++)
            rec.GetFrame(new HapticContext(i * 0.01, 0.01, input));

        Assert.True(done.Wait(2000));
        Assert.NotNull(take);
        Assert.InRange(take!.Length, 48, 52);
        Assert.All(take.Left, b => Assert.Equal(255, b));
    }

    [Fact]
    public void DualChannelSeparatesSides()
    {
        var rec = new Recorder();
        rec.Begin(new RecorderOptions { Mapping = RecordMapping.DualChannel, LengthSeconds = 1, CountIn = false, FaceButtonHits = false });
        var f = rec.GetFrame(new HapticContext(0, 0.01, new InputState { L2 = 255, R2 = 0 }));
        Assert.Equal(1f, f.Left, 3);
        Assert.Equal(0f, f.Right, 3);
        rec.Cancel();
    }
}
