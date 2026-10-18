using System.Diagnostics;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using SuspensionSimulation.Design;

namespace SuspensionSimulation.Solvers.Design;

public sealed record InstalledAnalysisSide(GarageEntry Entry,PortableDesignBundle Source,InstalledEquilibriumResult Static,
    InstalledEquilibriumResult State,InstalledRollMetrics RollMetrics,string SetupSha256,ImmutableArray<WheelAlignment> SteeringReference);
public sealed record InstalledAnalysisSnapshot(long Revision,InstalledAnalysisSide A,InstalledAnalysisSide B,
    double LongitudinalG,double LateralG,double SteeringWheelDegrees,double SolveMilliseconds,string RequestSha256);

/// <summary>One queued CPU worker, frozen source per job, bounded gravity-result cache. UI owns atomic acceptance.</summary>
public sealed class InstalledAnalysisEngine(IDesignAssetSource source)
{
    private readonly SemaphoreSlim _worker=new(1,1);
    private readonly Dictionary<string,InstalledEquilibriumResult> _staticCache=new(StringComparer.Ordinal);
    public async Task<InstalledAnalysisSnapshot> SolveAsync(long revision,GarageEntry a,GarageEntry b,double ax,double ay,double steering,CancellationToken cancellation=default,InstalledAnalysisSnapshot? previous=null)
    {
        await _worker.WaitAsync(cancellation).ConfigureAwait(false);
        try {return await Task.Run(()=>Solve(revision,a,b,ax,ay,steering,cancellation,previous),cancellation).ConfigureAwait(false);}
        finally {_worker.Release();}
    }
    private InstalledAnalysisSnapshot Solve(long revision,GarageEntry a,GarageEntry b,double ax,double ay,double steering,CancellationToken cancellation,InstalledAnalysisSnapshot? previous)
    {
        var watch=Stopwatch.StartNew();
        InstalledAnalysisSide Side(GarageEntry entry)
        {
            cancellation.ThrowIfCancellationRequested();if(entry.Archived) throw new InvalidOperationException("The selected Garage entry is archived.");
            var setup=entry.Configuration??throw new InvalidOperationException("The selected entry needs a physical assembly. Open Garage to migrate or choose one.");
            var sourceGraph=new AssemblyGraphResolver(source).Compile(setup.RootAssembly,setup.Equipment,setup.ParameterValues??[]);
            var prior=previous is null?null:new[]{previous.A,previous.B}.FirstOrDefault(s=>s.Entry.Id==entry.Id&&s.Source.Root.AssetId==setup.RootAssembly.AssetId);
            if(prior is not null) AssemblyGraphResolver.CheckInterfaces(AssemblyGraphResolver.OpenBundle(prior.Source),sourceGraph);
            var bundle=AssemblyGraphResolver.ExportConfigured(sourceGraph);
            // The worker uses this captured bundle only. Live library revisions cannot enter a solve midway through it.
            var graph=GarageSetupResolver.Freeze(AssemblyGraphResolver.OpenBundle(bundle),setup);
            var hash=Hash(DesignJson.Serialize(new {setup,bundle.Manifest}));var solver=new InstalledEquilibrium(graph);
            if(!_staticCache.TryGetValue(hash,out var gravity))
            {
                gravity=solver.Solve(new(setup.Mass),cancellation:cancellation);
                if(_staticCache.Count>=16) _staticCache.Clear();_staticCache[hash]=gravity;
            }
            var result=ax==0&&ay==0&&steering==0?gravity:solver.Solve(new(setup.Mass,ax,ay,steering),prior?.SetupSha256==hash?prior.State:gravity,cancellation);
            var metrics=InstalledRollMetricCalculator.Calculate(graph,result,gravity,cancellation);
            var referenceMotion=steering==0?gravity.Motion:new TwinsKinematics(graph).Solve(gravity.Motion.Travel,steering,gravity.Motion);
            var reference=referenceMotion.Wheels.Select(w=>
            {var forward=gravity.Chassis.Rotate(w.Forward);var outward=gravity.Chassis.Rotate(w.Outward);return w with {Forward=forward,Outward=outward,CenterM=gravity.Chassis.World(w.CenterM),HeadingRad=Math.Atan2(forward.Y,forward.X),CamberRad=-Math.Atan2(outward.Z,Math.Sqrt(outward.X*outward.X+outward.Y*outward.Y))};}).ToImmutableArray();
            return new(entry,bundle,gravity,result,metrics,hash,reference);
        }
        var first=Side(a);var second=a==b?first:Side(b);cancellation.ThrowIfCancellationRequested();
        return new(revision,first,second,ax,ay,steering,watch.Elapsed.TotalMilliseconds,Hash(DesignJson.Serialize(new {a=first.SetupSha256,b=second.SetupSha256,ax,ay,steering})));
    }
    public bool SourcesMatch(InstalledAnalysisSide side,GarageEntry entry)
    {
        if(DesignJson.Serialize(entry)!=DesignJson.Serialize(side.Entry)||entry.Configuration is null) return false;
        var graph=new AssemblyGraphResolver(source).Compile(entry.Configuration.RootAssembly,entry.Configuration.Equipment,entry.Configuration.ParameterValues??[]);
        return graph.Manifest.Dependencies.SequenceEqual(side.Source.Manifest.Dependencies);
    }
    private static string Hash(string text)=>Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
