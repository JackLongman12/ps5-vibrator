using System.IO;
using System.Windows;
using Velopack;

namespace DualSenseStudio;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Must run first: handles Velopack install/update/uninstall hooks and exits early when needed.
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.DispatcherUnhandledException += (_, e) =>
        {
            Log(e.Exception);
            MessageBox.Show(e.Exception.Message, "DualSense Studio - unexpected error", MessageBoxButton.OK, MessageBoxImage.Warning);
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log(e.ExceptionObject as Exception);
        app.Run();
    }

    public static void Log(Exception? ex)
    {
        if (ex == null) return;
        try
        {
            Directory.CreateDirectory(Services.AppPaths.DataDir);
            File.AppendAllText(Services.AppPaths.LogFile, $"[{DateTime.Now:u}] {ex}\n\n");
        }
        catch { }
    }
}
