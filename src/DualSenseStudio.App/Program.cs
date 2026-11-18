using System.IO;
using System.Windows;
using Velopack;

namespace DualSenseStudio;

public static class Program
{
    private const string MutexName = @"Local\DualSenseStudio.SingleInstance";
    private const string ActivateEventName = @"Local\DualSenseStudio.Activate";

    [STAThread]
    public static void Main(string[] args)
    {
        // Must run first: handles Velopack install/update/uninstall hooks and exits early when needed.
        VelopackApp.Build()
            .OnBeforeUninstallFastCallback(_ => Services.Uninstaller.CleanUpDataIfRequested())
            .Run();

        // One instance only: two copies would fight over the controller's output reports.
        using var mutex = new Mutex(initiallyOwned: true, MutexName, out bool isFirst);
        if (!isFirst)
        {
            try
            {
                using var signal = EventWaitHandle.OpenExisting(ActivateEventName);
                signal.Set();
            }
            catch { }
            return;
        }

        using var activate = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        var app = new App();
        app.InitializeComponent();
        app.DispatcherUnhandledException += (_, e) =>
        {
            Log(e.Exception);
            MessageBox.Show(e.Exception.Message, "DualSense Studio - unexpected error", MessageBoxButton.OK, MessageBoxImage.Warning);
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log(e.ExceptionObject as Exception);

        // Bring the existing window forward when someone launches the app again.
        var listener = new Thread(() =>
        {
            while (activate.WaitOne())
            {
                try { app.Dispatcher.BeginInvoke(() => (app.MainWindow as MainWindow)?.BringToFront()); }
                catch { return; }
            }
        }) { IsBackground = true, Name = "Single-instance listener" };
        listener.Start();

        app.Run();
    }

    public static void Log(Exception? ex)
    {
        if (ex == null) return;
        try
        {
            Directory.CreateDirectory(Services.AppPaths.DataDir);
            var file = Services.AppPaths.LogFile;
            // Keep the log small.
            if (File.Exists(file) && new FileInfo(file).Length > 512 * 1024) File.Delete(file);
            File.AppendAllText(file, $"[{DateTime.Now:u}] {ex}\n\n");
        }
        catch { }
    }
}
