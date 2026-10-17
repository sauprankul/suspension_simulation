using System.Numerics;
using System.Text.Json;
using SuspensionSimulation.Configuration;
using SuspensionSimulation.Core;
using SuspensionSimulation.Kinematics;
using SuspensionSimulation.Reference;
using SuspensionSimulation.Solvers.HeadToHead;
using SuspensionSimulation.Solvers.Kinematics;

namespace SuspensionSimulation.Tests;

internal static class InspectionChecks
{
    internal static void Branches()
    {
        var geometry=Gr86HardPointPackageLoader.Load("data/synthetic/gr86");
        var solver=new HeadToHeadComparisonSolver("data/synthetic/gr86");
        foreach(var (corner,map) in solver.TravelMaps)
        {
            foreach(var sample in map.Samples)
                if(sample.Pose.WheelJounceM!=sample.WheelJounceM) throw new Exception("Travel sample lost its full pose.");
            var forward=new List<RigidCornerPose3D>();
            for(var j=-.12537;j<.155;j+=.00731) forward.Add(map.EvaluatePose(j));
            foreach(var pose in forward.AsEnumerable().Reverse())
            {
                var reverse=map.EvaluatePose(pose.WheelJounceM);
                Near(0,Vector3.Distance(pose.SolvedPoints["wheelCenter"],reverse.SolvedPoints["wheelCenter"]),1e-8);
                if(pose.MaximumConstraintResidualM>1e-4) throw new Exception("Off-grid constraint residual too high.");
                var points=pose.SolvedPoints;
                if(geometry.Front.TryGetValue(corner,out var f))
                {
                    Length(f.LowerArmFrontPivot,f.Upright.LowerBallJoint,points["lowerBallJoint"]);
                    Length(f.LowerArmRearPivot,f.Upright.LowerBallJoint,points["lowerBallJoint"]);
                    Length(f.TieRodInner,f.Upright.TieRodOuter,points["tieRodOuter"]);
                    Near(f.Upright.WheelCenter.Z+pose.WheelJounceM,points["wheelCenter"].Z,1e-5);
                }
                else
                {
                    var r=geometry.Rear[corner];
                    Length(r.LowerArmFrontPivot,r.Upright.LowerBallJoint,points["lowerBallJoint"]);
                    Length(r.LowerArmRearPivot,r.Upright.LowerBallJoint,points["lowerBallJoint"]);
                    Length(r.UpperArmFrontPivot,r.Upright.UpperBallJoint,points["upperBallJoint"]);
                    Length(r.UpperArmRearPivot,r.Upright.UpperBallJoint,points["upperBallJoint"]);
                    Length(r.ToeLinkInner,r.Upright.TieRodOuter,points["tieRodOuter"]);
                    Length(r.LowerArmFrontPivot,r.SpringLowerOnLowerArm,points["springLower"]);
                    Length(r.LowerArmRearPivot,r.ArbLinkLowerOnLowerArm,points["arbLinkLower"]);
                }
            }
            try {map.EvaluatePose(map.MaximumJounceM+.0001);throw new Exception("Out-of-range pose extrapolated.");}
            catch(ArgumentOutOfRangeException) { }
        }
    }

    internal static void Snapshots()
    {
        const string directory="data/synthetic/gr86";
        var vehicle=ModularSetupLoader.Load(directory);var solver=new HeadToHeadComparisonSolver(directory);var builder=new InspectionSceneBuilder(directory);
        var reference=Stock86BrzReferenceLoader.Load("data/reference/stock-86-brz/reference-sheet.json");
        HeadToHeadSetupInput Input(StockSetupReference s)=>new(s.Name,2.9*9800,4*9800,s.FrontArb.OutsideDiameterM,s.FrontArb.InsideDiameterM,s.RearArb.OutsideDiameterM,s.RearArb.InsideDiameterM);
        using var fixtures=JsonDocument.Parse(File.ReadAllText(VisualizerBaseline.FixturePath));
        long revision=0;double maximumGap=0;
        foreach(var fixture in fixtures.RootElement.GetProperty("cases").EnumerateArray())
        {
            var load=JsonSerializer.Deserialize<LoadCase>(fixture.GetProperty("load"))!;
            var comparison=solver.Solve(vehicle,Input(reference.Setups[0]),Input(reference.Setups[1]),load);
            var jsonOptions=new JsonSerializerOptions {NumberHandling=System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals};
            var before=JsonSerializer.Serialize(comparison,jsonOptions);
            var snapshot=builder.CreateSnapshot(++revision,comparison,vehicle,solver.TravelMaps,0);
            if(before!=JsonSerializer.Serialize(comparison,jsonOptions)) throw new Exception("Scene generation mutated numerical output.");
            foreach(var (side,result) in new[]{(snapshot.A,comparison.A),(snapshot.B,comparison.B)})
            {
                Near(result.State.RollStiffnessDistributionFront,side.FrontRsd,0);
                if(side.Diagnostics.FinalEquilibrium.StateId==side.Diagnostics.StiffnessEvaluation.StateId) throw new Exception("Stiffness/final IDs are conflated.");
                foreach(var corner in result.State.Corners)
                    Near(corner.RelativeCompressionM,side.Corners[corner.Corner].WheelJounceM,1e-8);
                foreach(var bar in side.Bars)
                {
                    if(bar.Left.ConstraintResidualM>1e-6||bar.Right.ConstraintResidualM>1e-6) throw new Exception("Final bar endlink failed.");
                    foreach(var joint in new[]{bar.Left.LowerJoint,bar.Right.LowerJoint})
                        if(!side.Corners.Values.Any(c=>Vector3.Distance(c.SolvedPoints["arbLinkLower"],joint)<1e-8)) throw new Exception("Bar lower pickup detached from corner.");
                }
                foreach(var wheel in side.Scene.Wheels) maximumGap=Math.Max(maximumGap,Math.Abs(wheel.GroundGapM));
            }
            if(load.Name=="reported-top-out")
            {
                var limit=snapshot.B.Diagnostics.Limits.Single(x=>x.Corner=="FR");
                if(limit.Direction!="rebound") throw new Exception("Known event must highlight FR rebound.");
                Near(1,snapshot.B.FrontRsd,0);Near(0,limit.PenetrationM,0);
                if(!snapshot.B.Scene.Segments.Where(x=>x.ActiveLimit).All(x=>x.Corner=="FR")) throw new Exception("Wrong corner highlighted.");
            }
        }
        Directory.CreateDirectory("artifacts/visualizer");
        File.WriteAllText("artifacts/visualizer/contact-gaps.json",JsonSerializer.Serialize(new {cases=revision,maximumAbsoluteGapM=maximumGap,
            semantics="Finite body rotation plus small-angle equilibrium and estimated rigid tilted tire; no independent wheel snapping."}));
    }
    internal static void FailureAndBumpCases()
    {
        const string directory="data/synthetic/gr86";
        var vehicle=ModularSetupLoader.Load(directory);var solver=new HeadToHeadComparisonSolver(directory);var builder=new InspectionSceneBuilder(directory);
        var input=new HeadToHeadSetupInput("test",2.9*9800,4*9800,.018,0,.015,0);
        HeadToHeadComparisonResult Brake(double g)=>solver.Solve(vehicle,input,input,new("braking-bump",-g,0,0));
        var valid=Brake(0);
        void Rejected(Action action)
        {
            try {action();} catch(ArgumentException) {return;} catch(InvalidOperationException) {return;}
            throw new Exception("Invalid state was accepted.");
        }
        Rejected(()=>solver.Solve(vehicle,input with {FrontArbInsideDiameterM=.02},input,new("bad-tube",0,0,0)));
        Rejected(()=>solver.Solve(vehicle,input with {FrontSpringNPerM=double.NaN},input,new("bad-spring",0,0,0)));
        Rejected(()=>builder.CreateSnapshot(2,valid with {A=valid.A with {State=valid.A.State with {Converged=false}}},vehicle,solver.TravelMaps,0));
        Rejected(()=>builder.CreateSnapshot(3,valid with {A=valid.A with {State=valid.A.State with {Corners=valid.A.State.Corners.Select((x,i)=>i==0?x with {NormalLoadN=-1}:x).ToArray()}}},vehicle,solver.TravelMaps,0));
    }
    private static void Length(Vector3 pivot,Vector3 reference,Vector3 actual)=>Near(Vector3.Distance(pivot,reference),Vector3.Distance(pivot,actual),1e-4);
    private static void Near(double expected,double actual,double tolerance)
    {if(Math.Abs(expected-actual)>tolerance) throw new Exception($"Expected {expected:G17}, got {actual:G17}, tolerance {tolerance}.");}
}
