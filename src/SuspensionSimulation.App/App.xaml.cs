using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace SuspensionSimulation.App;

public partial class App : Application
{
    public static string LogPath { get; } = Path.Combine(Path.GetTempPath(), "SuspensionSimulation", "app.log");

    public App()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
        Log("Application object constructed.");
    }

    private void OnStartup(object sender, StartupEventArgs e)
    {
        Log($"Startup. Args: {string.Join(" ", e.Args)}");
        try
        {
            var window = new MainWindow();
            MainWindow = window;
            window.Show();
            window.Activate();
            Log("MainWindow shown.");
        }
        catch (Exception ex)
        {
            Log("Startup failure: " + ex);
            MessageBox.Show($"Suspension Simulation failed to start.\n\n{ex.Message}\n\nLog: {LogPath}", "Startup error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log("Unhandled UI exception: " + e.Exception);
        MessageBox.Show($"Suspension Simulation encountered an error.\n\n{e.Exception.Message}\n\nLog: {LogPath}", "Application error", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    public static void Log(string message)
    {
        try { File.AppendAllText(LogPath, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}"); } catch { }
    }
}
