using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using SuspensionSimulation.Components;

namespace SuspensionSimulation.Design;

/// <summary>New authoring contracts. Legacy vehicle packages remain separate adapters.</summary>
public static class DesignConventions
{
    public const string SchemaVersion = "1.0";
    public const double NewtonsPerMetrePerKgfPerMm = 9806.65;
    public const string Coordinates = "+X forward, +Y left, +Z up; metres, radians, newtons, kilograms";
}

public readonly record struct DesignVector(double X, double Y, double Z)
{
    [JsonIgnore] public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Z);
    [JsonIgnore] public double Length => Math.Sqrt(X*X+Y*Y+Z*Z);
    public double Dot(DesignVector other) => X*other.X+Y*other.Y+Z*other.Z;
    public static DesignVector operator -(DesignVector a, DesignVector b) => new(a.X-b.X,a.Y-b.Y,a.Z-b.Z);
    public static DesignVector operator +(DesignVector a, DesignVector b) => new(a.X+b.X,a.Y+b.Y,a.Z+b.Z);
    public static DesignVector operator *(DesignVector a, double b) => new(a.X*b,a.Y*b,a.Z*b);
    public static DesignVector operator /(DesignVector a, double b) => a*(1/b);
    public DesignVector Cross(DesignVector b)=>new(Y*b.Z-Z*b.Y,Z*b.X-X*b.Z,X*b.Y-Y*b.X);
}

// Axis is local +Z, reference direction is local +X; +Y is Z cross X.
public sealed record DesignFrame(DesignVector OriginM, DesignVector Axis, DesignVector ReferenceDirection);
public sealed record NamedDesignPoint(string Id, string Name, DesignVector PositionM);
public sealed record NamedDesignFrame(string Id, string Name, DesignFrame Frame);
public enum JointKind { Fixed, Revolute, Spherical, Prismatic, Cylindrical, Universal, RigidDistance }
public enum ComponentKind { RigidLink, Wishbone, Upright, TopMount, Structure, Spring, Damper, BumpStop, AntiRollBar, Rack, Wheel, Connector }
public enum AssetKind { Component, Assembly, Setup }
public sealed record DesignProvenance(DataProvenance Kind, string Note, string? Source);
public sealed record RevisionIdentity(string AssetId, string RevisionId, string Name, DesignProvenance Provenance,
    string SchemaVersion = DesignConventions.SchemaVersion);
public sealed record AttachmentPort(string Id, string FrameId, string Role, ImmutableArray<JointKind> SupportedJoints);
public sealed record ComponentMass(double MassKg, DesignVector CenterOfMassM);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(DrivingLength), "drivingLength")]
[JsonDerivedType(typeof(MeasuredDistance), "measuredDistance")]
public abstract record DesignDimension(string Id);
public sealed record DrivingLength(string Id, double LengthM) : DesignDimension(Id);
public sealed record MeasuredDistance(string Id, string StartPointId, string EndPointId) : DesignDimension(Id);

// Paths/force laws are added by their implementation stages, not guessed from a type name.
// A component may later compile into multiple rigid internal bodies (e.g. a telescopic shock).
public sealed record ComponentDefinition(RevisionIdentity Identity, ComponentKind Kind,
    ImmutableArray<NamedDesignPoint> Points, ImmutableArray<NamedDesignFrame> Frames,
    ImmutableArray<AttachmentPort> Ports, ImmutableArray<DesignDimension> Dimensions, ComponentMass? Mass)
{
    public RigidTemplateSpec? Template {get;init;}
    public ImmutableArray<VisualEdge> VisualEdges {get;init;}=[];
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public AxialRodGauge? AxialGauge {get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public RackSpecification? Rack {get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public MechanicalBehavior? Behavior {get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public MechanicalTemplate? MechanicalTemplate {get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public ImmutableArray<DefinitionParameter>? Parameters {get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public BumperMaterialCard? BumperMaterial {get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public WheelSpecification? Wheel {get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public ImmutableArray<GeometryAdjustment>? Adjustments {get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public ImmutableArray<AdjustmentParameterBinding>? AdjustmentParameters {get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public ImportedBumpCurveEvidence? BumpCurveEvidence {get;init;}
    [JsonIgnore] public bool IsRuntimeDerived {get;init;}
}
public sealed record RackSpecification(double TravelAtLockM,double SteeringWheelLockDegrees);
public sealed record WheelSpecification(double RollingRadiusM,double WidthM);
// A coordinate convention only, valid for a straight rod with spherical ends and no off-axis hardware.
public sealed record AxialRodGauge(string StartPointId,string EndPointId);

/// <summary>A null revision follows latest compatible published; frozen results always use exact revisions.</summary>
public sealed record AssetReference(string AssetId, string? RevisionId = null);
public sealed record MirrorPlane(DesignVector OriginM, DesignVector UnitNormal);
public sealed record InstancePlacement(DesignFrame Frame, MirrorPlane? Reflection = null);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ComponentInstance), "component")]
[JsonDerivedType(typeof(AssemblyInstance), "assembly")]
public abstract record PhysicalInstance(string Id, AssetReference Definition, InstancePlacement Placement)
{
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public ImmutableArray<ParameterAssignment>? ParameterValues {get;init;}
}
public sealed record ComponentInstance(string Id, AssetReference Definition, InstancePlacement Placement)
    : PhysicalInstance(Id, Definition, Placement);
public sealed record AssemblyInstance(string Id, AssetReference Definition, InstancePlacement Placement)
    : PhysicalInstance(Id, Definition, Placement);
public sealed record InstancePort(string InstanceId, string PortId);
public sealed record AssemblyJoint(string Id, JointKind Kind, InstancePort First, InstancePort Second)
{
    public double? DistanceM {get;init;}
}
public sealed record ExportedPort(string Id, InstancePort Source);
public sealed record AssemblyDefinition(RevisionIdentity Identity, ImmutableArray<PhysicalInstance> Instances,
    ImmutableArray<AssemblyJoint> Joints, ImmutableArray<ExportedPort> Ports)
{
    // Root-relative body or subtree paths. Nested definitions' local grounds are not world anchors.
    public ImmutableArray<string> GroundedInstances {get;init;}=[];
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public ImmutableArray<SpringSeatBinding>? SpringSeats {get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public VehicleGeometry? Vehicle {get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public ImmutableArray<EquipmentSlot>? EquipmentSlots {get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public ImmutableArray<SetupAdjustmentExposure>? SetupAdjustments {get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public ImmutableArray<DefinitionParameter>? Parameters {get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public int? ConfigurationVersion {get;init;}
}
// Slots replace direct children only. Compatibility is explicitly granted by the owning assembly.
public sealed record EquipmentSlot(string Id,string Name,string InstanceId,ImmutableArray<string> CompatibleAssetIds,string Description)
{
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public string? ParameterId {get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public ImmutableArray<EquipmentOption>? Options {get;init;}
}
public sealed record EquipmentOption(string ChoiceId,AssetReference Definition,ImmutableArray<ParameterAssignment> ParameterValues);
public sealed record AdjustmentParameterBinding(string AdjustmentId,string ParameterId);
// Null IDs expose all adjustments declared by the selected hardware; an empty list exposes none.
public sealed record SetupAdjustmentExposure(string InstancePath,string Name,ImmutableArray<string>? AdjustmentIds=null);
public sealed record ResolvedEquipmentSlot(string InstancePath,EquipmentSlot Definition,AssetKind Kind,AssetReference Default,AssetReference Selected);
public sealed record ResolvedSetupAdjustment(string InstancePath,string Name,GeometryAdjustment Definition);
public sealed record SpringSeatBinding(string SpringPath,string DamperPath);
public sealed record WheelGroundContact(string Corner,string InstancePath,string CenterPointId);
public sealed record VehicleGeometry(ImmutableArray<WheelGroundContact> Contacts,ImmutableArray<NamedDesignPoint> RideHeightDatums);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ContinuousAdjustmentSelection), "continuous")]
[JsonDerivedType(typeof(DiscreteAdjustmentSelection), "discrete")]
public abstract record AdjustmentSelection(string InstancePath, string AdjustmentId);
public sealed record ContinuousAdjustmentSelection(string InstancePath, string AdjustmentId, double Fraction)
    : AdjustmentSelection(InstancePath, AdjustmentId);
public sealed record DiscreteAdjustmentSelection(string InstancePath, string AdjustmentId, string ChoiceId)
    : AdjustmentSelection(InstancePath, AdjustmentId);
public sealed record EquipmentSelection(string InstancePath, string SlotId, AssetReference Component);
public enum MassAccountingPolicy { AggregateBase, ComponentSum }
public sealed record PayloadMass(string Id, double MassKg, DesignVector PositionM);
public sealed record SetupMass(MassAccountingPolicy Policy, double? AggregateBaseMassKg,
    DesignVector BaseCenterOfMassM, ImmutableArray<PayloadMass> AddedPayloads);

// Garage configures the imported complete-car assembly; alignment remains a solved output.
public sealed record GarageSetup(RevisionIdentity Identity, AssetReference RootAssembly,
    ImmutableArray<EquipmentSelection> Equipment, ImmutableArray<AdjustmentSelection> Adjustments, SetupMass Mass)
{
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public ImmutableArray<ParameterValue>? ParameterValues {get;init;}
    // Read-only compatibility for old documents/frozen replay; new saves use scoped ParameterValues.
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public ImmutableArray<InstanceMechanicalValue>? MechanicalValues {get;init;}
}
public sealed record ResolvedDependency(AssetKind Kind, string AssetId, string RevisionId, string Sha256);
public sealed record FrozenResultManifest(string RootAssemblyId, ImmutableArray<ResolvedDependency> Dependencies);

public static class DesignJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options)
        ?? throw new JsonException("A design document cannot be null.");
}
