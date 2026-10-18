using System.IO;
using System.Text.Json;
using System.Windows;

namespace SuspensionSimulation.App;

public partial class MainWindow
{
    private BumpStudioWindow? _bumpStudio;
    private void OpenBumpStudio()
    {
        if(_bumpStudio is not null) {_bumpStudio.Activate();return;}
        _bumpStudio=new BumpStudioWindow(_designLibrary!.Root) {Owner=this};
        _bumpStudio.ComponentCreated+=component=>{RefreshComponentLibrary();RefreshAssemblyCatalog();};
        _bumpStudio.Closed+=(_,_)=>_bumpStudio=null;_bumpStudio.Show();
    }
    internal async void RunBumpStudioProof()
    {
        var folder=Path.Combine(App.RepositoryRoot,"artifacts/bump-studio-ui");Directory.CreateDirectory(folder);Exception? failure=null;
        try
        {
            NavigateWorkspace(Workspace.ComponentDesigner);OpenBumpStudio();_bumpStudio!.SetProofUnits();
            await _bumpStudio.ImportStep(Path.Combine(App.RepositoryRoot,"artifacts/fea-step-mesh-spike/asymmetric-mm.step"));_bumpStudio.ConfigureProofFaces();_bumpStudio.SaveRecipe();var hash=_bumpStudio.RecipeHash??throw new InvalidOperationException("Preparation recipe was not saved.");
            _bumpStudio.OpenRecipe(hash);_bumpStudio.VerifyMaterialOverrideProof();hash=_bumpStudio.RecipeHash!;await Task.Delay(300);_bumpStudio.VerifyProofPick();_bumpStudio.CaptureProof(Path.Combine(folder,"studio.png"));
            foreach(var (index,name) in new[]{(2,"contacts"),(3,"material"),(4,"verification")}) {_bumpStudio.SelectProofTab(index);await Task.Delay(150);_bumpStudio.CaptureProof(Path.Combine(folder,name+".png"));}
            if(!_bumpStudio.ProofRawRecovery()) throw new InvalidOperationException("Invalid typed orientation did not survive draft recovery.");
            File.WriteAllText(Path.Combine(folder,"state.json"),JsonSerializer.Serialize(_bumpStudio.CaptureState(),new JsonSerializerOptions {WriteIndented=true}));
            _bumpStudio.Close();OpenBumpStudio();if(_bumpStudio!.RecipeHash!=hash) throw new InvalidOperationException("Reopening Studio lost its frozen recipe identity.");
            _bumpStudio.Close();
        }
        catch(Exception error) {failure=error;App.LogException("Bump Studio preparation UI proof",error);_bumpStudio?.Close();}
        File.WriteAllText(Path.Combine(folder,"proof.json"),JsonSerializer.Serialize(new {success=failure is null,error=failure?.ToString()},new JsonSerializerOptions {WriteIndented=true}));Application.Current.Shutdown(failure is null?0:2);
    }
}
