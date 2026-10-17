using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;

namespace SuspensionSimulation.App;

/// <summary>Local, atomic, continuously refreshed diagnostics independent of a terminal/chat connection.</summary>
internal sealed class LiveDiagnostics
{
    internal static string SessionPath=>Path.Combine(App.RepositoryRoot,"logs","current-session.json");
    internal static string StatePath=>Path.Combine(App.RepositoryRoot,"logs","current-state.json");
    private readonly DispatcherTimer _timer=new(DispatcherPriority.Background) {Interval=TimeSpan.FromSeconds(1)};
    private readonly Func<object?> _capture;
    private readonly DateTimeOffset _started=DateTimeOffset.Now;
    private JsonElement? _lastState;
    private static readonly JsonSerializerOptions Options=new()
    {
        WriteIndented=true,IncludeFields=true,NumberHandling=JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters={new JsonStringEnumConverter()}
    };
    public LiveDiagnostics(Func<object?> capture)
    {
        _capture=capture;_timer.Tick+=(_,_)=>Publish("running");
        Publish("starting");_timer.Start();
    }
    public void Stop(string status)
    {
        _timer.Stop();Publish(status,capture:false);
    }
    public void Publish(string status,bool capture=true)
    {
        try
        {
            try {if(capture) _lastState=JsonSerializer.SerializeToElement(_capture(),Options);}
            catch(Exception error) {App.LogException("Live state capture failed; prior state retained",error);}
            var timestamp=DateTimeOffset.Now;
            AtomicWrite(StatePath,new
            {
                schema="suspension-live-state/v1",runId=App.RunId,processId=Environment.ProcessId,
                status,updatedAt=timestamp,state=_lastState
            });
            // Publish the pointer last, so its heartbeat never advertises an unfinished state write.
            AtomicWrite(SessionPath,new
            {
                schema="suspension-live-session/v1",runId=App.RunId,processId=Environment.ProcessId,
                processStartedAt=Process.GetCurrentProcess().StartTime.ToUniversalTime(),
                startedAt=_started,updatedAt=timestamp,status,executablePath=Environment.ProcessPath,
                repositoryRoot=App.RepositoryRoot,logPath=App.LogPath,statePath=StatePath,
                instanceScope="one UI instance per Windows desktop session"
            });
        }
        catch(Exception error) {App.LogException("Live diagnostic publication failed",error);}
    }
    private static void AtomicWrite(string path,object value)
    {
        var temporary=path+"."+Environment.ProcessId+".tmp";
        try
        {
            File.WriteAllText(temporary,JsonSerializer.Serialize(value,Options));
            File.Move(temporary,path,overwrite:true);
        }
        finally {if(File.Exists(temporary)) File.Delete(temporary);}
    }
}
