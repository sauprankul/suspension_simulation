using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using SuspensionSimulation.Components;

namespace SuspensionSimulation.Design;

public enum CurveDisplacementUnit { Millimetres, Metres }
public enum CurveForceUnit { Newtons, Kilonewtons }
public sealed record ImportedBumpCurveEvidence(string CsvUtf8,CurveDisplacementUnit DisplacementUnit,CurveForceUnit ForceUnit,
    CurveZero Zero,double ContactSeparationM,string Conditions,string SourceNote,DataProvenance Provenance,string SchemaVersion="1.0")
{
    [JsonIgnore] public string RawSha256=>Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(CsvUtf8)));
}

/// <summary>Exact raw CSV and interpretation travel with the immutable component; no missing external evidence file.</summary>
public static class ImportedBumpCurve
{
    public static string Fingerprint(ImportedBumpCurveEvidence evidence)=>Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(DesignJson.Serialize(evidence))));
    public static ImmutableArray<BumpCurveSample> Parse(ImportedBumpCurveEvidence evidence)
    {
        if(evidence.SchemaVersion!="1.0"||!Enum.IsDefined(evidence.DisplacementUnit)||!Enum.IsDefined(evidence.ForceUnit)||!Enum.IsDefined(evidence.Zero)||!Enum.IsDefined(evidence.Provenance)) throw new ArgumentException("Unsupported curve schema or units.");
        if(!double.IsFinite(evidence.ContactSeparationM)||evidence.ContactSeparationM<=0||string.IsNullOrWhiteSpace(evidence.Conditions)||string.IsNullOrWhiteSpace(evidence.SourceNote)) throw new ArgumentException("Enter a positive contact separation, conditions and a source note.");
        if(evidence.CsvUtf8 is null||Encoding.UTF8.GetByteCount(evidence.CsvUtf8)>2_000_000) throw new ArgumentException("Import UTF-8 CSV of at most 2 MB.");
        var lines=evidence.CsvUtf8.Split('\n').Select(x=>x.Trim().TrimStart('\uFEFF')).Where(x=>x.Length>0&&!x.StartsWith('#')).ToArray();
        if(lines.Length<3||lines.Length>10001) throw new ArgumentException("CSV needs a header and 2–10000 samples.");
        var header=lines[0].ToLowerInvariant().Split(',').Select(x=>x.Trim()).ToArray();
        var displacement=evidence.DisplacementUnit==CurveDisplacementUnit.Millimetres?"mm":"m";
        var force=evidence.ForceUnit==CurveForceUnit.Newtons?"n":"kn";
        if(header.Length!=2||!new[]{"compression","displacement","compression_"+displacement,"displacement_"+displacement}.Contains(header[0])||!new[]{"force","force_"+force}.Contains(header[1]))
            throw new ArgumentException("Use two CSV columns: compression,force. Header unit suffixes must agree with the selected units.");
        var samples=new List<BumpCurveSample>();
        foreach(var (line,index) in lines.Skip(1).Select((line,index)=>(line,index)))
        {
            var values=line.Split(',');
            if(values.Length!=2||!double.TryParse(values[0],NumberStyles.Float,CultureInfo.InvariantCulture,out var x)||!double.TryParse(values[1],NumberStyles.Float,CultureInfo.InvariantCulture,out var y)||!double.IsFinite(x)||!double.IsFinite(y)) throw new ArgumentException($"CSV sample {index+1} must contain two finite numbers using a decimal point.");
            x*=evidence.DisplacementUnit==CurveDisplacementUnit.Millimetres?.001:1;y*=evidence.ForceUnit==CurveForceUnit.Kilonewtons?1000:1;
            if(!double.IsFinite(x)||!double.IsFinite(y)||x<0||y<0||samples.Count>0&&(x<=samples[^1].CompressionM||y<samples[^1].ForceN)) throw new ArgumentException($"CSV sample {index+1}: compression must strictly increase and force must be nonnegative/nondecreasing. No sorting, smoothing or extrapolation is applied.");
            samples.Add(new(x,y));
        }
        if(samples[0].CompressionM!=0||evidence.Zero==CurveZero.Contact&&samples[0].ForceN!=0) throw new ArgumentException("Start displacement at zero; a contact-origin curve must also start at zero force.");
        if(samples[^1].CompressionM>=evidence.ContactSeparationM) throw new ArgumentException("The curve range must retain positive attachment separation; check the contact datum and supported stroke.");
        return [..samples];
    }
    public static BumpCurveBehavior Behavior(ImportedBumpCurveEvidence evidence,string first="first",string second="second")
        =>new(first,second,evidence.ContactSeparationM,Parse(evidence),evidence.Zero,evidence.Conditions,Fingerprint(evidence));
    public static void Validate(ComponentDefinition component)
    {
        if(component.BumpCurveEvidence is not {} evidence) return;
        if(component.Kind!=ComponentKind.BumpStop||component.Behavior is not BumpCurveBehavior curve) throw new ArgumentException("Imported bump evidence belongs to a bump-stop curve.");
        var expected=Behavior(evidence,curve.FirstPortId,curve.SecondPortId);
        if(curve.EvidenceSha256!=expected.EvidenceSha256||curve.ContactLengthM!=expected.ContactLengthM||curve.Zero!=expected.Zero||curve.Conditions!=expected.Conditions||!curve.Samples.SequenceEqual(expected.Samples)) throw new ArgumentException("Curve behavior differs from its frozen raw source/units/conditions.");
    }
}
