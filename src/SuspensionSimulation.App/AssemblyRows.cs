using SuspensionSimulation.Design;

namespace SuspensionSimulation.App;

public sealed class AssemblyInstanceRow
{
    public AssemblyInstanceRow() { }
    public string Id {get;set;}="part";
    public string Kind {get;set;}="Component";
    public string AssetId {get;set;}="";
    public string Revision {get;set;}="";
    public string Xmm {get;set;}="0";public string Ymm {get;set;}="0";public string Zmm {get;set;}="0";
    public string AxisX {get;set;}="0";public string AxisY {get;set;}="0";public string AxisZ {get;set;}="1";
    public string RefX {get;set;}="1";public string RefY {get;set;}="0";public string RefZ {get;set;}="0";
    public bool Mirror {get;set;}
    public string PlaneXmm {get;set;}="0";public string PlaneYmm {get;set;}="0";public string PlaneZmm {get;set;}="0";
    public string NormalX {get;set;}="0";public string NormalY {get;set;}="1";public string NormalZ {get;set;}="0";
    public PhysicalInstance Read()
    {
        var frame=new DesignFrame(new(DesignerNumbers.Metres(Xmm),DesignerNumbers.Metres(Ymm),DesignerNumbers.Metres(Zmm)),
            new(DesignerNumbers.Number(AxisX),DesignerNumbers.Number(AxisY),DesignerNumbers.Number(AxisZ)),new(DesignerNumbers.Number(RefX),DesignerNumbers.Number(RefY),DesignerNumbers.Number(RefZ)));
        var reflection=Mirror?new MirrorPlane(new(DesignerNumbers.Metres(PlaneXmm),DesignerNumbers.Metres(PlaneYmm),DesignerNumbers.Metres(PlaneZmm)),new(DesignerNumbers.Number(NormalX),DesignerNumbers.Number(NormalY),DesignerNumbers.Number(NormalZ))):null;
        var reference=new AssetReference(AssetId,string.IsNullOrWhiteSpace(Revision)?null:Revision);var placement=new InstancePlacement(frame,reflection);
        return Kind.Trim().ToLowerInvariant() switch {"component"=>new ComponentInstance(Id,reference,placement),"assembly"=>new AssemblyInstance(Id,reference,placement),_=>throw new ArgumentException("Instance Kind must be Component or Assembly.")};
    }
    public static AssemblyInstanceRow From(PhysicalInstance item)
    {
        static string N(double v)=>v.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
        var f=item.Placement.Frame;var m=item.Placement.Reflection;
        return new() {Id=item.Id,Kind=item is ComponentInstance?"Component":"Assembly",AssetId=item.Definition.AssetId,Revision=item.Definition.RevisionId??"",
            Xmm=DesignerNumbers.Mm(f.OriginM.X),Ymm=DesignerNumbers.Mm(f.OriginM.Y),Zmm=DesignerNumbers.Mm(f.OriginM.Z),AxisX=N(f.Axis.X),AxisY=N(f.Axis.Y),AxisZ=N(f.Axis.Z),RefX=N(f.ReferenceDirection.X),RefY=N(f.ReferenceDirection.Y),RefZ=N(f.ReferenceDirection.Z),
            Mirror=m is not null,PlaneXmm=DesignerNumbers.Mm(m?.OriginM.X??0),PlaneYmm=DesignerNumbers.Mm(m?.OriginM.Y??0),PlaneZmm=DesignerNumbers.Mm(m?.OriginM.Z??0),NormalX=N(m?.UnitNormal.X??0),NormalY=N(m?.UnitNormal.Y??1),NormalZ=N(m?.UnitNormal.Z??0)};
    }
}
public sealed class AssemblyJointRow
{
    public AssemblyJointRow() { }
    public string Id {get;set;}="joint";public string Kind {get;set;}="Spherical";
    public string FirstInstance {get;set;}="";public string FirstPort {get;set;}="";public string SecondInstance {get;set;}="";public string SecondPort {get;set;}="";
    public string DistanceMm {get;set;}="";
    public AssemblyJoint Read()=>new(Id,Enum.Parse<JointKind>(Kind,true),new(FirstInstance,FirstPort),new(SecondInstance,SecondPort)) {DistanceM=string.IsNullOrWhiteSpace(DistanceMm)?null:DesignerNumbers.Metres(DistanceMm)};
    public static AssemblyJointRow From(AssemblyJoint j)=>new(){Id=j.Id,Kind=j.Kind.ToString(),FirstInstance=j.First.InstanceId,FirstPort=j.First.PortId,SecondInstance=j.Second.InstanceId,SecondPort=j.Second.PortId,DistanceMm=j.DistanceM is {} d?DesignerNumbers.Mm(d):""};
}
public sealed class AssemblyPortRow
{
    public AssemblyPortRow() { }
    public string Id {get;set;}="port";public string Instance {get;set;}="";public string Port {get;set;}="";
    public ExportedPort Read()=>new(Id,new(Instance,Port));
    public static AssemblyPortRow From(ExportedPort p)=>new(){Id=p.Id,Instance=p.Source.InstanceId,Port=p.Source.PortId};
}
