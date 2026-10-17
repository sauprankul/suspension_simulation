using System.IO;
using System.Text.Json;
using System.Windows;
using SuspensionSimulation.Design;
using SuspensionSimulation.Solvers.Design;

namespace SuspensionSimulation.App;

public partial class MainWindow
{
    internal async void RunAssemblyProof()
    {
        var folder=Path.Combine(App.RepositoryRoot,"artifacts","assembly-editor");Directory.CreateDirectory(folder);Exception? failure=null;
        try
        {
            await Task.Delay(600);var physics=_renderAttempt;NavigateWorkspace(Workspace.AssemblyEditor);
            LoadAssembly(SyntheticAssemblyCatalog.SeedCorner(_designLibrary!));
            var report=MechanismMobility.Inspect(_assemblyGraph!,1);
            if(report.DegreesOfFreedom!=1||!report.IsClosed) throw new InvalidOperationException("Corner did not load with one operating freedom.");
            _assemblyInstances.Single(x=>x.Id=="lower").Xmm="70";ApplyAssemblyPreview(true);
            _assemblyMateA.SelectedItem="chassis:lower";_assemblyMateB.SelectedItem="lower:inner";MateAssemblyPorts();
            if(!MechanismMobility.Inspect(_assemblyGraph!).IsClosed) throw new InvalidOperationException("Port mating did not recover the closed pose.");
            _assemblyInstanceGrid!.SelectedItems.Add(_assemblyInstances.Single(x=>x.Id=="upper"));_assemblyInstanceGrid.SelectedItems.Add(_assemblyInstances.Single(x=>x.Id=="upright"));
            GroupAssemblySelection();
            if(MechanismMobility.Inspect(_assemblyGraph!).DegreesOfFreedom!=1) throw new InvalidOperationException("Grouping changed mobility.");
            var groupId=_assemblyInstances.Single(x=>x.Kind=="Assembly").Id;PublishAssembly();
            var bundle=AssemblyGraphResolver.Export(_assemblyGraph!);File.WriteAllText(Path.Combine(folder,"frozen-bundle.json"),DesignJson.Serialize(bundle));
            var raw=CaptureAssemblyRaw();_designLibrary!.SaveDraft("assembly-workspace",raw);
            _assemblyInstances.Single(x=>x.Id=="lower").Xmm="150";ApplyAssemblyPreview(true);
            if(MechanismMobility.Inspect(_assemblyGraph!).IsClosed) throw new InvalidOperationException("Moved joint did not report closure failure.");
            UndoAssembly(false);if(!MechanismMobility.Inspect(_assemblyGraph!).IsClosed) throw new InvalidOperationException("Undo did not recover closed geometry.");
            RecoverAssembly();if(CaptureAssemblyRaw()!=raw) throw new InvalidOperationException("Recovery changed assembly data.");
            var arm=_designLibrary.LoadComponent("analytic-arm")!;var newArm=_designLibrary.Publish(arm with {Identity=arm.Identity with {Name="Compatible revision"}},arm.Identity.RevisionId);
            RefreshAssemblyDependencies();
            if(_assemblyGraph!.Bodies.Where(x=>x.Component.Identity.AssetId==arm.Identity.AssetId).Any(x=>x.Component.Identity.RevisionId!=newArm.Identity.RevisionId)) throw new InvalidOperationException("Latest compatible parts did not refresh consistently.");
            var previous=_assemblyGraph;_designLibrary.Publish(newArm with {Ports=newArm.Ports.RemoveAt(1)},newArm.Identity.RevisionId);RefreshAssemblyDependencies();
            if(!ReferenceEquals(previous,_assemblyGraph)||!_assemblyStatus.Text.Contains("conflict")) throw new InvalidOperationException("Incompatible revision replaced the accepted graph.");
            LoadAssembly(_designLibrary.LoadAssembly(_assemblyId,_assemblyBaseRevision)!);
            if(!_assemblyStatus.Text.Contains("conflict")||_assemblyGraph!.Bodies.Any(x=>x.Component.Identity.AssetId==arm.Identity.AssetId&&x.Component.Identity.RevisionId!=newArm.Identity.RevisionId)) throw new InvalidOperationException("Stored last-good graph did not survive reopening a conflicted assembly.");
            _designLibrary.Publish(newArm,_designLibrary.LoadComponent(arm.Identity.AssetId)!.Identity.RevisionId);RefreshAssemblyDependencies();
            _assemblyViewport!.Preset("Isometric");await Task.Delay(250);CaptureLayout(Path.Combine(folder,"editor.png"));
            _assemblyViewport.SaveImage(Path.Combine(folder,"editor.viewport.png"));
            var cornerId=_assemblyId;var cornerRevision=_assemblyBaseRevision;
            var axle=new AssemblyDefinition(_assemblyDefinition!.Identity with {AssetId=Guid.NewGuid().ToString("N"),Name="Mirrored synthetic corner pair"},[new AssemblyInstance("left",new(cornerId,cornerRevision),new(AssemblyAuthoring.IdentityPlacement.Frame with {OriginM=new(0,.6,0)}))],[],[]) {GroundedInstances=["left/chassis"]};
            _assemblyId=axle.Identity.AssetId;_assemblyBaseRevision=null;_assemblyExpectedDof.Text="2";FillAssembly(axle);ApplyAssemblyPreview(true);
            _assemblyInstanceGrid.SelectedItem=_assemblyInstances[0];MirrorAssemblySelection();
            if(MechanismMobility.Inspect(_assemblyGraph!).DegreesOfFreedom!=2||!MechanismMobility.Inspect(_assemblyGraph!).IsClosed) throw new InvalidOperationException("UI mirror changed corner-pair mobility.");
            _assemblyViewport.Preset("Isometric");await Task.Delay(150);CaptureLayout(Path.Combine(folder,"mirrored.png"));
            _assemblyExpectedDof.Text="1";LoadAssembly(_designLibrary.LoadAssembly(cornerId,cornerRevision)!);
            var state=JsonSerializer.SerializeToElement(new {shell=CaptureShellState()});NavigateWorkspace(Workspace.MainMenu);await Task.Delay(150);var frames=_assemblyViewport.FrameCount;await Task.Delay(150);
            if(_assemblyViewport.FrameCount>frames+1||_assemblyViewport.TimerResolutionActive) throw new InvalidOperationException("Hidden assembly rendering/timer stayed active.");
            RestoreShellState(state);if(_workspace!=Workspace.AssemblyEditor||!_assemblyGraph!.Bodies.Any(x=>x.Path.StartsWith(groupId+"/",StringComparison.Ordinal))) throw new InvalidOperationException("Shell recovery lost nested geometry.");
            if(_renderAttempt!=physics) throw new InvalidOperationException("Assembly authoring invoked vehicle physics.");
        }
        catch(Exception error) {failure=error;App.LogException("Assembly editor proof",error);}
        File.WriteAllText(Path.Combine(folder,"proof.json"),JsonSerializer.Serialize(new {success=failure is null,error=failure?.ToString()},new JsonSerializerOptions {WriteIndented=true}));
        Application.Current.Shutdown(failure is null?0:2);
    }
}
