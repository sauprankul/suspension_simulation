using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace SuspensionSimulation.Design;

public enum StepLengthUnit { Millimetres, Metres }
public sealed record CadBounds(DesignVector MinimumM,DesignVector MaximumM);
public sealed record CadFace(int Tag,CadBounds Bounds,ImmutableArray<int> BoundaryCurveTags);
public sealed record CadNode(int Tag,DesignVector PositionM);
public sealed record CadTriangle(int FaceTag,int A,int B,int C);
public sealed record BumperCadInspection(string SourceSha256,string SourceFileName,string MesherSha256,StepLengthUnit SourceUnit,
    string TargetUnit,bool HealingApplied,double PreviewMeshSizeM,int VolumeTag,CadBounds Bounds,
    ImmutableArray<CadFace> Faces,ImmutableArray<CadNode> Nodes,ImmutableArray<CadTriangle> Triangles,ImmutableArray<string> Warnings,
    string SchemaVersion="1.0");
public enum BumperMaterialFamily { MicrocellularPolyurethane,SolidRubber,PrintedTpu,UserDefined }
public enum MaterialReadiness { NeedsCalibration,Estimated,Calibrated }
public enum PrintedGeometryRepresentation { ExplicitPrintedGeometry,DensePrintedSolid,EffectivePrintedStructure }
public sealed record BumperPrintRecipe(PrintedGeometryRepresentation Representation,string ManufacturerGrade,DesignVector LayerNormalInCad,
    double LayerHeightM,double NozzleDiameterM,double LineWidthM,int WallCount,double WallThicknessM,int TopLayers,int BottomLayers,
    string InfillPattern,double InfillPercent,string Processing,string Conditioning);
// A model key and its SI parameters are interpreted only by an independently verified worker adapter.
public sealed record BumperConstitutiveLaw(string ModelKey,ImmutableDictionary<string,double> ParametersSi,string StrainEnergyDefinition,string FitEvidenceSha256);
public sealed record BumperMaterialValidity(double MaximumCompressionStrain,string LoadingBranch,string TestConditions);
public sealed record BumperMaterialCard(string Id,string Name,BumperMaterialFamily Family,string Grade,double? DensityKgPerM3,double? ShoreA,
    MaterialReadiness Readiness,string Conditions,string SourceNote,ImmutableArray<string> EvidenceSha256,BumperConstitutiveLaw? Law=null,
    BumperPrintRecipe? PrintRecipe=null,string SchemaVersion="1.0")
{
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public BumperMaterialValidity? Validity {get;init;}
}
public enum RigidBumperFixtureKind { Plane,AnnularPlane,Cylinder }
public sealed record RigidBumperFixture(string Id,RigidBumperFixtureKind Kind,DesignFrame FrameInCad,double InnerRadiusM,double OuterRadiusM,double LengthM,bool Driven);
public sealed record BumperContactPair(string Id,ImmutableArray<int> SlaveFaceTags,ImmutableArray<int> MasterFaceTags,string? RigidFixtureId,double FrictionCoefficient,bool Bonded);
public sealed record BumperMeshSettings(double TargetElementSizeM,int ElementOrder,bool RequireThreeMeshConvergence);
// Frame +Z points from the lower fixture toward the upper fixture; the driven fixture compresses along -Z.
public sealed record BumperJobRecipe(string Name,string InspectionSha256,DesignFrame CompressionFrameInCad,double FreeLengthM,double RequestedCompressionM,
    ImmutableArray<BumperContactPair> Contacts,ImmutableArray<RigidBumperFixture> Fixtures,BumperMaterialCard Material,BumperMeshSettings Mesh,
    int BudgetSeconds=28800,long MemoryBudgetBytes=2147483648,int ThreadCount=4,string SchemaVersion="1.0");

public static class BumpStudioContracts
{
    public static string Hash<T>(T value)=>Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(DesignJson.Serialize(value))));
    public static bool IsSha(string value)=>value is {Length:64}&&value.All(Uri.IsHexDigit);
    public static IReadOnlyList<BumperMaterialCard> MaterialCatalog()
    {
        var cards=EstimatedBumperMaterials.Catalog().ToList();
        cards.Add(new("user-defined","User-defined material · Needs calibration",BumperMaterialFamily.UserDefined,"Unspecified",null,null,MaterialReadiness.NeedsCalibration,"Not supplied","Import a documented compatible material card with its raw-data evidence.",[]));return cards;
    }
    public static void ValidateInspection(BumperCadInspection model)
    {
        void Need(bool ok,string message) {if(!ok) throw new ArgumentException(message);}
        Need(model.SchemaVersion=="1.0"&&IsSha(model.SourceSha256)&&IsSha(model.MesherSha256),"Unsupported or incomplete CAD inspection identity.");
        Need(!model.HealingApplied&&model.TargetUnit=="m"&&Enum.IsDefined(model.SourceUnit),"The preparation path requires explicit STEP units, SI output and no healing.");
        Need(model.VolumeTag>0&&model.Faces.Length is >0 and <=4096&&model.Faces.Select(f=>f.Tag).Distinct().Count()==model.Faces.Length,"One volume with unique CAD face tags inside the 4096-face preview budget is required.");
        Need(model.Nodes.Length is >0 and <=250000&&model.Triangles.Length is >0 and <=500000,"The inspection tessellation is empty or exceeds the preview budget.");
        var nodes=model.Nodes.ToDictionary(n=>n.Tag,n=>n.PositionM);var faces=model.Faces.Select(f=>f.Tag).ToHashSet();
        Need(nodes.Values.All(p=>p.IsFinite)&&model.Bounds.MinimumM.IsFinite&&model.Bounds.MaximumM.IsFinite,"CAD coordinates must be finite.");
        bool Bounds(CadBounds b)=>b.MinimumM.IsFinite&&b.MaximumM.IsFinite&&b.MinimumM.X<=b.MaximumM.X&&b.MinimumM.Y<=b.MaximumM.Y&&b.MinimumM.Z<=b.MaximumM.Z;
        Need(Bounds(model.Bounds)&&model.Faces.All(f=>f.Tag>0&&Bounds(f.Bounds)&&f.BoundaryCurveTags.All(tag=>tag!=0)),"CAD face/bounds identities or extrema are invalid.");
        Need(double.IsFinite(model.PreviewMeshSizeM)&&model.PreviewMeshSizeM>0,"Preview size must be positive.");
        foreach(var triangle in model.Triangles) Need(faces.Contains(triangle.FaceTag)&&nodes.ContainsKey(triangle.A)&&nodes.ContainsKey(triangle.B)&&nodes.ContainsKey(triangle.C)&&(nodes[triangle.B]-nodes[triangle.A]).Cross(nodes[triangle.C]-nodes[triangle.A]).Length>1e-18,"Invalid or degenerate CAD-face tessellation.");
        var meshedFaces=model.Triangles.Select(t=>t.FaceTag).ToHashSet();Need(faces.SetEquals(meshedFaces),"Every selected CAD face must have inspection triangles.");
    }
    public static void ValidateMaterial(BumperMaterialCard material)
    {
        void Need(bool ok,string message) {if(!ok) throw new ArgumentException(message);}
        Need(material.SchemaVersion=="1.0"&&!string.IsNullOrWhiteSpace(material.Id)&&!string.IsNullOrWhiteSpace(material.Name)&&Enum.IsDefined(material.Family)&&Enum.IsDefined(material.Readiness),"Invalid material card identity.");
        if(material.Readiness==MaterialReadiness.Calibrated&&material.Law is {} estimatedLaw&&estimatedLaw.FitEvidenceSha256==Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(EstimatedBumperMaterials.Evidence(material.Id))))) throw new ArgumentException("An estimated preset source record cannot establish physical calibration.");
        Need(!string.IsNullOrWhiteSpace(material.Conditions)&&!string.IsNullOrWhiteSpace(material.SourceNote)&&material.EvidenceSha256.All(IsSha),"Material conditions/source/evidence are required.");
        Need((material.DensityKgPerM3 is null||double.IsFinite(material.DensityKgPerM3.Value)&&material.DensityKgPerM3.Value>0)&&(material.ShoreA is null||double.IsFinite(material.ShoreA.Value)&&material.ShoreA is >=0 and <=100),"Invalid density or hardness.");
        if(material.Law is {} law) Need(!string.IsNullOrWhiteSpace(law.ModelKey)&&!string.IsNullOrWhiteSpace(law.StrainEnergyDefinition)&&law.ParametersSi.Count>0&&law.ParametersSi.All(p=>!string.IsNullOrWhiteSpace(p.Key)&&double.IsFinite(p.Value))&&IsSha(law.FitEvidenceSha256)&&material.EvidenceSha256.Contains(law.FitEvidenceSha256),"A material law requires explicit SI parameters, energy definition and retained fitting evidence.");
        Need(material.Readiness==MaterialReadiness.NeedsCalibration||material.Law is not null&&material.EvidenceSha256.Length>0&&material.Validity is not null,"Estimated/calibrated cards require a law, supporting evidence and a declared use interval; hardness/density cannot supply coefficients.");
        if(material.Validity is {} validity) Need(double.IsFinite(validity.MaximumCompressionStrain)&&validity.MaximumCompressionStrain is >0 and <1&&!string.IsNullOrWhiteSpace(validity.LoadingBranch)&&!string.IsNullOrWhiteSpace(validity.TestConditions),"Material validity requires a finite compression interval, loading branch and actual test conditions.");
        if(material.PrintRecipe is {} p)
        {
            Need(material.Family==BumperMaterialFamily.PrintedTpu&&Enum.IsDefined(p.Representation),"Print recipe belongs to a printed material card.");
            Need(!string.IsNullOrWhiteSpace(p.ManufacturerGrade)&&!string.IsNullOrWhiteSpace(p.InfillPattern)&&!string.IsNullOrWhiteSpace(p.Processing)&&!string.IsNullOrWhiteSpace(p.Conditioning),"Record the grade, infill, processing and conditioning.");
            Need(p.LayerNormalInCad.IsFinite&&Math.Abs(p.LayerNormalInCad.Length-1)<1e-9,"Print layer normal must be a unit direction in the CAD frame.");
            Need(new[]{p.LayerHeightM,p.NozzleDiameterM,p.LineWidthM}.All(v=>double.IsFinite(v)&&v>0)&&double.IsFinite(p.WallThicknessM)&&p.WallThicknessM>=0&&p.WallCount>=0&&p.TopLayers>=0&&p.BottomLayers>=0&&double.IsFinite(p.InfillPercent)&&p.InfillPercent is >=0 and <=100,"Invalid print dimensions or infill percentage.");
        }
        Need(material.Family!=BumperMaterialFamily.PrintedTpu||material.PrintRecipe is not null,"Printed TPU requires its actual print recipe; it is not inferred from hardness.");
    }
    public static void ValidateRecipe(BumperJobRecipe recipe,BumperCadInspection model)
    {
        ValidateInspection(model);ValidateMaterial(recipe.Material);DesignValidation.Frame(recipe.CompressionFrameInCad);
        void Need(bool ok,string message) {if(!ok) throw new ArgumentException(message);}
        Need(recipe.SchemaVersion=="1.0"&&recipe.InspectionSha256==Hash(model)&&!string.IsNullOrWhiteSpace(recipe.Name),"Recipe must reference this exact CAD inspection.");
        Need(double.IsFinite(recipe.FreeLengthM)&&recipe.FreeLengthM>0&&double.IsFinite(recipe.RequestedCompressionM)&&recipe.RequestedCompressionM>0&&recipe.RequestedCompressionM<recipe.FreeLengthM,"Requested compression must lie inside the positive free length.");
        Need(recipe.Fixtures.Length>=2&&recipe.Fixtures.Select(f=>f.Id).Distinct().Count()==recipe.Fixtures.Length&&recipe.Fixtures.Count(f=>f.Driven)==1,"Declare separate supported/driven rigid fixtures with unique IDs.");
        foreach(var fixture in recipe.Fixtures)
        {
            DesignValidation.Frame(fixture.FrameInCad);Need(!string.IsNullOrWhiteSpace(fixture.Id)&&Enum.IsDefined(fixture.Kind),"Invalid fixture identity.");
            Need(double.IsFinite(fixture.InnerRadiusM)&&fixture.InnerRadiusM>=0&&double.IsFinite(fixture.OuterRadiusM)&&fixture.OuterRadiusM>fixture.InnerRadiusM&&double.IsFinite(fixture.LengthM)&&fixture.LengthM>=0,"Invalid rigid fixture dimensions.");
            Need(fixture.Kind!=RigidBumperFixtureKind.Cylinder||fixture.LengthM>0,"Shaft length must be positive.");
        }
        var tags=model.Faces.Select(f=>f.Tag).ToHashSet();Need(recipe.Contacts.Length>=2&&recipe.Contacts.Select(c=>c.Id).Distinct().Count()==recipe.Contacts.Length,"Declare contact pairs for the supporting and driven fixtures.");
        foreach(var pair in recipe.Contacts)
        {
            Need(pair.SlaveFaceTags.Length>0&&pair.SlaveFaceTags.All(tags.Contains)&&pair.SlaveFaceTags.Distinct().Count()==pair.SlaveFaceTags.Length&&pair.MasterFaceTags.All(tags.Contains),"Contact selection contains a missing or duplicate CAD face.");
            Need(pair.RigidFixtureId is not null?pair.MasterFaceTags.Length==0&&recipe.Fixtures.Any(f=>f.Id==pair.RigidFixtureId):pair.MasterFaceTags.Length>0&&!pair.MasterFaceTags.Intersect(pair.SlaveFaceTags).Any(),"Contact must target one rigid fixture or distinct CAD master faces.");
            Need(double.IsFinite(pair.FrictionCoefficient)&&pair.FrictionCoefficient>=0,"Contact friction must be finite and nonnegative.");
        }
        Need(recipe.Fixtures.Where(f=>f.Kind!=RigidBumperFixtureKind.Cylinder).All(f=>recipe.Contacts.Any(c=>c.RigidFixtureId==f.Id)),"Each platen needs its actual CAD-face contact selection.");
        Need(double.IsFinite(recipe.Mesh.TargetElementSizeM)&&recipe.Mesh.TargetElementSizeM>0&&recipe.Mesh.ElementOrder==2&&recipe.Mesh.RequireThreeMeshConvergence,"Production preparation requires a positive quadratic-mesh target and three-mesh convergence.");
        Need(recipe.BudgetSeconds is >0 and <=28800&&recipe.MemoryBudgetBytes>=67108864&&recipe.ThreadCount is >0 and <=4,"Worker budgets must be explicit and bounded (up to eight hours/four threads).");
    }
    public static void RequireVerifiedWorker(BumperJobRecipe recipe,BumperCadInspection model,IReadOnlySet<string> verifiedModelKeys,bool backendGatePassed)
    {
        ValidateRecipe(recipe,model);
        if(!backendGatePassed) throw new InvalidOperationException("The nonlinear folding/contact backend verification gate has not passed.");
        if(recipe.Material.Readiness==MaterialReadiness.NeedsCalibration||recipe.Material.Law is not {} law||!verifiedModelKeys.Contains(law.ModelKey)) throw new InvalidOperationException("This material needs calibration or has no independently verified compatible worker law.");
        if(recipe.Material.Validity is not {} validity||recipe.RequestedCompressionM/recipe.FreeLengthM>validity.MaximumCompressionStrain) throw new InvalidOperationException("Requested shortening exceeds the material's declared compression interval.");
    }
}
