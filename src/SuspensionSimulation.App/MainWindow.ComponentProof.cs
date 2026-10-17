using System.IO;
using System.Text.Json;
using System.Windows;
using SuspensionSimulation.Design;

namespace SuspensionSimulation.App;

public partial class MainWindow
{
    internal async void RunComponentProof()
    {
        var folder=Path.Combine(App.RepositoryRoot,"artifacts","component-designer");Directory.CreateDirectory(folder);Exception? failure=null;
        try
        {
            await Task.Delay(600);var revision=_renderAttempt;NavigateWorkspace(Workspace.ComponentDesigner);await Task.Delay(300);
            var initialCamera=JsonSerializer.Serialize(_componentViewport!.CaptureCamera());
            foreach(var kind in ComponentAuthoring.RigidTemplates)
            {
                _componentKind.SelectedItem=kind;_componentLength.Text="300";_componentWidth.Text="200";_componentHeight.Text="100";NewComponent(false);PublishComponent();
                await Task.Delay(60);
                if(_componentViewport.SceneObjectCount>150) throw new InvalidOperationException("Component preview retained old topology objects.");
            }
            var link=_designLibrary!.Components().Single(x=>x.Kind==ComponentKind.RigidLink);_componentLibraryList.SelectedValue=link.Identity.AssetId;OpenSelectedComponent();
            _componentLength.Text="400";ApplyComponentPreview(true);
            if(_componentDefinition!.Points[1].PositionM.X!=.4) throw new InvalidOperationException("Driving dimension did not update the link.");
            _componentLength.Text="450";ApplyComponentPreview(true);UndoComponent(false);
            if(_componentDefinition!.Points[1].PositionM.X!=.4) throw new InvalidOperationException("Undo lost geometry.");
            UndoComponent(true);if(_componentDefinition!.Points[1].PositionM.X!=.45) throw new InvalidOperationException("Redo lost geometry.");
            MakeComponentCustom();_componentPoints[1].Xmm="520";ApplyComponentPreview(true);PublishComponent();
            if(_designLibrary.LoadComponent(_componentId)!.Points[1].PositionM.X!=.52) throw new InvalidOperationException("Custom point edits did not publish/reopen.");
            ShowMeasurement(new(0,0,0),new(.3,.4,0));if(!_measurement.Text.Contains("500")) throw new InvalidOperationException("Any-two-point measurement failed.");
            var raw=CaptureComponentRaw();_designLibrary.SaveDraft("component-workspace",raw);_componentPoints[1].Xmm="999";RecoverComponent();
            if(_componentDefinition!.Points[1].PositionM.X!=.52) throw new InvalidOperationException("Recovery draft lost geometry.");
            _componentViewport.Preset("Isometric");await Task.Delay(200);CaptureDesignerLayout(Path.Combine(folder,"designer.png"));
            NavigateWorkspace(Workspace.MainMenu);await Task.Delay(150);var frames=_componentViewport.FrameCount;await Task.Delay(150);
            if(_componentViewport.FrameCount>frames+1 || _componentViewport.TimerResolutionActive) throw new InvalidOperationException("Hidden component viewport kept rendering.");
            NavigateWorkspace(Workspace.ComponentDesigner);
            if(_renderAttempt!=revision) throw new InvalidOperationException("Component authoring invoked vehicle physics.");
            if(_designLibrary.Components().Count!=4) throw new InvalidOperationException("Template revisions did not round-trip independently.");
            _=initialCamera;
        }
        catch(Exception error) {failure=error;App.LogException("Component authoring proof",error);}
        File.WriteAllText(Path.Combine(folder,"proof.json"),JsonSerializer.Serialize(new {success=failure is null,error=failure?.ToString()},new JsonSerializerOptions {WriteIndented=true}));
        Application.Current.Shutdown(failure is null?0:2);
    }
    private void CaptureDesignerLayout(string path)
    {
        CaptureLayout(path);
        if(_componentViewport is not null) _componentViewport.SaveImage(Path.ChangeExtension(path,"viewport.png"));
    }
}
