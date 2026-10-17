using SuspensionSimulation.Components;
using SuspensionSimulation.Core;
using SuspensionSimulation.Metrics;
using SuspensionSimulation.Solvers;
using SuspensionSimulation.Configuration;
using SuspensionSimulation.Kinematics;
using SuspensionSimulation.Solvers.HeadToHead;
using SuspensionSimulation.Solvers.Kinematics;
using System.Numerics;
using System.Text.Json;

namespace SuspensionSimulation.Tests;

internal static class HardStopChecks
{
    internal static void Analytic(VehiclePackage reference)
    {
        const double rate=30_000;const double bound=.01;
        var package=reference with {Springs=reference.Springs with {Corners=reference.Springs.Corners.Select(c=>c with
            {WheelRateNPerM=rate,BumpStopRateNPerM=0,ParallelElements=[],HardTravelLimit=new(c.Corner,-bound,bound)}).ToArray()},
            AntiRollBars=[new("front",0,"test",null),new("rear",0,"test",null)]};
        var solver=new FourStrutQuasiStaticSolver();
        foreach(var direction in new[]{-1d,1d})
        {
            var first=solver.Solve(package,new("heave",0,0,direction*.5));
            var second=solver.Solve(package,new("more-heave",0,0,direction));
            foreach(var state in new[]{first,second})
            {
                if(!state.Converged) throw new Exception("Rigid equilibrium failed.");
                Near(direction*bound,state.HeaveCompressionM,1e-11);Near(0,state.RollRad,1e-11);Near(0,state.PitchRad,1e-11);
                foreach(var corner in state.Corners)
                {
                    Near(direction*bound,corner.RelativeCompressionM,1e-11);
                    if(corner.HardStopReactionN*direction< -1e-7) throw new Exception("A stop pulled in the wrong direction.");
                }
                var elasticForce=4*rate*state.HeaveCompressionM;
                Near(package.Chassis.SprungMassKg*FourStrutQuasiStaticSolver.GravityMPerS2*state.LoadCase.VerticalG-elasticForce,state.Corners.Sum(c=>c.HardStopReactionN),1e-6);
                foreach(var corner in state.Corners) Near(state.Corners.Sum(c=>c.HardStopReactionN)/4,corner.HardStopReactionN,1e-6);
                Near(0,state.VerticalResidualN,1e-6);Near(0,state.RollResidualNm,1e-6);Near(0,state.PitchResidualNm,1e-6);
                if(!double.IsNaN(state.RollStiffnessDistributionFront)) throw new Exception("Two rigid axles must not invent an infinity/infinity RSD.");
            }
            if(Math.Abs(second.Corners.Sum(c=>c.HardStopReactionN))<=Math.Abs(first.Corners.Sum(c=>c.HardStopReactionN))) throw new Exception("More load did not increase the rigid-stop reaction.");
        }
        var released=solver.Solve(package,new("released",0,0,0));
        foreach(var corner in released.Corners)
        {Near(0,corner.RelativeCompressionM,0);Near(0,corner.HardStopReactionN,0);if(corner.TravelLimitDirection!="none") throw new Exception("Stop did not release.");}
        var oneCorner=package with {Springs=package.Springs with {Corners=package.Springs.Corners.Select(c=>c.Corner=="FR"?c:c with {HardTravelLimit=null}).ToArray()}};
        var turn=solver.Solve(oneCorner,new("turn",0,-1,0));
        var fr=turn.Corners.Single(c=>c.Corner=="FR");Near(-bound,fr.RelativeCompressionM,1e-10);
        if(fr.TravelLimitDirection!="rebound"||fr.HardStopReactionN>=0) throw new Exception("Rebound reaction missing.");
        Near(1,turn.RollStiffnessDistributionFront,0);
        if(!double.IsPositiveInfinity(turn.CornerTangentStiffnesses.Single(c=>c.Corner=="FR").TotalSelfNPerM)) throw new Exception("Rigid loading tangent is not infinite.");
        if(turn.Corners.Where(c=>c.Corner!="FR").All(c=>Math.Abs(c.RelativeCompressionM)<=bound)) throw new Exception("A corner stop incorrectly froze the entire body.");
        Near(0,turn.VerticalResidualN,1e-6);Near(0,turn.RollResidualNm,1e-6);Near(0,turn.PitchResidualNm,1e-6);
        Near(0,BalanceMetrics.RollStiffnessDistributionFront(123,double.PositiveInfinity),0);
        var soft=package with {Springs=package.Springs with {Corners=package.Springs.Corners.Select(c=>c with
            {ParallelElements=[new CurveParallelElasticElement(new ParallelElasticElementConfig {Id="progressive",Corners=[Corner.FL],
                ForceCurve=[new(-20,-200),new(0,0),new(5,100),new(10,2000),new(20,3000)]})]}).ToArray()}};
        var softened=solver.Solve(soft,new("progressive-on-stop",0,0,2));
        foreach(var corner in softened.Corners) Near(bound,corner.RelativeCompressionM,1e-11);
        Near(0,softened.VerticalResidualN,1e-6);
    }
    internal static void Strokes()
    {
        const string directory="data/synthetic/gr86";var solver=new HeadToHeadComparisonSolver(directory);var builder=new InspectionSceneBuilder(directory);
        var reference=builder.BuildReference(.5);
        foreach(var stroke in reference.SpringStrokes)
        {Near(stroke.Corner.StartsWith("F")?.05:.062,stroke.BumpRemainingM,1e-7);Near(stroke.Corner.StartsWith("F")?.085:.062,stroke.ReboundRemainingM,1e-7);}
        foreach(var bump in new[]{true,false})
        {
            var poses=solver.TravelMaps.ToDictionary(x=>x.Key,x=>x.Value.EvaluatePose(bump?solver.PhysicalTravelLimits[x.Key].MaximumJounceM:solver.PhysicalTravelLimits[x.Key].MinimumJounceM));
            var scene=builder.Build(bump?"all-bottomed":"all-topped",poses,new(.01,.04,-.03,.5),poses.Keys.ToHashSet());
            if(scene.SpringStrokes.Length!=4) throw new Exception("Every spring requires a stroke marker.");
            foreach(var stroke in scene.SpringStrokes)
            {
                var upper=scene.Points.Single(x=>x.Id==stroke.Corner+".springUpper").PositionM;
                var lower=scene.Points.Single(x=>x.Id==stroke.Corner+".springLower").PositionM;
                var marker=scene.Points.Single(x=>x.Id==stroke.Corner+".stroke-marker").PositionM;
                Near(0,Vector3.Distance(upper,stroke.UpperMountM),0);Near(0,Vector3.Distance(lower,stroke.LowerMountM),0);Near(0,Vector3.Distance(marker,stroke.MarkerM),0);
                Near(stroke.BumpRemainingM,Vector3.Distance(upper,marker),1e-7);
                if(bump) {Near(0,stroke.BumpRemainingM,0);Near(0,Vector3.Distance(marker,upper),0);}
                else {Near(0,stroke.ReboundRemainingM,0);Near(stroke.MaximumLengthM-stroke.MinimumLengthM,stroke.BumpRemainingM,1e-5);}
                Near(0,Vector3.Cross(lower-upper,marker-upper).Length(),1e-7);
            }
        }
        var vehicle=ModularSetupLoader.Load(directory);var input=new HeadToHeadSetupInput("rigid",2.9*9800,4*9800,.018,0,.015,0);
        var low=solver.Solve(vehicle,input,input,new("stop",.07733333333333318,-1.1304347826086956,0));
        var high=solver.Solve(vehicle,input,input,new("more-stop",.07733333333333318,-1.131,0));
        var fr=low.A.State.Corners.Single(x=>x.Corner=="FR");var more=high.A.State.Corners.Single(x=>x.Corner=="FR");
        Near(fr.RelativeCompressionM,more.RelativeCompressionM,1e-10);
        // The head-to-head model re-evaluates its elastic tangents once per load;
        // reaction monotonicity is checked separately by the fixed-law analytic test.
        if(more.HardStopReactionN>=0||fr.HardStopReactionN>=0) throw new Exception("Loaded rebound stops require tensile reactions.");
        var snapshot=builder.CreateSnapshot(1,low,vehicle,solver.TravelMaps,0);
        var options=new JsonSerializerOptions {IncludeFields=true,NumberHandling=System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals};
        using var json=JsonDocument.Parse(JsonSerializer.Serialize(snapshot,options));
        if(json.RootElement.GetProperty("A").GetProperty("Diagnostics").GetProperty("Limits").EnumerateArray().Single(x=>x.GetProperty("Corner").GetString()=="FR").GetProperty("TangentNPerM").GetString()!="Infinity") throw new Exception("Rigid stiffness did not survive export.");
        Directory.CreateDirectory("artifacts/hard-stops");File.WriteAllText("artifacts/hard-stops/accepted-state.json",JsonSerializer.Serialize(snapshot,options));
    }
    private static void Near(double expected,double actual,double tolerance)
    {if(!double.IsFinite(actual)||Math.Abs(expected-actual)>tolerance) throw new Exception($"Expected {expected:G17}, got {actual:G17} (±{tolerance}).");}
}
