using System.Numerics;
using SuspensionSimulation.Kinematics;

namespace SuspensionSimulation.Solvers.Kinematics;

public static class RackSteeringSolver
{
    public static RigidCornerPose3D Solve(MacPhersonCorner3D reference,RigidCornerPose3D centeredPose,double rackDisplacementM)
    {
        if(rackDisplacementM==0) return centeredPose;
        var solver=new RigidMechanism3DSolver();var pose=centeredPose;
        var steps=Math.Max(1,(int)Math.Ceiling(Math.Abs(rackDisplacementM)/.001));
        for(var i=1;i<=steps;i++)
        {
            var candidate=solver.Solve(reference with {RackDisplacementM=new(0,(float)(rackDisplacementM*i/steps),0)},centeredPose.WheelJounceM,pose.UprightPose);
            if(!candidate.Converged||candidate.MaximumConstraintResidualM>1e-4||
                Vector3.Distance(candidate.UprightPose.RotationVectorRad,pose.UprightPose.RotationVectorRad)>.25)
                throw new InvalidOperationException($"{reference.Id}: rack geometry cannot reach {rackDisplacementM*1000:0.0} mm at jounce {pose.WheelJounceM*1000:0.0} mm (step {i}/{steps}, residual {candidate.MaximumConstraintResidualM*1000:0.000} mm).");
            pose=candidate;
        }
        return pose;
    }
}
