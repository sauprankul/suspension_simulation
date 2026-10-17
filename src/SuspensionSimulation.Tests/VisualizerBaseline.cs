using System.Text.Json;
using SuspensionSimulation.Configuration;
using SuspensionSimulation.Core;
using SuspensionSimulation.Reference;
using SuspensionSimulation.Solvers.HeadToHead;

namespace SuspensionSimulation.Tests;

internal static class VisualizerBaseline
{
    internal const string FixturePath = "data/testdata/visualizer/baseline.json";
    internal static object Capture()
    {
        var directory = "data/synthetic/gr86";
        var vehicle = ModularSetupLoader.Load(directory);
        var reference = Stock86BrzReferenceLoader.Load("data/reference/stock-86-brz/reference-sheet.json");
        HeadToHeadSetupInput Input(StockSetupReference s) => new(s.Name, 2.9*9800, 4.0*9800,
            s.FrontArb.OutsideDiameterM, s.FrontArb.InsideDiameterM, s.RearArb.OutsideDiameterM, s.RearArb.InsideDiameterM);
        var a = Input(reference.Setups[0]); var b = Input(reference.Setups[1]);
        var solver = new HeadToHeadComparisonSolver(directory);
        const double ax = .07733333333333318;
        const double ay = -1.1304347826086956;
        var cases = new List<LoadCase>
        {
            new("zero",0,0,0), new("acceleration",.5,0,0), new("braking",-.5,0,0),
            new("left",0,.5,0), new("right",0,-.5,0), new("combined",.3,-.6,0), new("reported-top-out",ax,ay,0)
        };
        // Bracket using the actual named FR element at the final equilibrium pose, never rounded RSD.
        double Crossing(bool first)
        {
            double low=0, high=1.5;
            for (var i=0;i<36;i++)
            {
                var middle=(low+high)/2;
                var result=solver.Solve(vehicle,a,b,new("threshold",ax,-middle,0));
                var side=first?result.A:result.B;
                var corner=side.State.Corners.Single(x=>x.Corner=="FR");
                if(side.Inspection!.Limits.Single(x=>x.Corner=="FR").Direction=="rebound") high=middle; else low=middle;
            }
            return (low+high)/2;
        }
        var crossingA=Crossing(true); var crossingB=Crossing(false);
        foreach(var (name,crossing) in new[]{("a",crossingA),("b",crossingB)})
        {
            cases.Add(new($"{name}-before-top-out",ax,-crossing+1e-5,0));
            cases.Add(new($"{name}-after-top-out",ax,-crossing-1e-5,0));
        }
        object Side(HeadToHeadSideResult s) => new
        {
            s.State.HeaveCompressionM, s.State.RollRad, s.State.PitchRad,
            s.State.RollStiffnessDistributionFront, s.State.RollCoupleBiasPercentagePoints,
            s.State.Converged,
            corners=s.State.Corners.Select(c=>new {c.Corner,c.RelativeCompressionM,c.NormalLoadN,c.ParallelElementForceN,c.ActiveParallelElements}),
            stiffness=s.State.TangentRollStiffnesses
        };
        return new
        {
            schema="visualizer-numerical-baseline/v1", frontUnsprungKg=vehicle.Chassis.FrontUnsprungMassPerCornerKg,
            rearUnsprungKg=vehicle.Chassis.RearUnsprungMassPerCornerKg, a,b,
            frontRightReboundThresholdLateralG=new {a=-crossingA,b=-crossingB},
            limits=solver.PhysicalTravelLimits.Values,
            cases=cases.Select(load=>{var r=solver.Solve(vehicle,a,b,load);return new {load,a=Side(r.A),b=Side(r.B)};}).ToArray()
        };
    }
    internal static void Write()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FixturePath)!);
        const string path="artifacts/hard-stops/current-baseline.json";Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path,JsonSerializer.Serialize(Capture(),new JsonSerializerOptions {WriteIndented=true,NumberHandling=System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals}));
        Console.WriteLine($"Wrote current model report: {path}; historical penalty baseline retained.");
    }
    internal static void Verify()
    {
        using var expected=JsonDocument.Parse(File.ReadAllText(FixturePath));
        const string directory="data/synthetic/gr86";var vehicle=ModularSetupLoader.Load(directory);var solver=new HeadToHeadComparisonSolver(directory);
        var a=JsonSerializer.Deserialize<HeadToHeadSetupInput>(expected.RootElement.GetProperty("a"))!;
        var b=JsonSerializer.Deserialize<HeadToHeadSetupInput>(expected.RootElement.GetProperty("b"))!;
        foreach(var fixture in expected.RootElement.GetProperty("cases").EnumerateArray().Take(6))
        {
            var load=JsonSerializer.Deserialize<LoadCase>(fixture.GetProperty("load"))!;var result=solver.Solve(vehicle,a,b,load);
            foreach(var (name,side) in new[]{("a",result.A),("b",result.B)})
            {
                if(side.Inspection!.Limits.Any(x=>x.Active)) throw new Exception("A historical free-travel baseline unexpectedly reached a stop.");
                using var actual=JsonDocument.Parse(JsonSerializer.Serialize(new {side.State.HeaveCompressionM,side.State.RollRad,side.State.PitchRad,
                    side.State.RollStiffnessDistributionFront,side.State.RollCoupleBiasPercentagePoints,side.State.Converged,
                    corners=side.State.Corners.Select(c=>new {c.Corner,c.RelativeCompressionM,c.NormalLoadN,c.ParallelElementForceN,c.ActiveParallelElements}),stiffness=side.State.TangentRollStiffnesses}));
                Compare(fixture.GetProperty(name),actual.RootElement,load.Name+"."+name);
            }
        }
    }
    private static void Compare(JsonElement a,JsonElement b,string path)
    {
        if(a.ValueKind!=b.ValueKind) throw new Exception($"{path} changed kind.");
        if(a.ValueKind==JsonValueKind.Object)
            foreach(var p in a.EnumerateObject()) Compare(p.Value,b.GetProperty(p.Name),path+"."+p.Name);
        else if(a.ValueKind==JsonValueKind.Array)
        {
            if(a.GetArrayLength()!=b.GetArrayLength()) throw new Exception($"{path} changed length.");
            for(var i=0;i<a.GetArrayLength();i++) Compare(a[i],b[i],$"{path}[{i}]");
        }
        else if(a.ValueKind==JsonValueKind.Number)
        {
            var av=a.GetDouble();var bv=b.GetDouble();
            if(Math.Abs(av-bv)>1e-8*Math.Max(1,Math.Abs(av))) throw new Exception($"{path}: {av:G17} became {bv:G17}.");
        }
        else if(a.ToString()!=b.ToString()) throw new Exception($"{path}: {a} became {b}.");
    }
}
