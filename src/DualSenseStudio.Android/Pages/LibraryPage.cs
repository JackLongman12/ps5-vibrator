using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using DualSenseStudio.Core.Haptics;
using DualSenseStudio.Core.Storage;
using DualSenseStudio.Droid.Ui;
using U = DualSenseStudio.Droid.Ui.Ui;
using Pattern = DualSenseStudio.Core.Haptics.Pattern;

namespace DualSenseStudio.Droid.Pages;

/// <summary>My Patterns: saved *.dspattern.json files in app storage, with the editor and import / export.</summary>
public sealed class LibraryPage(Activity activity) : Page(activity)
{
    public const int ImportRequest = 4101;
    public const int ExportRequest = 4102;

    private readonly Handler _handler = new(Looper.MainLooper!);
    private readonly List<Pattern> _items = new();
    private Pattern? _current;
    private bool _dirty;
    private LinearLayout _list = null!, _detail = null!;
    private TextView _empty = null!, _savedText = null!, _message = null!;
    private EditText _name = null!;
    private PatternEditorView _editor = null!;

    public override string Title => "Library";

    protected override View Build()
    {
        var c = Activity;
        var col = U.Column(c);
        col.AddView(U.Heading(c, "My Patterns"));
        col.AddView(U.Help(c, "Recordings and edited patterns. Files use the same .dspattern.json format as the Windows app, so you can move them between devices with Import / Export."));

        var actions = U.Flow(c);
        actions.AddView(U.Button(c, "+ New", () =>
            AddAndSelect(Pattern.Create(4 * Pattern.DefaultSampleRate, "New pattern")), primary: true));
        actions.AddView(U.Button(c, "Copy a preset", PickPreset));
        actions.AddView(U.Button(c, "Import…", Import));
        col.AddView(actions);
        _message = U.Help(c, "");
        col.AddView(_message);

        var listCard = U.Card(c, col);
        _empty = U.Help(c, "Nothing saved yet. Record a take, or copy a preset to start editing.");
        listCard.AddView(_empty);
        _list = U.Column(c);
        listCard.AddView(_list, U.Match());

        _detail = U.Card(c, col);
        var nameRow = U.Row(c);
        _name = U.Edit(c, "", "Name");
        _name.EditorAction += (_, e) => { CommitName(); e.Handled = false; };
        _name.FocusChange += (_, e) => { if (!e.HasFocus) CommitName(); };
        nameRow.AddView(_name, U.Weight());
        _savedText = U.Text(c, "", 12, U.Muted);
        nameRow.AddView(_savedText);
        _detail.AddView(nameRow, U.Match());
        var buttons = U.Flow(c);
        buttons.AddView(U.Button(c, "Duplicate", Duplicate));
        buttons.AddView(U.Button(c, "Export…", Export));
        buttons.AddView(U.Button(c, "Delete", Delete));
        _detail.AddView(buttons);
        _editor = new PatternEditorView(c);
        _editor.Edited += () =>
        {
            _dirty = true;
            _savedText.Text = "Editing…";
            _handler.RemoveCallbacksAndMessages(null);
            _handler.PostDelayed(SaveCurrent, 800);
        };
        _detail.AddView(_editor, U.Match());

        AppHost.LibraryChanged += Reload;
        Reload();
        Select(null);
        return col;
    }

    private void Reload()
    {
        var keep = _current?.Id;
        _items.Clear();
        try { _items.AddRange(AppHost.Store.LoadAll()); }
        catch (Exception ex) { _message.Text = "Could not read the library: " + ex.Message; }
        // Keep the object being edited (it may have unsaved strokes).
        if (_current != null)
        {
            int i = _items.FindIndex(p => p.Id == keep);
            if (i >= 0) _items[i] = _current;
        }
        RebuildList();
    }

    private void RebuildList()
    {
        var c = Activity;
        _list.RemoveAllViews();
        Show(_empty, _items.Count == 0);
        foreach (var p in _items)
        {
            var row = U.Row(c);
            row.SetPadding(U.Dp(c, 8), U.Dp(c, 8), U.Dp(c, 8), U.Dp(c, 8));
            row.Background = p == _current ? U.Rounded(U.CardActive, U.Dp(c, 8)) : null;
            var texts = U.Column(c);
            texts.AddView(U.Text(c, p.Name, 14, U.Text1, bold: true));
            texts.AddView(U.Text(c, $"{p.DurationSeconds:0.0} s · {p.Category} · {p.Modified:g}", 11, U.Muted));
            row.AddView(texts, U.Weight());
            var pattern = p;
            var play = U.Button(c, "▶", () =>
            {
                if (AppHost.Playback.CurrentPattern?.Pattern == pattern) AppHost.Playback.Stop();
                else AppHost.Playback.Play(pattern);
            });
            row.AddView(play);
            row.Clickable = true;
            row.Click += (_, _) => Select(pattern);
            _list.AddView(row, U.Match());
        }
    }

    private void Select(Pattern? p)
    {
        if (_current != null && _dirty) SaveCurrent();
        _current = p;
        _editor.Pattern = p;
        Show(_detail, p != null);
        _name.Text = p?.Name ?? "";
        _savedText.Text = p == null ? "" : "Saved";
        RebuildList();
    }

    private void SaveCurrent()
    {
        _handler.RemoveCallbacksAndMessages(null);
        if (_current == null) return;
        try
        {
            AppHost.Store.Save(_current);
            _dirty = false;
            _savedText.Text = "Saved";
        }
        catch (Exception ex)
        {
            _savedText.Text = "Not saved";
            _message.Text = ex.Message;
        }
    }

    private void CommitName()
    {
        if (_current == null) return;
        var name = _name.Text?.Trim() ?? "";
        if (name.Length == 0 || name == _current.Name) return;
        _current.Name = name;
        SaveCurrent();
        RebuildList();
    }

    private void AddAndSelect(Pattern p)
    {
        try { AppHost.Store.Save(p); }
        catch (Exception ex) { _message.Text = ex.Message; return; }
        _items.Insert(0, p);
        Select(p);
    }

    private void PickPreset()
    {
        var names = PresetLibrary.All.Select(x => x.Name).ToArray();
        new AlertDialog.Builder(Activity)
            .SetTitle("Copy a preset")!
            .SetItems(names, (_, e) =>
            {
                var preset = PresetLibrary.All[e.Which].Pattern;
                var copy = preset.Clone(newId: true);
                copy.Name = preset.Name + " (copy)";
                copy.Category = "Custom";
                copy.Created = DateTime.Now;
                AddAndSelect(copy);
            })!
            .Show();
    }

    private void Duplicate()
    {
        if (_current == null) return;
        var copy = _current.Clone(newId: true);
        copy.Name = _current.Name + " (copy)";
        copy.Created = DateTime.Now;
        AddAndSelect(copy);
    }

    private void Delete()
    {
        var victim = _current;
        if (victim == null) return;
        new AlertDialog.Builder(Activity)
            .SetTitle("Delete pattern")!
            .SetMessage($"Delete \"{victim.Name}\"?\n\nIt is moved to a 'deleted' folder in the app's storage.")!
            .SetPositiveButton("Delete", (_, _) =>
            {
                if (AppHost.Playback.CurrentPattern?.Pattern == victim) AppHost.Playback.Stop();
                _handler.RemoveCallbacksAndMessages(null);
                try
                {
                    var path = AppHost.Store.PathFor(victim);
                    var trash = System.IO.Path.Combine(AppHost.Store.Directory, "deleted");
                    Directory.CreateDirectory(trash);
                    if (File.Exists(path)) File.Move(path, System.IO.Path.Combine(trash, System.IO.Path.GetFileName(path)), overwrite: true);
                }
                catch (Exception ex)
                {
                    _message.Text = "Could not delete: " + ex.Message;
                    return;
                }
                _dirty = false;
                _current = null;
                _items.Remove(victim);
                Select(null);
            })!
            .SetNegativeButton("Cancel", (_, _) => { })!
            .Show();
    }

    // ---- import / export via the system file picker (Storage Access Framework) ----------

    private void Import()
    {
        var intent = new Intent(Intent.ActionOpenDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType("*/*");
        intent.PutExtra(Intent.ExtraAllowMultiple, true);
        Activity.StartActivityForResult(intent, ImportRequest);
    }

    private Pattern? _exporting;

    private void Export()
    {
        if (_current == null) return;
        SaveCurrent();
        _exporting = _current;
        var safe = string.Concat(_current.Name.Select(ch => System.IO.Path.GetInvalidFileNameChars().Contains(ch) || ch == '/' ? '_' : ch));
        var intent = new Intent(Intent.ActionCreateDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType("application/json");
        intent.PutExtra(Intent.ExtraTitle, safe + PatternStore.Extension);
        Activity.StartActivityForResult(intent, ExportRequest);
    }

    /// <summary>Forwarded from the Activity.</summary>
    public void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        if (resultCode != Result.Ok || data == null || _message == null) return;
        var resolver = Activity.ContentResolver!;
        if (requestCode == ImportRequest)
        {
            var uris = new List<Android.Net.Uri>();
            if (data.ClipData != null)
                for (int i = 0; i < data.ClipData.ItemCount; i++) { var u = data.ClipData.GetItemAt(i)?.Uri; if (u != null) uris.Add(u); }
            else if (data.Data != null) uris.Add(data.Data);

            int ok = 0, failed = 0;
            foreach (var uri in uris)
            {
                // PatternStore.Import reads a file path, so copy the picked document to the cache first.
                var tmp = System.IO.Path.Combine(Activity.CacheDir!.AbsolutePath, Guid.NewGuid().ToString("N") + PatternStore.Extension);
                try
                {
                    using (var input = resolver.OpenInputStream(uri))
                    using (var output = File.Create(tmp))
                    {
                        if (input == null) throw new IOException();
                        CopyLimited(input, output, 32 * 1024 * 1024);
                    }
                    if (AppHost.Store.Import(tmp) != null) ok++;
                    else failed++;
                }
                catch { failed++; }
                finally { try { File.Delete(tmp); } catch { } }
            }
            _message.Text = failed > 0 ? $"Imported {ok}. {failed} file(s) could not be read as patterns." : $"Imported {ok} pattern(s).";
            Reload();
        }
        else if (requestCode == ExportRequest && _exporting != null && data.Data != null)
        {
            var tmp = System.IO.Path.Combine(Activity.CacheDir!.AbsolutePath, "export" + PatternStore.Extension);
            try
            {
                PatternStore.Export(_exporting, tmp);
                using var output = resolver.OpenOutputStream(data.Data, "wt") ?? throw new IOException("Could not open the destination.");
                using var input = File.OpenRead(tmp);
                input.CopyTo(output);
                _message.Text = $"Exported \"{_exporting.Name}\".";
            }
            catch (Exception ex)
            {
                _message.Text = "Export failed: " + ex.Message;
            }
            finally { try { File.Delete(tmp); } catch { } }
        }
    }

    private static void CopyLimited(Stream input, Stream output, long max)
    {
        var buf = new byte[81920];
        long total = 0;
        int n;
        while ((n = input.Read(buf, 0, buf.Length)) > 0)
        {
            total += n;
            if (total > max) throw new IOException("File too large.");
            output.Write(buf, 0, n);
        }
    }

    public override void Tick() => _editor.Tick();

    public override void Detach()
    {
        AppHost.LibraryChanged -= Reload;
        _handler.RemoveCallbacksAndMessages(null);
    }

    public override void OnHidden()
    {
        if (_dirty) SaveCurrent();
    }
}
