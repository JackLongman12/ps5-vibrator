using System.Diagnostics;
using Velopack;
using Velopack.Sources;

namespace DualSenseStudio.Services;

public enum UpdateStatus { NotInstalled, Idle, Checking, UpToDate, Downloading, ReadyToRestart, Error }

/// <summary>
/// Auto-update through Velopack + GitHub Releases. Releases are produced by scripts/release.ps1.
/// Works for a private repo when a token is available (settings, or the GitHub CLI's login on this PC).
/// </summary>
public sealed class UpdateService
{
    public const string DefaultRepoUrl = "https://github.com/JackLongman12/dualsense-studio";

    private readonly AppSettings _settings;
    private UpdateManager? _mgr;
    private UpdateInfo? _pending;

    public UpdateService(AppSettings settings)
    {
        _settings = settings;
        try
        {
            _mgr = CreateManager();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
        Status = IsInstalled ? UpdateStatus.Idle : UpdateStatus.NotInstalled;
        if (_mgr?.UpdatePendingRestart != null)
            Status = UpdateStatus.ReadyToRestart;
    }

    public UpdateStatus Status { get; private set; }
    public string? LastError { get; private set; }
    public string? AvailableVersion => _pending?.TargetFullRelease.Version.ToString()
                                       ?? _mgr?.UpdatePendingRestart?.Version.ToString();
    public int DownloadProgress { get; private set; }
    public DateTime? LastChecked { get; private set; }
    public bool IsInstalled => _mgr?.IsInstalled == true;
    public string CurrentVersion => _mgr?.CurrentVersion?.ToString() ?? "dev build";

    /// <summary>Raised (any thread) when status / progress changes.</summary>
    public event Action? Changed;

    private UpdateManager CreateManager()
    {
        var token = ResolveToken();
        var source = new GithubSource(_settings.UpdateRepoUrl, token, prerelease: true);
        return new UpdateManager(source);
    }

    /// <summary>Call after the token or repo URL changes in Settings.</summary>
    public void Reconfigure()
    {
        try
        {
            _mgr = CreateManager();
            LastError = null;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
        Changed?.Invoke();
    }

    public string TokenSourceDescription
    {
        get
        {
            if (!string.IsNullOrEmpty(_settings.GitHubToken)) return "token saved in Settings";
            return GhCliToken() != null ? "GitHub CLI login on this PC" : "none (works only if the repo is public)";
        }
    }

    private string? ResolveToken() => _settings.GitHubToken ?? GhCliToken();

    private static string? _ghToken;
    private static bool _ghChecked;

    private static string? GhCliToken()
    {
        if (_ghChecked) return _ghToken;
        _ghChecked = true;
        try
        {
            var psi = new ProcessStartInfo("gh", "auth token")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p == null) return null;
            var output = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit(5000);
            _ghToken = p.ExitCode == 0 && output.Length > 0 ? output : null;
        }
        catch
        {
            _ghToken = null;
        }
        return _ghToken;
    }

    /// <summary>Checks GitHub for a newer release and (optionally) downloads it in the background.</summary>
    public async Task CheckAsync(bool download)
    {
        if (_mgr == null || !IsInstalled)
        {
            Status = UpdateStatus.NotInstalled;
            Changed?.Invoke();
            return;
        }
        if (Status is UpdateStatus.Checking or UpdateStatus.Downloading) return;
        if (Status == UpdateStatus.ReadyToRestart && _pending == null) return;

        try
        {
            Status = UpdateStatus.Checking;
            LastError = null;
            Changed?.Invoke();

            var info = await _mgr.CheckForUpdatesAsync();
            LastChecked = DateTime.Now;
            if (info == null)
            {
                _pending = null;
                Status = _mgr.UpdatePendingRestart != null ? UpdateStatus.ReadyToRestart : UpdateStatus.UpToDate;
                Changed?.Invoke();
                return;
            }

            _pending = info;
            if (!download)
            {
                Status = UpdateStatus.Idle;
                Changed?.Invoke();
                return;
            }
            await DownloadAsync();
        }
        catch (Exception ex)
        {
            Status = UpdateStatus.Error;
            LastError = ex.Message;
            Changed?.Invoke();
        }
    }

    public async Task DownloadAsync()
    {
        if (_mgr == null || _pending == null) return;
        try
        {
            Status = UpdateStatus.Downloading;
            DownloadProgress = 0;
            Changed?.Invoke();
            await _mgr.DownloadUpdatesAsync(_pending, p =>
            {
                DownloadProgress = p;
                Changed?.Invoke();
            });
            Status = UpdateStatus.ReadyToRestart;
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            Status = UpdateStatus.Error;
            LastError = ex.Message;
            Changed?.Invoke();
        }
    }

    public bool HasPendingDownload => _pending != null && Status != UpdateStatus.ReadyToRestart;

    /// <summary>Installs the downloaded update and restarts the app.</summary>
    public void ApplyAndRestart()
    {
        if (_mgr == null) return;
        var asset = _pending?.TargetFullRelease ?? _mgr.UpdatePendingRestart;
        if (asset == null) return;
        _mgr.ApplyUpdatesAndRestart(asset);
    }
}
