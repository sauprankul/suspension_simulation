using System.Collections.Immutable;
using SuspensionSimulation.Design;

namespace SuspensionSimulation.Solvers.Design;

public sealed record InstalledAxleTangent(string Axle,double SpringNmPerRad,double BarNmPerRad,double OtherNmPerRad,bool RigidLoading)
{
    public double ElasticNmPerRad=>SpringNmPerRad+BarNmPerRad+OtherNmPerRad;
}
public sealed record InstalledRollMetrics(ImmutableArray<InstalledAxleTangent> Axles,double? FrontDistribution,double StaticFrontLoadFraction,double? RollCoupleBiasPercentagePoints);

/// <summary>Second derivative of installed elastic energy under body roll with grounded wheel heights held.
/// Re-solves contact geometry, retaining preload curvature. Rigid reactions remain a separate loading constraint.</summary>
public static class InstalledRollMetricCalculator
{
    public static InstalledRollMetrics Calculate(ResolvedAssemblyGraph graph,InstalledEquilibriumResult state,InstalledEquilibriumResult gravity,CancellationToken cancellation=default,double rollStepRad=.0003)
    {
        if(!double.IsFinite(rollStepRad)||rollStepRad<=0||rollStepRad>.01) throw new ArgumentOutOfRangeException(nameof(rollStepRad));
        var kinematics=new TwinsKinematics(graph);var mechanics=new InstalledMechanics(kinematics.System);var h=rollStepRad;
        var split=(state.Motion.Wheels[0].CenterM.X+state.Motion.Wheels[2].CenterM.X)/2;
        var axleByElement=graph.ElasticElements.ToDictionary(e=>e.Path,e=>(e.First.Frame.OriginM.X+e.Second.Frame.OriginM.X)/2>split?"front":"rear");
        InstalledMechanicalState Perturb(double angle)
        {
            var chassis=state.Chassis with {RollRad=state.Chassis.RollRad+angle};var travel=state.Motion.Travel.Values;var motion=state.Motion;
            for(var iteration=0;iteration<16;iteration++)
            {
                cancellation.ThrowIfCancellationRequested();motion=kinematics.Solve(new(travel[0],travel[1],travel[2],travel[3]),state.Input.SteeringWheelDegrees,motion,1e-12);
                var gaps=motion.Wheels.Select((w,i)=>state.NormalLoadsN[i]>1e-5?chassis.World(w.CenterM).Z-state.Wheels[i].CenterM.Z:0).ToArray();
                if(gaps.Max(Math.Abs)<1e-11) return mechanics.Evaluate(motion.Pose.Coordinates,false);
                var slope=chassis.Rotate(new(0,0,1)).Z;
                for(var i=0;i<4;i++) travel[i]-=gaps[i]/slope;
            }
            throw new InvalidOperationException("Roll-tangent contact continuation did not close.");
        }
        var plus=Perturb(h);var minus=Perturb(-h);
        var axles=new List<InstalledAxleTangent>();
        foreach(var axle in new[]{"front","rear"})
        {
            cancellation.ThrowIfCancellationRequested();
            double Tangent(string kind)=>state.Mechanics.Elements.Where(e=>axleByElement[e.Path]==axle&&(kind=="other"?e.Kind is not "spring" and not "torsion-bar":e.Kind==kind))
                .Sum(e=>(plus.Elements.Single(p=>p.Path==e.Path).EnergyJ-2*e.EnergyJ+minus.Elements.Single(p=>p.Path==e.Path).EnergyJ)/(h*h));
            var rigid=state.RigidReactions.Any(r=>r.ForceN>1e-5&&axleByElement.Any(e=>e.Value==axle&&r.Id.StartsWith(e.Key+":",StringComparison.Ordinal)));
            axles.Add(new(axle,Tangent("spring"),Tangent("torsion-bar"),Tangent("other"),rigid));
        }
        var front=axles[0];var rear=axles[1];var total=front.ElasticNmPerRad+rear.ElasticNmPerRad;
        double? distribution=front.RigidLoading&&rear.RigidLoading?null:front.RigidLoading?1:rear.RigidLoading?0:total>0?front.ElasticNmPerRad/total:null;
        if(state.NormalLoadsN.Any(n=>n<1e-5)) distribution=null; // Ground-supported balance is undefined after contact release.
        var staticFraction=gravity.NormalLoadsN.Take(2).Sum()/gravity.NormalLoadsN.Sum();
        return new([..axles],distribution,staticFraction,distribution is {} d?100*(d-staticFraction):null);
    }
}
