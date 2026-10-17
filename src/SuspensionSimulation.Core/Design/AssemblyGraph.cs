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
}
public sealed record GraphResolution(ResolvedAssemblyGraph? Graph,bool IsStale,string? Conflict);
public sealed record PortableDesignBundle(string SchemaVersion,AssetReference Root,ImmutableArray<ComponentDefinition> Components,
    ImmutableArray<AssemblyDefinition> Assemblies,FrozenResultManifest Manifest);

/// <summary>Captures each latest head once. A failed update retains the entire previous graph, never a mixture.</summary>
public sealed class AssemblyGraphResolver(IDesignAssetSource library)
{
    public GraphResolution Resolve(AssetReference root,ResolvedAssemblyGraph? lastGood=null,Func<ResolvedAssemblyGraph,string?>? validate=null)
    {
        try
        {
            var graph=Compile(root);
            if(lastGood is not null && lastGood.Root.Identity.AssetId==root.AssetId) CheckInterfaces(lastGood,graph);
            if(validate?.Invoke(graph) is {} error) throw new InvalidOperationException(error);
            return new(graph,false,null);
        }
        catch(Exception error) when(error is ArgumentException or InvalidOperationException or IOException or System.Text.Json.JsonException)
        {return new(lastGood?.Root.Identity.AssetId==root.AssetId?lastGood:null,true,error.Message);}
    }
    public ResolvedAssemblyGraph Compile(AssetReference root)=>Compile(root,null);
    public ResolvedAssemblyGraph CompileDraft(AssemblyDefinition draft)=>Compile(new(draft.Identity.AssetId,draft.Identity.RevisionId),draft);
    private ResolvedAssemblyGraph Compile(AssetReference root,AssemblyDefinition? draft)
    {
        var components=new Dictionary<string,ComponentDefinition>(StringComparer.Ordinal);var assemblies=new Dictionary<string,AssemblyDefinition>(StringComparer.Ordinal);
        var heads=new Dictionary<(Type,string),object>();
        var bodies=new List<ResolvedBody>();var joints=new List<ResolvedJoint>();var active=new HashSet<string>(StringComparer.Ordinal);
        var interfaces=new Dictionary<string,ResolvedPort>(StringComparer.Ordinal);
        var seatBindings=new List<SpringSeatBinding>();
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
        Dictionary<string,ResolvedPort> Walk(AssemblyDefinition a,string path,DesignTransform parent)
        {
            DesignValidation.Validate(a);
            string PathOf(string relative)=>path.Length==0?relative:path+"/"+relative;
            foreach(var binding in a.SpringSeats??[]) seatBindings.Add(new(PathOf(binding.SpringPath),PathOf(binding.DamperPath)));
            if(!active.Add(a.Identity.AssetId)) throw new InvalidOperationException("Assembly cycle at "+path+" ("+a.Identity.AssetId+").");
            var ports=new Dictionary<string,Dictionary<string,ResolvedPort>>(StringComparer.Ordinal);
            foreach(var instance in a.Instances)
            {
                Segment(instance.Id);var childPath=path.Length==0?instance.Id:path+"/"+instance.Id;
                var transform=DesignTransform.From(instance.Placement).Then(parent);
                if(instance is ComponentInstance)
                {
                    var c=Fetch(instance.Definition,components,library.LoadComponent,x=>x.Identity);ComponentAuthoring.ValidateForPublish(c);
                    bodies.Add(new(childPath,c,transform));var mapped=new Dictionary<string,ResolvedPort>(StringComparer.Ordinal);
                    foreach(var port in c.Ports) {Segment(port.Id);mapped.Add(port.Id,new(childPath,port.Id,port.Role,port.SupportedJoints,transform.Frame(c.Frames.Single(x=>x.Id==port.FrameId).Frame)));}
                    ports.Add(instance.Id,mapped);
                }
                else
                {
                    var child=Fetch(instance.Definition,assemblies,library.LoadAssembly,x=>x.Identity);
                    ports.Add(instance.Id,Walk(child,childPath,transform));
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
        var exported=Walk(rootDefinition,"",DesignTransform.Identity);
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
        return new(rootDefinition,[..bodies],[..joints],mappedExports.ToImmutableDictionary(StringComparer.Ordinal),[..grounded.Order(StringComparer.Ordinal)],new(root.AssetId,dependencies),[..components.Values],[..assemblies.Values]) {Interfaces=interfaces.ToImmutableDictionary(StringComparer.Ordinal),ElasticElements=[..elements],SpringSeats=[..seatBindings]};
    }
    private static void CheckInterfaces(ResolvedAssemblyGraph old,ResolvedAssemblyGraph next)
    {
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
    }
    public static PortableDesignBundle Export(ResolvedAssemblyGraph graph)
    {
        if(graph.AppliedAdjustments.Length>0) throw new InvalidOperationException("Export the source assembly bundle and setup settings separately; runtime adjusted geometry is not source data.");
        if(graph.Root.Identity.RevisionId.Length!=64) throw new InvalidOperationException("Publish the root before exporting.");
        return new(DesignConventions.SchemaVersion,new(graph.Root.Identity.AssetId,graph.Root.Identity.RevisionId),graph.Components,graph.Assemblies,graph.Manifest);
    }
    public static ResolvedAssemblyGraph OpenBundle(PortableDesignBundle bundle)
    {
        if(bundle.SchemaVersion!=DesignConventions.SchemaVersion) throw new InvalidDataException("Unsupported bundle schema.");
        DesignValidation.Validate(bundle.Manifest);
        if(bundle.Root.AssetId!=bundle.Manifest.RootAssemblyId||bundle.Root.RevisionId is null) throw new InvalidDataException("Bundle root must be an exact frozen revision.");
        var source=new FrozenSource(bundle);var graph=new AssemblyGraphResolver(source).Compile(bundle.Root);
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
