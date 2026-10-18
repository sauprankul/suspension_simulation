using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;

namespace SuspensionSimulation.Design;

public enum FeaDiagnosticPartRole { Body,Fixture }
public sealed record FeaDiagnosticNode(int Id,DesignVector PositionM);
public sealed record FeaDiagnosticTriangle(string PartId,int A,int B,int C);
public sealed record FeaDiagnosticPart(string Id,string Name,FeaDiagnosticPartRole Role);
public sealed record FeaDiagnosticFrame(double NormalizedTime,double CompressionM,double ForceN,ImmutableArray<DesignVector> DisplacementsM);
public sealed record FeaDiagnosticCurvePoint(double CompressionM,double ForceN);
public sealed record FeaDiagnosticResult(string Schema,string Label,bool Complete,bool CurveAccepted,bool Synthetic,
    string SourceModelSha256,string InputFileSha256,string PlotFileSha256,double RequestedCompressionM,string Conditions,
    ImmutableArray<FeaDiagnosticNode> Nodes,ImmutableArray<FeaDiagnosticTriangle> Triangles,ImmutableArray<FeaDiagnosticPart> Parts,
    ImmutableArray<FeaDiagnosticFrame> Frames,ImmutableArray<FeaDiagnosticCurvePoint> Curve,ImmutableArray<string> Warnings);
public sealed record FeaDiagnosticReadout(double ForceN,double? TangentNPerM,double WorkFromFirstSampleJ);
public sealed record LoadedFeaDiagnostic(FeaDiagnosticResult Result,string FileSha256,string FilePath);

/// <summary>Read-only diagnostic data. Completion is not backend verification, calibration or publication authority.</summary>
public static class FeaDiagnosticResults
{
    public const string Schema="suspension-fea/diagnostic-result/v1";
    public const int FileLimitBytes=50_000_000;
    public static LoadedFeaDiagnostic Load(string path)
    {
        var info=new FileInfo(path);if(info.Length is <=0 or >FileLimitBytes) throw new InvalidDataException("Diagnostic JSON must be nonempty and at most 50 MB.");
        var bytes=File.ReadAllBytes(path);if(bytes.Length>FileLimitBytes) throw new InvalidDataException("Diagnostic file grew beyond the 50-MB limit.");
        using(var document=JsonDocument.Parse(bytes,new JsonDocumentOptions {MaxDepth=32}))
        {
            void Unique(JsonElement element)
            {
                if(element.ValueKind==JsonValueKind.Object) {var names=new HashSet<string>(StringComparer.Ordinal);foreach(var property in element.EnumerateObject()) {if(!names.Add(property.Name)) throw new InvalidDataException("Duplicate diagnostic JSON property: "+property.Name);Unique(property.Value);}}
                else if(element.ValueKind==JsonValueKind.Array) foreach(var child in element.EnumerateArray()) Unique(child);
            }
            Unique(document.RootElement);var root=document.RootElement;
            if(root.GetProperty("nodes").GetArrayLength()>100000||root.GetProperty("triangles").GetArrayLength()>250000||root.GetProperty("frames").GetArrayLength()>61) throw new InvalidDataException("Diagnostic node/triangle/frame budget exceeded.");
        }
        var result=DesignJson.Deserialize<FeaDiagnosticResult>(System.Text.Encoding.UTF8.GetString(bytes));Validate(result);
        return new(result,Convert.ToHexStringLower(SHA256.HashData(bytes)),Path.GetFullPath(path));
    }
    public static void Validate(FeaDiagnosticResult result)
    {
        void Need(bool condition,string message) {if(!condition) throw new ArgumentException(message);}
        bool Bounded(DesignVector value)=>value.IsFinite&&Math.Max(Math.Abs(value.X),Math.Max(Math.Abs(value.Y),Math.Abs(value.Z)))<=1000;
        bool FiniteForce(double value)=>double.IsFinite(value)&&Math.Abs(value)<=1e12;
        Need(result.Schema==Schema,"Unsupported diagnostic schema.");
        Need(!string.IsNullOrWhiteSpace(result.Label)&&result.Label.Length<=500&&!string.IsNullOrWhiteSpace(result.Conditions)&&result.Conditions.Length<=8000,"Diagnostic label/conditions are missing or too long.");
        Need(BumpStudioContracts.IsSha(result.SourceModelSha256)&&BumpStudioContracts.IsSha(result.InputFileSha256)&&BumpStudioContracts.IsSha(result.PlotFileSha256),"Diagnostic source/input/plot identities require SHA-256 values.");
        Need(!result.CurveAccepted||result.Complete,"A partial diagnostic cannot claim an accepted curve.");
        Need(double.IsFinite(result.RequestedCompressionM)&&result.RequestedCompressionM is >0 and <=10,"Requested shortening must be positive and inside the 10-m diagnostic envelope.");
        Need(result.Nodes.Length is >0 and <=100000&&result.Triangles.Length is >0 and <=250000&&result.Frames.Length is >0 and <=61&&result.Parts.Length is >0 and <=64&&result.Curve.Length is >0 and <=200000,"Diagnostic arrays exceed their bounded viewing budgets or are empty.");
        Need(result.Nodes.All(n=>n.Id>0&&Bounded(n.PositionM))&&result.Nodes.Select(n=>n.Id).Distinct().Count()==result.Nodes.Length,"Diagnostic solver node IDs/coordinates must be unique, positive, finite and bounded.");
        Need(result.Parts.All(p=>!string.IsNullOrWhiteSpace(p.Id)&&p.Id.Length<=120&&!string.IsNullOrWhiteSpace(p.Name)&&p.Name.Length<=500&&Enum.IsDefined(p.Role))&&result.Parts.Select(p=>p.Id).Distinct().Count()==result.Parts.Length&&result.Parts.Any(p=>p.Role==FeaDiagnosticPartRole.Body),"Diagnostic part identities/roles are invalid.");
        var nodes=result.Nodes.ToDictionary(n=>n.Id,n=>n.PositionM);var parts=result.Parts.Select(p=>p.Id).ToHashSet(StringComparer.Ordinal);var seen=new HashSet<(string,int,int,int)>();
        foreach(var triangle in result.Triangles)
        {
            Need(parts.Contains(triangle.PartId)&&nodes.ContainsKey(triangle.A)&&nodes.ContainsKey(triangle.B)&&nodes.ContainsKey(triangle.C)&&triangle.A!=triangle.B&&triangle.A!=triangle.C&&triangle.B!=triangle.C,"Diagnostic boundary triangle has a missing part/node or repeated corner.");
            var ids=new[]{triangle.A,triangle.B,triangle.C};Array.Sort(ids);Need(seen.Add((triangle.PartId,ids[0],ids[1],ids[2])),"Duplicate diagnostic boundary triangle.");
            Need((nodes[triangle.B]-nodes[triangle.A]).Cross(nodes[triangle.C]-nodes[triangle.A]).Length>1e-18,"Degenerate undeformed diagnostic boundary triangle.");
        }
        Need(parts.SetEquals(result.Triangles.Select(t=>t.PartId)),"Every diagnostic part must own a boundary surface.");
        for(var i=0;i<result.Curve.Length;i++)
        {
            var point=result.Curve[i];Need(double.IsFinite(point.CompressionM)&&point.CompressionM>=0&&point.CompressionM<=result.RequestedCompressionM+1e-9&&FiniteForce(point.ForceN),"Diagnostic reaction curve has invalid displacement/force.");
            if(i>0) Need(point.CompressionM>result.Curve[i-1].CompressionM,"Diagnostic reaction displacement samples must strictly increase; force reversals remain allowed.");
        }
        for(var i=0;i<result.Frames.Length;i++)
        {
            var frame=result.Frames[i];Need(double.IsFinite(frame.NormalizedTime)&&frame.NormalizedTime is >=0 and <=1&&double.IsFinite(frame.CompressionM)&&frame.CompressionM>=result.Curve[0].CompressionM-1e-9&&frame.CompressionM<=result.Curve[^1].CompressionM+1e-9&&FiniteForce(frame.ForceN),"Diagnostic converged frame time/shortening/force is invalid.");
            if(i>0) Need(frame.NormalizedTime>result.Frames[i-1].NormalizedTime&&frame.CompressionM>result.Frames[i-1].CompressionM,"Diagnostic converged frames must increase in time and shortening.");
            Need(frame.DisplacementsM.Length==result.Nodes.Length&&frame.DisplacementsM.All(Bounded),"Each diagnostic frame needs one bounded finite displacement per solver node, in the declared node order.");
            for(var n=0;n<result.Nodes.Length;n++) Need(Bounded(result.Nodes[n].PositionM+frame.DisplacementsM[n]),"Deformed diagnostic geometry exceeds its coordinate envelope.");
            var readout=Evaluate(result,frame.CompressionM);Need(Math.Abs(readout.ForceN-frame.ForceN)<=Math.Max(1e-5,Math.Max(Math.Abs(frame.ForceN),Math.Abs(readout.ForceN))*1e-6),"Frame force disagrees with the retained reaction curve.");
        }
        if(result.Complete) Need(Math.Abs(result.Frames[^1].NormalizedTime-1)<1e-7&&Math.Abs(result.Frames[^1].CompressionM-result.RequestedCompressionM)<1e-7&&Math.Abs(result.Curve[^1].CompressionM-result.RequestedCompressionM)<1e-7,"A complete diagnostic must reach its requested final stroke.");
        Need(result.Warnings.Length<=100&&result.Warnings.All(w=>!string.IsNullOrWhiteSpace(w)&&w.Length<=4000),"Diagnostic warning text exceeds its budget.");
    }
    public static FeaDiagnosticReadout Evaluate(FeaDiagnosticResult result,double compressionM)
    {
        if(!double.IsFinite(compressionM)||compressionM<result.Curve[0].CompressionM-1e-9||compressionM>result.Curve[^1].CompressionM+1e-9) throw new ArgumentOutOfRangeException(nameof(compressionM),"No extrapolation beyond retained converged diagnostic curve samples.");
        if(result.Curve.Length==1) return new(result.Curve[0].ForceN,null,0);
        var work=0d;
        for(var i=1;i<result.Curve.Length;i++)
        {
            var a=result.Curve[i-1];var b=result.Curve[i];var dx=Math.Clamp(compressionM-a.CompressionM,0,b.CompressionM-a.CompressionM);var tangent=(b.ForceN-a.ForceN)/(b.CompressionM-a.CompressionM);
            work+=a.ForceN*dx+.5*tangent*dx*dx;
            if(compressionM<=b.CompressionM+1e-12) return new(a.ForceN+tangent*dx,tangent,work);
        }
        var last=result.Curve[^1];var previous=result.Curve[^2];return new(last.ForceN,(last.ForceN-previous.ForceN)/(last.CompressionM-previous.CompressionM),work);
    }
}
