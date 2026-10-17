using System.Runtime.InteropServices;

namespace SuspensionSimulation.App;

/// <summary>Match the visible renderer's timer request with an explicit release when idle.</summary>
internal sealed class RenderTimerResolution : IDisposable
{
    public bool Active {get;private set;}
    public void SetActive(bool active)
    {
        if(active==Active) return;
        if(active) Active=timeBeginPeriod(1)==0;
        else {timeEndPeriod(1);Active=false;}
    }
    public void Dispose()=>SetActive(false);
    [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint period);
    [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint period);
}
