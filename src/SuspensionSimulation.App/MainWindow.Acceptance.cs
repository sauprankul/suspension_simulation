using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Media3D;

namespace SuspensionSimulation.App;

public partial class MainWindow
{
    internal async void RunInspectionSoak()
    {
        var directory=Path.Combine(App.RepositoryRoot,"artifacts","stage3");Directory.CreateDirectory(directory);
        var samples=new List<object>();var latencies=new List<double>();
        var elapsed=Stopwatch.StartNew();Exception? failure=null;
        var path=Path.Combine(directory,"soak.json");
        var steps=0;long? steadyGpu=null;var maximumSteadyGpu=0L;var minimumSteadyGpu=long.MaxValue;
        string? steadyResources=null;
        void Record(bool complete)
        {
            samples.Add(new {elapsedSeconds=elapsed.Elapsed.TotalSeconds,objects=_inspectionViewport?.SceneObjectCount,
                resources=_inspectionViewport?.ResourceSummary,privateBytes=Process.GetCurrentProcess().PrivateMemorySize64,
                managedBytes=GC.GetTotalMemory(false),gpuLocalBytes=_inspectionViewport?.GpuMemoryUsageBytes,timerResolutionActive=_inspectionViewport?.TimerResolutionActive});
            File.WriteAllText(path,JsonSerializer.Serialize(new {complete,success=complete && failure is null,error=failure?.ToString(),
                elapsedSeconds=elapsed.Elapsed.TotalSeconds,steps,maximumSteadyGpu,minimumSteadyGpu,steadyResources,
                latencyP95Ms=latencies.Count==0?0:latencies.Order().ElementAt((int)Math.Ceiling(latencies.Count*.95)-1),samples},new JsonSerializerOptions {WriteIndented=true}));
        }
        try
        {
            await Task.Delay(400);SetInspectionMode(true);await Task.Delay(1000);
            if(_inspectionViewport is null) throw new InvalidOperationException("No GPU viewport.");
            _inspectionViewport.ShowLabels(true);_inspectionViewport.ShowLayer("reference",true);_inspectionViewport.ShowLayer("ghost",true);await Task.Delay(600);
            var objects=_inspectionViewport.SceneObjectCount;
            var step=0;Record(false);
            while(elapsed.Elapsed<TimeSpan.FromMinutes(10))
            {
                _ax=.3*Math.Sin(step*.17);_ay=.65*Math.Cos(step*.23);
                SteeringAngle.Text=(60*Math.Sin(step*.11)).ToString("0.0",System.Globalization.CultureInfo.InvariantCulture);
                var watch=Stopwatch.StartNew();Render();watch.Stop();latencies.Add(watch.Elapsed.TotalMilliseconds);
                if(_rejectedRevision is not null) throw new InvalidOperationException(_rejectedError);
                InspectionSetup.SelectedIndex=step%2;
                _inspectionViewport.ShowLabels(step%10<7);_inspectionViewport.ShowLayer("ghost",step%8<4);_inspectionViewport.ShowLayer("reference",step%12<3);
                _inspectionViewport.Select(new[]{"FL.spring","FR.stroke-marker","RL.spring","RR.endlink"}[step%4]);
                var camera=_inspectionViewport.Viewport.Camera!;
                var angle=step*.04;
                camera.Position=new Point3D(5*Math.Cos(angle),5*Math.Sin(angle),3);
                camera.LookDirection=new Vector3D(-camera.Position.X,-camera.Position.Y,-2.65);
                if(step%5==0)
                {
                    SetInspectionMode(false);await Task.Delay(200);
                    var frames=_inspectionViewport.FrameCount;await Task.Delay(400);
                    if(_inspectionViewport.FrameCount>frames+1) throw new InvalidOperationException("Hidden viewport kept rendering.");
                    if(_inspectionViewport.TimerResolutionActive) throw new InvalidOperationException("Hidden viewport retained its high-resolution timer request.");
                    SetInspectionMode(true);
                }
                if(step%30==15)
                {
                    WindowState=WindowState.Minimized;await Task.Delay(300);
                    var frames=_inspectionViewport.FrameCount;await Task.Delay(400);
                    if(_inspectionViewport.FrameCount>frames+1) throw new InvalidOperationException("Minimized viewport kept rendering.");
                    if(_inspectionViewport.TimerResolutionActive) throw new InvalidOperationException("Minimized viewport retained its high-resolution timer request.");
                    WindowState=WindowState.Normal;
                }
                if(_inspectionViewport.SceneObjectCount!=objects) throw new InvalidOperationException("Live scene objects grew during repeated toggles.");
                if(elapsed.Elapsed.TotalSeconds>=120)
                {
                    var resources=_inspectionViewport.ResourceSummary;
                    steadyResources??=resources;
                    if(resources!=steadyResources) throw new InvalidOperationException("GPU resource counts changed after warm-up.");
                    if(_inspectionViewport.GpuMemoryUsageBytes is {} gpu)
                    {
                        steadyGpu??=gpu;maximumSteadyGpu=Math.Max(maximumSteadyGpu,gpu);minimumSteadyGpu=Math.Min(minimumSteadyGpu,gpu);
                        if(gpu>steadyGpu.Value+8*1024*1024) throw new InvalidOperationException("GPU allocation grew more than 8 MiB after warm-up.");
                    }
                }
                if(step%25==0) Record(false);
                steps=++step;await Task.Delay(1900);
            }
        }
        catch(Exception error) {failure=error;}
        Record(true);Close();Application.Current.Shutdown(failure is null?0:2);
    }
}
