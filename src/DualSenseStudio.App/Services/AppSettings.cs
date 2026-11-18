using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DualSenseStudio.Core.Haptics;

namespace DualSenseStudio.Services;

public static class AppPaths
{
    public static string DataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DualSenseStudio");
    public static string PatternsDir => Path.Combine(DataDir, "patterns");
    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string LogFile => Path.Combine(DataDir, "log.txt");
}

/// <summary>User settings, persisted to %AppData%\DualSenseStudio\settings.json (survives updates).</summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    // Safety
    public double MasterCap { get; set; } = 1.0;
    public int AutoStopMinutes { get; set; }

    // Controller
    public bool ControllerShortcuts { get; set; } = true;
    public RumbleMode RumbleMode { get; set; } = RumbleMode.Auto;
    public LightSettings Lights { get; set; } = new();

    // Player
    public double Speed { get; set; } = 1.0;
    public double Intensity { get; set; } = 0.8;
    public double Balance { get; set; }
    public bool PlayTriggerChannels { get; set; } = true;

    // Updates
    public bool CheckForUpdates { get; set; } = true;
    public bool AutoDownloadUpdates { get; set; } = true;
    public string UpdateRepoUrl { get; set; } = UpdateService.DefaultRepoUrl;
    /// <summary>GitHub token for a private release repo, encrypted with Windows DPAPI (current user).</summary>
    public string? ProtectedGitHubToken { get; set; }

    [JsonIgnore]
    public string? GitHubToken
    {
        get
        {
            if (string.IsNullOrEmpty(ProtectedGitHubToken)) return null;
            try
            {
                var bytes = ProtectedData.Unprotect(Convert.FromBase64String(ProtectedGitHubToken), null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch { return null; }
        }
        set
        {
            ProtectedGitHubToken = string.IsNullOrWhiteSpace(value)
                ? null
                : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value.Trim()), null, DataProtectionScope.CurrentUser));
        }
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsFile), Json) ?? new AppSettings();
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(this, Json));
        }
        catch { }
    }
}
