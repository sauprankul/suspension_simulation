using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using SuspensionSimulation.Design;

namespace SuspensionSimulation.Solvers.Design;

/// <summary>Append a fully scoped hierarchy and configure its root; keep source/accepted revisions intact.</summary>
internal sealed class ScopedInterfaceMigration(DesignAssetLibrary library)
{
    private sealed record ConvertedComponent(ComponentDefinition Definition,ImmutableArray<ParameterValue> Suggestions);
    private sealed record ConvertedAssembly(AssemblyDefinition Definition,ImmutableArray<ParameterValue> Suggestions);
    private ResolvedAssemblyGraph _original=null!;
    private GarageSetup _setup=null!;
    private readonly HashSet<string> _active=new(StringComparer.Ordinal);
    private static string Fingerprint<T>(T value)=>System.Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(DesignJson.Serialize(value))))[..24];
    private static RevisionIdentity Identity(RevisionIdentity source)=>source with {RevisionId="draft",Name=source.Name.EndsWith(" (scoped)",StringComparison.Ordinal)?source.Name:source.Name+" (scoped)",Provenance=source.Provenance with {Note=source.Provenance.Note+" Mechanical/equipment ownership migration from "+source.AssetId+" revision "+source.RevisionId+"."}};
    private static string Path(string owner,string child)=>owner.Length==0?child:owner+"/"+child;
    private ImmutableArray<ParameterValue> Context(string path,IEnumerable<DefinitionParameter> declarations)
    {
        var parameters=declarations.ToArray();var previous=_original.ParameterConfigurations.SingleOrDefault(c=>c.InstancePath==path)?.Values??[];
        if(previous.Select(v=>v.ParameterId).ToHashSet(StringComparer.Ordinal).SetEquals(parameters.Select(p=>p.Id)))
            try {return DefinitionParameters.Resolve(parameters,previous);}catch(ArgumentException) { }
        return [..parameters.Select(p=>p.Choices is {} choices?ParameterValue.Choice(p.Id,choices[0].Id):new ParameterValue(p.Id,p.Minimum))];
    }
    public GarageSetup Convert(GarageSetup setup)
    {
        var source=library.LoadAssembly(setup.RootAssembly.AssetId,setup.RootAssembly.RevisionId)??throw new InvalidOperationException("The saved car assembly is missing.");
        if(source.ConfigurationVersion==2)
        {if(!setup.Equipment.IsEmpty||!setup.Adjustments.IsEmpty) throw new ArgumentException("A scoped car cannot contain descendant overrides.");return setup;}
        _setup=setup;_original=new AssemblyGraphResolver(library).Compile(setup.RootAssembly,setup.Equipment,setup.ParameterValues??[]);
        _=GarageSetupResolver.Freeze(_original,setup); // Validate existing exposed settings before copying them.
        var root=Assembly(source,"");
        var values=DefinitionParameters.Resolve(root.Definition.Parameters??[],root.Suggestions);
        var result=setup with {RootAssembly=new(root.Definition.Identity.AssetId,setup.RootAssembly.RevisionId is null?null:root.Definition.Identity.RevisionId),Equipment=[],Adjustments=[],ParameterValues=values,MechanicalValues=null};
        _=new GarageSetupResolver(library).Resolve(result);
        return result;
    }
    private ConvertedComponent Component(ComponentDefinition source,string path)
    {
        if(source.AdjustmentParameters is not null) return new(source,Context(path,source.Parameters??[]));
        var declarations=(source.Parameters??[]).ToList();var values=Context(path,declarations).ToDictionary(v=>v.ParameterId,StringComparer.Ordinal);var bindings=new List<AdjustmentParameterBinding>();
        var active=_original.Bodies.Select(b=>(b.Path,b.Component.Identity.AssetId)).Concat(_original.ElasticElements.Select(e=>(e.Path,e.Component.Identity.AssetId))).Any(b=>b.Path==path&&b.AssetId==source.Identity.AssetId);
        foreach(var adjustment in source.Adjustments??[])
        {
            if(adjustment.Path is FixedAdjustmentPath) continue;
            var id="adjustment."+adjustment.Id;
            if(declarations.Any(p=>p.Id==id)) throw new ArgumentException("New adjustment parameter conflicts with an existing declaration: "+path+":"+id);
            var declaration=adjustment.Path is DiscreteAdjustmentPath holes?DefinitionParameter.Choice(id,adjustment.Name,holes.Choices.Select(h=>new ParameterChoice(h.Id,h.Name)),adjustment.Description):new DefinitionParameter(id,adjustment.Name,"fraction",0,1,adjustment.Description);
            declarations.Add(declaration);bindings.Add(new(adjustment.Id,id));
            var legacy=active?_setup.Adjustments.SingleOrDefault(a=>a.InstancePath==path&&a.AdjustmentId==adjustment.Id):null;
            var setting=legacy switch {ContinuousAdjustmentSelection c=>new AdjustmentSetting(c.Fraction),DiscreteAdjustmentSelection d=>new AdjustmentSetting(ChoiceId:d.ChoiceId),_=>adjustment.Default};
            values[id]=setting.ChoiceId is {} choice?ParameterValue.Choice(id,choice):new(id,setting.Fraction??0);
        }
        var definition=source with {Identity=Identity(source.Identity),Parameters=declarations.Count==0?source.Parameters:[..declarations],AdjustmentParameters=[..bindings]};
        definition=definition with {Identity=definition.Identity with {AssetId="scoped2-"+Fingerprint(definition)}};
        var published=library.LoadComponent(definition.Identity.AssetId)??library.Publish(definition,null);
        var suggestions=DefinitionParameters.Resolve(published.Parameters??[],values.Values);
        return new(published,suggestions);
    }
    private ConvertedAssembly Assembly(AssemblyDefinition source,string path)
    {
        if(!_active.Add(source.Identity.AssetId)) throw new InvalidOperationException("Cycle in scoped equipment migration.");
        try
        {
            if(source.ConfigurationVersion==2) return new(source,Context(path,source.Parameters??[]));
            var declarations=(source.Parameters??[]).ToList();var originals=declarations.Select(p=>p.Id).ToHashSet(StringComparer.Ordinal);
            var values=Context(path,declarations).ToDictionary(v=>v.ParameterId,StringComparer.Ordinal);
            var instances=new List<PhysicalInstance>();var slots=new List<EquipmentSlot>();
            foreach(var instance in source.Instances)
            {
                var childPath=Path(path,instance.Id);var slot=(source.EquipmentSlots??[]).SingleOrDefault(s=>s.InstanceId==instance.Id);
                var actual=slot is null?instance.Definition:_original.EquipmentSlots.SingleOrDefault(s=>s.InstancePath==childPath&&s.Definition.Id==slot.Id)?.Selected??instance.Definition;
                (RevisionIdentity Identity,ImmutableArray<DefinitionParameter> Parameters,ImmutableArray<ParameterValue> Suggestions) Child(AssetReference reference)
                {
                    if(instance is ComponentInstance) {var c=Component(library.LoadComponent(reference.AssetId,reference.RevisionId)??throw new InvalidOperationException("Missing component in migration."),childPath);return(c.Definition.Identity,c.Definition.Parameters??[],c.Suggestions);}
                    var a=Assembly(library.LoadAssembly(reference.AssetId,reference.RevisionId)??throw new InvalidOperationException("Missing assembly in migration."),childPath);return(a.Definition.Identity,a.Definition.Parameters??[],a.Suggestions);
                }
                ImmutableArray<ParameterAssignment> Assign(ImmutableArray<DefinitionParameter> childParameters,ImmutableArray<ParameterValue> childValues,bool selected,string discriminator)
                {
                    var result=new List<ParameterAssignment>();
                    foreach(var parameter in childParameters)
                    {
                        var previous=(instance.ParameterValues??[]).SingleOrDefault(p=>p.ParameterId==parameter.Id);
                        if(previous is not null) {result.Add(previous);continue;}
                        var id=instance.Id+"."+parameter.Id;var existing=declarations.SingleOrDefault(p=>p.Id==id);
                        if(existing is not null&&!DefinitionParameters.CanForward(existing,parameter)) id=instance.Id+".option-"+Fingerprint(discriminator)+"."+parameter.Id;
                        existing=declarations.SingleOrDefault(p=>p.Id==id);
                        if(existing is null) declarations.Add(parameter with {Id=id,Name=instance.Id+" / "+parameter.Name});
                        else if(!DefinitionParameters.CanForward(existing,parameter)) throw new ArgumentException("Equipment alternatives have incompatible parameter interfaces: "+childPath+":"+parameter.Id);
                        var suggested=childValues.Single(v=>v.ParameterId==parameter.Id) with {ParameterId=id};
                        if(!values.ContainsKey(id)||selected&&!originals.Contains(id)) values[id]=suggested;
                        result.Add(new(parameter.Id,FromParameterId:id));
                    }
                    return [..result];
                }
                if(slot is null)
                {
                    var child=Child(instance.Definition);var assignments=Assign(child.Parameters,child.Suggestions,true,instance.Definition.AssetId);
                    instances.Add(instance with {Definition=new(child.Identity.AssetId,instance.Definition.RevisionId is null?null:child.Identity.RevisionId),ParameterValues=assignments.IsEmpty?null:assignments});continue;
                }
                if(slot.Options is not null) throw new ArgumentException("Mixed equipment interfaces require explicit source review before automatic migration.");
                var options=new List<EquipmentOption>();var choices=new List<ParameterChoice>();AssetReference? defaultReference=null;ImmutableArray<ParameterAssignment> defaultAssignments=[];
                // Process the selected option last so shared newly-exposed settings retain its actual saved position.
                foreach(var asset in slot.CompatibleAssetIds.OrderBy(id=>id==actual.AssetId?1:0))
                {
                    var reference=asset==actual.AssetId?actual:asset==instance.Definition.AssetId?instance.Definition:new AssetReference(asset);
                    var child=Child(reference);var assignments=Assign(child.Parameters,child.Suggestions,asset==actual.AssetId,asset);
                    var convertedReference=new AssetReference(child.Identity.AssetId,reference.RevisionId is null?null:child.Identity.RevisionId);
                    options.Add(new(asset,convertedReference,assignments));choices.Add(new(asset,child.Identity.Name));
                    if(asset==instance.Definition.AssetId) {defaultReference=convertedReference;defaultAssignments=assignments;}
                }
                var parameterId="equipment."+slot.Id;
                if(declarations.Any(p=>p.Id==parameterId)) throw new ArgumentException("New equipment parameter conflicts with an existing declaration: "+parameterId);
                declarations.Add(DefinitionParameter.Choice(parameterId,slot.Name,choices,slot.Description));values[parameterId]=ParameterValue.Choice(parameterId,actual.AssetId);
                slots.Add(slot with {CompatibleAssetIds=[..options.Select(o=>o.Definition.AssetId).Distinct(StringComparer.Ordinal)],ParameterId=parameterId,Options=[..options]});
                instances.Add(instance with {Definition=defaultReference??throw new ArgumentException("Equipment has no default choice."),ParameterValues=defaultAssignments.IsEmpty?null:defaultAssignments});
            }
            var definition=source with {Identity=Identity(source.Identity),Instances=[..instances],Parameters=[..declarations],EquipmentSlots=slots.Count==0?null:[..slots],SetupAdjustments=null,ConfigurationVersion=2};
            definition=definition with {Identity=definition.Identity with {AssetId="scoped2-"+Fingerprint(definition)}};
            var suggestions=DefinitionParameters.Resolve(declarations,values.Values);
            var published=library.LoadAssembly(definition.Identity.AssetId)??library.Publish(definition,null,suggestions);
            return new(published,suggestions);
        }
        finally {_active.Remove(source.Identity.AssetId);}
    }
}
