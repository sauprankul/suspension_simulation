using System.Numerics;
using System.Text.Json;
using SuspensionSimulation.Configuration;
using SuspensionSimulation.Core;
using SuspensionSimulation.Kinematics;
using SuspensionSimulation.Solvers.HeadToHead;
using SuspensionSimulation.Solvers.Kinematics;

namespace SuspensionSimulation.Tests;

internal static class SteeringChecks
{
    internal static void Constraints()
    {
        const string directory="data/synthetic/gr86";
        var geometry=Gr86HardPointPackageLoader.Load(directory);var config=SteeringGeometry.Load(directory);
        var maps=new HeadToHeadComparisonSolver(directory).TravelMaps;var report=new List<object>();
        foreach(var corner in new[]{"FL","FR"})
        {
            var front=config.Apply(geometry.Front[corner]);var map=maps[corner];
            foreach(var jounce in new[]{-.10,-.05,0,.05,.10})
            {
                var centered=map.EvaluatePose(jounce);
                if(!ReferenceEquals(centered,RackSteeringSolver.Solve(front,centered,0))) throw new Exception("Centered rack changed the accepted pose.");
                foreach(var angle in new[]{-450d,-225,0,225,450})
                {
                    var rack=config.RackDisplacementM(angle);RigidCornerPose3D pose;
                    try {pose=RackSteeringSolver.Solve(front,centered,rack);}
                    catch(InvalidOperationException error) when(jounce!=0 && angle!=0)
                    {report.Add(new {corner,jounceM=jounce,steeringWheelDegrees=angle,unreachable=error.Message});continue;}
                    var inner=front.TieRodInner+new Vector3(0,(float)rack,0);
                    Near(Vector3.Distance(front.TieRodInner,front.Upright.TieRodOuter),Vector3.Distance(inner,pose.SolvedPoints["tieRodOuter"]),1e-4);
                    if(!pose.Converged||pose.MaximumConstraintResidualM>1e-4) throw new Exception("Steered rigid constraints failed.");
                    Near(front.Upright.WheelCenter.Z+jounce,pose.SolvedPoints["wheelCenter"].Z,1e-5);
                    var r=pose.UprightPose.RotationVectorRad;var rotation=r.Length()<1e-12?Quaternion.Identity:Quaternion.CreateFromAxisAngle(Vector3.Normalize(r),r.Length());
                    var a=AlignmentMeasurement.Measure(corner,rotation);
                    report.Add(new {corner,jounceM=jounce,steeringWheelDegrees=angle,rackM=rack,a.Heading,a.Camber,residualM=pose.MaximumConstraintResidualM});
                }
            }
        }
        Directory.CreateDirectory("artifacts/steering");File.WriteAllText("artifacts/steering/kinematics.json",JsonSerializer.Serialize(report,new JsonSerializerOptions {WriteIndented=true}));
        var left=config.Apply(geometry.Front["FL"]);var right=config.Apply(geometry.Front["FR"]);
        Near(2*config.InnerHalfSpacingM,(left.TieRodInner-right.TieRodInner).Length(),1e-6);
        var altered=config with {RackCenterZM=config.RackCenterZM+.001};
        var changed=new HeadToHeadComparisonSolver(directory,altered).TravelMaps["FL"].EvaluatePose(.05);
        if(Vector3.Distance(changed.UprightPose.RotationVectorRad,maps["FL"].EvaluatePose(.05).UprightPose.RotationVectorRad)<1e-4) throw new Exception("Rack height did not change bump steer.");
    }

    internal static void AlignmentAndSnapshots()
    {
        var config=new SteeringGeometry {FrontStaticCamberDegrees=-2.5,RearStaticCamberDegrees=-1.5,FrontStaticTotalToeDegrees=.2,RearStaticTotalToeDegrees=.4};
        foreach(var corner in new[]{"FL","FR","RL","RR"})
        {
            var a=AlignmentMeasurement.Measure(corner,config.WheelReference(corner));
            Near(corner.StartsWith("F")?-2.5:-1.5,a.Camber,1e-5);
            Near((corner.EndsWith("L")?-1:1)*(corner.StartsWith("F")?.1:.2),a.Heading,1e-5);
        }
        Near(180,Math.Abs(AlignmentMeasurement.Axle(new("FL",179,0,0,0),new("FR",-179,0,0,0)).ThrustDegrees),1e-10);
        const string directory="data/synthetic/gr86";var vehicle=ModularSetupLoader.Load(directory);var solver=new HeadToHeadComparisonSolver(directory,config);var builder=new InspectionSceneBuilder(directory,config);
        var input=new HeadToHeadSetupInput("steering",2.9*9800,4*9800,.018,0,.015,0);
        var comparison=solver.Solve(vehicle,input,input,new("steering",.2,-.3,0));
        var centered=builder.CreateSnapshot(1,comparison,vehicle,solver.TravelMaps,0);
        var steered=builder.CreateSnapshot(2,comparison,vehicle,solver.TravelMaps,0,225);
        var staticComparison=solver.Solve(vehicle,input,input,new("static-lock",0,0,0));
        foreach(var lockAngle in new[]{-450d,450d}) builder.CreateSnapshot(3,staticComparison,vehicle,solver.TravelMaps,0,lockAngle);
        foreach(var rear in new[]{"RL","RR"})
        {
            Near(0,Vector3.Distance(centered.A.Corners[rear].SolvedPoints["wheelCenter"],steered.A.Corners[rear].SolvedPoints["wheelCenter"]),0);
            Near(0,Vector3.Distance(centered.A.Corners[rear].UprightPose.RotationVectorRad,steered.A.Corners[rear].UprightPose.RotationVectorRad),0);
            Near(centered.A.Steering!.Wheels.Single(x=>x.Corner==rear).HeadingDegrees,steered.A.Steering!.Wheels.Single(x=>x.Corner==rear).HeadingDegrees,0);
        }
        Near(centered.A.FrontRsd,steered.A.FrontRsd,0);
        var steering=steered.A.Steering!;
        if(steering.Front.ThrustDegrees-centered.A.Steering!.Front.ThrustDegrees<5) throw new Exception("Positive steering did not turn the front axle left.");
        var fl=steering.Wheels.Single(x=>x.Corner=="FL");var fr=steering.Wheels.Single(x=>x.Corner=="FR");
        if(Math.Abs(fl.HeadingDegrees-fr.HeadingDegrees)<.01) throw new Exception("Rack did not produce a geometric left/right steering difference.");
        Near(fr.HeadingDegrees-fl.HeadingDegrees,steering.Front.TotalToeDegrees,1e-10);
        Near((fl.HeadingDegrees+fr.HeadingDegrees)/2,steering.Front.ThrustDegrees,1e-8);
        foreach(var corner in new[]{"FL","FR"})
        {
            var line=steered.A.Scene.Segments.Single(x=>x.Id==corner+".steering");
            var original=centered.A.Scene.Segments.Single(x=>x.Id==corner+".steering");
            Near(config.RackTravelAtLockM/2,Vector3.Distance(line.StartM,original.StartM),1e-6);
            Near(Vector3.Distance(line.StartM,line.EndM),Vector3.Distance(original.StartM,original.EndM),1e-4);
        }
    }

    internal static void Camera()
    {
        var frame=new CameraFrame(new(4,-5,3.5f),new(0,0,.35f),Vector3.UnitZ);
        var orbit=frame.Orbit(40,-23);var roll=orbit.Roll(.8f);
        Near(0,Vector3.Distance(frame.Target,orbit.Target),0);
        Near(Vector3.Distance(frame.Position,frame.Target),Vector3.Distance(orbit.Position,orbit.Target),1e-6);
        Near(0,Vector3.Distance(orbit.Position,roll.Position),0);Near(0,Vector3.Distance(orbit.Target,roll.Target),0);
        Near(0,Vector3.Dot(Vector3.Normalize(roll.Target-roll.Position),roll.Up),1e-6);
        Near(1,roll.Up.Length(),1e-6);
        var back=orbit.Orbit(-40,23);Near(0,Vector3.Distance(frame.Position,back.Position),2e-6);
        Near(0,Vector3.Distance(orbit.Up,roll.Roll(-.8f).Up),1e-6);
    }
    private static void Near(double expected,double actual,double tolerance)
    {if(!double.IsFinite(actual)||Math.Abs(expected-actual)>tolerance) throw new Exception($"Expected {expected:G17}, got {actual:G17} (±{tolerance}).");}
}
