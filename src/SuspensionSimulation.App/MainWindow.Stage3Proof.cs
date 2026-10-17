using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace SuspensionSimulation.App;

public partial class MainWindow
{
    internal async void RunStage3Proof(bool nativePointer=true)
    {
        var directory=Path.Combine(App.RepositoryRoot,"artifacts","stage3");Directory.CreateDirectory(directory);
        var results=new List<object>();var latencies=new List<double>();var benchmarks=new List<object>();Exception? failure=null;
        var cursor=AcceptanceInput.Cursor;
        try
        {
            await Task.Delay(400);
            WindowState=WindowState.Normal;Width=1600;Height=900;Topmost=nativePointer;SetInspectionMode(true);await Task.Delay(600);
            var viewport=_inspectionViewport??throw new InvalidOperationException("Viewport unavailable.");
            var hwnd=new WindowInteropHelper(this).Handle;
            if(nativePointer)
            {
                Activate();AcceptanceInput.Activate(hwnd);var ready=Stopwatch.StartNew();
                while(!AcceptanceInput.OwnsForeground && ready.Elapsed.TotalSeconds<120) await Task.Delay(100);
                if(!AcceptanceInput.OwnsForeground) throw new InvalidOperationException("Test app was not activated; actual pointer acceptance remains pending.");
            }
            await Task.Delay(200);
            File.WriteAllText(Path.Combine(directory,"window.json"),JsonSerializer.Serialize(AcceptanceInput.WindowState(hwnd)));
            if(nativePointer) {await AcceptanceInput.Click(PointToScreen(new(120,12)));await Task.Delay(100);}
            var monitors=AcceptanceInput.Monitors();File.WriteAllText(Path.Combine(directory,"monitors.json"),JsonSerializer.Serialize(monitors.Select(x=>new{x.X,x.Y,x.Width,x.Height})));
            viewport.ShowLabels(true);await Task.Delay(300);
            async Task PointerChecks(string phase)
            {
                await Task.Delay(100);var revision=_renderAttempt;var scene=_acceptedSnapshot;
                if(!nativePointer)
                {
                    viewport.Preset("Isometric");await Task.Delay(100);var projected=viewport.FindPickTestPoint();
                    results.Add(new {phase,dpi=VisualTreeHelper.GetDpi(this).PixelsPerDip,hitProjection=true,pointer=false,projected.Id});return;
                }
                var camera=viewport.Viewport.Camera!;var before=camera.LookDirection;
                await AcceptanceInput.Drag(viewport.CubeScreenPoint(new(50,50)),viewport.CubeScreenPoint(new(70,62)));
                if((camera.LookDirection-before).Length<.01) throw new InvalidOperationException(phase+": cube drag did not orbit. "+JsonSerializer.Serialize(viewport.CubeInputState));
                var look=camera.LookDirection;var up=camera.UpDirection;
                await AcceptanceInput.Drag(viewport.CubeScreenPoint(new(106,56)),viewport.CubeScreenPoint(new(91,91)));
                if((camera.UpDirection-up).Length<.02 || (camera.LookDirection-look).Length>.001) throw new InvalidOperationException(phase+": ring drag did not roll around the viewing direction.");
                var position=camera.Position;look=camera.LookDirection;
                await AcceptanceInput.Drag(viewport.Viewport.PointToScreen(new(80,60)),viewport.Viewport.PointToScreen(new(120,80)));
                if((camera.Position-position).Length<.01 || (camera.LookDirection-look).Length>.001) throw new InvalidOperationException(phase+": model drag did not pan.");
                var width=((HelixToolkit.Wpf.SharpDX.OrthographicCamera)camera).Width;
                AcceptanceInput.Move(viewport.Viewport.PointToScreen(new(100,100)));AcceptanceInput.Wheel(120);await Task.Delay(100);
                if(Math.Abs(((HelixToolkit.Wpf.SharpDX.OrthographicCamera)camera).Width-width)<.01) throw new InvalidOperationException(phase+": mouse wheel did not zoom.");
                viewport.Preset("Isometric");await Task.Delay(80);var hit=viewport.FindPickTestPoint();viewport.Select("");
                await AcceptanceInput.Click(hit.Point);
                if(viewport.SelectedId!=hit.Id) throw new InvalidOperationException(phase+$": component click selected {viewport.SelectedId??"nothing"}, expected {hit.Id}.");
                if(revision!=_renderAttempt||!ReferenceEquals(scene,_acceptedSnapshot)) throw new InvalidOperationException(phase+": navigation invoked physics.");
                results.Add(new {phase,dpi=VisualTreeHelper.GetDpi(this).PixelsPerDip,pointer=true,selection=hit.Id,cameraIndependent=true});
            }
            await PointerChecks("initial");
            foreach(var monitor in monitors)
            {AcceptanceInput.MoveWindow(hwnd,monitor);await Task.Delay(200);await PointerChecks($"monitor-{monitor.X}-{monitor.Y}");}
            foreach(var dpi in new[]{120,144,96})
            {
                AcceptanceInput.InjectDpi(hwnd,dpi);await Task.Delay(200);
                if(Math.Abs(VisualTreeHelper.GetDpi(this).PixelsPerDip-dpi/96d)>.01) throw new InvalidOperationException($"Injected DPI {dpi} was not applied by WPF.");
                await PointerChecks("injected-dpi-"+dpi);CaptureLayout(Path.Combine(directory,$"dpi-{dpi}.png"));
            }
            Width=1600;Height=900;await Task.Delay(200);
            for(var i=0;i<32;i++)
            {
                var ax=.3*Math.Sin(i*.6);var ay=.6*Math.Cos(i*.6);var revision=_acceptedSnapshot!.Revision;
                var point=new Point(GCircleCanvas.ActualWidth/2-ay/2*Radius(),GCircleCanvas.ActualHeight/2-ax/2*Radius());
                if(nativePointer) {AcceptanceInput.Move(GCircleCanvas.PointToScreen(point));await Task.Delay(10);}
                var watch=Stopwatch.StartNew();if(nativePointer) AcceptanceInput.Button(true);else QueueGPoint(point);
                try {while(_acceptedSnapshot?.Revision==revision && watch.ElapsedMilliseconds<1000) await Task.Delay(2);}
                finally {if(nativePointer) AcceptanceInput.Button(false);}
                if(_acceptedSnapshot?.Revision==revision||_rejectedRevision is not null) throw new InvalidOperationException("Pointer input failed to publish a valid scene: "+_rejectedError);
                latencies.Add(watch.Elapsed.TotalMilliseconds);
            }
            if(latencies.Order().ElementAt((int)Math.Ceiling(latencies.Count*.95)-1)>=150)
                throw new InvalidOperationException("Input-to-accepted-scene p95 exceeds the 150 ms target.");
            foreach(var labels in new[]{false,true})
            {
                viewport.ShowLabels(labels);
                var warm=Stopwatch.StartNew();while(warm.Elapsed.TotalSeconds<1) {viewport.OrbitCamera(.4f,.1f);await Task.Delay(8);}
                var revision=_renderAttempt;var frames=viewport.FrameCount;var watch=Stopwatch.StartNew();
                while(watch.Elapsed.TotalSeconds<4) {viewport.OrbitCamera(.6f,.2f);await Task.Delay(8);}
                var count=viewport.FrameCount-frames;var fps=count/watch.Elapsed.TotalSeconds;
                benchmarks.Add(new {labels,fps,frames=count,seconds=watch.Elapsed.TotalSeconds});
                if(fps<30||revision!=_renderAttempt) throw new InvalidOperationException("Camera performance is below the 30 fps interaction floor, or invoked physics.");
            }
            CaptureLayout(Path.Combine(directory,"rotated-layout.png"));
            viewport.Preset("Isometric");await Task.Delay(300);CaptureLayout(Path.Combine(directory,"final-layout.png"));
        }
        catch(Exception error) {failure=error;App.LogException("Stage 3 acceptance",error);}
        finally {try {if(nativePointer) {AcceptanceInput.Button(false);AcceptanceInput.Move(cursor,false);}} catch(Exception cleanup) {App.LogException("Acceptance cursor cleanup",cleanup);}}
        File.WriteAllText(Path.Combine(directory,nativePointer?"acceptance.json":"automated-acceptance.json"),JsonSerializer.Serialize(new {success=failure is null,nativePointer,error=failure?.ToString(),results,benchmarks,
            inputToAcceptedMs=latencies,p95Milliseconds=latencies.Count==0?0:latencies.Order().ElementAt((int)Math.Ceiling(latencies.Count*.95)-1)},new JsonSerializerOptions {WriteIndented=true}));
        Close();Application.Current.Shutdown(failure is null?0:2);
    }
}
