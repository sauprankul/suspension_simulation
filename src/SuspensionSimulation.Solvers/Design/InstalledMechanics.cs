using System.Collections.Immutable;
using SuspensionSimulation.Design;

namespace SuspensionSimulation.Solvers.Design;

public sealed record RigidElementGap(string Id,double GapM,string Description);
public sealed record InstalledElementState(string Path,string Kind,double LengthM,double ForceOrTorque,double Tangent,double EnergyJ,double? SpringSeatLengthM=null,bool Unseated=false);
public sealed record InstalledMechanicalState(double EnergyJ,ImmutableArray<InstalledElementState> Elements,ImmutableArray<RigidElementGap> Limits)
{
    public bool LimitsSatisfied=>Limits.All(l=>l.GapM>=-1e-9);
}

public sealed class InstalledMechanics(ConstraintMechanism mechanism)
{
    public InstalledMechanicalState Evaluate(double[] pose,bool requireFeasible=true)
    {
        var graph=mechanism.Graph;var states=new List<InstalledElementState>();var limits=new List<RigidElementGap>();
        foreach(var element in graph.ElasticElements)
        {
            var first=mechanism.Frame(element.First,pose);var second=mechanism.Frame(element.Second,pose);var length=(second.OriginM-first.OriginM).Length;
            if(!double.IsFinite(length)||length<1e-9) throw new InvalidOperationException("Axial element has zero/invalid installed length: "+element.Path);
            switch(element.Component.Behavior)
            {
                case CompressionSpringBehavior spring:
                {
                    var seats=length;var binding=graph.SpringSeats.SingleOrDefault(b=>b.SpringPath==element.Path);
                    if(binding is not null)
                    {
                        var damperElement=graph.ElasticElements.Single(e=>e.Path==binding.DamperPath);var damper=(AxialDamperBehavior)damperElement.Component.Behavior!;
                        seats=ElasticLaws.SeatSeparation(damper,length,GraphAdjustments.ThreadedOffset(graph,damperElement.Path,damper.SpringSeats?.PerchAdjustmentId)+GraphAdjustments.ThreadedOffset(graph,damperElement.Path,damper.BodyLengthAdjustmentId));
                    }
                    var compression=Math.Max(0,spring.FreeLengthM-seats);var k=spring.RateKgfPerMm*DesignConventions.NewtonsPerMetrePerKgfPerMm;
                    // Finite extension of energy is used only by constrained-solver trial points. Accepted states cannot penetrate bind.
                    states.Add(new(element.Path,"spring",length,k*compression,compression>0?k:0,.5*k*compression*compression,seats,seats>spring.FreeLengthM));
                    limits.Add(new(element.Path+":coil-bind",seats-spring.BlockLengthM,"Coil bind"));break;
                }
                case AxialDamperBehavior damper:
                {
                    var body=GraphAdjustments.ThreadedOffset(graph,element.Path,damper.BodyLengthAdjustmentId);
                    limits.Add(new(element.Path+":bottom",length-damper.MinimumEyeLengthM-body,"Damper bottom-out"));limits.Add(new(element.Path+":top",damper.MaximumEyeLengthM+body-length,"Damper top-out"));
                    states.Add(new(element.Path,"damper",length,damper.GasExtensionForceN,0,-damper.GasExtensionForceN*length));break;
                }
                case BumpCurveBehavior bump:
                {var state=ElasticLaws.Bump(bump,length);states.Add(new(element.Path,"bump-stop",length,state.ForceN,state.TangentNPerM,state.EnergyJ));break;}
                case TorsionBarBehavior bar:
                {
                    var parity=Math.Sign(element.Transform.X.Dot(element.Transform.Y.Cross(element.Transform.Z)));var axis=element.Transform.Direction(bar.UnitAxis)*parity;
                    var a=DesignTransform.RotationError(element.FirstReference,first).Dot(axis);var b=DesignTransform.RotationError(element.SecondReference,second).Dot(axis);var state=ElasticLaws.Bar(bar,a,b);
                    states.Add(new(element.Path,"torsion-bar",length,state.TorqueNm,state.TangentNmPerRad,state.EnergyJ));break;
                }
                default:throw new InvalidOperationException("Mechanical behavior is missing.");
            }
        }
        var result=new InstalledMechanicalState(states.Sum(s=>s.EnergyJ),[..states],[..limits]);
        if(!double.IsFinite(result.EnergyJ)||states.Any(s=>!double.IsFinite(s.ForceOrTorque)||!double.IsFinite(s.Tangent))) throw new InvalidOperationException("Mechanical evaluation overflowed.");
        if(requireFeasible&&!result.LimitsSatisfied) throw new InvalidOperationException("Rigid travel constraint violated: "+string.Join(", ",limits.Where(l=>l.GapM< -1e-9).Select(l=>l.Id)));
        return result;
    }
}
