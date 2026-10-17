using System.Collections.Immutable;
using SuspensionSimulation.Components;

namespace SuspensionSimulation.Solvers.HeadToHead;

public sealed record CornerLimitDiagnostic(string Corner, string LimitId, string Direction,
    double JounceM, double ReboundBoundaryM, double BumpBoundaryM,
    double ReboundRemainingM, double BumpRemainingM, double PenetrationM,
    double ReactionForceN, double TangentNPerM, double RollStiffnessContributionNmPerRad, string Message)
{
    public bool Active => Direction != "none";
    public static CornerLimitDiagnostic Create(GeometryDerivedWheelTravelLimit limit, double jounceM, double trackM,double reactionForceN=0,string direction="none")
    {
        if(!double.IsFinite(jounceM)||!double.IsFinite(reactionForceN)||direction is not ("none" or "rebound" or "bump")) throw new ArgumentException("Invalid rigid-stop diagnostic state.");
        if(jounceM<limit.MinimumJounceM-1e-9 || jounceM>limit.MaximumJounceM+1e-9) throw new InvalidOperationException($"{limit.Corner} penetrated its rigid travel boundary.");
        var rebound=direction=="rebound";var bump=direction=="bump";
        if(rebound && (reactionForceN>=0||Math.Abs(jounceM-limit.MinimumJounceM)>1e-9) || bump && (reactionForceN<=0||Math.Abs(jounceM-limit.MaximumJounceM)>1e-9) || !rebound && !bump && reactionForceN!=0)
            throw new InvalidOperationException($"{limit.Corner} inconsistent rigid-stop reaction.");
        var tangent=rebound||bump ? double.PositiveInfinity : 0;
        var name=limit.Corner switch { "FL"=>"Front-left", "FR"=>"Front-right", "RL"=>"Rear-left", _=>"Rear-right" };
        return new(limit.Corner,$"{limit.SourceId}-{limit.Corner}",rebound?"rebound":bump?"bump":"none",jounceM,
            limit.MinimumJounceM,limit.MaximumJounceM,Math.Max(0,jounceM-limit.MinimumJounceM),Math.Max(0,limit.MaximumJounceM-jounceM),
            0,reactionForceN,tangent,tangent*trackM*trackM/4,
            rebound?$"{name} topped out: rebound travel limit active.":bump?$"{name} bottomed out: bump travel limit active.":$"{name}: within travel limits.");
    }
}

public sealed record GeometryEvaluationState(string StateId, double HeaveCompressionM, double RollRad,
    double PitchRad, ImmutableDictionary<string,double> JouncesM);
public sealed record HeadToHeadInspectionDiagnostics(GeometryEvaluationState StiffnessEvaluation,
    GeometryEvaluationState FinalEquilibrium, ImmutableArray<CornerLimitDiagnostic> Limits)
{
    public ImmutableArray<InspectionBarPose> StiffnessEvaluationBars { get; init; }=[];
}
