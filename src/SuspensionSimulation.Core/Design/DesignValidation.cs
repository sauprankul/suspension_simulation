using System.Collections.Immutable;

namespace SuspensionSimulation.Design;

/// <summary>Structural authoring checks only; success is not mechanism mobility or physical validity.</summary>
public static class DesignValidation
{
    public static void Validate(ComponentDefinition component)
    {
        Identity(component.Identity);
        Unique(component.Points, x => x.Id);Unique(component.Frames, x => x.Id);
        Unique(component.Ports, x => x.Id);Unique(component.Dimensions, x => x.Id);
        foreach(var point in component.Points) Require(point.PositionM.IsFinite,"Point coordinates must be finite.");
        foreach(var frame in component.Frames) Frame(frame.Frame);
        foreach(var port in component.Ports)
        {
            Require(component.Frames.Any(x => x.Id == port.FrameId),$"Port {port.Id} refers to missing frame {port.FrameId}.");
            Text(port.Role);Require(!port.SupportedJoints.IsDefaultOrEmpty,"A port must declare joint capabilities.");
            Require(port.SupportedJoints.All(Enum.IsDefined),"Unknown joint capability.");
        }
        Require(Enum.IsDefined(component.Kind),"Unknown component kind.");
        foreach(var dimension in component.Dimensions)
            switch(dimension)
            {
                case DrivingLength driving: Positive(driving.LengthM,"Driving length");break;
                case MeasuredDistance measured:
                    Require(component.Points.Any(x => x.Id == measured.StartPointId) && component.Points.Any(x => x.Id == measured.EndPointId),
                        $"Measurement {measured.Id} has an unresolved point.");break;
                default: throw new ArgumentException("Unsupported dimension kind.");
            }
        if(component.Mass is {} mass) {Require(double.IsFinite(mass.MassKg)&&mass.MassKg>=0,"Component mass must be finite and nonnegative (zero explicitly identifies a massless proxy).");Require(mass.CenterOfMassM.IsFinite,"Invalid mass datum.");}
        if(component.Wheel is {} wheel) {Require(component.Kind==ComponentKind.Wheel,"Wheel dimensions belong to a wheel component.");Positive(wheel.RollingRadiusM,"Rolling radius");Positive(wheel.WidthM,"Wheel width");}
        else Require(component.Kind!=ComponentKind.Wheel,"Wheel contact dimensions are missing.");
        if(component.Rack is {} rack) {Require(component.Kind==ComponentKind.Rack,"Rack specification belongs to a rack.");Positive(rack.TravelAtLockM,"Rack travel");Positive(rack.SteeringWheelLockDegrees,"Steering wheel lock angle");}
        MechanicalValidation.Validate(component);AdjustmentPaths.Validate(component);
        ImportedBumpCurve.Validate(component);
        if(component.AxialGauge is {} gauge)
        {
            Require(component.Kind==ComponentKind.RigidLink&&component.Ports.Length==2,"Axial spin gauges require a two-port rigid rod.");
            var a=component.Points.Single(x=>x.Id==gauge.StartPointId).PositionM;var b=component.Points.Single(x=>x.Id==gauge.EndPointId).PositionM;var direction=b-a;
            Require(direction.Length>1e-9,"Axial gauge endpoints must differ.");
            foreach(var point in component.Points.Select(x=>x.PositionM).Concat(component.Frames.Select(x=>x.Frame.OriginM)).Concat(component.Mass is {} m?new[]{m.CenterOfMassM}:[]))
                Require((point-a).Cross(direction).Length/direction.Length<1e-9,"An axial rod gauge cannot hide off-axis geometry or mass.");
            foreach(var port in component.Ports) Require(port.SupportedJoints.All(x=>x==JointKind.Spherical),"Axial gauge ports support spherical joints only.");
            var centers=component.Ports.Select(p=>component.Frames.Single(x=>x.Id==p.FrameId).Frame.OriginM).ToArray();
            Require(centers.Any(x=>(x-a).Length<1e-9)&&centers.Any(x=>(x-b).Length<1e-9),"Axial gauge ports must occupy the rod endpoints.");
        }
    }

    public static void Validate(AssemblyDefinition assembly)
    {
        Identity(assembly.Identity);Unique(assembly.Instances,x => x.Id);Unique(assembly.Joints,x => x.Id);Unique(assembly.Ports,x => x.Id);
        foreach(var instance in assembly.Instances)
        {
            Reference(instance.Definition);Frame(instance.Placement.Frame);
            Require(instance is ComponentInstance or AssemblyInstance,"Unknown physical instance kind.");
            if(instance is AssemblyInstance) Require(instance.Definition.AssetId != assembly.Identity.AssetId,"Assembly directly references itself.");
            if(instance.Placement.Reflection is {} mirror)
            {Require(mirror.OriginM.IsFinite,"Invalid mirror origin.");Unit(mirror.UnitNormal);}
        }
        void Endpoint(InstancePort endpoint)
        {Require(assembly.Instances.Any(x => x.Id == endpoint.InstanceId),$"Missing instance {endpoint.InstanceId}.");Text(endpoint.PortId);}
        foreach(var joint in assembly.Joints)
        {
            Require(Enum.IsDefined(joint.Kind),"Unknown joint kind.");Endpoint(joint.First);Endpoint(joint.Second);
            Require(joint.First.InstanceId != joint.Second.InstanceId,"An assembly joint must connect distinct instances.");
            if(joint.DistanceM is {} distance) {Require(joint.Kind==JointKind.RigidDistance,"Rest distance belongs only to a distance joint.");Positive(distance,"Joint rest distance");}
        }
        foreach(var port in assembly.Ports) Endpoint(port.Source);
        Unique(assembly.EquipmentSlots??[],x=>x.Id);Unique(assembly.EquipmentSlots??[],x=>x.InstanceId);
        foreach(var slot in assembly.EquipmentSlots??[])
        {
            Text(slot.Name);Text(slot.Description);Unique(slot.CompatibleAssetIds,x=>x);
            var instance=assembly.Instances.SingleOrDefault(x=>x.Id==slot.InstanceId);
            Require(instance is not null,$"Equipment slot {slot.Id} has no child instance.");
            Require(slot.CompatibleAssetIds.Contains(instance!.Definition.AssetId),$"Slot {slot.Id} must accept its default hardware.");
        }
        Unique(assembly.SetupAdjustments??[],x=>x.InstancePath);
        foreach(var exposure in assembly.SetupAdjustments??[]) {Text(exposure.Name);if(exposure.AdjustmentIds is {} ids) Unique(ids,x=>x);}
        Unique(assembly.GroundedInstances,x=>x);
        if(assembly.Vehicle is {} vehicle)
        {Unique(vehicle.Contacts,c=>c.Corner);Unique(vehicle.RideHeightDatums,p=>p.Id);foreach(var contact in vehicle.Contacts) {Text(contact.InstancePath);Text(contact.CenterPointId);}foreach(var datum in vehicle.RideHeightDatums) Require(datum.PositionM.IsFinite,"Ride-height datum must be finite.");}
        // Transitive cycles, resolved port compatibility, axes/limits and Jacobian rank belong to graph compilation.
    }

    public static void Validate(GarageSetup setup)
    {
        Identity(setup.Identity);Reference(setup.RootAssembly);
        if(setup.MechanicalValues is {} mechanical) InstanceMechanicalValidation.Validate(mechanical);
        Unique(setup.Equipment,x => x.InstancePath+"\0"+x.SlotId);
        Unique(setup.Adjustments,x => x.InstancePath+"\0"+x.AdjustmentId);
        foreach(var equipment in setup.Equipment)
        {Text(equipment.InstancePath);Text(equipment.SlotId);Reference(equipment.Component);}
        foreach(var adjustment in setup.Adjustments)
        {
            Text(adjustment.InstancePath);Text(adjustment.AdjustmentId);
            switch(adjustment)
            {
                case ContinuousAdjustmentSelection continuous:
                    Require(double.IsFinite(continuous.Fraction) && continuous.Fraction is >= 0 and <= 1,"Adjustment fraction must be in [0,1].");break;
                case DiscreteAdjustmentSelection discrete: Text(discrete.ChoiceId);break;
                default: throw new ArgumentException("Unsupported adjustment selection.");
            }
        }
        Require(Enum.IsDefined(setup.Mass.Policy),"Unknown mass policy.");
        if(setup.Mass.Policy == MassAccountingPolicy.AggregateBase)
        {Require(setup.Mass.AggregateBaseMassKg is not null,"Aggregate base mass is required.");Positive(setup.Mass.AggregateBaseMassKg!.Value,"Base mass");}
        else Require(setup.Mass.AggregateBaseMassKg is null,"Component-sum mass cannot also include an aggregate base mass.");
        Require(setup.Mass.BaseCenterOfMassM.IsFinite,"Invalid base CG datum.");Unique(setup.Mass.AddedPayloads,x => x.Id);
        foreach(var payload in setup.Mass.AddedPayloads) {Positive(payload.MassKg,"Payload mass");Require(payload.PositionM.IsFinite,"Invalid payload datum.");}
        // Exposed IDs, selected equipment compatibility and allowed settings must still resolve against the assembly.
    }

    public static void Validate(FrozenResultManifest manifest)
    {
        Text(manifest.RootAssemblyId);Unique(manifest.Dependencies,x => x.Kind+"\0"+x.AssetId);
        Require(manifest.Dependencies.Any(x => x.Kind == AssetKind.Assembly && x.AssetId == manifest.RootAssemblyId),"Frozen root assembly is missing.");
        foreach(var dependency in manifest.Dependencies)
        {
            Require(Enum.IsDefined(dependency.Kind),"Unknown asset kind.");Text(dependency.AssetId);Text(dependency.RevisionId);
            Require(dependency.Sha256 is {Length:64} && dependency.Sha256.All(Uri.IsHexDigit),"Frozen dependencies require a SHA-256 content hash.");
        }
    }

    public static void Frame(DesignFrame frame)
    {
        Require(frame.OriginM.IsFinite,"Invalid frame origin.");Unit(frame.Axis);Unit(frame.ReferenceDirection);
        Require(Math.Abs(frame.Axis.Dot(frame.ReferenceDirection)) < 1e-9,"Frame axis and reference direction must be orthogonal.");
    }
    private static void Identity(RevisionIdentity identity)
    {
        Text(identity.AssetId);Text(identity.RevisionId);Text(identity.Name);Text(identity.Provenance.Note);
        Require(Enum.IsDefined(identity.Provenance.Kind),"Unknown provenance.");
        Require(identity.SchemaVersion == DesignConventions.SchemaVersion,"Unsupported design schema version.");
    }
    private static void Reference(AssetReference reference) {Text(reference.AssetId);if(reference.RevisionId is {} revision) Text(revision);}
    private static void Unit(DesignVector value) => Require(value.IsFinite && Math.Abs(value.Length-1) < 1e-9,"Frame/mirror directions must be unit vectors.");
    private static void Positive(double value,string name) => Require(double.IsFinite(value) && value > 0,name+" must be finite and positive.");
    private static void Text(string value) => Require(!string.IsNullOrWhiteSpace(value),"An ID, name or provenance note is missing.");
    private static void Unique<T>(ImmutableArray<T> items,Func<T,string> id)
    {
        Require(!items.IsDefault,"A collection is missing.");var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var item in items) {var key=id(item);Text(key);Require(seen.Add(key),$"Duplicate ID {key}.");}
    }
    private static void Require(bool condition,string message) {if(!condition) throw new ArgumentException(message);}
}
