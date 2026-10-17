using System.Globalization;
using SuspensionSimulation.Design;

namespace SuspensionSimulation.App;

internal sealed class DesignerPointRow
{
    public DesignerPointRow() { }
    public string Id {get;set;}="point";public string Name {get;set;}="Point";
    public string Xmm {get;set;}="0";public string Ymm {get;set;}="0";public string Zmm {get;set;}="0";
    internal NamedDesignPoint Read()=>new(Id,Name,new(DesignerNumbers.Metres(Xmm),DesignerNumbers.Metres(Ymm),DesignerNumbers.Metres(Zmm)));
    internal static DesignerPointRow From(NamedDesignPoint p)=>new(){Id=p.Id,Name=p.Name,Xmm=DesignerNumbers.Mm(p.PositionM.X),Ymm=DesignerNumbers.Mm(p.PositionM.Y),Zmm=DesignerNumbers.Mm(p.PositionM.Z)};
}
internal sealed class DesignerFrameRow
{
    public DesignerFrameRow() { }
    public string Id {get;set;}="frame";public string Name {get;set;}="Frame";
    public string Xmm {get;set;}="0";public string Ymm {get;set;}="0";public string Zmm {get;set;}="0";
    public string AxisX {get;set;}="0";public string AxisY {get;set;}="0";public string AxisZ {get;set;}="1";
    public string ReferenceX {get;set;}="1";public string ReferenceY {get;set;}="0";public string ReferenceZ {get;set;}="0";
    internal NamedDesignFrame Read()=>new(Id,Name,new(new(DesignerNumbers.Metres(Xmm),DesignerNumbers.Metres(Ymm),DesignerNumbers.Metres(Zmm)),
        new(DesignerNumbers.Number(AxisX),DesignerNumbers.Number(AxisY),DesignerNumbers.Number(AxisZ)),new(DesignerNumbers.Number(ReferenceX),DesignerNumbers.Number(ReferenceY),DesignerNumbers.Number(ReferenceZ))));
    internal static DesignerFrameRow From(NamedDesignFrame f)=>new(){Id=f.Id,Name=f.Name,Xmm=DesignerNumbers.Mm(f.Frame.OriginM.X),Ymm=DesignerNumbers.Mm(f.Frame.OriginM.Y),Zmm=DesignerNumbers.Mm(f.Frame.OriginM.Z),
        AxisX=DesignerNumbers.Text(f.Frame.Axis.X),AxisY=DesignerNumbers.Text(f.Frame.Axis.Y),AxisZ=DesignerNumbers.Text(f.Frame.Axis.Z),ReferenceX=DesignerNumbers.Text(f.Frame.ReferenceDirection.X),ReferenceY=DesignerNumbers.Text(f.Frame.ReferenceDirection.Y),ReferenceZ=DesignerNumbers.Text(f.Frame.ReferenceDirection.Z)};
}
internal sealed class DesignerPortRow
{
    public DesignerPortRow() { }
    public string Id {get;set;}="port";public string FrameId {get;set;}="frame";public string Role {get;set;}="attachment";public string Joints {get;set;}="Spherical";
    internal AttachmentPort Read()=>new(Id,FrameId,Role,[..Joints.Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries).Select(x=>Enum.Parse<JointKind>(x,true))]);
    internal static DesignerPortRow From(AttachmentPort p)=>new(){Id=p.Id,FrameId=p.FrameId,Role=p.Role,Joints=string.Join(',',p.SupportedJoints)};
}
internal sealed class DesignerEdgeRow
{
    public DesignerEdgeRow() { }
    public string Id {get;set;}="edge";public string StartPointId {get;set;}="inner";public string EndPointId {get;set;}="outer";
}
internal static class DesignerNumbers
{
    internal static string Text(double value)=>value.ToString("G12",CultureInfo.InvariantCulture);
    internal static string Mm(double metres)=>Text(metres*1000);
    internal static double Number(string value)=>double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out var v)&&double.IsFinite(v)?v:throw new ArgumentException("Enter a finite number using a decimal point.");
    internal static double Metres(string value)=>Number(value)/1000;
}
