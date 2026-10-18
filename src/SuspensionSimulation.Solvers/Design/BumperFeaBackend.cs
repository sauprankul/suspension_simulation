using System.Collections.Immutable;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SuspensionSimulation.Components;
using SuspensionSimulation.Design;

namespace SuspensionSimulation.Solvers.Design;

public sealed record BumperFeaProgress(string Stage,double Fraction,double ElapsedSeconds,string Message,string Folder);
public sealed record BumperFeaWorkerResult(string SchemaVersion,string Quality,string RecipeSha256,string StepSha256,string MeshSha256,
    string SolverSha256,string CsvSha256,ImmutableArray<BumpCurveSample> Curve,int Nodes,int Tetrahedra,double MaximumSupportImbalanceN,double Seconds);
public sealed record CompletedBumperFeaJob(string Folder,BumperJobRecipe Recipe,BumperFeaWorkerResult Result,string ResultSha256,string Csv)
{
    public string DiagnosticPath=>Path.Combine(Folder,"diagnostic-result.json");
    public ComponentDefinition Component(string id)
    {
        var note=$"Coarse single-mesh FEA estimate · {Result.Tetrahedra} tetrahedra · no mesh-convergence or physical calibration claim. Recipe {Result.RecipeSha256}; STEP {Result.StepSha256}; solver {Result.SolverSha256}.";
        var evidence=new ImportedBumpCurveEvidence(Csv,CurveDisplacementUnit.Metres,CurveForceUnit.Newtons,CurveZero.Contact,Recipe.FreeLengthM,
            Recipe.Material.Conditions+"; "+note,note,DataProvenance.Estimated) {AllowSoftening=true};
        var component=ElasticComponentFactory.ImportedBumper(new(id,"draft",Recipe.Name+" · coarse FEA estimate",new(DataProvenance.Estimated,note,"FEBio coarse product workflow")),evidence) with
        {BumperMaterial=Recipe.Material,BumperFeaSource=new(Recipe,Result.RecipeSha256,Result.StepSha256,Result.MeshSha256,Result.SolverSha256,ResultSha256,Result.CsvSha256,Result.Quality)};
        ComponentAuthoring.ValidateForPublish(component);return component;
    }
}

/// <summary>The UI consumes curves/jobs without depending on a particular native FE solver.</summary>
public interface IBumperFeaBackend
{
    string Id {get;}
    Task<CompletedBumperFeaJob> RunAsync(string recipeHash,int steps,IProgress<BumperFeaProgress>? progress=null,CancellationToken cancellation=default);
    CompletedBumperFeaJob LoadCompleted(string folder);
}

public sealed class LocalFebioBumperBackend(string repository,string libraryRoot):IBumperFeaBackend
{
    private static readonly SemaphoreSlim Worker=new(1,1);
    private readonly StepBumperPreparation _preparation=new(repository,libraryRoot);
    public string Id=>"febio.coarse-estimate.v1";
    public string JobsRoot=>Path.Combine(_preparation.Root,"jobs");
    private static string Hash(byte[] bytes)=>Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static string HashFile(string path)=>Hash(File.ReadAllBytes(path));
    public static string PythonPath()
    {
        var configured=Environment.GetEnvironmentVariable("SUSPENSION_FEA_PYTHON");if(!string.IsNullOrWhiteSpace(configured)) return configured;
        var installed=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"anaconda3","python.exe");return File.Exists(installed)?installed:"python";
    }
    public static void RequestCancel(string folder)
    {Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"cancel.request"),"Cancellation requested by Suspension Simulation");}
    public static BumperFeaProgress? ReadProgress(string folder)
    {
        var file=Path.Combine(folder,"status.json");if(!File.Exists(file)) return null;
        try
        {
            using var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
            using var json=JsonDocument.Parse(stream);var r=json.RootElement;
            return new(r.GetProperty("state").GetString()!,r.GetProperty("fraction").GetDouble(),r.GetProperty("elapsedSeconds").GetDouble(),r.GetProperty("message").GetString()!,folder);
        }
        catch(IOException) {return null;}catch(JsonException) {return null;}
    }
    public async Task<CompletedBumperFeaJob> RunAsync(string recipeHash,int steps,IProgress<BumperFeaProgress>? progress=null,CancellationToken cancellation=default)
    {
        if(steps is <2 or >100) throw new ArgumentException("Choose 2–100 compression steps.");
        var recipe=_preparation.LoadRecipe(recipeHash);
        if(recipe.Quality!=BumperSolveQuality.CoarseEstimate) throw new ArgumentException("Save this preparation as a CoarseEstimate recipe before running.");
        if(recipe.ThreadCount!=4||recipe.MemoryBudgetBytes!=2147483648L) throw new ArgumentException("This coarse worker currently supports the fixed four-thread / 2-GB profile. Save a recipe using that profile.");
        if(recipe.Material.Law is null||recipe.Material.Readiness==MaterialReadiness.NeedsCalibration) throw new ArgumentException("Select an estimated material preset or documented coefficients.");
        var folder=Path.Combine(JobsRoot,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        var start=new ProcessStartInfo(PythonPath()) {UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=repository,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var argument in new[]{Path.Combine(repository,"tools","bump-studio-worker.py"),"--recipe",_preparation.RecipePath(recipeHash),"--inspection",_preparation.InspectionPath(recipe.InspectionSha256),
            "--source",_preparation.SourcePath(_preparation.LoadInspection(recipe.InspectionSha256).SourceSha256),"--job",folder,"--steps",steps.ToString(System.Globalization.CultureInfo.InvariantCulture),"--budget-seconds",recipe.BudgetSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}) start.ArgumentList.Add(argument);
        using var process=new Process {StartInfo=start};Task<string>? stdout=null,stderr=null;var started=false;var watch=Stopwatch.StartNew();
        await Worker.WaitAsync(cancellation);
        watch.Restart();
        try
        {
            progress?.Report(new("starting",0,0,"Starting coarse FEA job",folder));
            process.Start();started=true;stdout=process.StandardOutput.ReadToEndAsync();stderr=process.StandardError.ReadToEndAsync();
            File.WriteAllText(Path.Combine(folder,"host.json"),JsonSerializer.Serialize(new {backend=Id,workerPid=process.Id,startedAtUtc=DateTimeOffset.UtcNow,steps,recipeHash}));
            while(!process.HasExited)
            {
                cancellation.ThrowIfCancellationRequested();
                if(watch.Elapsed.TotalSeconds>recipe.BudgetSeconds+15) throw new TimeoutException("The coarse job exceeded its time budget. Use a simpler part, coarser mesh or shorter stroke.");
                if(ReadProgress(folder) is {} update) progress?.Report(update);
                await Task.Delay(250,cancellation);
            }
            var status=ReadProgress(folder);if(status is not null) progress?.Report(status);
            if(process.ExitCode!=0||status?.Stage!="completed") throw new InvalidOperationException(status?.Message??"The external worker failed. Inspect the preserved job logs in "+folder);
            cancellation.ThrowIfCancellationRequested();return LoadCompleted(folder);
        }
        finally
        {
            try
            {
                if(started&&!process.HasExited)
                {
                    RequestCancel(folder);
                    try {await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));}catch(TimeoutException) {if(!process.HasExited) process.Kill(true);await process.WaitForExitAsync();}
                }
                if(stdout is not null) File.WriteAllText(Path.Combine(folder,"worker.stdout.log"),await stdout);
                if(stderr is not null) File.WriteAllText(Path.Combine(folder,"worker.stderr.log"),await stderr);
            }
            finally {Worker.Release();}
        }
    }
    public CompletedBumperFeaJob LoadCompleted(string folder)
    {
        if(ReadProgress(folder)?.Stage!="completed") throw new InvalidDataException("Only a completed job can create a component. Failed/partial jobs retain diagnostics only.");
        var resultPath=Path.Combine(folder,"result.json");var resultBytes=File.ReadAllBytes(resultPath);if(resultBytes.Length>2_000_000) throw new InvalidDataException("FEA result exceeds the curve budget.");
        var result=DesignJson.Deserialize<BumperFeaWorkerResult>(Encoding.UTF8.GetString(resultBytes));var recipe=DesignJson.Deserialize<BumperJobRecipe>(File.ReadAllText(Path.Combine(folder,"recipe.json")));
        var csv=File.ReadAllText(Path.Combine(folder,"curve.csv"));
        if(result.SchemaVersion!="1.0"||result.Quality!="CoarseEstimate"||recipe.Quality!=BumperSolveQuality.CoarseEstimate||result.RecipeSha256!=BumpStudioContracts.Hash(recipe)||
            result.CsvSha256!=Hash(Encoding.UTF8.GetBytes(csv))||result.StepSha256!=HashFile(Path.Combine(folder,"source.step"))||result.MeshSha256!=HashFile(Path.Combine(folder,"mesh","volume.json")))
            throw new InvalidDataException("The computed result no longer matches its frozen recipe, STEP, mesh or curve.");
        if(result.Nodes<=0||result.Tetrahedra<=0||result.Curve.Length<2||!double.IsFinite(result.Seconds)||result.Seconds<0||!double.IsFinite(result.MaximumSupportImbalanceN)) throw new InvalidDataException("Invalid FEA result counts or diagnostics.");
        var complete=new CompletedBumperFeaJob(Path.GetFullPath(folder),recipe,result,Hash(resultBytes),csv);
        var component=complete.Component("fea-result-validation");
        if(!((BumpCurveBehavior)component.Behavior!).Samples.SequenceEqual(result.Curve)) throw new InvalidDataException("FEA samples differ from the retained raw CSV.");
        return complete;
    }
}
