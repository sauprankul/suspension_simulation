using System.Numerics;
using SuspensionSimulation.Components;
using SuspensionSimulation.Kinematics;

namespace SuspensionSimulation.Solvers.Kinematics;

/// <summary>
/// Rigid hard-point continuation for the deliberately bounded first MacPherson slice. It solves
/// a lower-arm length, strut length, and prescribed wheel-centre jounce in the front-view plane.
/// The unmodelled 3D tie-rod constraint is exported as a residual, never hidden by a fitted toe.
/// </summary>
public sealed class MacPhersonPlanarSolver
{
    public MacPhersonPlanarPose Solve(MacPhersonPlanarCornerConfig config, double wheelJounceM)
    {
        Validate(config, wheelJounceM);
        var centre = SolvePose(config, wheelJounceM);
        var diagnostics = new List<KinematicDiagnostic>();
        if (!centre.Converged)
        {
            diagnostics.Add(new(KinematicValidity.Invalid, "constraint-solve-failed", "Planar lower-arm/strut/jounce constraints did not converge."));
            return Build(config, wheelJounceM, centre, null, diagnostics);
        }

        const double derivativeStepM = 1e-5;
        var plus = SolvePose(config, wheelJounceM + derivativeStepM);
        var minus = SolvePose(config, wheelJounceM - derivativeStepM);
        TravelDependentMotionRatio? ratio = null;
        var plus2 = SolvePose(config, wheelJounceM + 2 * derivativeStepM);
        var minus2 = SolvePose(config, wheelJounceM - 2 * derivativeStepM);
        if (plus.Converged && minus.Converged && plus2.Converged && minus2.Converged)
        {
            var motionRatio = (plus.StrutCompressionM - minus.StrutCompressionM) / (2 * derivativeStepM);
            // Derivative of the local virtual-work motion ratio. Re-evaluate with a symmetric local stencil.
            var plusRatio = (plus2.StrutCompressionM - centre.StrutCompressionM) / (2 * derivativeStepM);
            var minusRatio = (centre.StrutCompressionM - minus2.StrutCompressionM) / (2 * derivativeStepM);
            var derivative = (plusRatio - minusRatio) / (2 * derivativeStepM);
            if (double.IsFinite(motionRatio) && double.IsFinite(derivative)) ratio = new(motionRatio, derivative);
            else diagnostics.Add(new(KinematicValidity.Unavailable, "motion-ratio-nonfinite", "Local geometry-derived motion ratio was non-finite."));
        }
        else diagnostics.Add(new(KinematicValidity.Unavailable, "motion-ratio-boundary", "Motion ratio unavailable because a finite-difference neighbour did not converge."));

        if (config.TieRodInnerM is not null && config.TieRodOuterReferenceM is not null)
            diagnostics.Add(new(KinematicValidity.Estimated, "tie-rod-residual", "Tie-rod length is a reported residual in this front-view slice; toe/bump steer requires the later 3D solve."));
        diagnostics.Add(new(KinematicValidity.Estimated, "planar-rigid-model", "Hard-point result is a rigid, planar front-view estimate; compliance, steering and out-of-plane motion are unavailable."));
        return Build(config, wheelJounceM, centre, ratio, diagnostics);
    }

    private static MacPhersonPlanarPose Build(MacPhersonPlanarCornerConfig c, double jounce, SolveState state,
        TravelDependentMotionRatio? ratio, IReadOnlyList<KinematicDiagnostic> diagnostics)
    {
        var rc = RollCentre(c, state);
        var jacking = rc.ForcePathVerticalPerLateralForce is { } ratioValue
            ? new JackingDiagnostic(ratioValue, KinematicValidity.Estimated,
                "Front-view force-path ratio only; no tire lateral force or jacking load is applied to the chassis solver.")
            : new JackingDiagnostic(null, KinematicValidity.Unavailable,
                "Jacking is unavailable because the front-view force path is ill-conditioned or no tire-force model is present.");
        var tieResidual = TieResidual(c, state);
        return new(c.Id, jounce, Point3(c.WheelCentreReferenceM, state.WheelCentre), Point3(c.LowerBallJointReferenceM, state.LowerBallJoint),
            Point3(c.StrutLowerReferenceM, state.StrutLower), state.ThetaRad, state.StrutLengthM, state.StrutCompressionM,
            ratio, tieResidual, rc, jacking, diagnostics, state.Converged);
    }

    private static SolveState SolvePose(MacPhersonPlanarCornerConfig c, double jounce)
    {
        var lowerArmLength = Distance(Yz(c.LowerArmInnerPivotM), Yz(c.LowerBallJointReferenceM));
        var strutLength = Distance(Yz(c.UpperStrutMountM), Yz(c.StrutLowerReferenceM));
        var q = new[] { Yz(c.LowerBallJointReferenceM).Y, Yz(c.LowerBallJointReferenceM).Z, 0.0 };
        for (var iteration = 0; iteration < 32; iteration++)
        {
            var state = State(c, q, strutLength);
            var residual = new[]
            {
                Distance(Yz(c.LowerArmInnerPivotM), state.LowerBallJoint) - lowerArmLength,
                Distance(Yz(c.UpperStrutMountM), state.StrutLower) - strutLength,
                state.WheelCentre.Z - Yz(c.WheelCentreReferenceM).Z - jounce
            };
            if (residual.Max(Math.Abs) <= c.SolverToleranceM) return state with { Converged = true };
            var jacobian = new double[3, 3];
            var steps = new[] { 1e-6, 1e-6, 1e-6 };
            for (var column = 0; column < 3; column++)
            {
                var plus = (double[])q.Clone(); var minus = (double[])q.Clone(); plus[column] += steps[column]; minus[column] -= steps[column];
                var rp = Residual(c, plus, strutLength, lowerArmLength, jounce); var rm = Residual(c, minus, strutLength, lowerArmLength, jounce);
                for (var row = 0; row < 3; row++) jacobian[row, column] = (rp[row] - rm[row]) / (2 * steps[column]);
            }
            try
            {
                var step = Solve3(jacobian, residual);
                for (var i = 0; i < 3; i++) q[i] -= step[i];
            }
            catch (InvalidOperationException) { break; }
        }
        return State(c, q, strutLength) with { Converged = false };
    }

    private static double[] Residual(MacPhersonPlanarCornerConfig c, double[] q, double strutLength, double lowerArmLength, double jounce)
    {
        var state = State(c, q, strutLength);
        return [Distance(Yz(c.LowerArmInnerPivotM), state.LowerBallJoint) - lowerArmLength,
            Distance(Yz(c.UpperStrutMountM), state.StrutLower) - strutLength,
            state.WheelCentre.Z - Yz(c.WheelCentreReferenceM).Z - jounce];
    }

    private static SolveState State(MacPhersonPlanarCornerConfig c, double[] q, double referenceStrutLength)
    {
        var lower = new Point2(q[0], q[1]); var theta = q[2];
        var strut = lower + Rotate(Yz(c.StrutLowerReferenceM) - Yz(c.LowerBallJointReferenceM), theta);
        var wheel = lower + Rotate(Yz(c.WheelCentreReferenceM) - Yz(c.LowerBallJointReferenceM), theta);
        var length = Distance(Yz(c.UpperStrutMountM), strut);
        return new(lower, strut, wheel, theta, length, referenceStrutLength - length, false);
    }

    private static PlanarRollCentreDiagnostic RollCentre(MacPhersonPlanarCornerConfig c, SolveState state)
    {
        if (!TryIntersect(Yz(c.LowerArmInnerPivotM), state.LowerBallJoint, Yz(c.UpperStrutMountM), state.StrutLower, out var instant))
            return new(null, null, KinematicValidity.Unavailable, "Lower-arm and strut projected axes are parallel; front-view instant centre is unavailable.");
        var patch = new Point2(state.WheelCentre.Y, c.GroundPlaneZM);
        if (Math.Abs(instant.Y - patch.Y) < 1e-9)
            return new(null, null, KinematicValidity.Unavailable, "Force path is vertical in front view; centre-plane roll-centre intersection is undefined.");
        var t = -patch.Y / (instant.Y - patch.Y); var centreHeight = patch.Z + t * (instant.Z - patch.Z);
        return new(centreHeight, (instant.Z - patch.Z) / (instant.Y - patch.Y), KinematicValidity.Estimated,
            "Front-view projected force-line construction only; this is not yet a full axle roll-centre/jacking solution.");
    }

    private static double? TieResidual(MacPhersonPlanarCornerConfig c, SolveState state)
    {
        if (c.TieRodInnerM is null || c.TieRodOuterReferenceM is null) return null;
        var reference = Distance(Yz(c.TieRodInnerM.Value), Yz(c.TieRodOuterReferenceM.Value));
        var outer = state.LowerBallJoint + Rotate(Yz(c.TieRodOuterReferenceM.Value) - Yz(c.LowerBallJointReferenceM), state.ThetaRad);
        return Distance(Yz(c.TieRodInnerM.Value), outer) - reference;
    }

    private static Point2 Yz(Vector3 point) => new(point.Y, point.Z);
    private static Vector3 Point3(Vector3 reference, Point2 point) => new(reference.X, (float)point.Y, (float)point.Z);
    private static Point2 Rotate(Point2 p, double theta) => new(Math.Cos(theta) * p.Y - Math.Sin(theta) * p.Z, Math.Sin(theta) * p.Y + Math.Cos(theta) * p.Z);
    private static double Distance(Point2 a, Point2 b) => Math.Sqrt(Math.Pow(a.Y - b.Y, 2) + Math.Pow(a.Z - b.Z, 2));
    private static bool TryIntersect(Point2 a, Point2 b, Point2 c, Point2 d, out Point2 point)
    {
        var r = b - a; var s = d - c; var determinant = Cross(r, s);
        if (Math.Abs(determinant) < 1e-10) { point = default; return false; }
        var t = Cross(c - a, s) / determinant; point = a + t * r; return true;
    }
    private static double Cross(Point2 a, Point2 b) => a.Y * b.Z - a.Z * b.Y;
    private static double[] Solve3(double[,] a, double[] b)
    {
        var m = new double[3, 4];
        for (var r = 0; r < 3; r++) { for (var col = 0; col < 3; col++) m[r, col] = a[r, col]; m[r, 3] = b[r]; }
        for (var pivotColumn = 0; pivotColumn < 3; pivotColumn++)
        {
            var pivot = Enumerable.Range(pivotColumn, 3 - pivotColumn).OrderByDescending(r => Math.Abs(m[r, pivotColumn])).First();
            if (Math.Abs(m[pivot, pivotColumn]) < 1e-12) throw new InvalidOperationException("Planar hard-point Jacobian is singular.");
            if (pivot != pivotColumn) for (var col = pivotColumn; col < 4; col++) (m[pivotColumn, col], m[pivot, col]) = (m[pivot, col], m[pivotColumn, col]);
            for (var r = pivotColumn + 1; r < 3; r++) { var scale = m[r, pivotColumn] / m[pivotColumn, pivotColumn]; for (var col = pivotColumn; col < 4; col++) m[r, col] -= scale * m[pivotColumn, col]; }
        }
        var x = new double[3];
        for (var r = 2; r >= 0; r--) x[r] = (m[r, 3] - Enumerable.Range(r + 1, 2 - r).Sum(col => m[r, col] * x[col])) / m[r, r];
        return x;
    }
    private static void Validate(MacPhersonPlanarCornerConfig c, double jounce)
    {
        ArgumentNullException.ThrowIfNull(c); ArgumentException.ThrowIfNullOrWhiteSpace(c.Id);
        if (!double.IsFinite(jounce) || !double.IsFinite(c.GroundPlaneZM) || !double.IsFinite(c.SolverToleranceM) || c.SolverToleranceM <= 0)
            throw new ArgumentOutOfRangeException(nameof(c), "Jounce, ground plane, and tolerance must be finite; tolerance must be positive.");
        if (Distance(Yz(c.LowerArmInnerPivotM), Yz(c.LowerBallJointReferenceM)) <= 1e-8 || Distance(Yz(c.UpperStrutMountM), Yz(c.StrutLowerReferenceM)) <= 1e-8)
            throw new ArgumentException("Lower-arm and strut reference lengths must be non-zero.", nameof(c));
    }

    private readonly record struct Point2(double Y, double Z)
    {
        public static Point2 operator +(Point2 a, Point2 b) => new(a.Y + b.Y, a.Z + b.Z);
        public static Point2 operator -(Point2 a, Point2 b) => new(a.Y - b.Y, a.Z - b.Z);
        public static Point2 operator *(double scalar, Point2 a) => new(scalar * a.Y, scalar * a.Z);
    }
    private sealed record SolveState(Point2 LowerBallJoint, Point2 StrutLower, Point2 WheelCentre, double ThetaRad,
        double StrutLengthM, double StrutCompressionM, bool Converged);
}
