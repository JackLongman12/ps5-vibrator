using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using DualSenseStudio.Core.Haptics;
using DualSenseStudio.Services;

namespace DualSenseStudio.Views;

public partial class SettingsView : UserControl
{
    private bool _loading = true;

    private static AppSettings S => AppHost.Settings;

    public SettingsView()
    {
        InitializeComponent();
        CapSlider.Value = S.MasterCap;
        AutoStopSlider.Value = S.AutoStopMinutes;
        ShortcutsCheck.IsChecked = S.ControllerShortcuts;
        AutoCheck.IsChecked = S.CheckForUpdates;
        AutoDownload.IsChecked = S.AutoDownloadUpdates;
        RepoBox.Text = S.UpdateRepoUrl;
        foreach (var rb in RumbleChips.Children.OfType<RadioButton>())
            rb.IsChecked = (string)rb.Tag == S.RumbleMode.ToString();
        DataPath.Text = AppPaths.DataDir;
        _loading = false;
        UpdateLabels();

        AppHost.Updates.Changed += () => Dispatcher.BeginInvoke(RefreshUpdates);
        Loaded += (_, _) => RefreshUpdates();
    }

    private void UpdateLabels()
    {
        CapText.Text = $"{CapSlider.Value * 100:0}%";
        AutoStopText.Text = AutoStopSlider.Value == 0 ? "never" : $"{AutoStopSlider.Value:0} min";
    }

    private void RefreshUpdates()
    {
        var u = AppHost.Updates;
        VersionInfo.Text = u.IsInstalled
            ? $"Installed version {u.CurrentVersion}"
            : "Running a development build (not installed), so auto-update is off. Install once with Setup.exe from the latest GitHub release to enable it.";
        UpdateStatusText.Text = u.Status switch
        {
            UpdateStatus.NotInstalled => "",
            UpdateStatus.Checking => "Checking GitHub for a newer release…",
            UpdateStatus.UpToDate => $"You're up to date. Last checked {u.LastChecked:t}.",
            UpdateStatus.Downloading => $"Downloading v{u.AvailableVersion}… {u.DownloadProgress}%",
            UpdateStatus.ReadyToRestart => $"v{u.AvailableVersion} is ready. Restart to finish updating.",
            UpdateStatus.Error => $"Update check failed: {u.LastError}",
            _ when u.HasPendingDownload => $"v{u.AvailableVersion} is available.",
            _ => u.LastChecked is { } t ? $"Last checked {t:t}." : "Not checked yet.",
        };
        CheckButton.IsEnabled = u.IsInstalled && u.Status is not (UpdateStatus.Checking or UpdateStatus.Downloading);
        RestartButton.Visibility = u.Status == UpdateStatus.ReadyToRestart ? Visibility.Visible : Visibility.Collapsed;
        TokenSource.Text = "Token in use: " + u.TokenSourceDescription;
    }

    private async void OnCheck(object sender, RoutedEventArgs e) => await AppHost.Updates.CheckAsync(download: true);

    private void OnRestart(object sender, RoutedEventArgs e)
    {
        AppHost.Playback.Stop();
        S.Save();
        AppHost.Updates.ApplyAndRestart();
    }

    private void OnUpdatePrefs(object sender, RoutedEventArgs e)
    {
        S.CheckForUpdates = AutoCheck.IsChecked == true;
        S.AutoDownloadUpdates = AutoDownload.IsChecked == true;
        S.Save();
    }

    private void OnSaveSource(object sender, RoutedEventArgs e)
    {
        var repo = RepoBox.Text.Trim();
        if (!repo.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show("The repository must be a https://github.com/owner/name URL.", "Update source");
            return;
        }
        S.UpdateRepoUrl = repo.TrimEnd('/');
        if (TokenBox.Password.Length > 0) S.GitHubToken = TokenBox.Password;
        TokenBox.Clear();
        S.Save();
        AppHost.Updates.Reconfigure();
        RefreshUpdates();
    }

    private void OnClearToken(object sender, RoutedEventArgs e)
    {
        S.GitHubToken = null;
        S.Save();
        AppHost.Updates.Reconfigure();
        RefreshUpdates();
    }

    private void OnCap(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        S.MasterCap = e.NewValue;
        AppHost.Engine.MasterCap = e.NewValue;
        UpdateLabels();
        S.Save();
    }

    private void OnAutoStop(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        S.AutoStopMinutes = (int)e.NewValue;
        UpdateLabels();
        S.Save();
    }

    private void OnShortcuts(object sender, RoutedEventArgs e)
    {
        S.ControllerShortcuts = ShortcutsCheck.IsChecked == true;
        S.Save();
    }

    private void OnRumble(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton { Tag: string tag }) return;
        if (Enum.TryParse<RumbleMode>(tag, out var m))
        {
            S.RumbleMode = m;
            AppHost.Engine.RumbleMode = m;
            S.Save();
        }
    }

    private void OnTest(object sender, RoutedEventArgs e)
    {
        var p = Pattern.Create(80, "Rumble test");
        for (int i = 0; i < 40; i++) p.Left[i] = 200;
        for (int i = 40; i < 80; i++) p.Right[i] = 200;
        AppHost.Playback.Play(p, loop: false);
    }

    private void OnReconnect(object sender, RoutedEventArgs e)
    {
        var c = AppHost.Controllers.Current;
        if (c != null) AppHost.Controllers.ReportFailure(c);
        AppHost.Controllers.TryConnect();
    }

    private void OnOpenData(object sender, RoutedEventArgs e)
    {
        System.IO.Directory.CreateDirectory(AppPaths.DataDir);
        Process.Start(new ProcessStartInfo("explorer.exe", AppPaths.DataDir) { UseShellExecute = true });
    }
}
