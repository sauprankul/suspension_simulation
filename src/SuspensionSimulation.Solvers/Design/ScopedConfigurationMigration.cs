using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using SuspensionSimulation.Design;

namespace SuspensionSimulation.Solvers.Design;

/// <summary>Append new scoped source assets; never rewrite legacy definitions or accepted car settings.</summary>
internal sealed class ScopedConfigurationMigration(DesignAssetLibrary library)
{
    private readonly Dictionary<(AssetKind,string,string),RevisionIdentity> _converted=[];
    private readonly HashSet<(AssetKind,string,string)> _active=[];
    private static string Key(string path,string field)=>path.Replace('/','.')+"."+field;
    private static string NewId(RevisionIdentity id)=>"scoped-"+System.Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(id.AssetId+"\n"+id.RevisionId)))[..24];
    private static RevisionIdentity Identity(RevisionIdentity id)=>id with {AssetId=NewId(id),RevisionId="draft",Name=id.Name+" (scoped)",Provenance=id.Provenance with {Note=id.Provenance.Note+" Explicit parameter ownership migration; original asset "+id.AssetId+" revision "+id.RevisionId+"."}};
    private static DefinitionParameter Rate()=>new("rate","Spring rate","kgf/mm",.000001,10000);
    private static DefinitionParameter Shear()=>new("shear","Shear modulus","Pa",1,1e13);
    private static DefinitionParameter Outside()=>new("outside","Outside diameter","m",.000001,10);
    private static DefinitionParameter Inside()=>new("inside","Inside diameter","m",0,10);

    public GarageSetup Convert(GarageSetup setup)
    {
        if(setup.ParameterValues is not null) return setup;
        var original=new AssemblyGraphResolver(library).Compile(setup.RootAssembly,setup.Equipment);
        var mechanics=setup.MechanicalValues??GarageMechanicalConfiguration.Capture(original);
        // Validate legacy values before copying anything; invalid saved settings remain a reported migration conflict.
        _=GarageMechanicalConfiguration.Apply(original,mechanics);
        var values=new List<ParameterValue>();
        foreach(var setting in mechanics)
            switch(setting)
            {
                case InstanceSpringRate s:values.Add(new(Key(s.InstancePath,"rate"),s.RateKgfPerMm));break;
                case InstanceTorsionSection t:values.AddRange([new(Key(t.InstancePath,"shear"),t.ShearModulusPa),new(Key(t.InstancePath,"outside"),t.OutsideDiameterM),new(Key(t.InstancePath,"inside"),t.InsideDiameterM)]);break;
            }
        var root=Assembly(original.Root);
        var equipment=setup.Equipment.Select(e=>
        {
            var slot=original.EquipmentSlots.Single(s=>s.InstancePath==e.InstancePath&&s.Definition.Id==e.SlotId);
            var identity=slot.Kind==AssetKind.Component?Component(library.LoadComponent(e.Component.AssetId,e.Component.RevisionId)!).Identity:Assembly(library.LoadAssembly(e.Component.AssetId,e.Component.RevisionId)!).Identity;
            return e with {Component=new(identity.AssetId,e.Component.RevisionId is null?null:identity.RevisionId)};
        }).ToImmutableArray();
        var selected=DefinitionParameters.Resolve(root.Parameters??[],values);
        return setup with {RootAssembly=new(root.Identity.AssetId,setup.RootAssembly.RevisionId is null?null:root.Identity.RevisionId),Equipment=equipment,MechanicalValues=null,ParameterValues=selected};
    }
    private ComponentDefinition Component(ComponentDefinition source)
    {
        if(source.Behavior is not (CompressionSpringBehavior or TorsionBarBehavior)) return source;
        var id=NewId(source.Identity);if(library.LoadComponent(id) is {} existing) return existing;
        var converted=source with {Identity=Identity(source.Identity),Behavior=null};
        converted=source.Behavior switch
        {
            CompressionSpringBehavior s=>converted with {Parameters=[Rate()],MechanicalTemplate=new SpringMechanicalTemplate(s.FirstPortId,s.SecondPortId,new NumericParameterReference("rate"),s.FreeLengthM,s.BlockLengthM)},
            TorsionBarBehavior t=>converted with {Parameters=[Shear(),Outside(),Inside()],MechanicalTemplate=new TorsionMechanicalTemplate(t.FirstPortId,t.SecondPortId,t.UnitAxis,new NumericParameterReference("shear"),new NumericParameterReference("outside"),new NumericParameterReference("inside"),t.TorsionLengthM,t.RestTwistRad)},
            _=>throw new InvalidOperationException("Unsupported migration.")
        };
        return library.Publish(converted,null);
    }
    private AssemblyDefinition Assembly(AssemblyDefinition source)
    {
        var key=(AssetKind.Assembly,source.Identity.AssetId,source.Identity.RevisionId);
        if(_converted.TryGetValue(key,out var cached)) return library.LoadAssembly(cached.AssetId,cached.RevisionId)!;
        if(!_active.Add(key)) throw new InvalidOperationException("Cycle in legacy migration/equipment alternatives.");
        try
        {
        if(source.Parameters is not null) throw new InvalidOperationException("Mixed legacy/scoped hierarchy needs explicit configuration review.");
        var declarations=new List<DefinitionParameter>();var instances=new List<PhysicalInstance>();
        var choices=new Dictionary<(AssetKind,string),RevisionIdentity>();
        RevisionIdentity ConvertReference(AssetReference reference,AssetKind kind)
        {
            var identity=kind==AssetKind.Component?Component(library.LoadComponent(reference.AssetId,reference.RevisionId)??throw new InvalidOperationException("Missing migration component.")).Identity:Assembly(library.LoadAssembly(reference.AssetId,reference.RevisionId)??throw new InvalidOperationException("Missing migration assembly.")).Identity;
            choices[(kind,reference.AssetId)]=identity;return identity;
        }
        foreach(var instance in source.Instances)
        {
            var kind=instance is ComponentInstance?AssetKind.Component:AssetKind.Assembly;var identity=ConvertReference(instance.Definition,kind);
            var child=kind==AssetKind.Component?library.LoadComponent(identity.AssetId,identity.RevisionId)!.Parameters:library.LoadAssembly(identity.AssetId,identity.RevisionId)!.Parameters;
            var assignments=new List<ParameterAssignment>();
            foreach(var p in child??[]) {var id=instance.Id+"."+p.Id;declarations.Add(p with {Id=id,Name=instance.Id+" / "+p.Name});assignments.Add(new(p.Id,FromParameterId:id));}
            instances.Add(instance with {Definition=new(identity.AssetId,instance.Definition.RevisionId is null?null:identity.RevisionId),ParameterValues=assignments.Count==0?null:[..assignments]});
        }
        // Approved alternative equipment must retain the same declared parameter interface.
        var slots=source.EquipmentSlots?.Select(slot=>
        {
            var kind=source.Instances.Single(i=>i.Id==slot.InstanceId) is ComponentInstance?AssetKind.Component:AssetKind.Assembly;
            return slot with {CompatibleAssetIds=[..slot.CompatibleAssetIds.Select(id=>choices.TryGetValue((kind,id),out var known)?known.AssetId:ConvertReference(new(id),kind).AssetId)]};
        }).ToImmutableArray();
        if(declarations.Count==0) {_converted[key]=source.Identity;return source;}
        var converted=source with {Identity=Identity(source.Identity),Instances=[..instances],Parameters=[..declarations],EquipmentSlots=slots};
        // Include converted dependency identities: the same legacy root may later follow a newer child source.
        converted=converted with {Identity=converted.Identity with {AssetId="scoped-"+System.Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(DesignJson.Serialize(converted))))[..24]}};
        var published=library.LoadAssembly(converted.Identity.AssetId);
        if(published is null)
        {
            // Explicit temporary importer context for publication validation only, never persisted in the definition.
            var probe=declarations.Select(p=>new ParameterValue(p.Id,p.Id.EndsWith(".outside",StringComparison.Ordinal)?p.Maximum:p.Minimum));
            published=library.Publish(converted,null,probe);
        }
        _converted[key]=published.Identity;return published;
        }
        finally {_active.Remove(key);}
    }
}
