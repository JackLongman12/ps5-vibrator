using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using DualSenseStudio.Core.Haptics;
using DualSenseStudio.Core.Storage;
using DualSenseStudio.Services;
using Microsoft.Win32;

namespace DualSenseStudio.Views;

public partial class LibraryView : UserControl
{
    private readonly ObservableCollection<Pattern> _items = new();
    private readonly DispatcherTimer _saveTimer;
    private Pattern? _current;

    public LibraryView()
    {
        InitializeComponent();
        List.ItemsSource = _items;
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveCurrent(); };
        Editor.Edited += () => { SavedText.Text = "Editing…"; _saveTimer.Stop(); _saveTimer.Start(); };
        AppHost.LibraryChanged += Reload;
        Reload();
        Select(null);
    }

    private void Reload()
    {
        var keepId = _current?.Id;
        _items.Clear();
        foreach (var p in AppHost.Store.LoadAll()) _items.Add(p);
        EmptyText.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var again = _items.FirstOrDefault(p => p.Id == keepId);
        if (again != null) List.SelectedItem = again;
    }

    private void Select(Pattern? p)
    {
        if (_current != null && _saveTimer.IsEnabled) { _saveTimer.Stop(); SaveCurrent(); }
        _current = p;
        Editor.Pattern = p;
        Editor.Visibility = p == null ? Visibility.Collapsed : Visibility.Visible;
        SelectHint.Visibility = p == null ? Visibility.Visible : Visibility.Collapsed;
        NameBox.IsEnabled = p != null;
        NameBox.Text = p?.Name ?? "";
        SavedText.Text = p == null ? "" : "Saved";
    }

    private void SaveCurrent()
    {
        if (_current == null) return;
        AppHost.Store.Save(_current);
        SavedText.Text = "Saved";
        List.Items.Refresh();
    }

    private void AddAndSelect(Pattern p)
    {
        AppHost.Store.Save(p);
        _items.Insert(0, p);
        EmptyText.Visibility = Visibility.Collapsed;
        List.SelectedItem = p;
    }

    private void OnSelect(object sender, SelectionChangedEventArgs e) => Select(List.SelectedItem as Pattern);

    private void OnListDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_current != null) AppHost.Playback.Play(_current);
    }

    private void OnNameCommit(object sender, RoutedEventArgs e)
    {
        if (_current == null) return;
        var name = NameBox.Text.Trim();
        if (name.Length == 0 || name == _current.Name) return;
        _current.Name = name;
        SaveCurrent();
    }

    private void OnNameKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) OnNameCommit(sender, e);
    }

    private void OnNew(object sender, RoutedEventArgs e)
    {
        var p = Pattern.Create(4 * Pattern.DefaultSampleRate, "New pattern");
        AddAndSelect(p);
    }

    private void OnFromPreset(object sender, RoutedEventArgs e)
    {
        var src = AppHost.Playback.CurrentPattern?.Pattern;
        var preset = src != null && src.Category == "Preset" ? src : PresetLibrary.All[0].Pattern;
        var copy = preset.Clone(newId: true);
        copy.Name = preset.Name + " (copy)";
        copy.Category = "Custom";
        copy.Created = DateTime.Now;
        AddAndSelect(copy);
    }

    private void OnDuplicate(object sender, RoutedEventArgs e)
    {
        if (_current == null) return;
        var copy = _current.Clone(newId: true);
        copy.Name = _current.Name + " (copy)";
        copy.Created = DateTime.Now;
        AddAndSelect(copy);
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (_current == null) return;
        var answer = MessageBox.Show($"Delete \"{_current.Name}\"?\n\nIt is moved to the 'deleted' folder inside your data folder, so you can still recover it.",
            "Delete pattern", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer != MessageBoxResult.OK) return;
        if (AppHost.Playback.CurrentPattern?.Pattern == _current) AppHost.Playback.Stop();
        _saveTimer.Stop();
        var path = AppHost.Store.PathFor(_current);
        try
        {
            var trash = Path.Combine(AppPaths.PatternsDir, "deleted");
            Directory.CreateDirectory(trash);
            if (File.Exists(path)) File.Move(path, Path.Combine(trash, Path.GetFileName(path)), overwrite: true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Could not delete");
            return;
        }
        var victim = _current;
        _current = null;
        _items.Remove(victim);
        EmptyText.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Select(null);
    }

    private void OnImport(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "DualSense patterns (*.dspattern.json)|*.dspattern.json|JSON (*.json)|*.json",
            Multiselect = true,
        };
        if (dlg.ShowDialog() != true) return;
        int failed = 0;
        foreach (var file in dlg.FileNames)
        {
            var p = AppHost.Store.Import(file);
            if (p == null) failed++;
        }
        Reload();
        if (failed > 0) MessageBox.Show($"{failed} file(s) could not be read as patterns.", "Import");
    }

    private void OnExport(object sender, RoutedEventArgs e)
    {
        if (_current == null) return;
        var safe = string.Concat(_current.Name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        var dlg = new SaveFileDialog
        {
            FileName = safe + PatternStore.Extension,
            Filter = "DualSense pattern (*.dspattern.json)|*.dspattern.json",
        };
        if (dlg.ShowDialog() == true) PatternStore.Export(_current, dlg.FileName);
    }
}
