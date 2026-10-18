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
                VerifyGarageChoicesForProof(folder);UpdateLayout();await Task.Delay(250);CaptureLayout(Path.Combine(folder,"garage-root-parameters.png"));
                _garageRootChoiceInputs.Values.First().BringIntoView();UpdateLayout();await Task.Delay(200);CaptureLayout(Path.Combine(folder,"garage-choice-inputs.png"));
                OpenGarageVehicle();UpdateLayout();await Task.Delay(250);CaptureLayout(Path.Combine(folder,"complete-vehicle.png"));
                File.WriteAllText(Path.Combine(folder,"proof.json"),"{\"success\":true,\"scope\":\"scoped authoring and Garage configuration UI; no repeated static solve\"}");Application.Current.Shutdown(0);return;
            }
            await Task.Delay(500);NavigateWorkspace(Workspace.Garage);RefreshGarage(ReferenceGarageCatalog.Gr86Id);
            if(_garage!.List().Count!=2||_garage.List().Any(e=>e.Configuration is null)||_garagePhysicalEquipment.Count!=0||_garagePhysicalAdjustments.Count!=0||_garageRootChoiceInputs.Count==0) throw new InvalidOperationException("Stock root-only physical Garage fields are incomplete.");
            var stock=_garageSelected!;GarageAction("Duplicate");var custom=_garageSelected!;
            var sourceGraph=new AssemblyGraphResolver(_designLibrary!).Compile(stock.Configuration!.RootAssembly,stock.Configuration.Equipment,stock.Configuration.ParameterValues??[]);
            var springSource=sourceGraph.ElasticElements.Single(e=>e.Path=="FL-spring").Component;
            var configuredRate=((CompressionSpringBehavior)springSource.Behavior!).RateKgfPerMm*1.15;
            SetGarageContractValue("FL-spring.rate",configuredRate);
            var outside=stock.Configuration.ParameterValues!.Value.First(p=>p.ParameterId.EndsWith(".outside",StringComparison.Ordinal));
            SetGarageContractValue(outside.ParameterId,outside.Value*1.03);
            _workspaceDrafts[Workspace.Garage].Name.Text="Independent adjustable GR86";
            var slot=sourceGraph.Root.EquipmentSlots!.Value.Single(s=>s.InstanceId=="FL-damper");var option=slot.Options!.Value.Single(o=>o.ChoiceId=="example-adjustable-front-damper");
            _garageRootChoiceInputs[slot.ParameterId!].SelectedValue=option.ChoiceId;
            var damper=_designLibrary!.LoadComponent(option.Definition.AssetId,option.Definition.RevisionId)!;var perch=damper.Adjustments!.Value.Single(a=>a.Id=="perch");var thread=(ThreadedAdjustmentPath)perch.Path;
            var perchId=option.ParameterValues.Single(p=>p.ParameterId==damper.AdjustmentParameters!.Value.Single(b=>b.AdjustmentId=="perch").ParameterId).FromParameterId!;
            if(!_garageRootParameterLabels[perchId].Label.Text.Contains("inactive with current hardware",StringComparison.Ordinal)) throw new InvalidOperationException("Stock damper did not mark the alternate perch parameter inactive.");
            var perchFraction=(thread.NominalLengthM+.01-thread.MinimumLengthM)/(thread.MaximumLengthM-thread.MinimumLengthM);
            SetGarageContractValue(perchId,perchFraction);
            _garageBaseMass.Text="1400";_garagePayloads.Add(new(){Id="ballast",Kg="20",Xmm="-800",Ymm="0",Zmm="200"});GarageAction("Save changes");
            var saved=_garage.Load(custom.Id)!;var graph=new GarageSetupResolver(_designLibrary!).Resolve(saved.Configuration!);
            if(_garageRootParameterLabels[perchId].Label.Text.Contains("inactive with current hardware",StringComparison.Ordinal)) throw new InvalidOperationException("Selected adjustable damper did not activate its root perch parameter.");
            if(Math.Abs(((CompressionSpringBehavior)graph.ElasticElements.Single(e=>e.Path=="FL-spring").Component.Behavior!).RateKgfPerMm-configuredRate)>1e-8||_designLibrary!.LoadComponent(springSource.Identity.AssetId)!.Identity.RevisionId!=springSource.Identity.RevisionId) throw new InvalidOperationException("Garage mechanical edits were lost or published a shared part.");
            if(Math.Abs(GraphAdjustments.ThreadedOffset(graph,"FL-damper","perch")-.01)>1e-9||saved.Configuration!.Mass.AddedPayloads.Length!=1||saved.Configuration.Equipment.Length!=0||saved.Configuration.Adjustments.Length!=0||_garage.Load(stock.Id)!.Configuration!.Adjustments.Length!=0) throw new InvalidOperationException("Saved root-only hardware/adjustments/mass did not round trip independently.");
            await CheckPhysicalGarage();if(!_garagePhysicalReadout.Text.Contains("1420.0 kg",StringComparison.Ordinal)) throw new InvalidOperationException("Saved static readout did not include payload once: "+_garagePhysicalReadout.Text);
            var revision=saved.Revision;_garageRootParameterInputs[perchId].Text="invalid typed setting";
            _garageRootParameterInputs["FL-spring.rate"].Text="invalid spring rate";
            _garageRootChoiceInputs[slot.ParameterId!].SelectedIndex=-1;_garageRootChoiceInputs[slot.ParameterId!].Text="invalid equipment choice";
            NavigateWorkspace(Workspace.MainMenu);NavigateWorkspace(Workspace.Garage);
            if(_garageRootParameterInputs[perchId].Text!="invalid typed setting"||_garageRootChoiceInputs[slot.ParameterId!].Text!="invalid equipment choice") throw new InvalidOperationException("Navigation discarded raw invalid Garage numeric/choice input.");
            if(_garageRootParameterInputs["FL-spring.rate"].Text!="invalid spring rate") throw new InvalidOperationException("Navigation discarded raw root parameter input.");
            GarageAction("Save changes");if(_garage.Load(custom.Id)!.Revision!=revision) throw new InvalidOperationException("Invalid adjustment was saved.");
            SetGarageContractValue(perchId,perchFraction);_garageRootChoiceInputs[slot.ParameterId!].SelectedValue=option.ChoiceId;
            SetGarageContractValue("FL-spring.rate",configuredRate);
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

    private void VerifyGarageChoicesForProof(string folder)
    {
        NavigateWorkspace(Workspace.Garage);RefreshGarage(ReferenceGarageCatalog.Gr86Id);var stock=_garageSelected!;GarageAction("Duplicate");var custom=_garageSelected!;
        var setup=custom.Configuration!;var root=_designLibrary!.LoadAssembly(setup.RootAssembly.AssetId,setup.RootAssembly.RevisionId)!;
        if(root.ConfigurationVersion!=2||_garageRootChoiceInputs.Count==0||_garagePhysicalEquipment.Count!=0||_garagePhysicalAdjustments.Count!=0) throw new InvalidOperationException("Garage must expose only complete-car root numeric/choice parameters.");
        var slot=root.EquipmentSlots!.Value.Single(s=>s.InstanceId=="FL-damper");var option=slot.Options!.Value.Single(o=>o.ChoiceId=="example-adjustable-front-damper");
        _garageRootChoiceInputs[slot.ParameterId!].SelectedValue=option.ChoiceId;
        var damper=_designLibrary.LoadComponent(option.Definition.AssetId,option.Definition.RevisionId)!;var binding=damper.AdjustmentParameters!.Value.Single(b=>b.AdjustmentId=="perch");var rootId=option.ParameterValues.Single(p=>p.ParameterId==binding.ParameterId).FromParameterId!;
        var thread=(ThreadedAdjustmentPath)damper.Adjustments!.Value.Single(a=>a.Id=="perch").Path;var fraction=(thread.NominalLengthM+.005-thread.MinimumLengthM)/(thread.MaximumLengthM-thread.MinimumLengthM);SetGarageContractValue(rootId,fraction);
        if(!_garageRootParameterLabels[rootId].Label.Text.Contains("inactive with current hardware",StringComparison.Ordinal)) throw new InvalidOperationException("An alternate-hardware perch setting was not marked inactive for the stock damper.");
        GarageAction("Save changes");var saved=_garage!.Load(custom.Id)!;var graph=new GarageSetupResolver(_designLibrary).Resolve(saved.Configuration!);
        if(saved.Revision==custom.Revision||saved.Configuration!.Equipment.Length!=0||saved.Configuration.Adjustments.Length!=0||Math.Abs(GraphAdjustments.ThreadedOffset(graph,"FL-damper","perch")-.005)>1e-10||_garage.Load(stock.Id)!.Revision!=stock.Revision) throw new InvalidOperationException("Root equipment/adjustment inputs did not save independently through their owning interfaces.");
        if(_garageRootParameterLabels[rootId].Label.Text.Contains("inactive with current hardware",StringComparison.Ordinal)) throw new InvalidOperationException("A selected adjustable damper retained an inactive perch label.");
        _garageRootChoiceInputs[slot.ParameterId!].SelectedIndex=-1;_garageRootChoiceInputs[slot.ParameterId!].Text="invalid raw equipment";_garageRootParameterInputs[rootId].Text="invalid raw fraction";
        var raw=JsonSerializer.SerializeToElement(CapturePhysicalGarageWorkspace());_garagePhysicalDrafts.Clear();RestorePhysicalGarageWorkspace(raw);
        if(_garageRootChoiceInputs[slot.ParameterId!].Text!="invalid raw equipment"||_garageRootParameterInputs[rootId].Text!="invalid raw fraction"||DraftFor(_garageSelected!).Mechanics is not null) throw new InvalidOperationException("Root choice/fraction recovery lost invalid raw text or recreated descendant overrides.");
        GarageAction("Save changes");if(_garage.Load(custom.Id)!.Revision!=saved.Revision) throw new InvalidOperationException("Invalid root choice/fraction was accepted.");
        _garageRootChoiceInputs[slot.ParameterId!].SelectedValue=option.ChoiceId;SetGarageContractValue(rootId,fraction);CachePhysicalGarageDraft();FillPhysicalGarage(_garageSelected!);
        File.WriteAllText(Path.Combine(folder,"root-only-car.json"),DesignJson.Serialize(saved));
    }
}
