using System.IO;
using System.Text.Json;
using System.Windows;

namespace SuspensionSimulation.App;

public partial class MainWindow
{
    internal async void RunSteeringProof()
    {
        var directory=Path.Combine(App.RepositoryRoot,"artifacts","steering");Directory.CreateDirectory(directory);
        Exception? failure=null;var stages=new List<object>();
        try
        {
            await Task.Delay(300);SetInspectionMode(true);await Task.Delay(800);
            var viewport=_inspectionViewport??throw new InvalidOperationException("3D viewport unavailable.");
            foreach(var size in new[]{(1280d,720d),(1600d,900d)})
            {
                Width=size.Item1;Height=size.Item2;_ax=_ay=0;SteeringAngle.Text="0";Render();await Task.Delay(250);viewport.Preset("Isometric");await Task.Delay(150);
                var layout=JsonSerializer.Serialize(viewport.CaptureDashboardLayout());stages.Add(new {size,mode="neutral",layout=viewport.CaptureDashboardLayout()});
                CaptureLayout(Path.Combine(directory,$"dashboard-{size.Item1}x{size.Item2}.png"));
                foreach(var angle in new[]{-450d,225,450})
                {
                    SteeringAngle.Text=angle.ToString(System.Globalization.CultureInfo.InvariantCulture);Render();await Task.Delay(100);
                    if(_rejectedRevision is not null) throw new InvalidOperationException(_rejectedError);
                    if(layout!=JsonSerializer.Serialize(viewport.CaptureDashboardLayout())) throw new InvalidOperationException("Dashboard resized on steering input.");
                    stages.Add(new {angle,front=_acceptedSnapshot!.A.Steering!.Front});
                }
                viewport.ShowLabels(true);await Task.Delay(150);CaptureLayout(Path.Combine(directory,$"labels-{size.Item1}x{size.Item2}.png"));viewport.ShowLabels(false);
                SteeringAngle.Text="0";_ax=.07733333333333318;_ay=-1.1304347826086956;Render();await Task.Delay(150);
                if(_rejectedRevision is not null) throw new InvalidOperationException(_rejectedError);
                if(layout!=JsonSerializer.Serialize(viewport.CaptureDashboardLayout())) throw new InvalidOperationException("Dashboard resized on travel limit.");
                var old=AFrontId.Text;AFrontId.Text="999";Render();await Task.Delay(100);
                if(_rejectedRevision is null || layout!=JsonSerializer.Serialize(viewport.CaptureDashboardLayout())) throw new InvalidOperationException("Stale dashboard layout changed.");
                AFrontId.Text=old;Render();await Task.Delay(100);
            }
            var revision=_renderAttempt;var snapshot=_acceptedSnapshot;var before=JsonSerializer.Serialize(viewport.CaptureCamera());
            viewport.OrbitCamera(35,-20);viewport.RollCamera(.4f);viewport.Viewport.AddPanForce(6,3);viewport.Viewport.AddZoomForce(.1);await Task.Delay(150);
            if(before==JsonSerializer.Serialize(viewport.CaptureCamera()) || revision!=_renderAttempt || !ReferenceEquals(snapshot,_acceptedSnapshot)) throw new InvalidOperationException("Camera navigation did not stay independent of simulation.");
            var properties=JsonSerializer.SerializeToElement(viewport.CaptureDashboardLayout());
            if(properties.GetProperty("labelCount").GetInt32()<35 || !properties.GetProperty("whiteLabels").GetBoolean() || !properties.GetProperty("leftDragPans").GetBoolean()) throw new InvalidOperationException("Component labels or model pan bindings missing.");
            stages.Add(new {cameraIndependent=true,layout=viewport.CaptureDashboardLayout()});
        }
        catch(Exception error) {failure=error;App.LogException("Steering proof",error);}
        File.WriteAllText(Path.Combine(directory,"app-proof.json"),JsonSerializer.Serialize(new {success=failure is null,error=failure?.ToString(),stages},new JsonSerializerOptions {WriteIndented=true,IncludeFields=true}));
        Close();Application.Current.Shutdown(failure is null?0:2);
    }
}
