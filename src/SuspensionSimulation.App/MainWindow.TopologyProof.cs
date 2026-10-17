using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using SuspensionSimulation.Design;
using SuspensionSimulation.Solvers.Design;

namespace SuspensionSimulation.App;

public partial class MainWindow
{
    internal async void RunTopologyProof()
    {
        var folder=Path.Combine(App.RepositoryRoot,"artifacts","explicit-topology");Directory.CreateDirectory(folder);Exception? failure=null;
        try
        {
            await Task.Delay(500);var physics=_renderAttempt;NavigateWorkspace(Workspace.AssemblyEditor);var seed=TwinsTopologyCatalog.Seed(_designLibrary!,Path.Combine(App.RepositoryRoot,"data/synthetic/gr86/design-topology.json"));
            _assemblyExpectedDof.Text="5";LoadAssembly(seed.Vehicle);_assemblyViewport!.Preset("Isometric");
            _twinsTravel[0].Text="15";_twinsTravel[1].Text="-10";_twinsTravel[2].Text="20";_twinsTravel[3].Text="-15";_twinsSteering.Text="225";SolveTwinsMotionPreview();
            if(_twinsMotion is null||!_twinsMotion.Pose.Converged||_twinsMotion.Front.ThrustAngleRad<=0) throw new InvalidOperationException("Explicit topology preview did not steer and solve.");
            var corner=_twinsMotion.Wheels[0].CenterM;var accepted=_twinsMotion;_twinsSteering.Text="451";
            try {SolveTwinsMotionPreview();throw new InvalidOperationException("Invalid operating request was accepted.");}catch(ArgumentException) { }
            if(!ReferenceEquals(accepted,_twinsMotion)) throw new InvalidOperationException("Rejected request replaced the accepted motion state.");
            _twinsSteering.Text="225";
            var state=JsonSerializer.SerializeToElement(new {shell=CaptureShellState()});NavigateWorkspace(Workspace.MainMenu);RestoreShellState(state);
            if((_twinsMotion!.Wheels[0].CenterM-corner).Length>1e-8) throw new InvalidOperationException("Motion recovery changed the solved pose.");
            var tabs=((Grid)_workspaceViews[Workspace.AssemblyEditor]).Children.OfType<TabControl>().Single();tabs.SelectedIndex=tabs.Items.Count-1;
            await Task.Delay(250);CaptureLayout(Path.Combine(folder,"motion.png"));_assemblyViewport.SaveImage(Path.Combine(folder,"motion.viewport.png"));
            File.WriteAllText(Path.Combine(folder,"motion.json"),JsonSerializer.Serialize(_twinsMotion,new JsonSerializerOptions {WriteIndented=true}));
            if(_renderAttempt!=physics) throw new InvalidOperationException("Kinematic authoring preview invoked the reference vehicle equilibrium.");
            // Shared component metadata, especially axial gauges and rack ratios, must survive a geometry-editor round trip.
            NavigateWorkspace(Workspace.ComponentDesigner);_componentLibraryList.SelectedValue="twins-rack";OpenSelectedComponent();PublishComponent();
            if(_designLibrary!.LoadComponent("twins-rack")!.Rack is null) throw new InvalidOperationException("Component editor dropped the rack ratio.");
            _componentLibraryList.SelectedValue="twins-rear-traction";OpenSelectedComponent();PublishComponent();
            if(_designLibrary.LoadComponent("twins-rear-traction")!.AxialGauge is null) throw new InvalidOperationException("Component editor dropped axial spin metadata.");
        }
        catch(Exception error) {failure=error;App.LogException("Explicit topology proof",error);}
        File.WriteAllText(Path.Combine(folder,"proof.json"),JsonSerializer.Serialize(new {success=failure is null,error=failure?.ToString()},new JsonSerializerOptions {WriteIndented=true}));Application.Current.Shutdown(failure is null?0:2);
    }
}
