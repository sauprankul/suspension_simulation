using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Numerics;
using SuspensionSimulation.Core;

namespace SuspensionSimulation.App;

public partial class MainWindow
{
    internal async void RunHardStopProof()
    {
        var directory=Path.Combine(App.RepositoryRoot,"artifacts","hard-stops");Directory.CreateDirectory(directory);
        var stages=new List<object>();Exception? failure=null;
        var options=new JsonSerializerOptions {WriteIndented=true,IncludeFields=true,NumberHandling=JsonNumberHandling.AllowNamedFloatingPointLiterals};
        try
        {
            await Task.Delay(300);SetInspectionMode(true);await Task.Delay(600);
            var viewport=_inspectionViewport??throw new InvalidOperationException("No 3D viewport.");
            Width=1600;Height=900;Set(BFrontSpring,2.9);Set(BRearSpring,4);SteeringAngle.Text="0";
            _ax=_ay=0;Render();await Task.Delay(200);viewport.Preset("Isometric");viewport.ShowLabels(false);await Task.Delay(100);
            var layout=JsonSerializer.Serialize(viewport.CaptureDashboardLayout());
            var display=JsonSerializer.SerializeToElement(viewport.CaptureStrokeDisplay(),options);
            if(display.GetProperty("dots").GetArrayLength()!=12 || display.GetProperty("dots").EnumerateArray().Any(x=>!x.GetProperty("visible").GetBoolean())) throw new InvalidOperationException("Expected three visible spring dots per corner.");
            CaptureLayout(Path.Combine(directory,"neutral-layout.png"));stages.Add(new {name="neutral",display=viewport.CaptureStrokeDisplay()});
            using var fixtures=JsonDocument.Parse(File.ReadAllText(Path.Combine(App.RepositoryRoot,"data/testdata/visualizer/verified-limit-pairs.json")));
            var brake=fixtures.RootElement.EnumerateArray().Single(x=>x.GetProperty("Name").GetString()=="A-FL-bump").GetProperty("Active");
            _ax=brake.GetProperty("LongitudinalG").GetDouble();_ay=brake.GetProperty("LateralG").GetDouble();Render();await Task.Delay(150);
            if(_rejectedRevision is not null) throw new InvalidOperationException(_rejectedError);
            var bottom=_acceptedSnapshot!.A.Scene.SpringStrokes.Single(x=>x.Corner=="FL");
            if(bottom.BumpRemainingM!=0 || Vector3.Distance(bottom.MarkerM,bottom.UpperMountM)!=0) throw new InvalidOperationException("Bottom-out dot did not meet the upper mount.");
            CaptureLayout(Path.Combine(directory,"bottom-out-layout.png"));stages.Add(new {name="bottom-out",display=viewport.CaptureStrokeDisplay(),limits=_acceptedSnapshot.A.Diagnostics.Limits});
            var c=viewport.Viewport.Camera!;var target=(bottom.UpperMountM+bottom.LowerMountM)/2;
            c.Position=new(target.X+1,target.Y-2,target.Z+1);c.LookDirection=new(-1,2,-1);((HelixToolkit.Wpf.SharpDX.OrthographicCamera)c).Width=.9;
            await Task.Delay(100);CaptureInspection(Path.Combine(directory,"bottom-out-closeup.png"));viewport.Preset("Isometric");
            _ax=.07733333333333318;_ay=-1.1304347826086956;Render();await Task.Delay(150);
            if(_rejectedRevision is not null || _acceptedSnapshot!.B.Diagnostics.Limits.Single(x=>x.Corner=="FR").Direction!="rebound" || BRsd.Text!="100.00%") throw new InvalidOperationException("Rigid rebound/RSD display failed: "+_rejectedError);
            CaptureLayout(Path.Combine(directory,"top-out-layout.png"));stages.Add(new {name="top-out",display=viewport.CaptureStrokeDisplay(),limits=_acceptedSnapshot.B.Diagnostics.Limits});
            if(layout!=JsonSerializer.Serialize(viewport.CaptureDashboardLayout())) throw new InvalidOperationException("Stop state resized the dashboard.");
            _ax=_ay=0;Render();if(_acceptedSnapshot!.A.Diagnostics.Limits.Any(x=>x.Active)) throw new InvalidOperationException("Stops did not release.");
            var comparison=_headToHeadSolver!.Solve(_vehicle!,ReadSetup("A",true),ReadSetup("B",false),new LoadCase("proof-both-rigid",0,0,1.5));
            _comparison=comparison;_acceptedSnapshot=_sceneBuilder!.CreateSnapshot(++_renderAttempt,comparison,_vehicle!,_headToHeadSolver.TravelMaps,0);
            UpdateInspectionScene();RenderResults(comparison);await Task.Delay(150);
            if(ARsd.Text!="—" || BRsd.Text!="—") throw new InvalidOperationException("Infinity/infinity RSD needs an undefined readout.");
            _=CaptureResult(_renderAttempt,comparison);ExportInspection();
            var export=Directory.EnumerateFiles(Path.Combine(App.RepositoryRoot,"artifacts/inspection-snapshots"),"snapshot.json",SearchOption.AllDirectories).MaxBy(File.GetLastWriteTimeUtc)!;
            using var exported=JsonDocument.Parse(File.ReadAllText(export));
            if(exported.RootElement.GetProperty("snapshot").GetProperty("A").GetProperty("FrontRsd").GetString()!="NaN") throw new InvalidOperationException("Undefined RSD export failed.");
            CaptureLayout(Path.Combine(directory,"both-axles-rigid-layout.png"));stages.Add(new {name="both-axles-rigid",export,display=viewport.CaptureStrokeDisplay()});
        }
        catch(Exception error) {failure=error;App.LogException("Hard-stop proof",error);}
        File.WriteAllText(Path.Combine(directory,"app-proof.json"),JsonSerializer.Serialize(new {success=failure is null,error=failure?.ToString(),stages},options));
        Close();Application.Current.Shutdown(failure is null?0:2);
    }
}
