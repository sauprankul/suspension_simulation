using System.IO;
using System.Text.Json;
using System.Windows;
using SuspensionSimulation.Components;
using SuspensionSimulation.Design;

namespace SuspensionSimulation.App;

public partial class MainWindow
{
    internal async void RunBumpCurveProof()
    {
        var folder=Path.Combine(App.RepositoryRoot,"artifacts","imported-bump-ui");Directory.CreateDirectory(folder);Exception? failure=null;
        try
        {
            await Task.Delay(400);NavigateWorkspace(Workspace.ComponentDesigner);_bumpCurveTab!.IsSelected=true;
            _bumpCsv.Text="compression_mm,force_n\n0,0\n5,400\n10,1400\n20,4200\n";_bumpContact.Text="40";_bumpProvenance.SelectedItem=DataProvenance.Synthetic;
            _bumpSource.Text="Synthetic UI proof; not a physical bumper test";_bumpConditions.Text="Synthetic flat fixture, quasi-static loading";CreateImportedBumpComponent();PublishComponent();
            var original=_componentDefinition!;var curve=(BumpCurveBehavior)original.Behavior!;var source=original.BumpCurveEvidence!;
            if(curve.Samples[1].ForceN!=400||curve.Samples[1].CompressionM!=.005||source.Provenance!=DataProvenance.Synthetic) throw new InvalidOperationException("Curve authoring lost units/source classification.");
            _bumpConditions.Text="Different fixture condition";PublishComponent();
            if(((BumpCurveBehavior)_componentDefinition!.Behavior!).EvidenceSha256==curve.EvidenceSha256||_componentBaseRevision==original.Identity.RevisionId) throw new InvalidOperationException("Changed curve conditions reused the old revision/evidence.");
            var saved=_componentDefinition;_componentLibraryList.SelectedValue=_componentId;OpenSelectedComponent();
            if(_bumpConditions.Text!="Different fixture condition"||_bumpCsv.Text!=source.CsvUtf8) throw new InvalidOperationException("Reopen discarded raw CSV/conditions.");
            var raw=CaptureComponentRaw();_bumpCsv.Text="invalid raw csv input";await Task.Delay(450);NavigateWorkspace(Workspace.MainMenu);NavigateWorkspace(Workspace.ComponentDesigner);
            if(_bumpCsv.Text!="invalid raw csv input") throw new InvalidOperationException("Navigation lost invalid curve draft.");
            ComponentAction(PublishComponent);if(_designLibrary!.LoadComponent(_componentId)!.Identity.RevisionId!=saved.Identity.RevisionId) throw new InvalidOperationException("Invalid source CSV was published.");
            RestoreComponentRaw(raw,true);_bumpCurveTab.IsSelected=true;_componentViewport!.Preset("Fit / reset");await Task.Delay(250);CaptureLayout(Path.Combine(folder,"curve-options.png"));
            ((System.Windows.Controls.ScrollViewer)_bumpCurveTab.Content).ScrollToEnd();await Task.Delay(150);CaptureLayout(Path.Combine(folder,"curve.png"));
            File.WriteAllText(Path.Combine(folder,"component.json"),DesignJson.Serialize(saved));
        }
        catch(Exception error) {failure=error;App.LogException("Imported bump UI proof",error);}
        File.WriteAllText(Path.Combine(folder,"proof.json"),JsonSerializer.Serialize(new {success=failure is null,error=failure?.ToString()}));Application.Current.Shutdown(failure is null?0:2);
    }
}
