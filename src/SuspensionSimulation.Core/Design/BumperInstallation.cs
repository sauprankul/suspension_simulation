namespace SuspensionSimulation.Design;

/// <summary>Install an axial bumper on two declared assembly mounting references.</summary>
public static class BumperInstallation
{
    public static AssemblyDefinition Add(AssemblyDefinition assembly,IDesignAssetSource source,AssetReference bumperReference,
        string instanceId,InstancePort firstMount,InstancePort secondMount,bool exposeInGarage)
    {
        if(string.IsNullOrWhiteSpace(instanceId)||instanceId.Contains('/')||instanceId.Contains(':')||instanceId.Contains('\0'))
            throw new ArgumentException("Choose a nonempty bumper instance ID without '/' or ':'.");
        if(assembly.Instances.Any(i=>i.Id==instanceId)) throw new ArgumentException("That instance ID is already used.");
        var bumper=source.LoadComponent(bumperReference.AssetId,bumperReference.RevisionId)??throw new ArgumentException("Select a published bumper curve.");
        if(bumper.Kind!=ComponentKind.BumpStop||bumper.Behavior is not BumpCurveBehavior curve) throw new ArgumentException("The selected component has no axial bumper curve.");
        ComponentAuthoring.ValidateForPublish(bumper);
        var resolver=new AssemblyGraphResolver(source);var graph=resolver.CompileDraft(assembly);
        ResolvedPort Mount(InstancePort endpoint)
        {
            if(endpoint.InstanceId.Contains('/')||!assembly.Instances.Any(i=>i.Id==endpoint.InstanceId))
                throw new ArgumentException("Choose a direct child's port or an exported port of a child assembly.");
            if(!graph.Interfaces.TryGetValue(endpoint.InstanceId+":"+endpoint.PortId,out var port)||!port.SupportedJoints.Contains(JointKind.Spherical))
                throw new ArgumentException("The mounting reference must support a spherical axial-element attachment.");
            return port;
        }
        var first=Mount(firstMount);var second=Mount(secondMount);
        if(first.BodyPath==second.BodyPath) throw new ArgumentException("Choose mounting references on different rigid supports.");
        var direction=second.Frame.OriginM-first.Frame.OriginM;
        if(direction.Length<1e-9) throw new ArgumentException("The mounting references must have a nonzero separation.");
        var axis=direction/direction.Length;var reference=Math.Abs(axis.X)<.9?new DesignVector(1,0,0):new DesignVector(0,1,0);reference-=axis*reference.Dot(axis);reference/=reference.Length;
        var localFirst=bumper.Frames.Single(f=>f.Id==bumper.Ports.Single(p=>p.Id==curve.FirstPortId).FrameId).Frame;
        var localSecond=bumper.Frames.Single(f=>f.Id==bumper.Ports.Single(p=>p.Id==curve.SecondPortId).FrameId).Frame;
        // Placement is a reference illustration only. Each force end follows its actual support independently.
        var localAxis=localSecond.OriginM-localFirst.OriginM;
        if(localAxis.Length<1e-9) throw new ArgumentException("The bumper's two local attachment references must differ.");
        localAxis/=localAxis.Length;
        var localReference=localFirst.ReferenceDirection-localAxis*localFirst.ReferenceDirection.Dot(localAxis);
        if(localReference.Length<1e-9) {localReference=Math.Abs(localAxis.X)<.9?new(1,0,0):new(0,1,0);localReference-=localAxis*localReference.Dot(localAxis);}
        localReference/=localReference.Length;var localY=localAxis.Cross(localReference);var y=axis.Cross(reference);
        DesignVector Rotate(DesignVector v)=>reference*v.Dot(localReference)+y*v.Dot(localY)+axis*v.Dot(localAxis);
        var placement=new InstancePlacement(new(first.Frame.OriginM-Rotate(localFirst.OriginM),Rotate(new(0,0,1)),Rotate(new(1,0,0))));
        var added=assembly with
        {
            Instances=assembly.Instances.Add(new ComponentInstance(instanceId,bumperReference,placement)),
            Joints=assembly.Joints.Add(new(instanceId+"-first",JointKind.Spherical,new(instanceId,curve.FirstPortId),firstMount))
                .Add(new(instanceId+"-second",JointKind.Spherical,new(instanceId,curve.SecondPortId),secondMount)),
            EquipmentSlots=exposeInGarage?(assembly.EquipmentSlots??[]).Add(new(instanceId,instanceId+" bumper",instanceId,[bumper.Identity.AssetId],
                "Axial bumper installed on declared mounting references. The Assembly Editor must explicitly approve any alternative bumper and its installation conditions.")):assembly.EquipmentSlots
        };
        _=resolver.CompileDraft(added);return added;
    }
}
