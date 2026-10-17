using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using SuspensionSimulation.Design;

namespace SuspensionSimulation.Solvers.Design;

/// <summary>External pinned CAD import and inspection tessellation only; never a volume FEA mesh or a solve.</summary>
public sealed class StepBumperPreparation(string repository,string libraryRoot)
{
    public const string GmshSha256="317c43391e5b1fab3a1dd80dc5245dad6e2d087910f4b8ebc234bd6d4b8f41a1";
    private static readonly SemaphoreSlim ImportWorker=new(1,1);
    public string Root {get;}=Path.Combine(libraryRoot,"bump-studio");
    public string InspectionPath(string hash)=>Path.Combine(Root,"inspections",CheckedSha(hash)+".json");
    public string SourcePath(string hash)=>Path.Combine(Root,"sources",CheckedSha(hash)+".step");
    public string RecipePath(string hash)=>Path.Combine(Root,"recipes",CheckedSha(hash)+".json");
    public string MaterialEvidencePath(string hash)=>Path.Combine(Root,"material-evidence",CheckedSha(hash));
    public string PreserveMaterialEvidence(string file)
    {var bytes=File.ReadAllBytes(file);if(bytes.Length is 0 or >20000000) throw new ArgumentException("Material evidence must be nonempty and at most 20 MB per artifact.");var hash=BytesHash(bytes);ImmutableWrite(MaterialEvidencePath(hash),bytes);return hash;}
    private void CheckMaterialEvidence(BumperMaterialCard card)
    {foreach(var hash in card.EvidenceSha256) {var file=MaterialEvidencePath(hash);if(!File.Exists(file)||BytesHash(File.ReadAllBytes(file))!=hash) throw new InvalidDataException("Retain the original material evidence artifact with SHA-256 "+hash+" before saving this material card.");}}
    private static string CheckedSha(string hash)=>BumpStudioContracts.IsSha(hash)?hash:throw new ArgumentException("Invalid content hash.");
    private static string BytesHash(byte[] bytes)=>Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static void ImmutableWrite(string path,byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if(File.Exists(path)) {if(!File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes)) throw new InvalidDataException("Stored content-addressed artifact changed.");return;}
        var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";try {File.WriteAllBytes(temp,bytes);File.Move(temp,path);}finally {if(File.Exists(temp)) File.Delete(temp);}
    }
    public static StepLengthUnit DeclaredUnit(byte[] bytes)
    {
        if(bytes.Length is 0 or >50000000) throw new ArgumentException("STEP must be nonempty and at most 50 MB for this preparation path.");
        var text=Encoding.Latin1.GetString(bytes);
        if(!text.TrimStart('\uFEFF',' ','\r','\n','\t').StartsWith("ISO-10303-21;",StringComparison.Ordinal)||!text.Contains("END-ISO-10303-21;",StringComparison.Ordinal)) throw new ArgumentException("A complete STEP Part21 solid file is required.");
        var units=new HashSet<StepLengthUnit>();
        foreach(Match entity in Regex.Matches(text,@"#[0-9]+\s*=\s*(.*?);",RegexOptions.Singleline))
        {
            var body=entity.Groups[1].Value;if(!body.Contains("LENGTH_UNIT()",StringComparison.Ordinal)) continue;
            var match=Regex.Match(body,@"SI_UNIT\(\s*(\$|\.[A-Z]+\.)\s*,\s*\.METRE\.\s*\)");
            if(!match.Success) throw new ArgumentException("This importer currently supports explicit STEP millimetre/metre SI length units. Re-export unsupported/ambiguous units; no scale is guessed.");
            units.Add(match.Groups[1].Value switch {".MILLI."=>StepLengthUnit.Millimetres,"$"=>StepLengthUnit.Metres,_=>throw new ArgumentException("Re-export STEP in millimetres or metres; this unit prefix is not supported yet.")});
        }
        return units.Count==1?units.Single():throw new ArgumentException("STEP length units are absent or inconsistent. Re-export with one explicit millimetre/metre unit system.");
    }
    public async Task<BumperCadInspection> ImportAsync(string path,StepLengthUnit confirmedSourceUnit,double previewSizeM,CancellationToken cancellation=default)
    {
        if(!double.IsFinite(previewSizeM)||previewSizeM<1e-6||previewSizeM>1) throw new ArgumentException("Preview element size must lie between 0.001 and 1000 mm.");
        var bytes=await File.ReadAllBytesAsync(path,cancellation);var unit=DeclaredUnit(bytes);
        if(unit!=confirmedSourceUnit) throw new ArgumentException("The selected source units disagree with the STEP length metadata. Check the source/export; no scale was changed.");
        var hash=BytesHash(bytes);ImmutableWrite(SourcePath(hash),bytes);
        await ImportWorker.WaitAsync(cancellation);
        try
        {
            cancellation.ThrowIfCancellationRequested();var gmsh=Environment.GetEnvironmentVariable("SUSPENSION_GMSH_EXE")??Path.Combine(repository,"artifacts/fea-toolchain/gmsh-4.15.2/gmsh-4.15.2-Windows64/gmsh.exe");
            if(!File.Exists(gmsh)||BytesHash(await File.ReadAllBytesAsync(gmsh,cancellation))!=GmshSha256) throw new InvalidOperationException("Install the pinned external Gmsh 4.15.2 executable (or set SUSPENSION_GMSH_EXE). Its reviewed executable hash must match.");
            var folder=Path.Combine(Root,"imports",hash,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);File.WriteAllBytes(Path.Combine(folder,"input.step"),bytes);
            var size=previewSizeM.ToString("R",CultureInfo.InvariantCulture);
            var geo="""
                SetFactory("OpenCASCADE");
                Geometry.OCCTargetUnit="M";
                Geometry.OCCFixDegenerated=0;
                Geometry.OCCFixSmallEdges=0;
                Geometry.OCCFixSmallFaces=0;
                Geometry.OCCSewFaces=0;
                Geometry.OCCMakeSolids=0;
                Merge "input.step";
                Mesh.SaveAll=1;
                Mesh.Binary=0;
                Mesh.ElementOrder=1;
                Mesh.MshFileVersion=4.1;
                """+$"\nMesh.MeshSizeMin={size};\nMesh.MeshSizeMax={size};\n";
            File.WriteAllText(Path.Combine(folder,"inspect.geo"),geo);
            var start=new ProcessStartInfo(gmsh) {WorkingDirectory=folder,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(var arg in new[]{"inspect.geo","-2","-nt","1","-format","msh4","-o","inspection.msh","-v","4","-noenv","-nopopup"}) start.ArgumentList.Add(arg);
            using var process=new Process {StartInfo=start};var watch=Stopwatch.StartNew();long peak=0;string status="running";string? error=null;Task<string>? stdout=null,stderr=null;var started=false;
            void Journal()=>File.WriteAllText(Path.Combine(folder,"process.json"),System.Text.Json.JsonSerializer.Serialize(new {status,error,sourceSha256=hash,mesherSha256=GmshSha256,processId=started?(int?)process.Id:null,elapsedSeconds=watch.Elapsed.TotalSeconds,peakWorkingSetBytes=peak,budgetSeconds=120,memoryBudgetBytes=2147483648L,workerThreads=1,healingApplied=false}));
            try
            {
                process.Start();started=true;stdout=process.StandardOutput.ReadToEndAsync();stderr=process.StandardError.ReadToEndAsync();Journal();
                while(!process.HasExited)
                {
                    cancellation.ThrowIfCancellationRequested();process.Refresh();peak=Math.Max(peak,process.WorkingSet64);
                    if(watch.Elapsed.TotalSeconds>120||peak>2147483648L) throw new TimeoutException("CAD inspection exceeded its 120-second / 2-GB budget; reduce the preview detail or inspect the source solid.");
                    await Task.Delay(100,cancellation);
                }
                var output=await stdout;var errors=await stderr;
                if(process.ExitCode!=0||Regex.IsMatch(output+errors,@"(?m)^Error\s*:")) throw new InvalidOperationException("Gmsh rejected this STEP/inspection mesh. Review the preserved import log: "+folder);
                var model=ParseMesh(File.ReadAllText(Path.Combine(folder,"inspection.msh")),hash,Path.GetFileName(path),unit,previewSizeM);
                BumpStudioContracts.ValidateInspection(model);var inspectionHash=BumpStudioContracts.Hash(model);ImmutableWrite(InspectionPath(inspectionHash),Encoding.UTF8.GetBytes(DesignJson.Serialize(model)));status="completed";return model;
            }
            catch(Exception failure) {status=failure is OperationCanceledException?"canceled":"failed";error=failure.Message;throw;}
            finally
            {
                if(started&&!process.HasExited) {process.Kill(true);await process.WaitForExitAsync();}
                if(stdout is not null) File.WriteAllText(Path.Combine(folder,"stdout.log"),await stdout);if(stderr is not null) File.WriteAllText(Path.Combine(folder,"stderr.log"),await stderr);Journal();
            }
        }
        finally {ImportWorker.Release();}
    }
    public BumperCadInspection LoadInspection(string hash)
    {
        var inspection=DesignJson.Deserialize<BumperCadInspection>(File.ReadAllText(InspectionPath(hash)));BumpStudioContracts.ValidateInspection(inspection);
        if(BumpStudioContracts.Hash(inspection)!=hash||BytesHash(File.ReadAllBytes(SourcePath(inspection.SourceSha256)))!=inspection.SourceSha256) throw new InvalidDataException("CAD source or inspection hash no longer matches.");return inspection;
    }
    public string SaveRecipe(BumperJobRecipe recipe)
    {
        var model=LoadInspection(recipe.InspectionSha256);BumpStudioContracts.ValidateRecipe(recipe,model);CheckMaterialEvidence(recipe.Material);var hash=BumpStudioContracts.Hash(recipe);ImmutableWrite(RecipePath(hash),Encoding.UTF8.GetBytes(DesignJson.Serialize(recipe)));return hash;
    }
    public BumperJobRecipe LoadRecipe(string hash)
    {var recipe=DesignJson.Deserialize<BumperJobRecipe>(File.ReadAllText(RecipePath(hash)));if(BumpStudioContracts.Hash(recipe)!=hash) throw new InvalidDataException("Recipe content hash changed.");BumpStudioContracts.ValidateRecipe(recipe,LoadInspection(recipe.InspectionSha256));CheckMaterialEvidence(recipe.Material);return recipe;}
    public static BumperCadInspection ParseMesh(string mesh,string sourceHash,string name,StepLengthUnit unit,double previewSizeM)
    {
        var lines=mesh.Split('\n').Select(x=>x.Trim()).ToArray();var i=Array.IndexOf(lines,"$Entities");if(i<0) throw new InvalidDataException("CAD entity mapping is absent from the inspection mesh.");
        static string[] Fields(string value)=>value.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);
        static int I(string v)=>int.Parse(v,CultureInfo.InvariantCulture);static double D(string v)=>double.Parse(v,CultureInfo.InvariantCulture);
        var counts=Fields(lines[++i]).Select(I).ToArray();if(counts.Length!=4||counts[3]!=1) throw new InvalidDataException("Import requires exactly one closed CAD volume; shells and disconnected/multiple solids are unsupported in this preparation slice.");
        i+=counts[0]+counts[1];var faces=new Dictionary<int,CadFace>();
        for(var f=0;f<counts[2];f++) {var p=Fields(lines[++i]);var physical=I(p[7]);var at=8+physical;var count=I(p[at]);faces.Add(I(p[0]),new(I(p[0]),new(new(D(p[1]),D(p[2]),D(p[3])),new(D(p[4]),D(p[5]),D(p[6]))),[..p.Skip(at+1).Take(count).Select(I)]));}
        var volume=Fields(lines[++i]);var boundaryAt=8+I(volume[7]);var boundary=volume.Skip(boundaryAt+1).Take(I(volume[boundaryAt])).Select(x=>Math.Abs(I(x))).ToHashSet();
        if(boundary.Count!=faces.Count||boundary.Any(tag=>!faces.ContainsKey(tag))) throw new InvalidDataException("CAD has orphan or unassociated faces; inspect/repair the source explicitly before importing.");
        var curveUse=faces.Values.SelectMany(f=>f.BoundaryCurveTags).GroupBy(Math.Abs).ToDictionary(g=>g.Key,g=>g.Count());
        if(curveUse.Values.Any(count=>count!=2)) throw new InvalidDataException("The imported volume boundary has an open/nonmanifold edge. No sewing/healing was applied.");
        i=Array.IndexOf(lines,"$Nodes");if(i<0) throw new InvalidDataException("Inspection nodes are missing.");var nodeHeader=Fields(lines[++i]);var nodeBlocks=I(nodeHeader[0]);if(I(nodeHeader[1])>250000) throw new InvalidDataException("Inspection exceeds the node budget.");var nodes=new List<CadNode>();
        for(var block=0;block<nodeBlocks;block++)
        {
            var header=Fields(lines[++i]);var count=I(header[3]);var tags=new List<int>();while(tags.Count<count) tags.AddRange(Fields(lines[++i]).Select(I));if(tags.Count!=count) throw new InvalidDataException("Invalid inspection node-tag block.");
            for(var n=0;n<count;n++) {var p=Fields(lines[++i]);nodes.Add(new(tags[n],new(D(p[0]),D(p[1]),D(p[2]))));}
        }
        i=Array.IndexOf(lines,"$Elements");if(i<0) throw new InvalidDataException("Inspection triangles are missing.");var elementBlocks=I(Fields(lines[++i])[0]);var triangles=new List<CadTriangle>();
        for(var block=0;block<elementBlocks;block++)
        {var h=Fields(lines[++i]);var dim=I(h[0]);var tag=I(h[1]);var type=I(h[2]);var count=I(h[3]);for(var n=0;n<count;n++) {var p=Fields(lines[++i]);if(dim==2) {if(type!=2||p.Length!=4) throw new InvalidDataException("Only linear triangular inspection facets are supported; this is not the production volume mesh.");triangles.Add(new(tag,I(p[1]),I(p[2]),I(p[3])));}}}
        // CAD bounds include the kernel's bounding tolerance; preview tessellation extrema are not physical dimensions.
        var bounds=new CadBounds(new(D(volume[1]),D(volume[2]),D(volume[3])),new(D(volume[4]),D(volume[5]),D(volume[6])));
        return new(sourceHash,name,GmshSha256,unit,"m",false,previewSizeM,I(volume[0]),bounds,[..faces.Values.OrderBy(f=>f.Tag)],[..nodes.OrderBy(n=>n.Tag)],[..triangles],
            ["One imported CAD volume with paired boundary edges. This topology check does not certify self-intersection, duplicate/sliver faces or suitability of a later volume mesh.","Linear surface facets are for inspection/picking only; no material or compression solution has been computed."]);
    }
}
