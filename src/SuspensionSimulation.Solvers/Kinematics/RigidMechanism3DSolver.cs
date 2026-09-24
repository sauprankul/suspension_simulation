using System.Numerics;
using SuspensionSimulation.Kinematics;

namespace SuspensionSimulation.Solvers.Kinematics;

/// <summary>
/// Finite-displacement rigid-upright continuation. The six unknowns are upright translation and
/// exponential-coordinate rotation relative to the entered reference pose. Link lengths are taken
/// from the same reference, so a zero-jounce solve is an exact closure check rather than a fit.
/// </summary>
public sealed class RigidMechanism3DSolver
{
    // Coordinates use Vector3 (single precision). A 0.2 mm centered step keeps
    // first/second travel derivatives above the floating-point noise floor.
    private const double DifferenceStepM = 1e-3;

    public RigidCornerPose3D Solve(MacPhersonCorner3D geometry, double wheelJounceM, RigidPoseState3D? seed = null)
    {
        ArgumentNullException.ThrowIfNull(geometry); ValidateFinite(wheelJounceM);
        var u = geometry.Upright;
        var lowerFrontLength = Vector3.Distance(u.LowerBallJoint, geometry.LowerArmFrontPivot);
        var lowerRearLength = Vector3.Distance(u.LowerBallJoint, geometry.LowerArmRearPivot);
        var tieLength = Vector3.Distance(u.TieRodOuter, geometry.TieRodInner);
        var referenceAxis = Unit(geometry.StrutTopMount - u.StrutLower);
        var (perp1, perp2) = PerpendicularBasis(referenceAxis);
        double[] Residual(double[] q)
        {
            var pose = State(q); var lbj = Transform(u.LowerBallJoint, u.WheelCenter, pose);
            var tie = Transform(u.TieRodOuter, u.WheelCenter, pose); var strut = Transform(u.StrutLower, u.WheelCenter, pose);
            var axis = Rotate(referenceAxis, pose.RotationVectorRad); var topVector = geometry.StrutTopMount - strut;
            var e1 = Rotate(perp1, pose.RotationVectorRad); var e2 = Rotate(perp2, pose.RotationVectorRad);
            var wheel = Transform(u.WheelCenter, u.WheelCenter, pose);
            return
            [
                Vector3.Distance(lbj, geometry.LowerArmFrontPivot) - lowerFrontLength,
                Vector3.Distance(lbj, geometry.LowerArmRearPivot) - lowerRearLength,
                Vector3.Distance(tie, geometry.TieRodInner) - tieLength,
                Vector3.Dot(topVector, e1),
                Vector3.Dot(topVector, e2),
                wheel.Z - (u.WheelCenter.Z + (float)wheelJounceM)
            ];
        }
        var solution = Newton(Residual, Initial(seed, wheelJounceM));
        return Result(geometry.Id, wheelJounceM, solution, u, geometry.SpringUpper, "macpherson-3d",
            ["Rigid lower arm, tie rod and strut axis; steering rack and chassis are fixed in the body frame."]);
    }

    public RigidCornerPose3D Solve(DoubleWishboneCorner3D geometry, double wheelJounceM, RigidPoseState3D? seed = null)
    {
        ArgumentNullException.ThrowIfNull(geometry); ValidateFinite(wheelJounceM);
        var u = geometry.Upright;
        var lf = Vector3.Distance(u.LowerBallJoint, geometry.LowerArmFrontPivot);
        var lr = Vector3.Distance(u.LowerBallJoint, geometry.LowerArmRearPivot);
        var uf = Vector3.Distance(u.UpperBallJoint, geometry.UpperArmFrontPivot);
        var ur = Vector3.Distance(u.UpperBallJoint, geometry.UpperArmRearPivot);
        var toe = Vector3.Distance(u.TieRodOuter, geometry.ToeLinkInner);
        double[] Residual(double[] q)
        {
            var pose = State(q); var lower = Transform(u.LowerBallJoint, u.WheelCenter, pose);
            var upper = Transform(u.UpperBallJoint, u.WheelCenter, pose); var toeOuter = Transform(u.TieRodOuter, u.WheelCenter, pose);
            var wheel = Transform(u.WheelCenter, u.WheelCenter, pose);
            return
            [
                Vector3.Distance(lower, geometry.LowerArmFrontPivot) - lf,
                Vector3.Distance(lower, geometry.LowerArmRearPivot) - lr,
                Vector3.Distance(upper, geometry.UpperArmFrontPivot) - uf,
                Vector3.Distance(upper, geometry.UpperArmRearPivot) - ur,
                Vector3.Distance(toeOuter, geometry.ToeLinkInner) - toe,
                wheel.Z - (u.WheelCenter.Z + (float)wheelJounceM)
            ];
        }
        var solution = Newton(Residual, Initial(seed, wheelJounceM));
        return RearResult(geometry, wheelJounceM, solution,
            ["Rigid upper/lower two-pivot arms plus toe link. Spring and ARB pickups follow the solved lower-arm rotation. Extra links must be declared as checks; an overconstrained graph is not silently fitted."]);
    }

    public RigidCornerTravelDerivative3D SolveTravel(MacPhersonCorner3D geometry, double wheelJounceM)
    {
        var center = Continue(geometry, wheelJounceM); return Derivative(center,
            h => Continue(geometry, wheelJounceM + h));
    }

    public RigidCornerTravelDerivative3D SolveTravel(DoubleWishboneCorner3D geometry, double wheelJounceM)
    {
        var center = Continue(geometry, wheelJounceM); return Derivative(center,
            h => Continue(geometry, wheelJounceM + h));
    }

    private RigidCornerPose3D Continue(MacPhersonCorner3D geometry, double target)
    {
        var pose = Solve(geometry,0); var count = Math.Max(1,(int)Math.Ceiling(Math.Abs(target)/.005));
        for (var i=1;i<=count;i++) pose=Solve(geometry,target*i/count,pose.UprightPose);
        return pose;
    }

    private RigidCornerPose3D Continue(DoubleWishboneCorner3D geometry, double target)
    {
        var pose = Solve(geometry,0); var count = Math.Max(1,(int)Math.Ceiling(Math.Abs(target)/.005));
        for (var i=1;i<=count;i++) pose=Solve(geometry,target*i/count,pose.UprightPose);
        return pose;
    }

    private static RigidCornerTravelDerivative3D Derivative(RigidCornerPose3D center, Func<double, RigidCornerPose3D> offset)
    {
        var plus = offset(DifferenceStepM); var minus = offset(-DifferenceStepM);
        static bool Usable(RigidCornerPose3D pose) => pose.Converged || pose.MaximumConstraintResidualM < 2e-4;
        if (!Usable(center) || !Usable(plus) || !Usable(minus))
            return new(center, double.NaN, double.NaN, new(float.NaN), "invalid", [$"Central travel continuation did not converge on all three poses (center {center.MaximumConstraintResidualM:G3} m, plus {plus.MaximumConstraintResidualM:G3} m, minus {minus.MaximumConstraintResidualM:G3} m)."]);
        // Spring compression is reference length minus current length.
        var ds = -(plus.SpringLengthM - minus.SpringLengthM) / (2 * DifferenceStepM);
        var d2s = -(plus.SpringLengthM - 2 * center.SpringLengthM + minus.SpringLengthM) / (DifferenceStepM * DifferenceStepM);
        var ap = plus.SolvedPoints["arbLinkLower"]; var am = minus.SolvedPoints["arbLinkLower"];
        var arb = (ap - am) / (float)(2 * DifferenceStepM);
        return new(center, ds, d2s, arb, "geometry-derived-3d", ["Derivatives use centered continuation of the exact rigid constraints."]);
    }

    private static RigidCornerPose3D Result(string id, double jounce, NewtonResult solution, RigidUprightPoints3D u, Vector3 springUpper, string topology, IReadOnlyList<string> diagnostics)
    {
        var pose = State(solution.Q);
        var points = new Dictionary<string, Vector3>(StringComparer.Ordinal)
        {
            ["wheelCenter"] = Transform(u.WheelCenter, u.WheelCenter, pose),
            ["lowerBallJoint"] = Transform(u.LowerBallJoint, u.WheelCenter, pose),
            ["upperBallJoint"] = Transform(u.UpperBallJoint, u.WheelCenter, pose),
            ["tieRodOuter"] = Transform(u.TieRodOuter, u.WheelCenter, pose),
            ["strutLower"] = Transform(u.StrutLower, u.WheelCenter, pose),
            ["springLower"] = Transform(u.SpringLower, u.WheelCenter, pose),
            ["arbLinkLower"] = Transform(u.ArbLinkLower, u.WheelCenter, pose)
        };
        return new(id, jounce, pose, points, Vector3.Distance(springUpper, points["springLower"]), solution.MaxResidual, solution.Iterations,
            solution.Converged, topology, solution.Converged ? diagnostics : [.. diagnostics, "Constraint solve did not converge; do not use this pose for forces."]);
    }

    private static RigidCornerPose3D RearResult(DoubleWishboneCorner3D geometry, double jounce, NewtonResult solution, IReadOnlyList<string> diagnostics)
    {
        var u = geometry.Upright; var pose = State(solution.Q);
        var lowerSolved = Transform(u.LowerBallJoint, u.WheelCenter, pose);
        var axis = Unit(geometry.LowerArmRearPivot - geometry.LowerArmFrontPivot);
        var referenceRadial = Reject(u.LowerBallJoint - geometry.LowerArmFrontPivot, axis);
        var solvedRadial = Reject(lowerSolved - geometry.LowerArmFrontPivot, axis);
        var angle = MathF.Atan2(Vector3.Dot(axis, Vector3.Cross(referenceRadial, solvedRadial)), Vector3.Dot(referenceRadial, solvedRadial));
        var springLower = RotateAboutAxis(geometry.SpringLowerOnLowerArm, geometry.LowerArmFrontPivot, axis, angle);
        var arbLower = RotateAboutAxis(geometry.ArbLinkLowerOnLowerArm, geometry.LowerArmFrontPivot, axis, angle);
        var points = new Dictionary<string, Vector3>(StringComparer.Ordinal)
        {
            ["wheelCenter"] = Transform(u.WheelCenter, u.WheelCenter, pose),
            ["lowerBallJoint"] = lowerSolved,
            ["upperBallJoint"] = Transform(u.UpperBallJoint, u.WheelCenter, pose),
            ["tieRodOuter"] = Transform(u.TieRodOuter, u.WheelCenter, pose),
            ["strutLower"] = lowerSolved,
            ["springLower"] = springLower,
            ["arbLinkLower"] = arbLower
        };
        return new(geometry.Id, jounce, pose, points, Vector3.Distance(geometry.SpringUpper, springLower), solution.MaxResidual, solution.Iterations,
            solution.Converged, "double-wishbone-multilink-3d", solution.Converged ? diagnostics : [.. diagnostics, "Constraint solve did not converge; do not use this pose for forces."]);
    }

    private static NewtonResult Newton(Func<double[], double[]> residual, double[] q)
    {
        for (var iteration = 0; iteration < 40; iteration++)
        {
            var r = residual(q); var max = r.Max(Math.Abs);
            // System.Numerics.Vector3 stores single-precision coordinates; metre-scale hard
            // points therefore have a practical closure floor around 1e-8 to 1e-7 m.
            if (max < 1e-5) return new(q, max, iteration, true);
            var jacobian = new double[6, 6];
            for (var c = 0; c < 6; c++)
            {
                var step = c < 3 ? 1e-6 : 1e-6;
                var qp = (double[])q.Clone(); var qm = (double[])q.Clone(); qp[c] += step; qm[c] -= step;
                var rp = residual(qp); var rm = residual(qm);
                for (var row = 0; row < 6; row++) jacobian[row, c] = (rp[row] - rm[row]) / (2 * step);
            }
            double[] dq;
            try { dq = SolveLinear(jacobian, r); }
            catch (InvalidOperationException) { return new(q, max, iteration, false); }
            var scale = 1.0; var accepted = false;
            while (scale >= 1.0 / 64)
            {
                var candidate = q.Select((x, i) => x - scale * dq[i]).ToArray();
                if (residual(candidate).Max(Math.Abs) < max) { q = candidate; accepted = true; break; }
                scale *= .5;
            }
            if (!accepted) return new(q, max, iteration, false);
        }
        var finalResidual = residual(q).Max(Math.Abs); return new(q, finalResidual, 40, false);
    }

    private static double[] SolveLinear(double[,] a, double[] b)
    {
        var n = b.Length; var m = new double[n, n + 1];
        for (var r = 0; r < n; r++) { for (var c = 0; c < n; c++) m[r,c] = a[r,c]; m[r,n] = b[r]; }
        for (var p = 0; p < n; p++)
        {
            var pivot = Enumerable.Range(p, n-p).OrderByDescending(r => Math.Abs(m[r,p])).First();
            if (Math.Abs(m[pivot,p]) < 1e-10) throw new InvalidOperationException("Mechanism Jacobian is singular or underconstrained.");
            if (pivot != p) for (var c = p; c <= n; c++) (m[p,c],m[pivot,c]) = (m[pivot,c],m[p,c]);
            for (var r = p+1; r < n; r++) { var f = m[r,p]/m[p,p]; for (var c = p; c <= n; c++) m[r,c] -= f*m[p,c]; }
        }
        var x = new double[n];
        for (var r = n-1; r >= 0; r--) { var sum = m[r,n]; for (var c = r+1; c < n; c++) sum -= m[r,c]*x[c]; x[r] = sum/m[r,r]; }
        return x;
    }

    private static double[] Initial(RigidPoseState3D? seed, double jounce) => seed is null
        ? [0, 0, jounce, 0, 0, 0]
        : [seed.TranslationM.X, seed.TranslationM.Y, seed.TranslationM.Z, seed.RotationVectorRad.X, seed.RotationVectorRad.Y, seed.RotationVectorRad.Z];
    private static RigidPoseState3D State(double[] q) => new(new((float)q[0],(float)q[1],(float)q[2]), new((float)q[3],(float)q[4],(float)q[5]));
    private static Vector3 Transform(Vector3 point, Vector3 origin, RigidPoseState3D pose) => origin + pose.TranslationM + Rotate(point-origin, pose.RotationVectorRad);
    private static Vector3 Rotate(Vector3 vector, Vector3 rotationVector)
    {
        var angle = rotationVector.Length(); if (angle < 1e-14f) return vector;
        return Vector3.Transform(vector, Quaternion.CreateFromAxisAngle(rotationVector/angle, angle));
    }
    private static Vector3 Reject(Vector3 vector, Vector3 axis) => vector - axis * Vector3.Dot(vector, axis);
    private static Vector3 RotateAboutAxis(Vector3 point, Vector3 axisPoint, Vector3 axis, float angle) => axisPoint + Vector3.Transform(point-axisPoint, Quaternion.CreateFromAxisAngle(axis, angle));
    private static Vector3 Unit(Vector3 v) => v.Length() > 1e-9f ? Vector3.Normalize(v) : throw new ArgumentException("A non-zero vector is required.");
    private static (Vector3, Vector3) PerpendicularBasis(Vector3 axis)
    {
        var candidate = Math.Abs(axis.Z) < .9 ? Vector3.UnitZ : Vector3.UnitY;
        var first = Unit(Vector3.Cross(axis, candidate)); return (first, Unit(Vector3.Cross(axis, first)));
    }
    private static void ValidateFinite(double value) { if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value)); }
    private sealed record NewtonResult(double[] Q, double MaxResidual, int Iterations, bool Converged);
}
