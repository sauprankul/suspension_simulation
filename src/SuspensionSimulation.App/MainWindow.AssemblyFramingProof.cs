using System.IO;
using System.Text.Json;
using System.Windows;
using SuspensionSimulation.Design;
using SuspensionSimulation.Solvers.Design;

namespace SuspensionSimulation.App;

public partial class MainWindow
{
    internal async void RunAssemblyFramingProof()
    {
        var folder=Path.Combine(App.RepositoryRoot,"artifacts/assembly-framing-ui");Directory.CreateDirectory(folder);Exception? failure=null;
        try
        {
            NavigateWorkspace(Workspace.AssemblyEditor);_assemblyExpectedDof.Text="";await Task.Delay(250);
            if(_assemblyViewport!.SceneFramingForProof().Fits!=0) throw new InvalidOperationException("An empty assembly consumed the initial scene fit.");
            var corner=SyntheticAssemblyCatalog.SeedCorner(_designLibrary!);var shifted=new AssemblyDefinition(corner.Identity with {AssetId="framing-shifted-assembly",RevisionId="draft",Name="Framing proof: assembly away from the origin"},
                [new AssemblyInstance("offset",new(corner.Identity.AssetId),new(AssemblyAuthoring.IdentityPlacement.Frame with {OriginM=new(20,-13,3)}))],[],[]) {GroundedInstances=["offset/chassis"]};
            shifted=_designLibrary!.Publish(shifted,null);RefreshAssemblyCatalog();_assemblyLibraryList.SelectedValue=shifted.Identity.AssetId;OpenSelectedAssembly();await Task.Delay(400);
            var opened=_assemblyViewport.SceneFramingForProof();if(opened.Pending||opened.PointCount==0||opened.Outside!=0||opened.Fits!=1) throw new InvalidOperationException("Opening a saved assembly did not immediately frame its geometry: "+opened);
            CaptureLayout(Path.Combine(folder,"opened-without-preset.png"));
            // Orbit/pan/zoom as a user would, then ensure ordinary scene refreshes cannot reset it.
            _assemblyViewport.OrbitCamera(19,-11);var camera=_assemblyViewport.Viewport.Camera!;camera.Position+=new System.Windows.Media.Media3D.Vector3D(.015,-.02,.01);
            ((HelixToolkit.Wpf.SharpDX.OrthographicCamera)camera).Width*=.85;var custom=JsonSerializer.Serialize(_assemblyViewport.CaptureCamera());var fits=opened.Fits;
            ApplyAssemblyPreview(true);await Task.Delay(150);if(custom!=JsonSerializer.Serialize(_assemblyViewport.CaptureCamera())||_assemblyViewport.SceneFramingForProof().Fits!=fits) throw new InvalidOperationException("Applying an assembly edit reset the user's camera.");
            var arm=_designLibrary.LoadComponent("analytic-arm")!;_designLibrary.Publish(arm with {Identity=arm.Identity with {Name="Compatible framing proof revision"}},arm.Identity.RevisionId);RefreshAssemblyDependencies();await Task.Delay(150);
            if(custom!=JsonSerializer.Serialize(_assemblyViewport.CaptureCamera())||_assemblyViewport.SceneFramingForProof().Fits!=fits) throw new InvalidOperationException("A compatible dependency update reset the user's camera.");
            // A saved camera takes precedence over a pending fit, including before first layout.
            _assemblyViewport.RequestSceneFit();_assemblyViewport.RestoreCamera(JsonSerializer.SerializeToElement(_assemblyViewport.CaptureCamera()));await Task.Delay(150);
            if(custom!=JsonSerializer.Serialize(_assemblyViewport.CaptureCamera())||_assemblyViewport.SceneFramingForProof().Pending||_assemblyViewport.SceneFramingForProof().Fits!=fits) throw new InvalidOperationException("A deferred document fit overwrote the restored camera.");
            NavigateWorkspace(Workspace.MainMenu);OpenSelectedAssembly();await Task.Delay(150);
            if(!_assemblyViewport.SceneFramingForProof().Pending||_assemblyViewport.SceneFramingForProof().Fits!=fits) throw new InvalidOperationException("Hidden assembly fitting did not wait for visible layout.");
            NavigateWorkspace(Workspace.AssemblyEditor);await Task.Delay(300);var revealed=_assemblyViewport.SceneFramingForProof();if(revealed.Pending||revealed.Outside!=0||revealed.Fits!=fits+1) throw new InvalidOperationException("Revealing the loaded assembly did not complete its deferred fit.");
            CaptureLayout(Path.Combine(folder,"loaded-while-hidden.png"));
            // Shared renderer scene updates (the path used by motion/solver snapshots) preserve framing after the load fit.
            var remembered=JsonSerializer.Serialize(_assemblyViewport.CaptureCamera());var scene=AssemblyScene(_assemblyGraph!);_assemblyViewport.SetScene(scene);_assemblyViewport.SetScene(scene);await Task.Delay(150);
            if(remembered!=JsonSerializer.Serialize(_assemblyViewport.CaptureCamera())||_assemblyViewport.SceneFramingForProof().Fits!=revealed.Fits) throw new InvalidOperationException("A repeated solved-scene update changed the camera.");
            File.WriteAllText(Path.Combine(folder,"framing.json"),JsonSerializer.Serialize(new {opened=new {opened.Fits,opened.PointCount,opened.Outside},revealed=new {revealed.Fits,revealed.PointCount,revealed.Outside},camera=_assemblyViewport.CaptureCamera()},new JsonSerializerOptions {WriteIndented=true}));
        }
        catch(Exception error) {failure=error;App.LogException("Assembly initial framing UI proof",error);}
        File.WriteAllText(Path.Combine(folder,"proof.json"),JsonSerializer.Serialize(new {success=failure is null,error=failure?.ToString()},new JsonSerializerOptions {WriteIndented=true}));Application.Current.Shutdown(failure is null?0:2);
    }
}
