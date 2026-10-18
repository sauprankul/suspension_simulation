using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using SuspensionSimulation.Design;
using SuspensionSimulation.Solvers.Design;
using SuspensionSimulation.Solvers.HeadToHead;

namespace SuspensionSimulation.App;

public partial class MainWindow
{
    internal async void RunInstalledFirstViewProof()
    {
        var folder=Path.Combine(App.RepositoryRoot,"artifacts","installed-first-view-ui");Directory.CreateDirectory(folder);Exception? failure=null;
        try
        {
            await WaitForInstalledAnalysis();NavigateWorkspace(Workspace.Visualizer3D);
            var watch=Stopwatch.StartNew();var framed=false;
            while(watch.Elapsed<TimeSpan.FromSeconds(5))
            {
                await Task.Delay(100);var report=_inspectionViewport!.SceneFramingForProof();
                if(!report.Pending&&report.PointCount>0&&report.Outside==0) {framed=true;break;}
            }
            if(!framed) throw new InvalidOperationException("The initial 3D view did not frame its configured car without a view-button action.");
            // Let the first GPU scene render settle before capturing the newly created viewport.
            await Task.Delay(1000);CaptureLayout(Path.Combine(folder,"first-view.png"));
        }
        catch(Exception error) {failure=error;App.LogException("Installed first-view proof",error);}
        File.WriteAllText(Path.Combine(folder,"proof.json"),JsonSerializer.Serialize(new {success=failure is null,error=failure?.ToString()}));Application.Current.Shutdown(failure is null?0:2);
    }
    internal async void RunInstalledLayoutProof()
    {
        var folder=Path.Combine(App.RepositoryRoot,"artifacts","installed-analysis-layout");Directory.CreateDirectory(folder);Exception? failure=null;
        try
        {
            await WaitForInstalledAnalysis();NavigateWorkspace(Workspace.Visualizer3D);_inspectionViewport!.ShowLabels(true);
            Width=1280;Height=720;await Task.Delay(200);_inspectionViewport.Preset("Fit / reset");await Task.Delay(300);CaptureLayout(Path.Combine(folder,"minimum.png"));
            Width=1600;Height=900;await Task.Delay(200);_inspectionViewport.Preset("Isometric");await Task.Delay(300);CaptureLayout(Path.Combine(folder,"normal.png"));
        }
        catch(Exception error) {failure=error;App.LogException("Installed layout proof",error);}
        File.WriteAllText(Path.Combine(folder,"proof.json"),JsonSerializer.Serialize(new {success=failure is null,error=failure?.ToString()}));Application.Current.Shutdown(failure is null?0:2);
    }
    private async Task WaitForInstalledAnalysis()
    {
        var watch=Stopwatch.StartNew();
        while(watch.Elapsed<TimeSpan.FromSeconds(100))
        {
            await Task.Delay(100);
            if(!_installedPending&&!_renderTimer.IsEnabled)
            {
                if(_rejectedRevision is not null) throw new InvalidOperationException("Physical analysis rejected: "+_rejectedError);
                if(_acceptedInstalled is not null) {await Task.Delay(200);if(!_installedPending&&!_renderTimer.IsEnabled) return;}
            }
        }
        throw new TimeoutException("Physical analysis did not settle within the proof budget.");
    }
    internal async void RunInstalledAnalysisProof()
    {
        var folder=Path.Combine(App.RepositoryRoot,"artifacts","installed-analysis-ui");Directory.CreateDirectory(folder);Exception? failure=null;
        try
        {
            await WaitForInstalledAnalysis();
            if(_acceptedInstalled?.A.Entry.Id!=ReferenceGarageCatalog.Gr86Id||_acceptedInstalled.B.Entry.Id!=ReferenceGarageCatalog.BrzId||_acceptedSnapshot is not null||_comparison is not null) throw new InvalidOperationException("Production analysis still uses the provisional reference model.");
            var initial=_acceptedInstalled;AFrontSpring.Text="999";AFrontId.Text="999";Render();await Task.Delay(150);
            if(!ReferenceEquals(initial,_acceptedInstalled)) throw new InvalidOperationException("Hidden legacy specification controls still drive analysis.");
            var stock=_garage!.Load(ReferenceGarageCatalog.Gr86Id)!;var custom=_garage.Duplicate(stock);var setup=custom.Configuration!;
            setup=setup with {ParameterValues=[..setup.ParameterValues!.Value.Select(p=>p.ParameterId is "FL-spring.rate" or "FR-spring.rate"?p with {Value=p.Value*1.15}:p)]};
            custom=_garage.Save(custom with {Name="Saved physical comparison",Configuration=setup});RefreshAnalysisChoices();_analysisA.SelectedValue=custom.Id;await WaitForInstalledAnalysis();
            if(_acceptedInstalled!.A.Entry.Revision!=custom.Revision||Math.Abs(_acceptedInstalled.A.State.Chassis.HeaveM-initial.A.State.Chassis.HeaveM)<1e-5) throw new InvalidOperationException("Saved hardware did not affect physical equilibrium.");
            NavigateWorkspace(Workspace.HeadToHead);await WaitForInstalledAnalysis();CaptureLayout(Path.Combine(folder,"2d.png"));var comparison=_acceptedInstalled;
            NavigateWorkspace(Workspace.Visualizer3D);await Task.Delay(400);
            if(!ReferenceEquals(comparison,_acceptedInstalled)||_installedScene!.Wheels.Length!=4||_installedScene.SpringStrokes.Length!=8) throw new InvalidOperationException("Switching views changed the accepted data or lost physical element strokes.");
            _inspectionViewport!.ShowLabels(true);_inspectionViewport.Preset("Isometric");await Task.Delay(250);CaptureLayout(Path.Combine(folder,"3d.png"));
            Width=1280;Height=720;await Task.Delay(250);CaptureLayout(Path.Combine(folder,"3d-minimum.png"));Width=1600;Height=900;await Task.Delay(250);
            var revision=_renderAttempt;_inspectionViewport.Preset("Front");_inspectionViewport.Preset("Isometric");
            if(_renderAttempt!=revision) throw new InvalidOperationException("Camera changes launched a physics solve.");
            _ax=.05;_ay=.25;SteeringAngle.Text="90";SteeringControl.Value=90;Render();await WaitForInstalledAnalysis();
            if(_acceptedInstalled!.A.State.Input.LateralG!=.25||_acceptedInstalled.B.State.Input.LongitudinalG!=.05||_acceptedInstalled.A.State.Input.SteeringWheelDegrees!=90) throw new InvalidOperationException("A/B did not share the requested G/steering state.");
            var accepted=_acceptedInstalled;ExportInspection();
            var export=Directory.GetFiles(Path.Combine(App.RepositoryRoot,"artifacts/inspection-snapshots"),"physical-result.json",SearchOption.AllDirectories).MaxBy(File.GetLastWriteTimeUtc)!;
            var reopened=DesignJson.Deserialize<InstalledAnalysisSnapshot>(File.ReadAllText(export));
            if(reopened.RequestSha256!=accepted.RequestSha256||reopened.A.Entry.Id!=custom.Id||reopened.A.Source.Manifest.Dependencies.Length<20) throw new InvalidOperationException("Export lost exact physical source/setup provenance.");
            CaptureLayout(Path.Combine(folder,"loaded.png"));
            _ay=.35;Render();_ay=.15;Render();await WaitForInstalledAnalysis();
            if(_acceptedInstalled!.LateralG!=.15) throw new InvalidOperationException("An older asynchronous request overwrote the latest state.");
            var prior=_acceptedInstalled;_ay=.45;Render();_cancelInstalled.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));await Task.Delay(350);
            if(!ReferenceEquals(prior,_acceptedInstalled)||_installedPending) throw new InvalidOperationException("Canceled work replaced the accepted result.");
            _ay=.15;Render();await WaitForInstalledAnalysis();prior=_acceptedInstalled;
            custom=_garage.Save(custom with {Archived=true});RefreshAnalysisChoices();Render();await Task.Delay(200);
            if(_rejectedRevision is null||!ReferenceEquals(prior,_acceptedInstalled)) throw new InvalidOperationException("Invalid Garage selection replaced a complete accepted result.");
            _analysisA.SelectedValue=ReferenceGarageCatalog.Gr86Id;await WaitForInstalledAnalysis();
            if(((FrameworkElement)AFrontSpring.Parent).Visibility!=Visibility.Collapsed||RackGeometryButton.Visibility!=Visibility.Collapsed) throw new InvalidOperationException("Analysis still contains specification editors.");
            File.WriteAllText(Path.Combine(folder,"accepted.json"),DesignJson.Serialize(_acceptedInstalled));
            var retained=_acceptedInstalled;_acceptedInstalled=null;RestoreInstalledCheckpoint();
            if(_acceptedInstalled?.RequestSha256!=retained!.RequestSha256) throw new InvalidOperationException("Frozen last-good checkpoint did not recover.");
        }
        catch(Exception error) {failure=error;App.LogException("Installed production analysis UI proof",error);}
        File.WriteAllText(Path.Combine(folder,"proof.json"),JsonSerializer.Serialize(new {success=failure is null,error=failure?.ToString()},new JsonSerializerOptions {WriteIndented=true}));
        Application.Current.Shutdown(failure is null?0:2);
    }
}
