using System.IO;
using System.Text.Json;
using System.Windows;

namespace SuspensionSimulation.App;

public partial class MainWindow
{
    internal async void RunFeaDiagnosticProof()
    {
        var folder=Path.Combine(App.RepositoryRoot,"artifacts/fea-result-viewer-ui");Directory.CreateDirectory(folder);Exception? failure=null;BumperDiagnosticWindow? viewer=null;
        try
        {
            NavigateWorkspace(Workspace.ComponentDesigner);var before=CaptureComponentRaw();viewer=new BumperDiagnosticWindow {Owner=this};viewer.Show();
            foreach(var (name,complete) in new[]{("rubber-complete",true),("fold-incomplete",false)})
            {
                if(!await viewer.OpenResult(Path.Combine(App.RepositoryRoot,"artifacts/fea-result-viewer",name+".json"))) throw new InvalidOperationException("Actual diagnostic file was rejected: "+name);
                if(!viewer.DiagnosticFlagsForProof(complete,complete)) throw new InvalidOperationException("Viewer hid or changed completion/provenance/acceptance flags.");
                await viewer.VerifyPlaybackForProof();viewer.SelectLastFrameForProof();await Task.Delay(200);viewer.CaptureProof(Path.Combine(folder,name+".png"));File.WriteAllText(Path.Combine(folder,name+"-state.json"),JsonSerializer.Serialize(viewer.CaptureState(),new JsonSerializerOptions {WriteIndented=true}));
            }
            if(CaptureComponentRaw()!=before) throw new InvalidOperationException("Read-only diagnostic viewing changed the component draft.");viewer.Close();viewer=null;
        }
        catch(Exception error) {failure=error;App.LogException("FEA diagnostic viewer UI proof",error);viewer?.Close();}
        File.WriteAllText(Path.Combine(folder,"proof.json"),JsonSerializer.Serialize(new {success=failure is null,error=failure?.ToString()},new JsonSerializerOptions {WriteIndented=true}));Application.Current.Shutdown(failure is null?0:2);
    }
}
