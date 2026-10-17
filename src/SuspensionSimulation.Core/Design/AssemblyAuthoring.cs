using System.Collections.Immutable;

namespace SuspensionSimulation.Design;

public sealed record AssemblyGroup(AssemblyDefinition Parent,AssemblyDefinition Child);
public static class AssemblyAuthoring
{
    public static InstancePlacement IdentityPlacement {get;}=new(new(new(0,0,0),new(0,0,1),new(1,0,0)));
    /// <summary>Group in the parent's frame. Cross-boundary connections are exported and rewired without changing geometry.</summary>
    public static AssemblyGroup Group(AssemblyDefinition parent,IEnumerable<string> selected,RevisionIdentity childIdentity,string instanceId)
    {
        var ids=selected.ToHashSet(StringComparer.Ordinal);
        if(ids.Count==0||ids.Any(id=>!parent.Instances.Any(x=>x.Id==id))) throw new ArgumentException("Select existing instances to group.");
        if(parent.Instances.Any(x=>x.Id==instanceId)) throw new ArgumentException("The group instance ID is already used.");
        var exports=new Dictionary<InstancePort,string>();
        InstancePort Map(InstancePort endpoint)
        {
            if(!ids.Contains(endpoint.InstanceId)) return endpoint;
            if(!exports.TryGetValue(endpoint,out var id)) {id="port-"+(exports.Count+1);exports.Add(endpoint,id);}
            return new(instanceId,id);
        }
        var internalJoints=parent.Joints.Where(x=>ids.Contains(x.First.InstanceId)&&ids.Contains(x.Second.InstanceId)).ToImmutableArray();
        var outsideJoints=parent.Joints.Except(internalJoints).Select(x=>x with {First=Map(x.First),Second=Map(x.Second)}).ToImmutableArray();
        var parentPorts=parent.Ports.Select(x=>x with {Source=Map(x.Source)}).ToImmutableArray();
        bool Inside(string path)=>ids.Any(id=>path==id||path.StartsWith(id+"/",StringComparison.Ordinal));
        string GroupedPath(string path)=>Inside(path)?instanceId+"/"+path:path;
        var childSeats=(parent.SpringSeats??[]).Where(s=>Inside(s.SpringPath)&&Inside(s.DamperPath)).ToImmutableArray();
        var parentSeats=(parent.SpringSeats??[]).Except(childSeats).Select(s=>new SpringSeatBinding(GroupedPath(s.SpringPath),GroupedPath(s.DamperPath))).ToImmutableArray();
        var childSlots=(parent.EquipmentSlots??[]).Where(s=>ids.Contains(s.InstanceId)).ToImmutableArray();
        var childAdjustments=(parent.SetupAdjustments??[]).Where(s=>Inside(s.InstancePath)).ToImmutableArray();
        var child=new AssemblyDefinition(childIdentity,[..parent.Instances.Where(x=>ids.Contains(x.Id))],internalJoints,[..exports.Select(x=>new ExportedPort(x.Value,x.Key))]) {SpringSeats=childSeats.IsEmpty?null:childSeats,EquipmentSlots=parent.EquipmentSlots is null?null:childSlots,SetupAdjustments=parent.SetupAdjustments is null?null:childAdjustments};
        var instances=parent.Instances.Where(x=>!ids.Contains(x.Id)).Append(new AssemblyInstance(instanceId,new(childIdentity.AssetId),IdentityPlacement)).ToImmutableArray();
        var grounded=parent.GroundedInstances.Select(path=>ids.Any(id=>path==id||path.StartsWith(id+"/",StringComparison.Ordinal))?instanceId+"/"+path:path).ToImmutableArray();
        return new(parent with {Instances=instances,Joints=outsideJoints,Ports=parentPorts,GroundedInstances=grounded,SpringSeats=parentSeats.IsEmpty?null:parentSeats,
            EquipmentSlots=parent.EquipmentSlots is {} slots?[..slots.Except(childSlots)]:null,SetupAdjustments=parent.SetupAdjustments is {} adjustments?[..adjustments.Except(childAdjustments)]:null,
            Vehicle=parent.Vehicle is {} vehicle?vehicle with {Contacts=[..vehicle.Contacts.Select(c=>c with {InstancePath=GroupedPath(c.InstancePath)})]}:null},child);
    }
    public static AssemblyDefinition MirrorInstance(AssemblyDefinition assembly,string sourceId,string newId,MirrorPlane plane)
    {
        if(assembly.Instances.Any(x=>x.Id==newId)) throw new ArgumentException("Mirror instance ID is already used.");
        var source=assembly.Instances.Single(x=>x.Id==sourceId);
        if(source.Placement.Reflection is not null) throw new ArgumentException("Group an already mirrored instance before mirroring it again.");
        var placement=source.Placement with {Reflection=plane};
        PhysicalInstance mirror=source switch {ComponentInstance c=>c with {Id=newId,Placement=placement},AssemblyInstance a=>a with {Id=newId,Placement=placement},_=>throw new ArgumentException("Unknown instance kind.")};
        return assembly with {Instances=assembly.Instances.Add(mirror)};
    }
}
