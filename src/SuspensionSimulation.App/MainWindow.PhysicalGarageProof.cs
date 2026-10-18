using System.IO;
using System.Text.Json;
using System.Windows;
using SuspensionSimulation.Design;
using SuspensionSimulation.Solvers.Design;
using SuspensionSimulation.Solvers.HeadToHead;

namespace SuspensionSimulation.App;

public partial class MainWindow
{
    internal async void RunPhysicalGarageProof()
    {
        var authoringOnly=Environment.GetCommandLineArgs().Contains("--scoped-authoring-only");
        var folder=Path.Combine(App.RepositoryRoot,"artifacts",authoringOnly?"scoped-authoring-ui":"physical-garage-ui");Directory.CreateDirectory(folder);Exception? failure=null;
        try
        {
            await VerifyScopedParametersForProof(folder);
            if(authoringOnly)
            {
                NavigateWorkspace(Workspace.Garage);RefreshGarage(ReferenceGarageCatalog.Gr86Id);UpdateLayout();await Task.Delay(250);CaptureLayout(Path.Combine(folder,"garage-root-parameters.png"));
                OpenGarageVehicle();UpdateLayout();await Task.Delay(250);CaptureLayout(Path.Combine(folder,"complete-vehicle.png"));
                File.WriteAllText(Path.Combine(folder,"proof.json"),"{\"success\":true,\"scope\":\"scoped authoring and Garage configuration UI; no repeated static solve\"}");Application.Current.Shutdown(0);return;
            }
            await Task.Delay(500);NavigateWorkspace(Workspace.Garage);RefreshGarage(ReferenceGarageCatalog.Gr86Id);
            if(_garage!.List().Count!=2||_garage.List().Any(e=>e.Configuration is null)||_garagePhysicalEquipment.Count!=18) throw new InvalidOperationException("Stock physical Garage fields are incomplete.");
            var stock=_garageSelected!;GarageAction("Duplicate");var custom=_garageSelected!;
            var sourceGraph=new AssemblyGraphResolver(_designLibrary!).Compile(stock.Configuration!.RootAssembly,stock.Configuration.Equipment,stock.Configuration.ParameterValues??[]);
            var springSource=sourceGraph.ElasticElements.Single(e=>e.Path=="FL-spring").Component;
            var configuredRate=((CompressionSpringBehavior)springSource.Behavior!).RateKgfPerMm*1.15;
            _garageRootParameterInputs["FL-spring.rate"].Text=GarageNumber(configuredRate);
            var outside=stock.Configuration.ParameterValues!.Value.First(p=>p.ParameterId.EndsWith(".outside",StringComparison.Ordinal));
            _garageRootParameterInputs[outside.ParameterId].Text=ParameterDeclarationRow.N(outside.Value*1.03);
            _workspaceDrafts[Workspace.Garage].Name.Text="Independent adjustable GR86";
            _garagePhysicalEquipment["FL-damper:FL-damper"].SelectedValue="example-adjustable-front-damper";
            if(!_garagePhysicalAdjustments.ContainsKey("FL-damper:perch")) throw new InvalidOperationException("Hardware change did not generate perch control.");
            ((System.Windows.Controls.TextBox)_garagePhysicalAdjustments["FL-damper:perch"].Input).Text="10";
            _garageBaseMass.Text="1400";_garagePayloads.Add(new(){Id="ballast",Kg="20",Xmm="-800",Ymm="0",Zmm="200"});GarageAction("Save changes");
            var saved=_garage.Load(custom.Id)!;var graph=new GarageSetupResolver(_designLibrary!).Resolve(saved.Configuration!);
            if(Math.Abs(((CompressionSpringBehavior)graph.ElasticElements.Single(e=>e.Path=="FL-spring").Component.Behavior!).RateKgfPerMm-configuredRate)>1e-8||_designLibrary!.LoadComponent(springSource.Identity.AssetId)!.Identity.RevisionId!=springSource.Identity.RevisionId) throw new InvalidOperationException("Garage mechanical edits were lost or published a shared part.");
            if(Math.Abs(GraphAdjustments.ThreadedOffset(graph,"FL-damper","perch")-.01)>1e-9||saved.Configuration!.Mass.AddedPayloads.Length!=1||_garage.Load(stock.Id)!.Configuration!.Adjustments.Length!=0) throw new InvalidOperationException("Saved hardware/adjustments/mass did not round trip independently.");
            await CheckPhysicalGarage();if(!_garagePhysicalReadout.Text.Contains("1420.0 kg",StringComparison.Ordinal)) throw new InvalidOperationException("Saved static readout did not include payload once: "+_garagePhysicalReadout.Text);
            var revision=saved.Revision;((System.Windows.Controls.TextBox)_garagePhysicalAdjustments["FL-damper:perch"].Input).Text="invalid typed setting";
            _garageRootParameterInputs["FL-spring.rate"].Text="invalid spring rate";
            NavigateWorkspace(Workspace.MainMenu);NavigateWorkspace(Workspace.Garage);
            if(((System.Windows.Controls.TextBox)_garagePhysicalAdjustments["FL-damper:perch"].Input).Text!="invalid typed setting") throw new InvalidOperationException("Navigation discarded raw invalid Garage input.");
            if(_garageRootParameterInputs["FL-spring.rate"].Text!="invalid spring rate") throw new InvalidOperationException("Navigation discarded raw root parameter input.");
            GarageAction("Save changes");if(_garage.Load(custom.Id)!.Revision!=revision) throw new InvalidOperationException("Invalid adjustment was saved.");
            ((System.Windows.Controls.TextBox)_garagePhysicalAdjustments["FL-damper:perch"].Input).Text="10";
            _garageRootParameterInputs["FL-spring.rate"].Text=GarageNumber(configuredRate);
            var recovery=JsonSerializer.SerializeToElement(CapturePhysicalGarageWorkspace());_garagePhysicalDrafts.Clear();RestorePhysicalGarageWorkspace(recovery);
            if(_garageSelected?.Id!=custom.Id||_garageBaseMass.Text!="1400") throw new InvalidOperationException("Garage live-state recovery lost physical setup input.");
            await Task.Delay(200);CaptureLayout(Path.Combine(folder,"garage.png"));
            OpenGarageVehicle();await Task.Delay(250);
            if(_assemblyDefinition?.Vehicle is null||_assemblyGraph?.Root.Identity.AssetId!=saved.Configuration.RootAssembly.AssetId||_assemblyViewport!.SceneObjectCount==0) throw new InvalidOperationException("Complete car is not openable in Assembly Editor.");
            CaptureLayout(Path.Combine(folder,"complete-vehicle.png"));
            File.WriteAllText(Path.Combine(folder,"saved-setup.json"),DesignJson.Serialize(saved));
        }
        catch(Exception error) {failure=error;App.LogException("Physical Garage UI proof",error);}
        File.WriteAllText(Path.Combine(folder,"proof.json"),JsonSerializer.Serialize(new {success=failure is null,error=failure?.ToString()},new JsonSerializerOptions {WriteIndented=true}));
        Application.Current.Shutdown(failure is null?0:2);
    }
}
