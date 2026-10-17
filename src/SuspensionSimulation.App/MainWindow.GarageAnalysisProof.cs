using System.IO;
using System.Text.Json;
using System.Windows;
using SuspensionSimulation.Design;
using SuspensionSimulation.Solvers.HeadToHead;

namespace SuspensionSimulation.App;

public partial class MainWindow
{
    internal async void RunGarageAnalysisProof()
    {
        var folder=Path.Combine(App.RepositoryRoot,"artifacts","garage-analysis-proof");Directory.CreateDirectory(folder);Exception? failure=null;
        try
        {
            await Task.Delay(600);
            if(_garage!.List().Count!=2 || _acceptedSnapshot?.GarageA?.EntryId!=ReferenceGarageCatalog.Gr86Id || _acceptedSnapshot.GarageB?.EntryId!=ReferenceGarageCatalog.BrzId)
                throw new InvalidOperationException("Both stock Garage entries must drive initial analysis.");
            AFrontSpring.Text="999";BFrontSpring.Text="999";Render();
            if(_acceptedSnapshot!.A.Input.FrontSpringNPerM!=28420 || _acceptedSnapshot.B.Input.FrontSpringNPerM!=30380) throw new InvalidOperationException("Ad-hoc controls still drive Garage analysis.");
            var stock=_garage.Load(ReferenceGarageCatalog.Gr86Id)!;var custom=_garage.Duplicate(stock);
            custom=_garage.Save(custom with {Name="Custom Garage car",ReferenceEquipment=custom.ReferenceEquipment! with {FrontSpringId=_garageCatalog!.Stock(true).FrontSpringId}});
            RefreshAnalysisChoices();_analysisA.SelectedValue=custom.Id;
            if(_acceptedSnapshot!.GarageA?.EntryId!=custom.Id || _acceptedSnapshot.A.Input.FrontSpringNPerM!=30380) throw new InvalidOperationException("Garage selection did not update analysis.");
            NavigateWorkspace(Workspace.HeadToHead);await Task.Delay(180);CaptureLayout(Path.Combine(folder,"2d.png"));
            var snapshot=_acceptedSnapshot;NavigateWorkspace(Workspace.Visualizer3D);await Task.Delay(350);
            if(!ReferenceEquals(snapshot,_acceptedSnapshot) || _acceptedSnapshot!.A.FrontRsd!=_comparison!.A.State.RollStiffnessDistributionFront) throw new InvalidOperationException("2D and 3D disagree for the same Garage source.");
            CaptureLayout(Path.Combine(folder,"3d.png"));ExportInspection();
            var path=Directory.GetFiles(Path.Combine(App.RepositoryRoot,"artifacts/inspection-snapshots"),"snapshot.json",SearchOption.AllDirectories).MaxBy(File.GetLastWriteTimeUtc)!;
            using(var export=JsonDocument.Parse(File.ReadAllText(path)))
                if(export.RootElement.GetProperty("snapshot").GetProperty("GarageA").GetProperty("EntryId").GetString()!=custom.Id) throw new InvalidOperationException("Export lost Garage identity.");
            custom=_garage.Save(custom with {Notes="Updated revision"});EnsureGarageAnalysisCurrent();
            if(_acceptedSnapshot!.GarageA!.Revision!=custom.Revision) throw new InvalidOperationException("Saved Garage revision was not resolved.");
            var last=_acceptedSnapshot;_garage.Save(custom with {Archived=true});RefreshAnalysisChoices();Render();
            if(_rejectedRevision is null || !ReferenceEquals(last,_acceptedSnapshot)) throw new InvalidOperationException("Invalid selection did not retain a stale complete snapshot.");
            _analysisA.SelectedValue=ReferenceGarageCatalog.Gr86Id;
            if(_rejectedRevision is not null) throw new InvalidOperationException("Valid Garage selection did not recover.");
            if(((FrameworkElement)AFrontSpring.Parent).Visibility!=Visibility.Collapsed || RackGeometryButton.Visibility!=Visibility.Collapsed) throw new InvalidOperationException("Specification editors remain visible in analysis.");
            NavigateWorkspace(Workspace.Garage);RefreshGarage(stock.Id);await Task.Delay(150);CaptureLayout(Path.Combine(folder,"garage.png"));
        }
        catch(Exception error) {failure=error;App.LogException("Garage analysis proof",error);}
        File.WriteAllText(Path.Combine(folder,"proof.json"),JsonSerializer.Serialize(new {success=failure is null,error=failure?.ToString()},new JsonSerializerOptions {WriteIndented=true}));
        Application.Current.Shutdown(failure is null?0:2);
    }
}
