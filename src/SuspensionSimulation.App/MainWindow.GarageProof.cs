using System.IO;
using System.Text.Json;
using System.Windows;
using SuspensionSimulation.Design;

namespace SuspensionSimulation.App;

public partial class MainWindow
{
    internal async void RunGarageProof()
    {
        var folder=Path.Combine(App.RepositoryRoot,"artifacts","garage-proof");Directory.CreateDirectory(folder);Exception? failure=null;
        try
        {
            await Task.Delay(500);_garage=new GarageLibrary(Path.Combine(folder,Guid.NewGuid().ToString("N")));NavigateWorkspace(Workspace.Garage);
            var fields=_workspaceDrafts[Workspace.Garage];
            foreach(var name in new[]{"Stock GR86","Stock BRZ"}) {GarageAction("New");fields.Name.Text=name;GarageAction("Save changes");}
            _garageList.SelectedItem=_garage.List().First(x=>x.Name=="Stock GR86");GarageAction("Duplicate");fields.Name.Text="My BRZ";GarageAction("Save changes");
            GarageAction("Archive");GarageAction("Restore");
            var entries=new GarageLibrary(_garage.DirectoryPath).List();
            if(entries.Count!=3 || entries.Any(x=>x.Archived) || !entries.Any(x=>x.Name=="My BRZ")) throw new InvalidOperationException("Garage actions did not persist independent entries.");
            _garageSearch.Text="Stock";if(_garageList.Items.Count!=2) throw new InvalidOperationException("Garage search failed.");_garageSearch.Text="";
            NavigateWorkspace(Workspace.MainMenu);NavigateWorkspace(Workspace.Garage);await Task.Delay(200);CaptureLayout(Path.Combine(folder,"garage.png"));
        }
        catch(Exception error) {failure=error;App.LogException("Garage proof",error);}
        File.WriteAllText(Path.Combine(folder,"proof.json"),JsonSerializer.Serialize(new {success=failure is null,error=failure?.ToString()}));
        Application.Current.Shutdown(failure is null?0:2);
    }
}
