using System.IO;
using System.Windows.Controls;
using SuspensionSimulation.Design;

namespace SuspensionSimulation.App;

public partial class MainWindow
{
    private async Task VerifyScopedParametersForProof(string folder)
    {
        NavigateWorkspace(Workspace.ComponentDesigner);_componentKind.SelectedItem=ComponentKind.Spring;NewComponent(false);
        _workspaceDrafts[Workspace.ComponentDesigner].Name.Text="Proof parameterized spring";
        _componentParameters.Add(new(){Id="rate",Name="Spring rate",Unit="kgf/mm",Minimum="1",Maximum="20"});
        _componentParameterFields["rate"].Mode.IsChecked=true;_componentParameterFields["rate"].Reference.Text="rate";RefreshComponentPreviewRows(false);
        var missingRejected=false;try {ApplyComponentPreview(false);}catch(ArgumentException) {missingRejected=true;}
        if(!missingRejected) throw new InvalidOperationException("Component silently chose its own parameter value.");
        _componentParameterPreview[0].Value="6";ApplyComponentPreview(false);PublishComponent();var spring=_componentDefinition!;
        if(spring.Behavior is not null||spring.MechanicalTemplate is not SpringMechanicalTemplate {RateKgfPerMm:NumericParameterReference {ParameterId:"rate"}}||(spring.Parameters??[]).Length!=1) throw new InvalidOperationException("Published spring baked an importer value into its owner definition.");
        var original=spring.Identity.RevisionId;_componentParameterPreview[0].Value="8";PublishComponent();if(_componentDefinition!.Identity.RevisionId!=original) throw new InvalidOperationException("Changing component preview changed the published source revision.");
        _componentParameterPreview[0].Value="invalid preview text";var raw=CaptureComponentRaw();try {RestoreComponentRaw(raw,true);}catch(ArgumentException) { }
        if(_componentParameterPreview[0].Value!="invalid preview text") throw new InvalidOperationException("Component raw preview recovery discarded invalid text.");
        _componentParameterPreview[0].Value="6";ApplyComponentPreview(false);_componentParametersTab!.IsSelected=true;Activate();UpdateLayout();await Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.Render);await Task.Delay(600);CaptureLayout(Path.Combine(folder,"component-parameters.png"));
        NewComponent(false);_physicalFields["rate"].Text="7";_workspaceDrafts[Workspace.ComponentDesigner].Name.Text="Proof fixed-property spring";PublishComponent();var fixedSpring=_componentDefinition!;

        NavigateWorkspace(Workspace.AssemblyEditor);NewAssembly();_assemblyExpectedDof.Text="0";
        SyntheticAssemblyCatalog.SeedCorner(_designLibrary!);var ground=_designLibrary!.LoadComponent("analytic-chassis")!;
        _assemblyInstances.Add(AssemblyInstanceRow.From(new ComponentInstance("mount",new(ground.Identity.AssetId),AssemblyAuthoring.IdentityPlacement)));
        foreach(var (id,component) in new[]{("left",spring),("right",spring),("fixed",fixedSpring)})
        {
            _assemblyInstances.Add(AssemblyInstanceRow.From(new ComponentInstance(id,new(component.Identity.AssetId,component.Identity.RevisionId),AssemblyAuthoring.IdentityPlacement)));
            _assemblyJoints.Add(AssemblyJointRow.From(new(id+"-a",JointKind.Spherical,new(id,"first"),new("mount","lower"))));
            _assemblyJoints.Add(AssemblyJointRow.From(new(id+"-b",JointKind.Spherical,new(id,"second"),new("mount","upper"))));
        }
        _assemblyGrounds.Text="mount";_assemblyParameters.Add(new(){Id="axleRate",Name="Axle rate",Unit="kgf/mm",Minimum="1",Maximum="20"});
        _assemblyParameterAssignments.Add(new(){Instance="left",ParameterId="rate",FromOwnerParameter="axleRate"});_assemblyParameterAssignments.Add(new(){Instance="right",ParameterId="rate",Value="9"});
        RefreshAssemblyPreviewRows(false);_assemblyParameterPreview[0].Value="6";_workspaceDrafts[Workspace.AssemblyEditor].Name.Text="Proof spring axle";ApplyAssemblyPreview(false);PublishAssembly();var axle=_assemblyDefinition!;
        var rates=_assemblyGraph!.ElasticElements.ToDictionary(e=>e.Path,e=>((CompressionSpringBehavior)e.Component.Behavior!).RateKgfPerMm);
        if(rates["left"]!=6||rates["right"]!=9||rates["fixed"]!=7) throw new InvalidOperationException("Direct-child literal/forwarded configurations were not independent.");
        _assemblyParameterAssignments.Add(new(){Instance="fixed",ParameterId="rate",Value="10"});var propertyRejected=false;try {ApplyAssemblyPreview(false);}catch(ArgumentException) {propertyRejected=true;}_assemblyParameterAssignments.RemoveAt(_assemblyParameterAssignments.Count-1);
        if(!propertyRejected||_designLibrary.LoadComponent(fixedSpring.Identity.AssetId)!.Identity.RevisionId!=fixedSpring.Identity.RevisionId) throw new InvalidOperationException("Importer configured a source property.");
        _assemblyParameterPreview[0].Value="invalid assembly preview";var assemblyRaw=CaptureAssemblyRaw();try {RestoreAssemblyRaw(assemblyRaw,true);}catch(ArgumentException) { }
        if(_assemblyParameterPreview[0].Value!="invalid assembly preview") throw new InvalidOperationException("Assembly recovery lost invalid temporary preview text.");
        _assemblyParameterPreview[0].Value="6";ApplyAssemblyPreview(false);_assemblyParametersTab!.IsSelected=true;RefreshAssemblyChildParameters();await Task.Delay(150);CaptureLayout(Path.Combine(folder,"assembly-parameters.png"));

        NewAssembly();_assemblyExpectedDof.Text="0";_assemblyInstances.Add(AssemblyInstanceRow.From(new AssemblyInstance("axle",new(axle.Identity.AssetId,axle.Identity.RevisionId),AssemblyAuthoring.IdentityPlacement)));
        _assemblyGrounds.Text="axle/mount";_assemblyParameters.Add(new(){Id="carRate",Name="Complete car rate",Unit="kgf/mm",Minimum="1",Maximum="20"});_assemblyParameterAssignments.Add(new(){Instance="axle",ParameterId="axleRate",FromOwnerParameter="carRate"});RefreshAssemblyPreviewRows(false);_assemblyParameterPreview[0].Value="11";
        _workspaceDrafts[Workspace.AssemblyEditor].Name.Text="Proof explicit nested forwarding";ApplyAssemblyPreview(false);PublishAssembly();var nested=_assemblyDefinition!;var nestedRevision=nested.Identity.RevisionId;
        if(((CompressionSpringBehavior)_assemblyGraph!.ElasticElements.Single(e=>e.Path=="axle/left").Component.Behavior!).RateKgfPerMm!=11||nested.Instances.Single().ParameterValues!.Value.Single().FromParameterId!="carRate") throw new InvalidOperationException("Nested forwarding did not retain every boundary.");
        _assemblyParameterPreview[0].Value="12";PublishAssembly();if(_assemblyDefinition!.Identity.RevisionId!=nestedRevision) throw new InvalidOperationException("Assembly publication baked its own preview value into the source.");
        _assemblyParameterAssignments.Add(new(){Instance="axle/left",ParameterId="rate",Value="14"});var descendantRejected=false;try {ReadAssembly();}catch(ArgumentException) {descendantRejected=true;}_assemblyParameterAssignments.RemoveAt(_assemblyParameterAssignments.Count-1);
        if(!descendantRejected) throw new InvalidOperationException("A parent reached past its direct child boundary.");
        File.WriteAllText(Path.Combine(folder,"parameterized-component.json"),DesignJson.Serialize(spring));File.WriteAllText(Path.Combine(folder,"parameterized-assembly.json"),DesignJson.Serialize(_assemblyDefinition));
        DuplicateAssembly();
        if(_assemblyId==nested.Identity.AssetId||ReadAssemblyPreviewValues().Single().Value!=12||((CompressionSpringBehavior)_assemblyGraph!.ElasticElements.Single(e=>e.Path=="axle/left").Component.Behavior!).RateKgfPerMm!=12) throw new InvalidOperationException("Duplicating a parameterized assembly lost its temporary importer context.");
        _assemblyInstanceGrid!.SelectedItems.Clear();_assemblyInstanceGrid.SelectedItems.Add(_assemblyInstances.Single(i=>i.Id=="axle"));GroupAssemblySelection();
        if(ReadAssemblyPreviewValues().Single().Value!=12||((CompressionSpringBehavior)_assemblyGraph!.ElasticElements.Single(e=>e.Path.EndsWith("/axle/left",StringComparison.Ordinal)).Component.Behavior!).RateKgfPerMm!=12) throw new InvalidOperationException("Grouping lost explicit forwarding or the parent's temporary preview.");
        PublishAssembly();var groupedId=_assemblyId;var bundle=AssemblyGraphResolver.Export(_assemblyGraph!);ImportAssemblyBundle(bundle);
        if(_assemblyId==groupedId||ReadAssemblyPreviewValues().Single().Value!=12||((CompressionSpringBehavior)_assemblyGraph!.ElasticElements.Single(e=>e.Path.EndsWith("/axle/left",StringComparison.Ordinal)).Component.Behavior!).RateKgfPerMm!=12) throw new InvalidOperationException("Frozen independent import lost its explicit temporary importer context.");
        await VerifyScopedChoicesForProof(folder);
        VerifyPriorScopedRecoveryForProof();
    }

    private async Task VerifyScopedChoicesForProof(string folder)
    {
        NavigateWorkspace(Workspace.ComponentDesigner);_componentKind.SelectedItem=ComponentKind.RigidLink;NewComponent(false);MakeComponentCustom();
        var source=_componentDefinition!;var frame=source.Frames.Single(f=>f.Id=="outer").Frame;
        var holes=new DiscreteAdjustmentPath([new("near","Near",frame),new("far","Far",frame with {OriginM=frame.OriginM+new DesignVector(.02,0,0)})]);
        _componentMetadataSeed=source with {Adjustments=[new("mount-hole","Mounting hole",holes,new(ChoiceId:"near"),["outer"],["outer"],"Actual named mounting positions")]};FillComponentTables(_componentMetadataSeed);
        _componentParameters.Add(new(){Id="hole",Name="Mounting hole",Unit="choice",Choices="near=Near; far=Far"});_componentAdjustmentBindings.Add(new(){AdjustmentId="mount-hole",ParameterId="hole"});RefreshComponentPreviewRows(false);_componentParameterPreview.Single().ChoiceId="far";
        _workspaceDrafts[Workspace.ComponentDesigner].Name.Text="Proof scoped mounting holes";PublishComponent();var adjustable=_componentDefinition!;
        if(adjustable.Points.Single(p=>p.Id=="outer").PositionM!=frame.OriginM||adjustable.AdjustmentParameters!.Value.Single().ParameterId!="hole"||MechanicalTemplates.Resolve(adjustable,[ParameterValue.Choice("hole","far")]).Points.Single(p=>p.Id=="outer").PositionM.X!=frame.OriginM.X+.02) throw new InvalidOperationException("Component choice preview altered source geometry or failed to move the actual mounting frame.");
        var revision=adjustable.Identity.RevisionId;_componentParameterPreview.Single().ChoiceId="near";PublishComponent();if(_componentDefinition!.Identity.RevisionId!=revision) throw new InvalidOperationException("A selected mounting hole became an owner default.");
        _componentParameterPreview.Single().ChoiceId="unrecognized raw hole";var raw=CaptureComponentRaw();try {RestoreComponentRaw(raw,true);}catch(ArgumentException) { }
        if(_componentParameterPreview.Single().ChoiceId!="unrecognized raw hole") throw new InvalidOperationException("Invalid raw component choice was lost.");
        _componentParameterPreview.Single().ChoiceId="far";ApplyComponentPreview(false);_componentParametersTab!.IsSelected=true;_componentViewport!.Preset("Fit / reset");UpdateLayout();await Task.Delay(150);CaptureLayout(Path.Combine(folder,"component-adjustment-parameters.png"));
        NewComponent(true);_componentParameters.Clear();_componentAdjustmentBindings.Clear();_componentParameterPreview.Clear();_workspaceDrafts[Workspace.ComponentDesigner].Name.Text="Proof fixed mounting property";PublishComponent();var fixedPart=_componentDefinition!;
        if(!fixedPart.AdjustmentParameters!.Value.IsEmpty||(fixedPart.Parameters??[]).Length!=0) throw new InvalidOperationException("A fixed adjustment retained an importer override.");

        NavigateWorkspace(Workspace.AssemblyEditor);NewAssembly();_assemblyExpectedDof.Text="0";_assemblyGrounds.Text="part";_assemblyInstances.Add(AssemblyInstanceRow.From(new ComponentInstance("part",new(adjustable.Identity.AssetId,adjustable.Identity.RevisionId),AssemblyAuthoring.IdentityPlacement)));
        _assemblyParameters.Add(new(){Id="hole",Name="Selected mounting hole",Unit="choice",Choices="near=Near; far=Far"});_assemblyParameters.Add(new(){Id="hardware",Name="Approved hardware",Unit="choice",Choices="adjustable=Adjustable; fixed=Fixed"});_assemblyParameterAssignments.Add(new(){Instance="part",ParameterId="hole",FromOwnerParameter="hole"});
        _assemblySlots.Add(new(){Id="part-slot",Name="Approved part",ChildInstance="part",CompatibleAssetIds=adjustable.Identity.AssetId+","+fixedPart.Identity.AssetId,ParameterId="hardware"});
        _assemblyEquipmentOptions.Add(new(){SlotId="part-slot",ChoiceId="adjustable",AssetId=adjustable.Identity.AssetId,Revision=adjustable.Identity.RevisionId});_assemblyEquipmentOptions.Add(new(){SlotId="part-slot",ChoiceId="fixed",AssetId=fixedPart.Identity.AssetId,Revision=fixedPart.Identity.RevisionId});_assemblyEquipmentOptionValues.Add(new(){SlotId="part-slot",EquipmentChoice="adjustable",ParameterId="hole",FromOwnerParameter="hole"});
        RefreshAssemblyPreviewRows(false);_assemblyParameterPreview.Single(p=>p.ParameterId=="hole").ChoiceId="far";_assemblyParameterPreview.Single(p=>p.ParameterId=="hardware").ChoiceId="adjustable";_workspaceDrafts[Workspace.AssemblyEditor].Name.Text="Proof owner equipment options";PublishAssembly();var owner=_assemblyDefinition!;
        if(owner.ConfigurationVersion!=2||_assemblyGraph!.Bodies.Single().Component.Points.Single(p=>p.Id=="outer").PositionM.X!=frame.OriginM.X+.02) throw new InvalidOperationException("Owner equipment option did not forward the named child mounting hole.");
        _assemblyParameterPreview.Single(p=>p.ParameterId=="hardware").ChoiceId="fixed";PublishAssembly();if(_assemblyDefinition!.Identity.RevisionId!=owner.Identity.RevisionId||_assemblyGraph!.Bodies.Single().Component.Identity.AssetId!=fixedPart.Identity.AssetId||_assemblyGraph.Bodies.Single().Component.Points.Single(p=>p.Id=="outer").PositionM!=frame.OriginM) throw new InvalidOperationException("Fixed owner hardware was overridden or preview equipment changed the source revision.");
        var recovery=CaptureAssemblyRaw();RestoreAssemblyRaw(recovery,true);if(_assemblyEquipmentOptions.Count!=2||_assemblyEquipmentOptionValues.Single().FromOwnerParameter!="hole"||ReadAssemblyPreviewValues().Single(p=>p.ParameterId=="hardware").ChoiceId!="fixed") throw new InvalidOperationException("Equipment option raw recovery lost importer configuration.");
        _assemblyParametersTab!.IsSelected=true;UpdateLayout();await Task.Delay(150);CaptureLayout(Path.Combine(folder,"assembly-choice-parameters.png"));File.WriteAllText(Path.Combine(folder,"choice-owner.json"),DesignJson.Serialize(owner));
    }

    private void VerifyPriorScopedRecoveryForProof()
    {
        var entry=_garage!.Load(SuspensionSimulation.Solvers.HeadToHead.ReferenceGarageCatalog.Gr86Id)!;var values=entry.Configuration!.ParameterValues!.Value.ToDictionary(p=>p.ParameterId,StringComparer.Ordinal);
        var prior=_designLibrary!.Assemblies().First(a=>a.Vehicle is not null&&a.ConfigurationVersion is null&&(a.Parameters??[]).Length>0&&(a.Parameters??[]).All(p=>values.ContainsKey(p.Id)));
        var raw=DraftFor(entry) with {Root=new(prior.Identity.AssetId,prior.Identity.RevisionId),Equipment=[],Adjustments=[],Mechanics=[],RootParameters=(prior.Parameters??[]).ToDictionary(p=>p.Id,p=>GarageParameterText(values[p.Id]))};
        var migrated=UpgradeGarageParameterDraft(entry,raw);
        if(migrated.Mechanics is not null||migrated.Equipment.Length!=0||migrated.Adjustments.Count!=0||_designLibrary.LoadAssembly(migrated.Root.AssetId,migrated.Root.RevisionId)!.ConfigurationVersion!=2) throw new InvalidOperationException("A valid prior scoped draft with empty legacy Mechanics did not migrate to root-only configuration.");
    }
}
