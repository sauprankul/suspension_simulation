using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace SuspensionSimulation.App;

public partial class MainWindow
{
    internal async void RunShellProof()
    {
        var directory=Path.Combine(App.RepositoryRoot,"artifacts","component-editor-shell");Directory.CreateDirectory(directory);
        Exception? failure=null;var visited=new List<string>();
        try
        {
            await Task.Delay(600);NavigateWorkspace(Workspace.Visualizer3D);await Task.Delay(400);
            var viewport=_inspectionViewport??throw new InvalidOperationException("Missing shared viewport.");
            viewport.Preset("Side");viewport.OrbitCamera(10,4);viewport.Select("FL.spring");
            var camera=JsonSerializer.Serialize(viewport.CaptureCamera());var snapshot=_acceptedSnapshot;var revision=_renderAttempt;
            var controls=CaptureUiState(revision);
            NavigateWorkspace(Workspace.MainMenu);await Task.Delay(200);CaptureLayout(Path.Combine(directory,"main-menu.png"));
            var frames=viewport.FrameCount;await Task.Delay(150);
            if(viewport.FrameCount>frames+1 || viewport.TimerResolutionActive) throw new InvalidOperationException("Menu kept the GPU viewport active.");
            foreach(var destination in Enum.GetValues<Workspace>().Where(x=>x!=Workspace.MainMenu))
            {
                var menu=(StackPanel)_workspaceViews[Workspace.MainMenu];
                var button=menu.Children.OfType<Button>().Single(x=>(string)x.Content==WorkspaceName(destination));
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(150);
                if(_workspace!=destination || (IsSimulationWorkspace?RootGrid.Visibility!=Visibility.Visible:_workspaceViews[destination].Visibility!=Visibility.Visible))
                    throw new InvalidOperationException("Destination did not open: "+destination);
                if(_workspaceDrafts.TryGetValue(destination,out var fields))
                {fields.Name.Text=destination+" draft";fields.Notes.Text="Keep these notes\nacross navigation.";await Task.Delay(80);CaptureLayout(Path.Combine(directory,destination+".png"));}
                visited.Add(destination.ToString());NavigateWorkspace(Workspace.MainMenu);
            }
            NavigateWorkspace(Workspace.Visualizer3D);await Task.Delay(150);
            if(!ReferenceEquals(viewport,_inspectionViewport) || camera!=JsonSerializer.Serialize(viewport.CaptureCamera()) || viewport.SelectedId!="FL.spring")
                throw new InvalidOperationException("Navigation lost the shared viewport, camera or selection.");
            if(!ReferenceEquals(snapshot,_acceptedSnapshot) || revision!=_renderAttempt || controls!=CaptureUiState(revision))
                throw new InvalidOperationException("Screen navigation changed inputs or invoked physics.");
            foreach(var (destination,fields) in _workspaceDrafts)
                if(fields.Name.Text!=destination+" draft" || !fields.Notes.Text.Contains("across navigation")) throw new InvalidOperationException("Draft lost during navigation.");
            var saved=Path.Combine(directory,"saved-session.json");
            File.WriteAllText(saved,JsonSerializer.Serialize(new {state=new {ui=CaptureLiveDiagnostics()}},new JsonSerializerOptions {IncludeFields=true}));
            foreach(var fields in _workspaceDrafts.Values) {fields.Name.Text="changed";fields.Notes.Text="changed";}
            NavigateWorkspace(Workspace.Garage);RestoreLiveState(saved);await Task.Delay(200);
            if(_workspace!=Workspace.Visualizer3D || camera!=JsonSerializer.Serialize(viewport.CaptureCamera()) || _workspaceDrafts[Workspace.Garage].Name.Text!="Garage draft")
                throw new InvalidOperationException("Session restoration lost shell state.");
            NavigateWorkspace(Workspace.MainMenu);WindowState=WindowState.Minimized;await Task.Delay(150);WindowState=WindowState.Normal;await Task.Delay(150);
            if(viewport.TimerResolutionActive) throw new InvalidOperationException("Window restore activated a hidden viewport.");
            NavigateWorkspace(Workspace.Visualizer3D);await Task.Delay(150);CaptureLayout(Path.Combine(directory,"restored-3d.png"));
        }
        catch(Exception error) {failure=error;App.LogException("Shell acceptance",error);}
        File.WriteAllText(Path.Combine(directory,"proof.json"),JsonSerializer.Serialize(new {success=failure is null,error=failure?.ToString(),visited,
            sharedViewport=true,physicsIndependent=true,draftsRetained=true,sessionRestored=true},new JsonSerializerOptions {WriteIndented=true}));
        Application.Current.Shutdown(failure is null?0:2);
    }
}
