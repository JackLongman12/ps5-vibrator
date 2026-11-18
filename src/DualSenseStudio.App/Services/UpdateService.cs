using Velopack;
using Velopack.Sources;

namespace DualSenseStudio.Services;

public enum UpdateStatus { NotInstalled, Idle, Checking, UpToDate, Downloading, ReadyToRestart, Error }

/// <summary>
/// Auto-update through Velopack + GitHub Releases. Releases are produced by scripts/release.ps1.
/// The release repo is public; a token is only used if the user saves one in Settings (e.g. for a private fork).
/// </summary>
public sealed class UpdateService
{
    public const string DefaultRepoUrl = "https://github.com/JackLongman12/ps5-vibrator";

    /// <summary>Earlier default feed URLs; settings still pointing at one are moved to <see cref="DefaultRepoUrl"/>.</summary>
    public static readonly string[] LegacyRepoUrls = ["https://github.com/JackLongman12/dualsense-studio"];

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
        var repo = IsValidRepoUrl(_settings.UpdateRepoUrl) ? _settings.UpdateRepoUrl : DefaultRepoUrl;
        var source = new GithubSource(repo, token, prerelease: false);
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

    public string TokenSourceDescription =>
        string.IsNullOrEmpty(_settings.GitHubToken)
            ? "none (public repository, no token needed)"
            : "token saved in Settings (encrypted for your Windows account)";

    /// <summary>
    /// Only a token the user explicitly saved is used. The release repo is public, so normally none is sent.
    /// (Earlier builds borrowed the GitHub CLI's login; that token has broad scopes, so it is no longer used.)
    /// </summary>
    private string? ResolveToken() => _settings.GitHubToken;

    /// <summary>Accepts only https://github.com/{owner}/{repo} so the update feed can't be pointed at another host.</summary>
    public static bool IsValidRepoUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) &&
        u.Scheme == Uri.UriSchemeHttps &&
        u.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
        u.AbsolutePath.Trim('/').Split('/') is { Length: 2 } parts &&
        parts.All(p => p.Length > 0 && p.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.')) &&
        string.IsNullOrEmpty(u.Query) && string.IsNullOrEmpty(u.Fragment) && string.IsNullOrEmpty(u.UserInfo);

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
