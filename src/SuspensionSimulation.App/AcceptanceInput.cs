using System.Runtime.InteropServices;
using System.Windows;

namespace SuspensionSimulation.App;

/// <summary>App-owned integration-test input, restricted to this process's own visible windows.</summary>
internal static class AcceptanceInput
{
    internal static Point Cursor {get {GetCursorPos(out var p);return new(p.X,p.Y);}}
    internal static void Move(Point p,bool checkOwner=true)
    {
        var point=new NativePoint((int)Math.Round(p.X),(int)Math.Round(p.Y));
        if(checkOwner)
        {
            GetWindowThreadProcessId(WindowFromPoint(point),out var pid);
            if(pid!=Environment.ProcessId) throw new InvalidOperationException($"Acceptance target ({point.X}, {point.Y}) belongs to PID {pid}, not test PID {Environment.ProcessId}; input was not sent.");
        }
        if(!SetCursorPos(point.X,point.Y)) throw new InvalidOperationException($"Test cursor could not move (error {Marshal.GetLastWin32Error()}).");
        if((Cursor-p).Length>2) throw new InvalidOperationException($"Test cursor did not reach {p}; actual {Cursor}.");
    }
    internal static void Button(bool down)=>Send(down?2u:4u);
    internal static void Wheel(int delta)=>Send(0x800,unchecked((uint)delta));
    private static void Send(uint flags,uint data=0)
    {
        if(flags is 2 or 0x800)
        {
            var p=Cursor;GetWindowThreadProcessId(WindowFromPoint(new((int)p.X,(int)p.Y)),out var owner);
            if(owner!=Environment.ProcessId) throw new InvalidOperationException("Cursor is outside the test app; input was not sent.");
        }
        var input=new Input {Type=0,Union=new(){Mouse=new(){Flags=flags,Data=data}}};
        var size=Marshal.SizeOf<Input>();
        if(SendInput(1,[input],size)!=1) throw new InvalidOperationException($"Native test input was not accepted (error {Marshal.GetLastWin32Error()}, structure {size} bytes, 64-bit {Environment.Is64BitProcess}).");
    }
    internal static async Task Drag(Point from,Point to,int steps=12)
    {
        Move(from);await Task.Delay(60);Button(true);await AwaitButton(true);
        App.Log($"Acceptance drag: downState={System.Windows.Input.Mouse.LeftButton}, over={System.Windows.Input.Mouse.DirectlyOver?.GetType().Name}, captured={System.Windows.Input.Mouse.Captured?.GetType().Name}, cursor={Cursor}","ACCEPTANCE");
        try {for(var i=1;i<=steps;i++) {Move(from+(to-from)*(i/(double)steps));await Task.Delay(12);}}
        finally {Button(false);}
        await AwaitButton(false);await Task.Delay(80);
    }
    internal static async Task Click(Point p)
    {Move(p);await Task.Delay(60);Button(true);await AwaitButton(true);await Task.Delay(30);Button(false);await AwaitButton(false);await Task.Delay(80);}
    private static async Task AwaitButton(bool down)
    {
        var watch=System.Diagnostics.Stopwatch.StartNew();
        while((System.Windows.Input.Mouse.LeftButton==System.Windows.Input.MouseButtonState.Pressed)!=down && watch.ElapsedMilliseconds<500)
            await Task.Delay(10);
        if((System.Windows.Input.Mouse.LeftButton==System.Windows.Input.MouseButtonState.Pressed)!=down)
            throw new InvalidOperationException("Test mouse button transition was not delivered to WPF.");
        await Task.Delay(30);
    }
    internal static void Activate(nint hwnd)
    {
        SetWindowPos(hwnd,new nint(-1),0,0,0,0,0x43);SetForegroundWindow(hwnd);
    }
    internal static object WindowState(nint hwnd)
    {
        GetWindowRect(hwnd,out var rect);GetWindowThreadProcessId(GetForegroundWindow(),out var foreground);
        return new {rect.Left,rect.Top,rect.Right,rect.Bottom,foreground,visible=IsWindowVisible(hwnd),minimized=IsIconic(hwnd),hwnd=hwnd.ToInt64()};
    }
    internal static bool OwnsForeground {get {GetWindowThreadProcessId(GetForegroundWindow(),out var pid);return pid==Environment.ProcessId;}}
    internal static IReadOnlyList<Monitor> Monitors()
    {
        var monitors=new List<Monitor>();
        EnumDisplayMonitors(0,0,(nint monitor,nint dc,ref NativeRect rectangle,nint data)=>
        {var info=new MonitorInfo {Size=Marshal.SizeOf<MonitorInfo>()};GetMonitorInfo(monitor,ref info);monitors.Add(new(monitor,info.Work.Left,info.Work.Top,info.Work.Right-info.Work.Left,info.Work.Bottom-info.Work.Top));return true;},0);
        return monitors;
    }
    internal static void MoveWindow(nint hwnd,Monitor monitor)=>SetWindowPos(hwnd,0,monitor.X+10,monitor.Y+10,Math.Min(1600,monitor.Width-20),Math.Min(900,monitor.Height-20),0x14);
    internal static void InjectDpi(nint hwnd,int dpi)
    {
        GetWindowRect(hwnd,out var rect);var pointer=Marshal.AllocHGlobal(Marshal.SizeOf<NativeRect>());
        try {Marshal.StructureToPtr(rect,pointer,false);SendMessage(hwnd,0x02E0,new nint(dpi|(dpi<<16)),pointer);}
        finally {Marshal.FreeHGlobal(pointer);}
    }
    internal sealed record Monitor(nint Handle,int X,int Y,int Width,int Height);
    [StructLayout(LayoutKind.Sequential)] internal struct NativePoint(int x,int y) {public int X=x;public int Y=y;}
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect {public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo {public int Size;public NativeRect Monitor,Work;public uint Flags;}
    [StructLayout(LayoutKind.Sequential)] private struct Input {public uint Type;public InputUnion Union;}
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion {[FieldOffset(0)]public MouseInput Mouse;}
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput {public int X,Y;public uint Data,Flags,Time;public nuint Extra;}
    private delegate bool MonitorCallback(nint monitor,nint dc,ref NativeRect rect,nint data);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll",SetLastError=true)] private static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd,out int pid);
    [DllImport("user32.dll",SetLastError=true)] private static extern uint SendInput(uint count,Input[] input,int size);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(nint dc,nint clip,MonitorCallback callback,nint data);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(nint monitor,ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd,out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd,nint after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")] private static extern nint SendMessage(nint hwnd,uint message,nint wParam,nint lParam);
}
