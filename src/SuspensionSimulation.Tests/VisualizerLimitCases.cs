using System.Text.Json;
using SuspensionSimulation.Configuration;
using SuspensionSimulation.Core;
using SuspensionSimulation.Solvers.HeadToHead;
using SuspensionSimulation.Solvers.Kinematics;

namespace SuspensionSimulation.Tests;

internal static class VisualizerLimitCases
{
    private const string PathName="data/testdata/visualizer/verified-limit-pairs.json";
    private static readonly HeadToHeadSetupInput A=new("GR86 2.9/4.0",2.9*9800,4*9800,.018,0,.015,0);
    private static readonly HeadToHeadSetupInput B=new("BRZ 2.9/4.0",2.9*9800,4*9800,.0183,.0086,.014,0);
    internal sealed record Case(string Name,string Setup,string Corner,string Direction,LoadCase Inactive,LoadCase Active);
    internal static void Write()
    {
        var vehicle=ModularSetupLoader.Load("data/synthetic/gr86");var solver=new HeadToHeadComparisonSolver("data/synthetic/gr86");
        var cases=new List<Case>();
        foreach(var (setup,corner,direction) in new[]{("A","FR","rebound"),("B","FR","rebound"),("A","FL","bump")})
        {
            LoadCase Load(double g)=>direction=="rebound"?new("verified-rebound",.07733333333333318,-g,0):new("verified-bump",-g,0,0);
            (bool Usable,bool Active) Evaluate(double g)
            {
                var r=solver.Solve(vehicle,A,B,Load(g));var side=setup=="A"?r.A:r.B;
                return (r.A.State.Converged && r.B.State.Converged && r.A.State.Corners.Concat(r.B.State.Corners).All(x=>x.NormalLoadN>0),
                    side.Inspection!.Limits.Single(x=>x.Corner==corner).Direction==direction);
            }
            var low=0d;double? high=null;
            for(var g=.002;g<=2;g+=.002)
            {
                var result=Evaluate(g);if(!result.Usable) continue;
                if(result.Active) {high=g;break;}low=g;
            }
            if(high is null) throw new Exception($"No converged {setup} {direction} crossing found.");
            for(var i=0;i<20;i++)
            {
                var middle=(low+high.Value)/2;var result=Evaluate(middle);
                if(!result.Usable) break;
                if(result.Active) high=middle;else low=middle;
            }
            if(Evaluate(low)!=(true,false)||Evaluate(high.Value)!=(true,true)) throw new Exception("Bracket endpoints are not verified.");
            cases.Add(new($"{setup}-{corner}-{direction}",setup,corner,direction,Load(low),Load(high.Value)));
        }
        File.WriteAllText(PathName,JsonSerializer.Serialize(cases,new JsonSerializerOptions {WriteIndented=true}));
        Console.WriteLine("Wrote actual converged inactive/active pairs. These are local observed brackets; provisional response is not globally monotonic.");
    }
    internal static void Verify()
    {
        var cases=JsonSerializer.Deserialize<Case[]>(File.ReadAllText(PathName))!;
        var vehicle=ModularSetupLoader.Load("data/synthetic/gr86");var solver=new HeadToHeadComparisonSolver("data/synthetic/gr86");var builder=new InspectionSceneBuilder("data/synthetic/gr86");
        foreach(var fixture in cases)
            foreach(var (load,active) in new[]{(fixture.Inactive,false),(fixture.Active,true)})
            {
                var result=solver.Solve(vehicle,A,B,load);var snapshot=builder.CreateSnapshot(1,result,vehicle,solver.TravelMaps,0);
                var side=fixture.Setup=="A"?snapshot.A:snapshot.B;
                var limit=side.Diagnostics.Limits.Single(x=>x.Corner==fixture.Corner);
                if(limit.Active!=active || active && limit.Direction!=fixture.Direction) throw new Exception($"{fixture.Name}: actual limit state mismatched.");
                if(side.Scene.Segments.Any(x=>x.Corner==fixture.Corner&&x.ActiveLimit)!=active) throw new Exception("Highlight mismatch at verified limit pair.");
            }
    }
}
