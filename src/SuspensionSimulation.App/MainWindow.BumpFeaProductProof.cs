using System.IO;
using System.Text.Json;
using System.Windows;
using SuspensionSimulation.Design;

namespace SuspensionSimulation.App;

public partial class MainWindow
{
    internal async void RunBumpFeaProductProof()
    {
        var folder=Path.Combine(App.RepositoryRoot,"artifacts/bump-fea-product-ui");Directory.CreateDirectory(folder);Exception? failure=null;
        try
        {
            NavigateWorkspace(Workspace.ComponentDesigner);OpenBumpStudio();var studio=_bumpStudio!;studio.SetProofUnits();
            await studio.ImportStep(Path.Combine(App.RepositoryRoot,"data/examples/bumper-cylinder-mm.step"));studio.SetupProductProof();
            var job=await studio.RunCompressionJob();await Task.Delay(300);studio.CaptureProof(Path.Combine(folder,"complete-curve.png"));
            var component=studio.PublishCompletedComponent();if(studio.PublishCompletedComponent().Identity!=component.Identity) throw new InvalidOperationException("Component publication was not idempotent.");
            if(!_assemblyBumperChoice.Items.Cast<ComponentDefinition>().Any(c=>c.Identity.AssetId==component.Identity.AssetId)) throw new InvalidOperationException("FEA component did not appear in Assembly Editor's installation choices.");
            await studio.ViewJobForProof();await Task.Delay(300);await studio.CaptureJobDiagnosticProof(Path.Combine(folder,"deformation.png"));
            File.WriteAllText(Path.Combine(folder,"state.json"),JsonSerializer.Serialize(studio.CaptureState(),new JsonSerializerOptions {WriteIndented=true}));
            studio.Close();OpenBumpStudio();if(!_bumpStudio!.HasCompletedJobForProof(job.ResultSha256)) throw new InvalidOperationException("Completed job did not recover after reopening Studio.");_bumpStudio.Close();
        }
        catch(Exception error) {failure=error;App.LogException("Bump FEA product UI proof",error);_bumpStudio?.Close();}
        File.WriteAllText(Path.Combine(folder,"proof.json"),JsonSerializer.Serialize(new {success=failure is null,error=failure?.ToString()},new JsonSerializerOptions {WriteIndented=true}));Application.Current.Shutdown(failure is null?0:2);
    }
}
