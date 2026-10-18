using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using SuspensionSimulation.Components;
using SuspensionSimulation.Design;
using SuspensionSimulation.Kinematics;
using SuspensionSimulation.Solvers.Design;

namespace SuspensionSimulation.App;

public partial class MainWindow
{
    private InstalledEquilibriumResult? _installedPreview;
    private GarageEntry? _installedPreviewGarage;
    internal async void RunInstalledProof()
    {
        var folder=Path.Combine(App.RepositoryRoot,"artifacts","installed-equilibrium-ui");Directory.CreateDirectory(folder);Exception? failure=null;
        try
        {
            await Task.Delay(500);var legacyPhysics=_renderAttempt;NavigateWorkspace(Workspace.AssemblyEditor);var models=TwinsInstalledCatalog.Seed(_designLibrary!,App.RepositoryRoot);
            var library=_designLibrary??throw new InvalidOperationException("Design library was not initialized.");
            var garage=new GarageLibrary(Path.Combine(folder,App.RunId,"garage"));var accepted=new List<(GarageEntry Entry,ResolvedAssemblyGraph Graph,InstalledEquilibriumResult Result)>();
            foreach(var model in new[]{models.Gr86,models.Brz})
            {
                var setup=new GarageSetup(new(Guid.NewGuid().ToString("N"),"draft",model.Identity.Name,new(DataProvenance.Estimated,"Physical proof: reference equipment, estimated geometry and dimensions",null)),new(model.Identity.AssetId),[],[],new(MassAccountingPolicy.AggregateBase,1360.778,new(.0515,0,.46),[])) {ParameterValues=[]};
                var saved=garage.Save(new(Guid.NewGuid().ToString("N"),0,model.Identity.Name,"Isolated physical proof entry",false,setup));var reopened=garage.Load(saved.Id)!;
                var graph=new AssemblyGraphResolver(library).Compile(reopened.Configuration!.RootAssembly);var frozen=GraphAdjustments.Freeze(graph,reopened.Configuration.Adjustments);
                var result=await Task.Run(()=>new InstalledEquilibrium(frozen).Solve(new(reopened.Configuration.Mass)));
                if(result.Manifest is null||result.InputSha256.Length!=64||result.ForceResidualN.Length>.05||result.MomentResidualNm.Length>.05) throw new InvalidOperationException("Saved Garage physical solve lost provenance or closure.");
                accepted.Add((reopened,frozen,result));
            }
            if(accepted[0].Result.InputSha256==accepted[1].Result.InputSha256) throw new InvalidOperationException("Independent physical Garage entries shared an input identity.");
            var selected=accepted[0];_assemblyExpectedDof.Text="5";LoadAssembly(library.LoadAssembly(selected.Graph.Root.Identity.AssetId)!);
            var resultToShow=await Task.Run(()=>new InstalledEquilibrium(selected.Graph).Solve(new(selected.Entry.Configuration!.Mass, LateralG:.3),selected.Result));
            _installedPreview=resultToShow;_installedPreviewGarage=selected.Entry;_assemblyViewport!.SetScene(InstalledWorldScene(AssemblyScene(selected.Graph,new ConstraintMechanism(selected.Graph),resultToShow.Motion.Pose),resultToShow.Chassis));_assemblyViewport.Preset("Isometric");
            _twinsMotionReadout.Text=$"Saved Garage revision {selected.Entry.Revision}\nMass {resultToShow.TotalMassKg:0.0} kg · CG height {resultToShow.CenterOfMassWorldM.Z*1000:0.0} mm\nBody heave {resultToShow.Chassis.HeaveM*1000:0.0} mm · roll {resultToShow.Chassis.RollRad*180/Math.PI:0.000}°\n\n"+
                string.Join("\n",resultToShow.Wheels.Select((w,i)=>$"{w.Corner}: {resultToShow.NormalLoadsN[i]:0.0} N · camber {w.CamberRad*180/Math.PI:0.000}° · heading {w.HeadingRad*180/Math.PI:0.000}°"))+"\n\n"+
                string.Join("\n",resultToShow.RideHeights.Select(h=>$"{h.Id} datum: {h.HeightM*1000:0.0} mm"))+$"\n\nForce residual {resultToShow.ForceResidualN.Length:0.###E+0} N\nMoment residual {resultToShow.MomentResidualNm.Length:0.###E+0} N·m\n\nEstimated physical model; common traction/normal-load ratio; effective rigid tire envelope. No measured bumper/damping data.";
            var tabs=((Grid)_workspaceViews[Workspace.AssemblyEditor]).Children.OfType<TabControl>().Single();tabs.SelectedIndex=tabs.Items.Count-1;
            var panel=(StackPanel)((ScrollViewer)((TabItem)tabs.SelectedItem).Content).Content;
            foreach(var child in panel.Children.OfType<FrameworkElement>()) if(!ReferenceEquals(child,_twinsMotionReadout)) child.Visibility=Visibility.Collapsed;
            panel.Children.Insert(0,new TextBlock {Text="Saved Garage · installed equilibrium",FontWeight=FontWeights.Bold,Margin=new Thickness(0,0,0,8)});
            await Task.Delay(200);CaptureLayout(Path.Combine(folder,"loaded.png"));
            File.WriteAllText(Path.Combine(folder,"saved-garage-results.json"),JsonSerializer.Serialize(accepted.Select(x=>new {entry=x.Entry,result=x.Result}),new JsonSerializerOptions {WriteIndented=true}));
            if(_renderAttempt!=legacyPhysics) throw new InvalidOperationException("Physical proof invoked the legacy reference model.");
            NavigateWorkspace(Workspace.MainMenu);await Task.Delay(100);var frames=_assemblyViewport.FrameCount;await Task.Delay(150);if(_assemblyViewport.FrameCount>frames+1||_assemblyViewport.TimerResolutionActive) throw new InvalidOperationException("Physical preview kept hidden rendering active.");
        }
        catch(Exception error) {failure=error;App.LogException("Installed equilibrium UI proof",error);}
        File.WriteAllText(Path.Combine(folder,"proof.json"),JsonSerializer.Serialize(new {success=failure is null,error=failure?.ToString()},new JsonSerializerOptions {WriteIndented=true}));Application.Current.Shutdown(failure is null?0:2);
    }
    private static InspectionScene InstalledWorldScene(InspectionScene scene,ChassisPose pose)
    {
        Vector3 P(Vector3 p) {var v=pose.World(new(p.X,p.Y,p.Z));return new((float)v.X,(float)v.Y,(float)v.Z);}
        var rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,(float)pose.PitchRad)*Quaternion.CreateFromAxisAngle(Vector3.UnitX,(float)pose.RollRad);
        return scene with {Validity="converged-installed-equilibrium",Points=[..scene.Points.Select(p=>p with {PositionM=P(p.PositionM)})],Segments=[..scene.Segments.Select(s=>s with {StartM=P(s.StartM),EndM=P(s.EndM)})],
            Wheels=[..scene.Wheels.Select(w=>w with {CenterM=P(w.CenterM),Orientation=rotation*w.Orientation,GroundGapM=P(w.CenterM).Z-w.RadiusM})],SpringStrokes=[..scene.SpringStrokes.Select(s=>s with {UpperMountM=P(s.UpperMountM),LowerMountM=P(s.LowerMountM),MarkerM=P(s.MarkerM)})]};
    }
}
