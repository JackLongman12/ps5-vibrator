using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DualSenseStudio.Services;

internal static partial class NativeMethods
{
    /// <summary>1 ms scheduler resolution so the 100 Hz haptics loop is steady.</summary>
    [LibraryImport("winmm.dll")]
    internal static partial uint timeBeginPeriod(uint period);

    [LibraryImport("winmm.dll")]
    internal static partial uint timeEndPeriod(uint period);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnregisterHotKey(IntPtr hWnd, int id);

    internal const uint MOD_ALT = 0x0001;
    internal const uint MOD_CONTROL = 0x0002;
    internal const uint MOD_NOREPEAT = 0x4000;
    internal const int WM_HOTKEY = 0x0312;
}

/// <summary>Detects apps known to grab or remap the DualSense.</summary>
public static class ConflictDetector
{
    private static readonly (string Process, string Advice)[] Known =
    [
        ("steam", "Steam is running. If vibration or triggers misbehave, turn off PlayStation controller support in Steam > Settings > Controller, or close Steam."),
        ("DS4Windows", "DS4Windows is running and may hold the controller exclusively. Close it while using DualSense Studio."),
        ("DSX", "DSX is running and also drives the controller. Close it to avoid fighting over vibration and triggers."),
        ("DualSenseX", "DualSenseX is running and also drives the controller. Close it to avoid conflicts."),
    ];

    public static List<string> Check()
    {
        var found = new List<string>();
        foreach (var (name, advice) in Known)
        {
            try
            {
                var procs = Process.GetProcessesByName(name);
                if (procs.Length > 0) found.Add(advice);
                foreach (var p in procs) p.Dispose();
            }
            catch { }
        }
        return found;
    }
}
