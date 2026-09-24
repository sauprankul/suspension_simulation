using System.Numerics;
using SuspensionSimulation.Kinematics;

namespace SuspensionSimulation.Solvers.Kinematics;

public sealed record RigidCornerTravelMapSample3D(
    double WheelJounceM,
    double SpringLengthM,
    Vector3 ArbLowerPointM,
    double ConstraintResidualM,
    int SolverIterations);

public sealed record RigidCornerTravelMapEvaluation3D(
    string Corner,
    double WheelJounceM,
    double SpringLengthM,
    double SpringCompressionPerWheelJounce,
    double SpringCompressionSecondDerivativePerM,
    Vector3 ArbLowerPointM,
    Vector3 ArbLowerDerivativePerWheelJounce,
    double NearbyMaximumConstraintResidualM,
    string Validity,
    IReadOnlyList<string> Diagnostics);

/// <summary>
/// A validated, continuous corner mechanism branch traced once from static ride height.
/// Runtime evaluation is cubic interpolation of exact rigid-constraint samples; it never
/// launches an independent Newton solve and therefore cannot jump to another branch.
/// </summary>
public sealed class RigidCornerTravelMap3D
{
    private readonly RigidCornerTravelMapSample3D[] _samples;
    public string Corner { get; }
    public string Topology { get; }
    public double MinimumJounceM => _samples[0].WheelJounceM;
    public double MaximumJounceM => _samples[^1].WheelJounceM;
    public double StepM { get; }
    public IReadOnlyList<RigidCornerTravelMapSample3D> Samples => _samples;
    public double MaximumConstraintResidualM { get; }

    internal RigidCornerTravelMap3D(string corner, string topology, double stepM, IEnumerable<RigidCornerTravelMapSample3D> samples)
    {
        Corner = corner; Topology = topology; StepM = stepM;
        _samples = samples.OrderBy(x => x.WheelJounceM).ToArray();
        if (_samples.Length < 5) throw new ArgumentException("A travel map requires at least five samples.", nameof(samples));
        MaximumConstraintResidualM = _samples.Max(x => x.ConstraintResidualM);
    }

    public RigidCornerTravelMapEvaluation3D Evaluate(double jounceM)
    {
        if (!double.IsFinite(jounceM)) throw new ArgumentOutOfRangeException(nameof(jounceM));
        if (jounceM < MinimumJounceM || jounceM > MaximumJounceM)
            return new(Corner, jounceM, double.NaN, double.NaN, double.NaN, new(float.NaN), new(float.NaN), double.NaN, "outside-validated-travel",
                [$"Requested {jounceM*1000:F2} mm is outside the validated [{MinimumJounceM*1000:F1}, {MaximumJounceM*1000:F1}] mm branch."]);

        var position = (jounceM-MinimumJounceM)/StepM;
        var i = Math.Clamp((int)Math.Floor(position), 0, _samples.Length-2);
        var t = Math.Clamp(position-i, 0, 1);
        var p0 = _samples[Math.Max(0,i-1)]; var p1 = _samples[i];
        var p2 = _samples[i+1]; var p3 = _samples[Math.Min(_samples.Length-1,i+2)];
        var spring = Cubic(p0.SpringLengthM,p1.SpringLengthM,p2.SpringLengthM,p3.SpringLengthM,t);
        var springFirst = CubicFirst(p0.SpringLengthM,p1.SpringLengthM,p2.SpringLengthM,p3.SpringLengthM,t)/StepM;
        var springSecond = CubicSecond(p0.SpringLengthM,p1.SpringLengthM,p2.SpringLengthM,p3.SpringLengthM,t)/(StepM*StepM);
        var arb = Cubic(p0.ArbLowerPointM,p1.ArbLowerPointM,p2.ArbLowerPointM,p3.ArbLowerPointM,t);
        var arbFirst = CubicFirst(p0.ArbLowerPointM,p1.ArbLowerPointM,p2.ArbLowerPointM,p3.ArbLowerPointM,t)/(float)StepM;
        var residual = Math.Max(Math.Max(p0.ConstraintResidualM,p1.ConstraintResidualM),Math.Max(p2.ConstraintResidualM,p3.ConstraintResidualM));
        return new(Corner,jounceM,spring,-springFirst,-springSecond,arb,arbFirst,residual,"precomputed-rigid-branch",
            [$"Interpolated on the startup-validated {Topology} branch at {StepM*1000:F1} mm sample spacing."]);
    }

    private static double Cubic(double p0,double p1,double p2,double p3,double t) => .5*((2*p1)+(-p0+p2)*t+(2*p0-5*p1+4*p2-p3)*t*t+(-p0+3*p1-3*p2+p3)*t*t*t);
    private static double CubicFirst(double p0,double p1,double p2,double p3,double t) => .5*((-p0+p2)+2*(2*p0-5*p1+4*p2-p3)*t+3*(-p0+3*p1-3*p2+p3)*t*t);
    private static double CubicSecond(double p0,double p1,double p2,double p3,double t) => .5*(2*(2*p0-5*p1+4*p2-p3)+6*(-p0+3*p1-3*p2+p3)*t);
    private static Vector3 Cubic(Vector3 p0,Vector3 p1,Vector3 p2,Vector3 p3,double t) => .5f*((2*p1)+(-p0+p2)*(float)t+(2*p0-5*p1+4*p2-p3)*(float)(t*t)+(-p0+3*p1-3*p2+p3)*(float)(t*t*t));
    private static Vector3 CubicFirst(Vector3 p0,Vector3 p1,Vector3 p2,Vector3 p3,double t) => .5f*((-p0+p2)+2*(2*p0-5*p1+4*p2-p3)*(float)t+3*(-p0+3*p1-3*p2+p3)*(float)(t*t));
}

public sealed class RigidCornerTravelMapBuilder3D
{
    private readonly RigidMechanism3DSolver _solver = new();

    public RigidCornerTravelMap3D Build(MacPhersonCorner3D geometry, double minimumJounceM=-.080, double maximumJounceM=.080, double stepM=.001) =>
        Build(geometry.Id,"macpherson-3d",minimumJounceM,maximumJounceM,stepM,
            (jounce,seed) => _solver.Solve(geometry,jounce,seed));

    public RigidCornerTravelMap3D Build(DoubleWishboneCorner3D geometry, double minimumJounceM=-.080, double maximumJounceM=.080, double stepM=.001) =>
        Build(geometry.Id,"double-wishbone-multilink-3d",minimumJounceM,maximumJounceM,stepM,
            (jounce,seed) => _solver.Solve(geometry,jounce,seed));

    private static RigidCornerTravelMap3D Build(string corner,string topology,double minimum,double maximum,double step,
        Func<double,RigidPoseState3D?,RigidCornerPose3D> solve)
    {
        if (minimum >= 0 || maximum <= 0 || step <= 0) throw new ArgumentOutOfRangeException(nameof(step),"Travel map must span zero with a positive step.");
        var reference=solve(0,null); Require(reference,corner,0);
        var negative=Trace(reference,minimum,-step,solve); var positive=Trace(reference,maximum,step,solve);
        var samples=negative.AsEnumerable().Reverse().Concat([Sample(reference)]).Concat(positive).ToArray();
        return new(corner,topology,step,samples);
    }

    private static List<RigidCornerTravelMapSample3D> Trace(RigidCornerPose3D reference,double limit,double signedStep,
        Func<double,RigidPoseState3D?,RigidCornerPose3D> solve)
    {
        var result=new List<RigidCornerTravelMapSample3D>(); var previous=reference; RigidCornerPose3D? beforePrevious=null;
        var count=(int)Math.Round(Math.Abs(limit/signedStep));
        for(var index=1;index<=count;index++)
        {
            var target=index*signedStep;
            var predicted=Predict(previous,beforePrevious);
            var pose=Advance(previous,target,predicted,solve,0);
            result.Add(Sample(pose)); beforePrevious=previous; previous=pose;
        }
        return result;
    }

    private static RigidCornerPose3D Advance(RigidCornerPose3D from,double target,RigidPoseState3D seed,
        Func<double,RigidPoseState3D?,RigidCornerPose3D> solve,int depth)
    {
        var direct=solve(target,seed);
        if (Usable(direct)) return direct;
        if (depth>=10 || Math.Abs(target-from.WheelJounceM)<1e-6)
            throw new InvalidOperationException($"{from.Id} could not continue from {from.WheelJounceM*1000:F3} to {target*1000:F3} mm; residual {direct.MaximumConstraintResidualM*1000:F3} mm.");
        var midpoint=.5*(from.WheelJounceM+target);
        var middle=Advance(from,midpoint,from.UprightPose,solve,depth+1);
        return Advance(middle,target,middle.UprightPose,solve,depth+1);
    }

    private static RigidPoseState3D Predict(RigidCornerPose3D previous,RigidCornerPose3D? beforePrevious)
    {
        if (beforePrevious is null) return previous.UprightPose;
        return new(previous.UprightPose.TranslationM+(previous.UprightPose.TranslationM-beforePrevious.UprightPose.TranslationM),
            previous.UprightPose.RotationVectorRad+(previous.UprightPose.RotationVectorRad-beforePrevious.UprightPose.RotationVectorRad));
    }

    private static bool Usable(RigidCornerPose3D pose) => pose.Converged && pose.MaximumConstraintResidualM<1e-4;
    private static void Require(RigidCornerPose3D pose,string corner,double jounce) { if(!Usable(pose)) throw new InvalidOperationException($"{corner} reference solve failed at {jounce:G4} m."); }
    private static RigidCornerTravelMapSample3D Sample(RigidCornerPose3D pose) => new(pose.WheelJounceM,pose.SpringLengthM,pose.SolvedPoints["arbLinkLower"],pose.MaximumConstraintResidualM,pose.Iterations);
}
