using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using SuspensionSimulation.Design;
using SuspensionSimulation.Solvers.Design;

namespace SuspensionSimulation.App;

public partial class MainWindow
{
    internal async void RunChassisProof()
    {
        var folder=Path.Combine(App.RepositoryRoot,"artifacts","chassis-ui");Directory.CreateDirectory(folder);Exception? failure=null;
        try
        {
            await WaitForInstalledAnalysis();var initial=_acceptedInstalled!;
            if(new[]{initial.A,initial.B}.Any(s=>!s.Source.Components.Any(c=>c.Chassis is not null)||s.State.Motion.ChassisFlex is null)) throw new InvalidOperationException("Both stock cars must contain the source chassis and actual solved flex.");
            NavigateWorkspace(Workspace.HeadToHead);await Task.Delay(300);CaptureLayout(Path.Combine(folder,"2d-chassis.png"));
            NavigateWorkspace(Workspace.Visualizer3D);UpdateLayout();await Task.Delay(400);_inspectionViewport!.Preset("Isometric");_inspectionViewport.ShowLabels(true);await Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.Render);await Task.Delay(800);
            var framing=_inspectionViewport.SceneFramingForProof();if(framing.Pending||framing.Outside!=0||!_installedScene!.Segments.Any(s=>s.Corner=="chassis"&&s.Id.Contains("rack-crossmember",StringComparison.Ordinal))) throw new InvalidOperationException("Solved chassis/rack crossmember is absent or outside the fitted3D scene.");CaptureLayout(Path.Combine(folder,"3d-chassis.png"));
            var chassis=initial.A.Source.Components.Single(c=>c.Chassis is not null);var setupText=DesignJson.Serialize(initial.A.Entry.Configuration!.ParameterValues);
            NavigateWorkspace(Workspace.ComponentDesigner);RefreshComponentLibrary();_componentLibraryList.SelectedValue=chassis.Identity.AssetId;OpenSelectedComponent();
            var tabs=((Grid)_workspaceViews[Workspace.ComponentDesigner]).Children.OfType<TabControl>().Single();tabs.SelectedItem=tabs.Items.OfType<TabItem>().Single(t=>t.Header as string=="Mechanics");UpdateLayout();await Task.Delay(250);_componentViewport!.Preset("Fit / reset");await Task.Delay(400);CaptureLayout(Path.Combine(folder,"source-properties.png"));
            _physicalFields["chassisLength"].Text=GarageNumber((chassis.Chassis!.LengthM+.1)*1000);_physicalFields["chassisTorsionGJ"].Text=GarageNumber(chassis.Chassis.TorsionalRigidityNm2*.8/1e6);PublishComponent();var published=_componentDefinition!;
            NavigateWorkspace(Workspace.HeadToHead);await WaitForInstalledAnalysis();var updated=_acceptedInstalled!;
            if(updated.A.Source.Components.Single(c=>c.Chassis is not null).Identity.RevisionId!=published.Identity.RevisionId||updated.A.SetupSha256==initial.A.SetupSha256||DesignJson.Serialize(updated.A.Entry.Configuration!.ParameterValues)!=setupText) throw new InvalidOperationException("Chassis source publication failed atomic propagation or changed car-owned settings.");
            var graph=GarageSetupResolver.Freeze(AssemblyGraphResolver.OpenBundle(updated.A.Source),updated.A.Entry.Configuration!);var neutral=new TwinsKinematics(graph).Solve(FourCornerTravel.Zero,0);var wheelbase=neutral.Wheels.Take(2).Average(w=>w.CenterM.X)-neutral.Wheels.Skip(2).Average(w=>w.CenterM.X);
            if(Math.Abs(wheelbase-published.Chassis!.LengthM)>1e-8) throw new InvalidOperationException("Editor chassis length did not drive axle-reference geometry.");
            File.WriteAllText(Path.Combine(folder,"accepted.json"),DesignJson.Serialize(updated));CaptureLayout(Path.Combine(folder,"updated-2d.png"));
            File.WriteAllText(Path.Combine(folder,"framing.json"),JsonSerializer.Serialize(new {framing.Fits,framing.PointCount,framing.Outside,wheelbase,source=published.Identity},new JsonSerializerOptions {WriteIndented=true}));
        }
        catch(Exception error) {failure=error;App.LogException("Chassis native proof",error);}
        File.WriteAllText(Path.Combine(folder,"proof.json"),JsonSerializer.Serialize(new {success=failure is null,error=failure?.ToString()},new JsonSerializerOptions {WriteIndented=true}));Application.Current.Shutdown(failure is null?0:2);
    }
}
