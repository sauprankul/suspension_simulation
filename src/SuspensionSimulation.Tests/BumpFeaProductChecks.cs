using SuspensionSimulation.Components;
using SuspensionSimulation.Design;
using SuspensionSimulation.Solvers.Design;
using SuspensionSimulation.Solvers.HeadToHead;

namespace SuspensionSimulation.Tests;

internal static class BumpFeaProductChecks
{
    internal static async Task RunActualAsync()
    {
        var repo=AppContext.BaseDirectory;while(!Directory.Exists(Path.Combine(repo,"data"))) repo=Directory.GetParent(repo)?.FullName??throw new Exception("Repository missing.");
        var folder=Path.Combine(repo,"artifacts","bump-fea-product",DateTime.UtcNow.ToString("yyyyMMddTHHmmss"));Directory.CreateDirectory(folder);
        var service=new StepBumperPreparation(repo,Path.Combine(folder,"library"));
        var cad=await service.ImportAsync(Path.Combine(repo,"data/examples/bumper-cylinder-mm.step"),StepLengthUnit.Millimetres,.008);
        var lower=cad.Faces.Where(f=>Math.Abs(f.Bounds.MinimumM.Z)<1e-6&&Math.Abs(f.Bounds.MaximumM.Z)<1e-6).Select(f=>f.Tag).ToArray();
        var upper=cad.Faces.Where(f=>Math.Abs(f.Bounds.MinimumM.Z-.04)<1e-6&&Math.Abs(f.Bounds.MaximumM.Z-.04)<1e-6).Select(f=>f.Tag).ToArray();
        var frame=AssemblyAuthoring.IdentityPlacement.Frame;
        var recipe=new BumperJobRecipe("Example cylinder TPU95A",BumpStudioContracts.Hash(cad),frame,.04,.004,
            [new("lower",[..lower],[],"lower",0,true),new("upper",[..upper],[],"upper",0,false)],
            [new("lower",RigidBumperFixtureKind.AnnularPlane,frame,0,.03,0,false),new("upper",RigidBumperFixtureKind.AnnularPlane,frame with {OriginM=new(0,0,.04)},0,.03,0,true)],
            BumpStudioContracts.MaterialCatalog().Single(c=>c.Id=="printed-tpu-95"),new(.008,2,false),BudgetSeconds:120) {Quality=BumperSolveQuality.CoarseEstimate};
        var hash=service.SaveRecipe(recipe);var backend=new LocalFebioBumperBackend(repo,Path.Combine(folder,"library"));
        var complete=await backend.RunAsync(hash,8,new Progress<BumperFeaProgress>(p=>Console.WriteLine($"{p.Stage} {p.Fraction:P0}: {p.Message} ({p.ElapsedSeconds:0}s)")));
        var component=complete.Component("example-fea-bumper");var library=new DesignAssetLibrary(Path.Combine(folder,"design"));var published=library.Publish(component,null);
        var reopened=library.LoadComponent(published.Identity.AssetId)!;
        if(reopened.Identity.Provenance.Kind!=DataProvenance.Estimated||reopened.BumperMaterial?.Id!="printed-tpu-95"||reopened.BumperFeaSource is null||reopened.BumpCurveEvidence!.CsvUtf8!=complete.Csv) throw new Exception("FEA source evidence/material did not survive publication.");
        if(complete.Result.Curve[0]!=new BumpCurveSample(0,0)||Math.Abs(complete.Result.Curve[^1].CompressionM-.004)>1e-10||complete.Result.Curve[^1].ForceN<=0) throw new Exception("Actual curve did not cover the complete stroke.");
        try {ComponentAuthoring.ValidateForPublish(reopened with {BumperMaterial=BumpStudioContracts.MaterialCatalog().Single(c=>c.Id=="printed-tpu-85")});throw new Exception("Material was changed without recomputing its source curve.");}catch(ArgumentException) {}
        var support=library.Publish(component with {Identity=component.Identity with {AssetId="fea-rigid-fixture"},Kind=ComponentKind.RigidLink,
            Behavior=null,BumpCurveEvidence=null,BumperMaterial=null,BumperFeaSource=null},null);
        var assembly=library.Publish(new AssemblyDefinition(published.Identity with {AssetId="fea-curve-assembly",RevisionId="draft",Name="FEA curve assembly"},
            [new ComponentInstance("bumper",new(published.Identity.AssetId),AssemblyAuthoring.IdentityPlacement),new ComponentInstance("support",new(support.Identity.AssetId),AssemblyAuthoring.IdentityPlacement)],
            [new("lower",JointKind.Spherical,new("bumper","first"),new("support","first")),new("upper",JointKind.Spherical,new("bumper","second"),new("support","second"))],[]),null);
        var graph=new AssemblyGraphResolver(library).Compile(new(assembly.Identity.AssetId));
        if(graph.ElasticElements.Single().Component.Behavior is not BumpCurveBehavior curve||!curve.Samples.SequenceEqual(complete.Result.Curve)) throw new Exception("Assembly did not resolve the actual generated curve.");
        var diagnostic=FeaDiagnosticResults.Load(complete.DiagnosticPath);
        if(!diagnostic.Result.Complete||diagnostic.Result.CurveAccepted||diagnostic.Result.Synthetic) throw new Exception("Coarse diagnostics confused full-stroke completion, source geometry and research validation.");
        var garage=new GarageLibrary(Path.Combine(folder,"garage"));new ReferenceGarageCatalog(Path.Combine(repo,"data/reference/stock-86-brz/reference-sheet.json")).Seed(garage);new PhysicalGarageCatalog(library,repo).Migrate(garage);
        var stock=garage.Load(ReferenceGarageCatalog.Gr86Id)!;var root=library.LoadAssembly(stock.Configuration!.RootAssembly.AssetId)!;
        var configured=root with {Identity=root.Identity with {AssetId="actual-fea-car",RevisionId="draft",Name="Example car with actual generated bumper"}};
        configured=BumperInstallation.Add(configured,library,new(published.Identity.AssetId),"actual-fea-bumper",new("front","left-shock-upper"),new("front","left-shock-lower"),true,stock.Configuration.ParameterValues??[]);
        configured=library.Publish(configured,null,stock.Configuration.ParameterValues??[]);var car=garage.Duplicate(stock);
        car=garage.Save(car with {Configuration=car.Configuration! with {RootAssembly=new(configured.Identity.AssetId)}});
        var carGraph=GarageSetupResolver.Freeze(new AssemblyGraphResolver(library).Compile(car.Configuration!.RootAssembly,[],car.Configuration.ParameterValues??[]),car.Configuration);
        var replay=AssemblyGraphResolver.OpenBundle(AssemblyGraphResolver.Export(carGraph));
        if(replay.ElasticElements.Single(e=>e.Path=="actual-fea-bumper").Component.BumpCurveEvidence!.CsvUtf8!=complete.Csv||replay.Components.Single(c=>c.Identity.AssetId==published.Identity.AssetId).BumperFeaSource is null) throw new Exception("Saved Garage assembly lost actual FEA curve/material evidence in frozen replay.");
        var foamHash=service.SaveRecipe(recipe with {Name="Example cylinder PU450",Material=BumpStudioContracts.MaterialCatalog().Single(c=>c.Id=="cellular-pu-450")});
        var foam=await backend.RunAsync(foamHash,4);if(foam.Result.Curve[^1].ForceN<=0||foam.Result.Curve[^1].ForceN>=complete.Result.Curve[^1].ForceN) throw new Exception("Foam coefficients did not reach their actual worker law.");
        _=foam.Component("foam-fea-bumper");
        string? canceledFolder=null;using(var cancel=new CancellationTokenSource())
        {
            try {await backend.RunAsync(hash,8,new InlineProgress(p=>{canceledFolder=p.Folder;if(p.Stage=="solving") cancel.Cancel();}),cancel.Token);throw new Exception("Canceled job returned a component result.");}catch(OperationCanceledException) {}
        }
        if(canceledFolder is null) throw new Exception("Cancellation never started a job.");
        try {backend.LoadCompleted(canceledFolder);throw new Exception("Partial/canceled job was accepted.");}catch(InvalidDataException) {}
        var recovered=await backend.RunAsync(foamHash,2);if(recovered.Result.Curve[^1].ForceN<=0) throw new Exception("Worker did not recover after cancellation.");
        var tampered=File.ReadAllText(Path.Combine(complete.Folder,"curve.csv"));File.WriteAllText(Path.Combine(complete.Folder,"curve.csv"),tampered+"1,1\n");
        try {backend.LoadCompleted(complete.Folder);throw new Exception("Changed native curve was accepted.");}catch(InvalidDataException) {}finally {File.WriteAllText(Path.Combine(complete.Folder,"curve.csv"),tampered);}
        File.WriteAllText(Path.Combine(folder,"completed-job.txt"),complete.Folder);
        Console.WriteLine($"Actual completed job: {complete.Folder}; force={complete.Result.Curve[^1].ForceN:0.###} N; tetrahedra={complete.Result.Tetrahedra}");
    }
    private sealed class InlineProgress(Action<BumperFeaProgress> report):IProgress<BumperFeaProgress>
    {public void Report(BumperFeaProgress value)=>report(value);}
}
