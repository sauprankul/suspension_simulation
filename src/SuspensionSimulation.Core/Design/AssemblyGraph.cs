using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace SuspensionSimulation.Design;

public sealed record ResolvedBody(string Path,ComponentDefinition Component,DesignTransform Transform);
public sealed record ResolvedPort(string BodyPath,string PortId,string Role,ImmutableArray<JointKind> SupportedJoints,DesignFrame Frame);
public sealed record ResolvedJoint(string Path,JointKind Kind,ResolvedPort First,ResolvedPort Second,double? DistanceM);
public sealed record ResolvedElasticElement(string Path,ComponentDefinition Component,ResolvedPort First,ResolvedPort Second,
    DesignFrame FirstReference,DesignFrame SecondReference,DesignTransform Transform);
public sealed record ResolvedAssemblyGraph(AssemblyDefinition Root,ImmutableArray<ResolvedBody> Bodies,ImmutableArray<ResolvedJoint> Joints,
    ImmutableDictionary<string,ResolvedPort> Exports,ImmutableArray<string> GroundedBodies,FrozenResultManifest Manifest,
    ImmutableArray<ComponentDefinition> Components,ImmutableArray<AssemblyDefinition> Assemblies)
{
    public ImmutableDictionary<string,ResolvedPort> Interfaces {get;init;}=ImmutableDictionary<string,ResolvedPort>.Empty;
    public ImmutableArray<ResolvedElasticElement> ElasticElements {get;init;}=[];
    public ImmutableArray<ResolvedAdjustment> AppliedAdjustments {get;init;}=[];
    public ImmutableArray<SpringSeatBinding> SpringSeats {get;init;}=[];
    public ImmutableArray<ResolvedEquipmentSlot> EquipmentSlots {get;init;}=[];
    public ImmutableArray<ResolvedSetupAdjustment> SetupAdjustments {get;init;}=[];
    public ImmutableArray<EquipmentSelection> SelectedEquipment {get;init;}=[];
    public ImmutableArray<InstanceMechanicalValue>? ConfiguredMechanics {get;init;}
    public ImmutableArray<ResolvedInstanceConfiguration> ParameterConfigurations {get;init;}=[];
    public ImmutableArray<ResolvedAdjustment> ParameterAdjustments {get;init;}=[];
    public ImmutableArray<string> ActiveRootParameterIds {get;init;}=[];
}
public sealed record GraphResolution(ResolvedAssemblyGraph? Graph,bool IsStale,string? Conflict);
public sealed record PortableDesignBundle(string SchemaVersion,AssetReference Root,ImmutableArray<ComponentDefinition> Components,
    ImmutableArray<AssemblyDefinition> Assemblies,FrozenResultManifest Manifest)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ImmutableArray<EquipmentSelection>? Equipment {get;init;}
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ImmutableArray<ParameterValue>? ParameterValues {get;init;}
}

/// <summary>Captures each latest head once. A failed update retains the entire previous graph, never a mixture.</summary>
public sealed class AssemblyGraphResolver(IDesignAssetSource library)
{
    public GraphResolution Resolve(AssetReference root,ResolvedAssemblyGraph? lastGood=null,Func<ResolvedAssemblyGraph,string?>? validate=null,IEnumerable<ParameterValue>? values=null)
    {
        try
        {
            var graph=Compile(root,[],values??[]);
            if(lastGood is not null && lastGood.Root.Identity.AssetId==root.AssetId) CheckInterfaces(lastGood,graph);
            if(validate?.Invoke(graph) is {} error) throw new InvalidOperationException(error);
            return new(graph,false,null);
        }
        catch(Exception error) when(error is ArgumentException or InvalidOperationException or IOException or System.Text.Json.JsonException)
        {return new(lastGood?.Root.Identity.AssetId==root.AssetId?lastGood:null,true,error.Message);}
    }
    public ResolvedAssemblyGraph Compile(AssetReference root)=>Compile(root,null,[],[]);
    public ResolvedAssemblyGraph Compile(AssetReference root,IEnumerable<EquipmentSelection> equipment)=>Compile(root,null,equipment.ToArray(),[]);
    public ResolvedAssemblyGraph Compile(AssetReference root,IEnumerable<EquipmentSelection> equipment,IEnumerable<ParameterValue> values)=>Compile(root,null,equipment.ToArray(),values.ToArray());
    public ResolvedAssemblyGraph CompileDraft(AssemblyDefinition draft)=>CompileDraft(draft,[]);
    public ResolvedAssemblyGraph CompileDraft(AssemblyDefinition draft,IEnumerable<ParameterValue> values)=>Compile(new(draft.Identity.AssetId,draft.Identity.RevisionId),draft,[],values.ToArray());
    private ResolvedAssemblyGraph Compile(AssetReference root,AssemblyDefinition? draft,EquipmentSelection[] equipment,ParameterValue[] rootValues)
    {
        var components=new Dictionary<string,ComponentDefinition>(StringComparer.Ordinal);var assemblies=new Dictionary<string,AssemblyDefinition>(StringComparer.Ordinal);
        var heads=new Dictionary<(Type,string),object>();
        var bodies=new List<ResolvedBody>();var joints=new List<ResolvedJoint>();var active=new HashSet<string>(StringComparer.Ordinal);
        var interfaces=new Dictionary<string,ResolvedPort>(StringComparer.Ordinal);
        var seatBindings=new List<SpringSeatBinding>();
        var configurations=new List<ResolvedInstanceConfiguration>();
        var parameterAdjustments=new List<ResolvedAdjustment>();
        var usedRootParameters=new HashSet<string>(StringComparer.Ordinal);
        if(equipment.GroupBy(x=>(x.InstancePath,x.SlotId)).Any(g=>g.Count()>1)) throw new ArgumentException("Duplicate equipment selection.");
        var usedEquipment=new HashSet<EquipmentSelection>();var slots=new List<ResolvedEquipmentSlot>();
        var exposures=new List<(string Path,SetupAdjustmentExposure Definition)>();
        static void Segment(string value) {if(string.IsNullOrWhiteSpace(value)||value.Contains('/')||value.Contains(':')||value.Contains('\0')) throw new ArgumentException("Instance and port IDs must be nonempty path segments without '/' or ':'.");}
        T Fetch<T>(AssetReference reference,Dictionary<string,T> cache,Func<string,string?,T?> load,Func<T,RevisionIdentity> identity) where T:class
        {
            T? latest=null;
            if(reference.RevisionId is null)
            {
                var key=(typeof(T),reference.AssetId);
                if(!heads.TryGetValue(key,out var captured)) {captured=load(reference.AssetId,null)??throw new InvalidOperationException("Missing definition "+reference.AssetId);heads.Add(key,captured);}
                latest=(T)captured;reference=reference with {RevisionId=identity(latest).RevisionId};
            }
            if(cache.TryGetValue(reference.AssetId,out var existing))
            {if(reference.RevisionId is {} pinned && pinned!=identity(existing).RevisionId) throw new InvalidOperationException("Conflicting revisions for "+reference.AssetId+" in one graph.");return existing;}
            var item=latest??load(reference.AssetId,reference.RevisionId)??throw new InvalidOperationException("Missing definition "+reference.AssetId);cache.Add(reference.AssetId,item);return item;
        }
        Dictionary<string,ResolvedPort> Walk(AssemblyDefinition a,string path,DesignTransform parent,ImmutableArray<ParameterValue> values,Dictionary<string,string> roots)
        {
            DesignValidation.Validate(a);
            if(a.ConfigurationVersion==2||(a.EquipmentSlots??[]).Any(s=>s.Options is not null)) Capture(a,true);
            if(a.ConfigurationVersion==2&&equipment.Length>0) throw new ArgumentException("Scoped equipment is configured only through the imported assembly's parameters.");
            configurations.Add(new(path,values));
            string PathOf(string relative)=>path.Length==0?relative:path+"/"+relative;
            foreach(var binding in a.SpringSeats??[]) seatBindings.Add(new(PathOf(binding.SpringPath),PathOf(binding.DamperPath)));
            foreach(var exposure in a.SetupAdjustments??[]) exposures.Add((PathOf(exposure.InstancePath),exposure));
            if(!active.Add(a.Identity.AssetId)) throw new InvalidOperationException("Assembly cycle at "+path+" ("+a.Identity.AssetId+").");
            var ports=new Dictionary<string,Dictionary<string,ResolvedPort>>(StringComparer.Ordinal);
            foreach(var instance in a.Instances)
            {
                Segment(instance.Id);var childPath=path.Length==0?instance.Id:path+"/"+instance.Id;
                var reference=instance.Definition;
                var childAssignments=instance.ParameterValues??[];
                if((a.EquipmentSlots??[]).SingleOrDefault(s=>s.InstanceId==instance.Id) is {} slot)
                {
                    var selection=equipment.SingleOrDefault(e=>e.InstancePath==childPath&&e.SlotId==slot.Id);
                    if(slot.Options is {} options)
                    {
                        if(selection is not null) throw new ArgumentException("Imported equipment properties cannot be replaced through a descendant selection.");
                        var choice=slot.ParameterId is {} parameterId?values.Single(v=>v.ParameterId==parameterId).ChoiceId:null;
                        if(slot.ParameterId is {} used&&roots.TryGetValue(used,out var rootId)) usedRootParameters.Add(rootId);
                        var option=choice is null?options.FirstOrDefault(o=>o.Definition==instance.Definition):options.SingleOrDefault(o=>o.ChoiceId==choice);
                        if(option is null) throw new ArgumentException("Equipment has no selected/default option: "+childPath);
                        reference=option.Definition;childAssignments=option.ParameterValues;
                    }
                    if(selection is not null)
                    {
                        if(!slot.CompatibleAssetIds.Contains(selection.Component.AssetId)) throw new InvalidOperationException("Incompatible equipment at "+childPath+" ("+slot.Name+").");
                        reference=selection.Component;usedEquipment.Add(selection);
                    }
                    slots.Add(new(childPath,slot,instance is ComponentInstance?AssetKind.Component:AssetKind.Assembly,instance.Definition,reference));
                }
                var transform=DesignTransform.From(instance.Placement).Then(parent);
                var childRoots=childAssignments.Where(p=>p.FromParameterId is {} id&&roots.ContainsKey(id)).ToDictionary(p=>p.ParameterId,p=>roots[p.FromParameterId!],StringComparer.Ordinal);
                if(instance is ComponentInstance)
                {
                    var c=Fetch(reference,components,library.LoadComponent,x=>x.Identity);ComponentAuthoring.ValidateForPublish(c);
                    if(reference.AssetId!=instance.Definition.AssetId)
                    {
                        var original=Fetch(instance.Definition,components,library.LoadComponent,x=>x.Identity);
                        if(c.Kind!=original.Kind) throw new InvalidOperationException("Equipment kind changed at "+childPath);
                        foreach(var port in original.Ports)
                        {var next=c.Ports.SingleOrDefault(p=>p.Id==port.Id);if(next is null||next.Role!=port.Role||port.SupportedJoints.Except(next.SupportedJoints).Any()) throw new InvalidOperationException("Equipment interface changed at "+childPath+":"+port.Id);}
                    }
                    var configured=DefinitionParameters.Import(c.Parameters??[],childAssignments,a.Parameters??[],values);
                    foreach(var id in childRoots.Values) usedRootParameters.Add(id);
                    if(c.AdjustmentParameters is not null)
                    {var positions=ComponentParameters.Settings(c,configured);foreach(var adjustment in c.Adjustments??[]) {var position=positions.GetValueOrDefault(adjustment.Id)??adjustment.Default;parameterAdjustments.Add(new(childPath,adjustment.Id,position,AdjustmentPaths.Evaluate(adjustment.Path,position)));}}
                    configurations.Add(new(childPath,configured));c=MechanicalTemplates.Resolve(c,configured);
                    bodies.Add(new(childPath,c,transform));var mapped=new Dictionary<string,ResolvedPort>(StringComparer.Ordinal);
                    foreach(var port in c.Ports) {Segment(port.Id);mapped.Add(port.Id,new(childPath,port.Id,port.Role,port.SupportedJoints,transform.Frame(c.Frames.Single(x=>x.Id==port.FrameId).Frame)));}
                    ports.Add(instance.Id,mapped);
                }
                else
                {
                    var child=Fetch(reference,assemblies,library.LoadAssembly,x=>x.Identity);
                    var configured=DefinitionParameters.Import(child.Parameters??[],childAssignments,a.Parameters??[],values);
                    ports.Add(instance.Id,Walk(child,childPath,transform,configured,childRoots));
                }
                foreach(var port in ports[instance.Id]) interfaces.Add(childPath+":"+port.Key,port.Value);
            }
            ResolvedPort Port(InstancePort p)=>ports.TryGetValue(p.InstanceId,out var child)&&child.TryGetValue(p.PortId,out var port)?port:
                throw new InvalidOperationException("Unresolved port "+path+"/"+p.InstanceId+":"+p.PortId);
            foreach(var joint in a.Joints)
            {
                var first=Port(joint.First);var second=Port(joint.Second);
                if(first.BodyPath==second.BodyPath) throw new InvalidOperationException("Joint "+joint.Id+" connects a body to itself through exported ports.");
                if(!first.SupportedJoints.Contains(joint.Kind)||!second.SupportedJoints.Contains(joint.Kind)) throw new InvalidOperationException("Unsupported mate "+joint.Id+" ("+joint.Kind+").");
                double? distance=joint.Kind==JointKind.RigidDistance?joint.DistanceM??(first.Frame.OriginM-second.Frame.OriginM).Length:null;
                if(distance is {} d&&(!double.IsFinite(d)||d<=0)) throw new InvalidOperationException("Distance link must have positive rest length.");
                joints.Add(new(path+"/"+joint.Id,joint.Kind,first,second,distance));
            }
            var exports=new Dictionary<string,ResolvedPort>(StringComparer.Ordinal);
            foreach(var port in a.Ports) {Segment(port.Id);exports.Add(port.Id,Port(port.Source));}
            active.Remove(a.Identity.AssetId);return exports;
        }
        var rootDefinition=draft??Fetch(root,assemblies,library.LoadAssembly,x=>x.Identity);
        assemblies[rootDefinition.Identity.AssetId]=rootDefinition;
        // Scoped equipment is part of the authored interface. Freeze its approved alternatives too,
        // so a portable clone never depends on missing live-library definitions for another choice.
        var captured=new HashSet<string>(StringComparer.Ordinal);var capturing=new HashSet<string>(StringComparer.Ordinal);
        void Capture(AssemblyDefinition a,bool complete)
        {
            if(!capturing.Add(a.Identity.AssetId)) throw new InvalidOperationException("Cycle in equipment alternatives: "+a.Identity.AssetId);
            if(captured.Contains(a.Identity.AssetId)) {capturing.Remove(a.Identity.AssetId);return;}
            DesignValidation.Validate(a);
            foreach(var instance in a.Instances)
            {
                var scoped=(a.EquipmentSlots??[]).SingleOrDefault(s=>s.InstanceId==instance.Id)?.Options;
                if(!complete&&scoped is null) continue;
                var context=DefinitionParameters.Preview(a.Parameters??[]);
                foreach(var option in scoped??[new EquipmentOption("",instance.Definition,instance.ParameterValues??[])])
                    if(instance is ComponentInstance)
                    {var target=Fetch(option.Definition,components,library.LoadComponent,x=>x.Identity);ComponentAuthoring.ValidateForPublish(target);_=DefinitionParameters.Import(target.Parameters??[],option.ParameterValues,a.Parameters??[],context);}
                    else
                    {var target=Fetch(option.Definition,assemblies,library.LoadAssembly,x=>x.Identity);_=DefinitionParameters.Import(target.Parameters??[],option.ParameterValues,a.Parameters??[],context);Capture(target,true);}
            }
            capturing.Remove(a.Identity.AssetId);captured.Add(a.Identity.AssetId);
        }
        if(rootDefinition.ConfigurationVersion==2) Capture(rootDefinition,true);
        var exported=Walk(rootDefinition,"",DesignTransform.Identity,DefinitionParameters.Resolve(rootDefinition.Parameters??[],rootValues),(rootDefinition.Parameters??[]).ToDictionary(p=>p.Id,p=>p.Id,StringComparer.Ordinal));
        if(usedEquipment.Count!=equipment.Length) throw new ArgumentException("Equipment selection does not match an exposed slot: "+equipment.First(e=>!usedEquipment.Contains(e)).InstancePath);
        var setupAdjustments=new List<ResolvedSetupAdjustment>();
        foreach(var exposure in exposures)
        {
            var body=bodies.SingleOrDefault(b=>b.Path==exposure.Path)??throw new InvalidOperationException("Adjustment exposure has no component at "+exposure.Path);
            var definitions=body.Component.Adjustments??[];
            if(exposure.Definition.AdjustmentIds is {} ids&&ids.Any(id=>!definitions.Any(a=>a.Id==id))) throw new InvalidOperationException("Exposed adjustment is missing at "+exposure.Path);
            foreach(var adjustment in definitions.Where(a=>exposure.Definition.AdjustmentIds is not {} allowed||allowed.Contains(a.Id)))
                setupAdjustments.Add(new(exposure.Path,exposure.Definition.Name,adjustment));
        }
        if(setupAdjustments.GroupBy(x=>(x.InstancePath,x.Definition.Id)).Any(g=>g.Count()>1)) throw new InvalidOperationException("Adjustment exposed more than once.");
        // Massless force-element ends follow their mates independently. They must never be welded into one rigid body.
        var elasticBodies=bodies.Where(b=>b.Component.Behavior is not null).ToDictionary(b=>b.Path,StringComparer.Ordinal);
        var consumed=new HashSet<string>(StringComparer.Ordinal);
        ResolvedPort Anchor(ResolvedPort port,HashSet<string> visiting)
        {
            if(!elasticBodies.TryGetValue(port.BodyPath,out var body)) return port;
            var key=port.BodyPath+":"+port.PortId;if(!visiting.Add(key)) throw new InvalidOperationException("Force-element attachment cycle without a rigid support: "+key);
            var mates=joints.Where(j=>(j.First.BodyPath==port.BodyPath&&j.First.PortId==port.PortId)||(j.Second.BodyPath==port.BodyPath&&j.Second.PortId==port.PortId)).ToArray();
            if(mates.Length!=1) throw new InvalidOperationException("Force-element port must have exactly one attachment: "+key);
            var mate=mates[0];var expected=body.Component.Behavior is TorsionBarBehavior?JointKind.Fixed:JointKind.Spherical;
            if(mate.Kind!=expected) throw new InvalidOperationException("Wrong force-element attachment kind: "+key);
            consumed.Add(mate.Path);var other=mate.First.BodyPath==port.BodyPath&&mate.First.PortId==port.PortId?mate.Second:mate.First;
            var resolved=Anchor(other,visiting);visiting.Remove(key);return resolved;
        }
        var elements=new List<ResolvedElasticElement>();
        foreach(var body in elasticBodies.Values)
        {
            var behavior=body.Component.Behavior!;var a=interfaces[body.Path+":"+behavior.FirstPortId];var b=interfaces[body.Path+":"+behavior.SecondPortId];
            elements.Add(new(body.Path,body.Component,Anchor(a,[]),Anchor(b,[]),a.Frame,b.Frame,body.Transform));
        }
        var seenBindings=new HashSet<string>(StringComparer.Ordinal);
        foreach(var binding in seatBindings)
        {
            if(!seenBindings.Add(binding.SpringPath)) throw new InvalidOperationException("A spring has more than one seat installation.");
            var spring=elements.SingleOrDefault(e=>e.Path==binding.SpringPath);var damper=elements.SingleOrDefault(e=>e.Path==binding.DamperPath);
            if(spring?.Component.Behavior is not CompressionSpringBehavior||damper?.Component.Behavior is not AxialDamperBehavior {SpringSeats:not null}) throw new InvalidOperationException("Spring seating requires a spring and an integrated-seat damper.");
            if(spring.First.BodyPath!=damper.First.BodyPath||spring.First.PortId!=damper.First.PortId||spring.Second.BodyPath!=damper.Second.BodyPath||spring.Second.PortId!=damper.Second.PortId) throw new InvalidOperationException("Coaxial spring seats must use the same ordered eye references as their damper.");
        }
        ResolvedPort Map(ResolvedPort p)=>elasticBodies.ContainsKey(p.BodyPath)?Anchor(p,[]) with {Role=p.Role,SupportedJoints=p.SupportedJoints}:p;
        var mappedExports=exported.ToDictionary(x=>x.Key,x=>Map(x.Value),StringComparer.Ordinal);
        interfaces=interfaces.ToDictionary(x=>x.Key,x=>Map(x.Value),StringComparer.Ordinal);
        joints.RemoveAll(j=>consumed.Contains(j.Path));bodies.RemoveAll(b=>elasticBodies.ContainsKey(b.Path));
        if(rootDefinition.Vehicle is {} vehicle)
            foreach(var contact in vehicle.Contacts)
            {
                var wheel=bodies.SingleOrDefault(b=>b.Path==contact.InstancePath);
                if(wheel?.Component.Kind!=ComponentKind.Wheel||wheel.Component.Wheel is null||!wheel.Component.Points.Any(p=>p.Id==contact.CenterPointId)) throw new InvalidOperationException("Unresolved wheel/contact datum: "+contact.Corner+" at "+contact.InstancePath);
            }
        var grounded=new HashSet<string>(StringComparer.Ordinal);
        foreach(var path in rootDefinition.GroundedInstances)
        {
            var matches=bodies.Where(x=>x.Path==path||x.Path.StartsWith(path+"/",StringComparison.Ordinal)).ToArray();
            if(matches.Length==0) throw new InvalidOperationException("Unresolved ground path "+path);
            foreach(var body in matches) grounded.Add(body.Path);
        }
        static string Sha(string json)=>Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        var dependencies=components.Values.Select(c=>new ResolvedDependency(AssetKind.Component,c.Identity.AssetId,c.Identity.RevisionId,Sha(DesignJson.Serialize(c))))
            .Concat(assemblies.Values.Select(a=>new ResolvedDependency(AssetKind.Assembly,a.Identity.AssetId,a.Identity.RevisionId,Sha(DesignJson.Serialize(a)))))
            .OrderBy(x=>x.Kind).ThenBy(x=>x.AssetId,StringComparer.Ordinal).ToImmutableArray();
        return ChassisAssemblyGeometry.Place(new(rootDefinition,[..bodies],[..joints],mappedExports.ToImmutableDictionary(StringComparer.Ordinal),[..grounded.Order(StringComparer.Ordinal)],new(root.AssetId,dependencies),[..components.Values],[..assemblies.Values]) {Interfaces=interfaces.ToImmutableDictionary(StringComparer.Ordinal),ElasticElements=[..elements],SpringSeats=[..seatBindings],EquipmentSlots=[..slots],SetupAdjustments=[..setupAdjustments],SelectedEquipment=[..equipment],ParameterConfigurations=[..configurations],ParameterAdjustments=[..parameterAdjustments],AppliedAdjustments=[..parameterAdjustments],ActiveRootParameterIds=[..usedRootParameters.Order(StringComparer.Ordinal)]});
    }
    public static void CheckInterfaces(ResolvedAssemblyGraph old,ResolvedAssemblyGraph next)
    {
        foreach(var before in old.Root.Parameters??[])
        {var after=(next.Root.Parameters??[]).SingleOrDefault(p=>p.Id==before.Id);if(after is null||after.Unit!=before.Unit||(before.Choices is null)!=(after.Choices is null)) throw new InvalidOperationException("Root parameter interface changed: "+before.Id);}
        foreach(var body in old.Bodies)
        {var current=next.Bodies.SingleOrDefault(x=>x.Path==body.Path);if(current is not null&&current.Component.Kind!=body.Component.Kind) throw new InvalidOperationException("Component kind changed at "+body.Path);}
        foreach(var export in old.Exports)
        {
            if(!next.Exports.TryGetValue(export.Key,out var port)||port.Role!=export.Value.Role||export.Value.SupportedJoints.Except(port.SupportedJoints).Any())
                throw new InvalidOperationException("Exported interface changed: "+export.Key);
        }
        foreach(var before in old.Joints)
        {
            var after=next.Joints.SingleOrDefault(x=>x.Path==before.Path);if(after is null) continue;
            if(before.First.Role!=after.First.Role||before.Second.Role!=after.Second.Role) throw new InvalidOperationException("Joint port role changed at "+before.Path);
        }
        foreach(var before in old.ElasticElements)
        {
            var after=next.ElasticElements.SingleOrDefault(e=>e.Path==before.Path);if(after is null) continue;
            if(before.Component.Kind!=after.Component.Kind||before.First.Role!=after.First.Role||before.Second.Role!=after.Second.Role) throw new InvalidOperationException("Elastic interface changed at "+before.Path);
        }
    }
    public static PortableDesignBundle Export(ResolvedAssemblyGraph graph)
    {
        if(graph.AppliedAdjustments.Except(graph.ParameterAdjustments).Any()||graph.SelectedEquipment.Length>0) throw new InvalidOperationException("Export the source assembly bundle and setup settings separately; runtime installed geometry is not source data.");
        if(graph.Root.Identity.RevisionId.Length!=64) throw new InvalidOperationException("Publish the root before exporting.");
        return new(DesignConventions.SchemaVersion,new(graph.Root.Identity.AssetId,graph.Root.Identity.RevisionId),graph.Components,graph.Assemblies,graph.Manifest) {ParameterValues=graph.Root.Parameters is null?null:graph.ParameterConfigurations.Single(c=>c.InstancePath=="").Values};
    }
    public static PortableDesignBundle ExportConfigured(ResolvedAssemblyGraph graph)
    {
        if(graph.AppliedAdjustments.Except(graph.ParameterAdjustments).Any()) throw new InvalidOperationException("Export exact source geometry before applying Garage settings.");
        if(graph.Root.Identity.RevisionId.Length!=64) throw new InvalidOperationException("Publish the root before exporting.");
        return new(DesignConventions.SchemaVersion,new(graph.Root.Identity.AssetId,graph.Root.Identity.RevisionId),graph.Components,graph.Assemblies,graph.Manifest) {Equipment=graph.SelectedEquipment,ParameterValues=graph.Root.Parameters is null?null:graph.ParameterConfigurations.Single(c=>c.InstancePath=="").Values};
    }
    public static ResolvedAssemblyGraph OpenBundle(PortableDesignBundle bundle)
    {
        if(bundle.SchemaVersion!=DesignConventions.SchemaVersion) throw new InvalidDataException("Unsupported bundle schema.");
        DesignValidation.Validate(bundle.Manifest);
        if(bundle.Root.AssetId!=bundle.Manifest.RootAssemblyId||bundle.Root.RevisionId is null) throw new InvalidDataException("Bundle root must be an exact frozen revision.");
        var source=new FrozenSource(bundle);var graph=new AssemblyGraphResolver(source).Compile(bundle.Root,bundle.Equipment??[],bundle.ParameterValues??[]);
        if(!graph.Manifest.Dependencies.SequenceEqual(bundle.Manifest.Dependencies.OrderBy(x=>x.Kind).ThenBy(x=>x.AssetId,StringComparer.Ordinal))) throw new InvalidDataException("Frozen dependency manifest differs from the resolved graph.");
        return graph;
    }
    private sealed class FrozenSource:IDesignAssetSource
    {
        private readonly PortableDesignBundle _bundle;
        public FrozenSource(PortableDesignBundle bundle)
        {
            _bundle=bundle;
            var entries=bundle.Components.Select(x=>(AssetKind.Component,x.Identity,Json:DesignJson.Serialize(x),Value:(object)x))
                .Concat(bundle.Assemblies.Select(x=>(AssetKind.Assembly,x.Identity,Json:DesignJson.Serialize(x),Value:(object)x))).ToArray();
            if(entries.Length!=bundle.Manifest.Dependencies.Length||entries.Select(x=>(x.Item1,x.Identity.AssetId)).Distinct().Count()!=entries.Length) throw new InvalidDataException("Duplicate or missing bundle definitions.");
            foreach(var entry in entries)
            {
                DesignAssetLibrary.VerifyRevision(entry.Value);
                var dependency=bundle.Manifest.Dependencies.SingleOrDefault(x=>x.Kind==entry.Item1&&x.AssetId==entry.Identity.AssetId);
                var sha=Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(entry.Json)));
                if(dependency is null||dependency.RevisionId!=entry.Identity.RevisionId||dependency.Sha256!=sha) throw new InvalidDataException("Bundle dependency hash mismatch.");
            }
        }
        private void Match(RevisionIdentity identity,string? revision) {if(revision is not null&&revision!=identity.RevisionId) throw new InvalidDataException("Bundle refers to an unfrozen revision.");}
        public ComponentDefinition? LoadComponent(string id,string? revision=null) {var c=_bundle.Components.SingleOrDefault(x=>x.Identity.AssetId==id);if(c is not null) Match(c.Identity,revision);return c;}
        public AssemblyDefinition? LoadAssembly(string id,string? revision=null) {var a=_bundle.Assemblies.SingleOrDefault(x=>x.Identity.AssetId==id);if(a is not null) Match(a.Identity,revision);return a;}
    }
}
