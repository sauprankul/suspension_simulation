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
    }
}
