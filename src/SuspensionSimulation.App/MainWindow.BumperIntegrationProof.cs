using System.IO;
using System.Text.Json;
using System.Windows;
using SuspensionSimulation.Components;
using SuspensionSimulation.Design;
using SuspensionSimulation.Solvers.Design;
using SuspensionSimulation.Solvers.HeadToHead;

namespace SuspensionSimulation.App;

public partial class MainWindow
{
    internal async void RunBumperIntegrationProof()
    {
        var folder=Path.Combine(App.RepositoryRoot,"artifacts","imported-bumper-integration-ui");Directory.CreateDirectory(folder);Exception? failure=null;
        try
        {
            await WaitForInstalledAnalysis();var baseline=_acceptedInstalled!;var eyeLength=baseline.A.Static.Mechanics.Elements.Single(e=>e.Path=="FL-damper").LengthM;
            NavigateWorkspace(Workspace.ComponentDesigner);_bumpCurveTab!.IsSelected=true;
            _bumpCsv.Text="compression_mm,force_n\n0,0\n5,400\n10,1400\n20,4200\n40,9000\n";_bumpContact.Text=DesignerNumbers.Mm(eyeLength+.005);
            _bumpProvenance.SelectedItem=DataProvenance.Synthetic;_bumpSource.Text="Synthetic integration proof; no physical part correlation";
            _bumpConditions.Text="Synthetic axial loading; zero separation measured between the declared front strut-eye references";CreateImportedBumpComponent();PublishComponent();var bumper=_componentDefinition!;
            NavigateWorkspace(Workspace.AssemblyEditor);_assemblyExpectedDof.Text="";LoadAssembly(_designLibrary!.LoadAssembly(baseline.A.Entry.Configuration!.RootAssembly.AssetId)!);var originalRoot=_assemblyId;
            DuplicateAssembly();var customRoot=_assemblyId;_workspaceDrafts[Workspace.AssemblyEditor].Name.Text="Synthetic imported bumper vehicle";
            _assemblyBumperTab!.IsSelected=true;_assemblyBumperChoice.SelectedValue=bumper.Identity.AssetId;_assemblyBumperFirst.SelectedItem="front:left-shock-upper";_assemblyBumperSecond.SelectedItem="front:left-shock-upper";
            var before=CaptureAssemblyRaw();try {InstallAssemblyBumper();throw new InvalidOperationException("A same-support bumper was installed.");}catch(ArgumentException) { }
            if(CaptureAssemblyRaw()!=before) throw new InvalidOperationException("Invalid mounting partially changed the assembly draft.");
            _assemblyBumperSecond.SelectedItem="front:left-shock-lower";_assemblyBumperId.Text="FL-bumper";InstallAssemblyBumper();
            if(!_assemblyGraph!.ElasticElements.Any(e=>e.Path=="FL-bumper")||!_assemblySlots.Any(s=>s.ChildInstance=="FL-bumper")) throw new InvalidOperationException("Installation did not create its complete attachments and Garage slot.");
            UndoAssembly(false);if(_assemblyInstances.Any(i=>i.Id=="FL-bumper")||_assemblyJoints.Any(j=>j.Id.StartsWith("FL-bumper-",StringComparison.Ordinal))||_assemblySlots.Any(s=>s.ChildInstance=="FL-bumper")) throw new InvalidOperationException("Undo retained part of the bumper installation.");
            UndoAssembly(true);if(!_assemblyGraph!.ElasticElements.Any(e=>e.Path=="FL-bumper")) throw new InvalidOperationException("Redo did not restore the complete installation.");
            PublishAssembly();_assemblyBumperTab.IsSelected=true;await Task.Delay(200);CaptureLayout(Path.Combine(folder,"assembly.png"));
            if(_designLibrary.LoadAssembly(originalRoot)!.Instances.Any(i=>i.Id=="FL-bumper")) throw new InvalidOperationException("Custom assembly changed the original vehicle.");
            NavigateWorkspace(Workspace.Garage);RefreshGarage(ReferenceGarageCatalog.Gr86Id);GarageAction("Duplicate");var customId=_garageSelected!.Id;
            _workspaceDrafts[Workspace.Garage].Name.Text="Saved synthetic imported bumper";_garageRoot.SelectedValue=customRoot;
            if(!_garagePhysicalEquipment.ContainsKey("FL-bumper:FL-bumper")) throw new InvalidOperationException("Garage did not generate the bumper's equipment picker.");
            GarageAction("Save changes");var custom=_garage!.Load(customId)!;
            if(custom.Configuration!.RootAssembly.AssetId!=customRoot) throw new InvalidOperationException("Garage did not save the custom mounted assembly.");
            await Task.Delay(200);CaptureLayout(Path.Combine(folder,"garage.png"));GarageAction("Open 2D");await WaitForInstalledAnalysis();var first=_acceptedInstalled!;
            var state=first.A.State.Mechanics.Elements.Single(e=>e.Path=="FL-bumper");
            if(first.A.Entry.Id!=customId||state.ForceOrTorque<=0||Math.Abs(first.A.State.Wheels[0].CamberRad-baseline.A.Static.Wheels[0].CamberRad)<1e-6) throw new InvalidOperationException("Saved bumper did not change the production physical result.");
            if(!ComparisonGrid.ItemsSource.Cast<ComparisonRow>().Any(r=>r.Metric=="FL-bumper force")) throw new InvalidOperationException("2D readouts omit the installed bumper.");
            await Task.Delay(200);CaptureLayout(Path.Combine(folder,"2d.png"));NavigateWorkspace(Workspace.Visualizer3D);await Task.Delay(250);
            if(!ReferenceEquals(first,_acceptedInstalled)||!_installedScene!.Segments.Any(s=>s.Id=="FL-bumper.bump-stop")) throw new InvalidOperationException("3D did not use the same accepted bumper state.");
            _inspectionViewport!.Select("FL-bumper.bump-stop");_inspectionViewport.ShowLabels(true);_inspectionViewport.Preset("Fit / reset");await Task.Delay(250);CaptureLayout(Path.Combine(folder,"3d.png"));
            var graph=GarageSetupResolver.Freeze(AssemblyGraphResolver.OpenBundle(first.A.Source),first.A.Entry.Configuration!);var mechanism=new ConstraintMechanism(graph);var element=graph.ElasticElements.Single(e=>e.Path=="FL-bumper");
            var segment=_installedScene.Segments.Single(s=>s.Id=="FL-bumper.bump-stop");
            var expectedFirst=first.A.State.Chassis.World(mechanism.Frame(element.First,first.A.State.Motion.Pose.Coordinates).OriginM);var expectedSecond=first.A.State.Chassis.World(mechanism.Frame(element.Second,first.A.State.Motion.Pose.Coordinates).OriginM);
            if((new DesignVector(segment.StartM.X,segment.StartM.Y,segment.StartM.Z)-expectedFirst).Length>1e-6||(new DesignVector(segment.EndM.X,segment.EndM.Y,segment.EndM.Z)-expectedSecond).Length>1e-6) throw new InvalidOperationException("Displayed bumper ends do not coincide with their solved mounting references.");
            // Publish through the actual Component Designer while remaining in the view, then let its source watcher update both sides atomically.
            _componentLibraryList.SelectedValue=bumper.Identity.AssetId;OpenSelectedComponent();_bumpCsv.Text="compression_mm,force_n\n0,0\n5,800\n10,2800\n20,8400\n40,18000\n";PublishComponent();
            var deadline=DateTime.UtcNow.AddSeconds(120);
            while(_acceptedInstalled!.A.Source.Components.Single(c=>c.Identity.AssetId==bumper.Identity.AssetId).Identity.RevisionId!=_componentBaseRevision)
            {await Task.Delay(100);if(_rejectedRevision is not null) throw new InvalidOperationException("Curve source update rejected: "+_rejectedError);if(DateTime.UtcNow>deadline) throw new TimeoutException("Published curve did not trigger a dependency update.");}
            await WaitForInstalledAnalysis();var revised=_acceptedInstalled!;
            if(revised.A.SetupSha256==first.A.SetupSha256||Math.Abs(revised.A.State.Wheels[0].CamberRad-first.A.State.Wheels[0].CamberRad)<1e-6||revised.A.Source.Components.Single(c=>c.Identity.AssetId==bumper.Identity.AssetId).Identity.RevisionId!=_componentBaseRevision) throw new InvalidOperationException("Curve publication did not reach the accepted production result.");
            var frozen=DesignJson.Deserialize<InstalledAnalysisSnapshot>(DesignJson.Serialize(first));var replayGraph=GarageSetupResolver.Freeze(AssemblyGraphResolver.OpenBundle(frozen.A.Source),frozen.A.Entry.Configuration!);
            var replay=await Task.Run(()=>new InstalledEquilibrium(replayGraph).Solve(frozen.A.State.Input,frozen.A.State));
            if(Math.Abs(replay.Chassis.HeaveM-first.A.State.Chassis.HeaveM)>1e-8) throw new InvalidOperationException("Frozen original result did not replay after curve publication.");
            // A valid source with too little measured range must reject the whole new result, retaining the old complete pair.
            var good=_componentDefinition!;_bumpContact.Text="1000";_bumpCsv.Text="compression_mm,force_n\n0,0\n1,100\n";PublishComponent();
            deadline=DateTime.UtcNow.AddSeconds(120);while(_rejectedRevision is null) {await Task.Delay(100);if(DateTime.UtcNow>deadline) throw new TimeoutException("Invalid curve range did not trigger dependency rejection.");}
            try {await WaitForInstalledAnalysis();throw new InvalidOperationException("Out-of-range bumper became an accepted result.");}catch(InvalidOperationException error) when(error.Message.Contains("Physical analysis rejected:",StringComparison.Ordinal)) { }
            if(!ReferenceEquals(revised,_acceptedInstalled)||_rejectedRevision is null) throw new InvalidOperationException("Invalid curve range partially replaced the accepted A/B result.");
            CaptureLayout(Path.Combine(folder,"range-conflict.png"));
            FillImportedBumpEvidence(good.BumpCurveEvidence);PublishComponent();await Task.Delay(300);await WaitForInstalledAnalysis();
            File.WriteAllText(Path.Combine(folder,"original-result.json"),DesignJson.Serialize(first));File.WriteAllText(Path.Combine(folder,"revised-result.json"),DesignJson.Serialize(revised));
            File.WriteAllText(Path.Combine(folder,"garage-entry.json"),DesignJson.Serialize(custom));
        }
        catch(Exception error) {failure=error;App.LogException("Imported bumper integration UI proof",error);}
        File.WriteAllText(Path.Combine(folder,"proof.json"),JsonSerializer.Serialize(new {success=failure is null,error=failure?.ToString()},new JsonSerializerOptions {WriteIndented=true}));Application.Current.Shutdown(failure is null?0:2);
    }
}
