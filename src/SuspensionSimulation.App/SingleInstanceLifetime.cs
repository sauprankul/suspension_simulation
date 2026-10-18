using System.IO;

namespace SuspensionSimulation.App;

/// <summary>One UI owner in this Windows desktop session, across Debug/Release and launch methods.</summary>
internal sealed class SingleInstanceLifetime : IDisposable
{
    private readonly Mutex _mutex=new(false,@"Local\SuspensionSimulation.Desktop.v1");
    private readonly EventWaitHandle _activation=new(false,EventResetMode.AutoReset,@"Local\SuspensionSimulation.Desktop.Activate.v1");
    private RegisteredWaitHandle? _wait;
    public bool IsPrimary {get;}

    public SingleInstanceLifetime(Action activate)
    {
        try {IsPrimary=_mutex.WaitOne(0);}
        catch(AbandonedMutexException) {IsPrimary=true;}
        if(IsPrimary)
            _wait=ThreadPool.RegisterWaitForSingleObject(_activation,(_,_)=>activate(),null,Timeout.Infinite,false);
        else
        {
            // Allow the owner to bring its existing window to the foreground.
            var sessionFile=Path.Combine(App.RepositoryRoot,"logs","current-session.json");
            try
            {
                using var session=System.Text.Json.JsonDocument.Parse(File.ReadAllText(sessionFile));
                var ownerId=session.RootElement.GetProperty("processId").GetInt32();
                AllowSetForegroundWindow(ownerId);
                using var owner=System.Diagnostics.Process.GetProcessById(ownerId);
                // A launch from Explorer carries the user's foreground permission.
                // Apply it here before this short-lived launcher exits; a later
                // dispatcher callback alone can be denied by Windows.
                if(owner.SessionId==System.Diagnostics.Process.GetCurrentProcess().SessionId&&owner.ProcessName==System.Diagnostics.Process.GetCurrentProcess().ProcessName&&owner.MainWindowHandle is var handle&&handle!=IntPtr.Zero)
                {
                    ShowWindowAsync(handle,IsIconic(handle)?9:5);
                    SetForegroundWindow(handle);
                }
            }
            catch(Exception error) when(error is IOException or System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception) { }
            _activation.Set();
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(IntPtr window,int command);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr window);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    public void Dispose()
    {
        _wait?.Unregister(null);_activation.Dispose();
        if(IsPrimary) _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
