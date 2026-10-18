using SuspensionSimulation.Design;
using SuspensionSimulation.Solvers.Design;
using SuspensionSimulation.Solvers.HeadToHead;

namespace SuspensionSimulation.Tests;

internal static class InstalledAnalysisChecks
{
    internal static void Verify()=>VerifyAsync().GetAwaiter().GetResult();
    private static async Task VerifyAsync()
    {
        var repository=AppContext.BaseDirectory;while(!Directory.Exists(Path.Combine(repository,"data"))) repository=Directory.GetParent(repository)?.FullName??throw new Exception("Repository missing.");
        var folder=Path.Combine(Path.GetTempPath(),"installed-analysis-"+Guid.NewGuid().ToString("N"));
        try
        {
            var library=new DesignAssetLibrary(Path.Combine(folder,"design"));var garage=new GarageLibrary(Path.Combine(folder,"garage"));
            new ReferenceGarageCatalog(Path.Combine(repository,"data/reference/stock-86-brz/reference-sheet.json")).Seed(garage);new PhysicalGarageCatalog(library,repository).Migrate(garage);
            var a=garage.Load(ReferenceGarageCatalog.Gr86Id)!;var b=garage.Load(ReferenceGarageCatalog.BrzId)!;var engine=new InstalledAnalysisEngine(library);
            var snapshot=await engine.SolveAsync(1,a,b,0,.3,90);
            if(snapshot.A.State.Input.LateralG!=.3||snapshot.A.State.Input.SteeringWheelDegrees!=90||snapshot.A.State.MomentResidualNm.Length>.05||snapshot.A.Source.Manifest.Dependencies.Length<20) throw new Exception("Accepted analysis lost requested load/steer, physical closure or source revisions.");
            foreach(var side in new[]{snapshot.A,snapshot.B})
            {
                if(side.RollMetrics.Axles.Any(x=>!double.IsFinite(x.ElasticNmPerRad)||x.SpringNmPerRad<=0||x.BarNmPerRad<=0)||side.RollMetrics.FrontDistribution is not (>0 and <1)) throw new Exception("Installed roll tangent/balance is invalid.");
                if(Math.Abs(side.State.Wheels[0].HeadingRad-side.SteeringReference[0].HeadingRad)<1e-5) throw new Exception("Loaded heading difference lost actual bump steer.");
                if(!engine.SourcesMatch(side,side.Entry)) throw new Exception("Fresh source immediately became stale.");
            }
            var graphA=GarageSetupResolver.Freeze(AssemblyGraphResolver.OpenBundle(snapshot.A.Source),snapshot.A.Entry.Configuration!);
            var coarser=InstalledRollMetricCalculator.Calculate(graphA,snapshot.A.State,snapshot.A.Static,rollStepRad:.0006);
            if(coarser.Axles.Zip(snapshot.A.RollMetrics.Axles).Any(pair=>Math.Abs(pair.First.ElasticNmPerRad-pair.Second.ElasticNmPerRad)/pair.Second.ElasticNmPerRad>.005)) throw new Exception("Installed roll tangent did not converge with the perturbation step.");
            var rigid=InstalledRollMetricCalculator.Calculate(graphA,snapshot.A.Static with {RigidReactions=[new("FL-damper:bottom",1,0)]},snapshot.A.Static);
            var bothRigid=InstalledRollMetricCalculator.Calculate(graphA,snapshot.A.Static with {RigidReactions=[new("FL-damper:bottom",1,0),new("RL-damper:bottom",1,0)]},snapshot.A.Static);
            if(rigid.FrontDistribution!=1||bothRigid.FrontDistribution is not null) throw new Exception("Rigid axle loading was presented as finite elastic balance.");
            var modified=b.Configuration! with {ParameterValues=[..b.Configuration!.ParameterValues!.Value.Select(v=>v.ParameterId=="equipment.FL-damper"?ParameterValue.Choice(v.ParameterId,"example-adjustable-front-damper"):v.ParameterId=="FL-damper.adjustment.perch"?new ParameterValue(v.ParameterId,.6):v)]};
            b=garage.Save(b with {Configuration=modified});var custom=await engine.SolveAsync(2,b,b,0,0,0);
            var export=DesignJson.Serialize(custom);var frozen=DesignJson.Deserialize<InstalledAnalysisSnapshot>(export);
            var source=AssemblyGraphResolver.OpenBundle(frozen.A.Source);var graph=GarageSetupResolver.Freeze(source,frozen.A.Entry.Configuration!);
            var replay=new InstalledEquilibrium(graph).Solve(frozen.A.State.Input,frozen.A.State);
            if(Math.Abs(replay.Chassis.HeaveM-frozen.A.State.Chassis.HeaveM)>1e-8||replay.InputSha256!=frozen.A.State.InputSha256||frozen.A.Source.ParameterValues is not {Length:>0}||frozen.A.Source.Equipment is {Length:>0}) throw new Exception("Configured equipment/source/settings did not replay exactly from export.");
            var spring=graph.ElasticElements.Single(e=>e.Path=="FL-spring").Component;
            var sourceSpring=library.LoadComponent(spring.Identity.AssetId)!;
            library.Publish(sourceSpring with {Identity=sourceSpring.Identity with {Name=sourceSpring.Identity.Name+" revised"}},sourceSpring.Identity.RevisionId);
            if(engine.SourcesMatch(custom.A,b)) throw new Exception("Changed source was not detected.");
            var frozenAgain=GarageSetupResolver.Freeze(AssemblyGraphResolver.OpenBundle(frozen.A.Source),frozen.A.Entry.Configuration!);
            if(frozenAgain.Components.Single(c=>c.Identity.AssetId==spring.Identity.AssetId).Identity.RevisionId!=sourceSpring.Identity.RevisionId) throw new Exception("Source update altered frozen result geometry/behavior.");
            using var canceled=new CancellationTokenSource();canceled.Cancel();try {await engine.SolveAsync(3,a,b,0,0,0,canceled.Token);throw new Exception("Canceled queued job ran.");}catch(OperationCanceledException) { }
            try {await engine.SolveAsync(4,a,b with {Archived=true},0,0,0);throw new Exception("Archived entry produced an accepted comparison.");}catch(InvalidOperationException) { }
            Directory.CreateDirectory(Path.Combine(repository,"artifacts/installed-analysis"));File.WriteAllText(Path.Combine(repository,"artifacts/installed-analysis/frozen-result.json"),export);
        }
        finally {if(Directory.Exists(folder)) Directory.Delete(folder,true);}
    }
}
