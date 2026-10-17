using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace SuspensionSimulation.App;

public partial class App : Application
{
    private SingleInstanceLifetime? _instance;
    private LiveDiagnostics? _liveDiagnostics;
    private bool _exiting;
    private int _activationCount;
    private static readonly object LogGate = new();
    public static string RepositoryRoot { get; } = ResolveRepositoryRoot();
    public static string RunId { get; } = $"{DateTimeOffset.Now:yyyyMMdd-HHmmss.fff}-{Environment.ProcessId}";
    public static string LogPath { get; } = Path.Combine(RepositoryRoot, "logs", $"run-{RunId}.log");

    public App()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
    }

    private void OnStartup(object sender, StartupEventArgs e)
    {
        try
        {
            _instance=new SingleInstanceLifetime(()=>Dispatcher.BeginInvoke(ActivateExistingWindow));
            if(!_instance.IsPrimary) {Shutdown(0);return;}
            Log("Application object constructed.");
            LogRunManifest();
            _liveDiagnostics=new LiveDiagnostics(()=>new
            {
                activationCount=_activationCount,
                ui=MainWindow is MainWindow window?window.CaptureLiveDiagnostics():null
            });
            Log($"Startup args={JsonSerializer.Serialize(e.Args)}");
            if(e.Args.Contains("--gpu-proof")) { GpuProof.Run(this); return; }
            var window = new MainWindow();
            MainWindow = window;
            window.Closing+=(_,_)=>_liveDiagnostics.Publish("closing");
            var restore=Array.IndexOf(e.Args,"--restore-state");
            // MainWindow's first Loaded handler initializes the package and defaults.
            // Restore afterwards so initialization cannot overwrite the saved view.
            if(restore>=0 && restore+1<e.Args.Length)
                window.Loaded+=(_,_)=>window.RestoreLiveState(e.Args[restore+1]);
            window.Show();
            if(e.Args.Contains("--inspection-proof")) window.RunInspectionProof();
            if(e.Args.Contains("--steering-proof")) window.RunSteeringProof();
            if(e.Args.Contains("--hard-stop-proof")) window.RunHardStopProof();
            if(e.Args.Contains("--stage3-proof")) window.RunStage3Proof();
            if(e.Args.Contains("--stage3-nonpointer")) window.RunStage3Proof(false);
            if(e.Args.Contains("--inspection-soak")) window.RunInspectionSoak();
            window.Activate();
            Log("MainWindow shown.");
            _liveDiagnostics.Publish("running");
        }
        catch (Exception ex)
        {
            LogException("Startup failure", ex);
            MessageBox.Show($"Suspension Simulation failed to start.\n\n{ex.Message}\n\nLog: {LogPath}", "Startup error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void ActivateExistingWindow()
    {
        if(_exiting || MainWindow is null) return;
        _activationCount++;
        MainWindow.Show();
        if(MainWindow.WindowState==WindowState.Minimized) MainWindow.WindowState=WindowState.Normal;
        var activated=MainWindow.Activate();
        if(!activated)
        {
            // Background launchers may lack foreground permission. Still reveal the existing
            // window, then restore its original topmost setting without stealing keyboard input.
            var topmost=MainWindow.Topmost;
            MainWindow.Topmost=true;MainWindow.Topmost=topmost;
        }
        Log(JsonSerializer.Serialize(new {eventName="existing-instance-activated",activationCount=_activationCount,activated}));
        _liveDiagnostics?.Publish("running");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _exiting=true;
        if(_instance?.IsPrimary==true)
        {
            _liveDiagnostics?.Stop(e.ApplicationExitCode==0?"closed":"failed");
            Log($"Application exiting; code={e.ApplicationExitCode}.");
        }
        _instance?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogException("Unhandled UI exception", e.Exception);
        MessageBox.Show($"Suspension Simulation encountered an error.\n\n{e.Exception.Message}\n\nLog: {LogPath}", "Application error", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    public static void Log(string message, string level = "INFO")
    {
        try
        {
            var lines = message.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            var timestamp = DateTimeOffset.Now.ToString("O");
            lock (LogGate)
            {
                using var writer = new StreamWriter(LogPath, append: true, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                foreach (var line in lines) writer.WriteLine($"{timestamp} [{level}] {line}");
                writer.Flush();
            }
        }
        catch
        {
            // Logging must never hide the original simulation failure.
        }
    }

    public static void LogException(string context, Exception exception) => Log($"{context}: {exception}", "ERROR");

    private static void LogRunManifest()
    {
        var manifest = new
        {
            eventName = "run-manifest",
            runId = RunId,
            logPath = LogPath,
            repositoryRoot = RepositoryRoot,
            currentDirectory = Environment.CurrentDirectory,
            appBaseDirectory = AppContext.BaseDirectory,
            commandLine = Environment.CommandLine,
            processId = Environment.ProcessId,
            framework = RuntimeInformation.FrameworkDescription,
            os = RuntimeInformation.OSDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            appVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(),
            git = GitSnapshot()
        };
        Log(JsonSerializer.Serialize(manifest), "MANIFEST");
    }

    private static object GitSnapshot()
    {
        try
        {
            return new
            {
                commit = Git("rev-parse", "HEAD").Trim(),
                branch = Git("branch", "--show-current").Trim(),
                status = Git("status", "--porcelain=v1").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            };
        }
        catch (Exception exception)
        {
            return new { error = exception.Message };
        }
    }

    private static string Git(params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("-c"); start.ArgumentList.Add($"safe.directory={RepositoryRoot.Replace('\\', '/')}");
        start.ArgumentList.Add("-C"); start.ArgumentList.Add(RepositoryRoot);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start git for the run manifest.");
        var output = process.StandardOutput.ReadToEnd(); var error = process.StandardError.ReadToEnd();
        process.WaitForExit(3000);
        if (process.ExitCode != 0) throw new InvalidOperationException(error.Trim());
        return output;
    }

    private static string ResolveRepositoryRoot()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory }.Distinct(StringComparer.OrdinalIgnoreCase))
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
                if (Directory.Exists(Path.Combine(directory.FullName, "data", "synthetic", "gr86")) && Directory.Exists(Path.Combine(directory.FullName, "src")))
                    return directory.FullName;
        return Environment.CurrentDirectory;
    }
}
