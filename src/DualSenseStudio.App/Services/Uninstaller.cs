using System.Diagnostics;
using System.IO;

namespace DualSenseStudio.Services;

/// <summary>
/// One-click uninstall. Runs Velopack's Update.exe --uninstall (the same thing Windows
/// "Installed apps" runs), optionally removing the user's patterns and settings too.
/// </summary>
public static class Uninstaller
{
    /// <summary>Marker that tells the uninstall hook to delete the data folder as well.</summary>
    private static string MarkerFile => Path.Combine(AppPaths.DataDir, ".remove-on-uninstall");

    /// <summary>Set once an uninstall has started; stops the app re-saving settings as it exits.</summary>
    public static bool InProgress { get; private set; }

    /// <summary>Velopack's Update.exe sits one level above the running app's "current" folder.</summary>
    public static string? UpdateExePath
    {
        get
        {
            var appDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            var root = Directory.GetParent(appDir)?.FullName;
            if (root == null) return null;
            var exe = Path.Combine(root, "Update.exe");
            return File.Exists(exe) ? exe : null;
        }
    }

    /// <summary>
    /// Starts the uninstaller. The caller must exit the app right after this returns true.
    /// </summary>
    public static bool Start(bool removeData, out string? error)
    {
        error = null;
        var exe = UpdateExePath;
        if (exe == null)
        {
            error = "This copy isn't installed (it's a development build), so there's nothing to uninstall.";
            return false;
        }
        try
        {
            if (removeData)
            {
                Directory.CreateDirectory(AppPaths.DataDir);
                File.WriteAllText(MarkerFile, "remove");
            }
            else if (File.Exists(MarkerFile))
            {
                File.Delete(MarkerFile);
            }
            InProgress = true;
            Process.Start(new ProcessStartInfo(exe, "--uninstall") { UseShellExecute = false });
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Called by the Velopack uninstall hook (the app isn't running at that point).
    /// Deletes %AppData%\DualSenseStudio only if the user asked for it in the app.
    /// </summary>
    public static void CleanUpDataIfRequested()
    {
        try
        {
            if (!File.Exists(MarkerFile)) return;
            var dir = Path.GetFullPath(AppPaths.DataDir);
            // Guard: only ever delete our own folder.
            if (!dir.EndsWith(Path.DirectorySeparatorChar + "DualSenseStudio", StringComparison.OrdinalIgnoreCase)) return;
            Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // Never block the uninstall because of leftover data.
        }
    }
}
