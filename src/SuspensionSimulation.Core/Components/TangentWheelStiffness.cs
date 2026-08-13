namespace SuspensionSimulation.Components;

public enum ComponentTangentValidity { Valid, Warning, Invalid }
public sealed record ComponentTangentDiagnostic(ComponentTangentValidity Validity, string Code, string Message);

// Compatibility contract for the existing four-corner solver. New component calculators use
// the explicitly prefixed types below to avoid conflating their component-local diagnostics.
public enum TangentValidity { Information, Valid, Warning, Invalid }
public sealed record TangentDiagnostic(TangentValidity Validity, string Code, string Message);
public sealed record CornerTangentWheelStiffness(
    string Corner, double SpringOnlyNPerM, double BumpStopOnlyNPerM,
    double ArbSelfNPerM, double ArbCrossToOppositeWheelNPerM, double TotalSelfNPerM,
    TangentValidity Validity, IReadOnlyList<TangentDiagnostic> Diagnostics,
    double ReferenceRelativeCompressionM = double.NaN,
    string PerturbationConvention = "positive wheel-centre jounce; opposite wheel held fixed");

/// <summary>
/// Local kinematic derivative at one operating point. MotionRatio is d(component compression)/d(wheel jounce).
/// MotionRatioDerivativePerM captures the force-times-curvature term in virtual-work tangent stiffness.
/// </summary>
public sealed record TravelDependentMotionRatio(double MotionRatio, double MotionRatioDerivativePerM = 0);

public sealed record ComponentCornerTangentWheelStiffness(
    string Corner,
    double SpringOnlyNPerM,
    double BumpStopOnlyNPerM,
    double ArbSelfNPerM,
    double ArbCrossToOppositeWheelNPerM,
    double TotalSelfNPerM,
    ComponentTangentValidity Validity,
    IReadOnlyList<ComponentTangentDiagnostic> Diagnostics,
    double ReferenceRelativeCompressionM = double.NaN,
    string PerturbationConvention = "positive wheel-centre jounce; opposite wheel held fixed");

public sealed record ComponentAxleTangentWheelStiffness(
    ComponentCornerTangentWheelStiffness Left,
    ComponentCornerTangentWheelStiffness Right,
    double[,] TotalStiffnessMatrixNPerM,
    IReadOnlyList<ComponentTangentDiagnostic> Diagnostics);

/// <summary>
/// Conservative local tangent calculator. "Self" is the diagonal d(resisting wheel force)/d(own jounce);
/// cross is d(resisting wheel force)/d(opposite jounce). For an ideal ARB cross terms are negative.
/// This is a local derivative, not a finite-travel suspension kinematic solution.
/// </summary>
public static class TangentWheelStiffnessCalculator
{
    public static ComponentCornerTangentWheelStiffness EvaluateCorner(string corner, Spring spring, BumpStop? bumpStop,
        double componentCompressionM, TravelDependentMotionRatio motion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(corner); ArgumentNullException.ThrowIfNull(spring); ArgumentNullException.ThrowIfNull(motion);
        ValidateMotion(motion);
        var springValue = spring.EvaluateCompression(componentCompressionM);
        var stopValue = bumpStop?.EvaluateCompression(componentCompressionM) ?? new ElementEvaluation(0, 0, false);
        var springTangent = WheelTangent(springValue, motion);
        var stopTangent = WheelTangent(stopValue, motion);
        var diagnostics = new List<ComponentTangentDiagnostic>();
        if (springValue.IsExtrapolated || stopValue.IsExtrapolated) diagnostics.Add(new(ComponentTangentValidity.Warning, "curve-extrapolation", "At least one component curve is extrapolated at this operating point."));
        var validity = diagnostics.Count == 0 ? ComponentTangentValidity.Valid : ComponentTangentValidity.Warning;
        return new(corner, springTangent, stopTangent, 0, 0, springTangent + stopTangent, validity, diagnostics);
    }

    /// <summary>Direct installed pair rate yields K[[1,-1],[-1,1]] at the wheel plane.</summary>
    public static ComponentAxleTangentWheelStiffness WithDirectArb(ComponentCornerTangentWheelStiffness left, ComponentCornerTangentWheelStiffness right, double installedWheelPairRateNPerM)
    {
        Guard.NonNegative(nameof(installedWheelPairRateNPerM), installedWheelPairRateNPerM);
        return Combine(left, right, installedWheelPairRateNPerM, -installedWheelPairRateNPerM, -installedWheelPairRateNPerM, installedWheelPairRateNPerM, []);
    }

    /// <summary>
    /// Tubular bar Hessian from U=1/2*K*(aL*qL-aR*qR)^2, where a is declared link vertical motion ratio / arm length.
    /// Link angle affects reported axial link force only; it does not alter this vertical virtual-work stiffness.
    /// </summary>
    public static ComponentAxleTangentWheelStiffness WithTubularArb(ComponentCornerTangentWheelStiffness left, ComponentCornerTangentWheelStiffness right, TubularAntiRollBar bar)
    {
        ArgumentNullException.ThrowIfNull(bar);
        var p = bar.Properties;
        // Recover geometry only through an explicit local geometric contract, never infer it from link length.
        var g = bar.Geometry;
        var aLeft = g.Left.WheelToEndLinkVerticalMotionRatio * Math.Pow(Math.Cos(g.Left.EndLinkAngleFromVerticalRad), 2) / g.Left.LeverArmM;
        var aRight = g.Right.WheelToEndLinkVerticalMotionRatio * Math.Pow(Math.Cos(g.Right.EndLinkAngleFromVerticalRad), 2) / g.Right.LeverArmM;
        var diagnostics = new List<ComponentTangentDiagnostic>();
        AddAngleDiagnostic(g.Left.EndLinkAngleFromVerticalRad, "left", diagnostics);
        AddAngleDiagnostic(g.Right.EndLinkAngleFromVerticalRad, "right", diagnostics);
        var kLl = p.TorsionalStiffnessNmPerRad * aLeft * aLeft;
        var kRr = p.TorsionalStiffnessNmPerRad * aRight * aRight;
        var kLr = -p.TorsionalStiffnessNmPerRad * aLeft * aRight;
        return Combine(left, right, kLl, kLr, kLr, kRr, diagnostics);
    }

    private static ComponentAxleTangentWheelStiffness Combine(ComponentCornerTangentWheelStiffness left, ComponentCornerTangentWheelStiffness right,
        double arbLl, double arbLr, double arbRl, double arbRr, IReadOnlyList<ComponentTangentDiagnostic> extra)
    {
        var diagnostics = left.Diagnostics.Concat(right.Diagnostics).Concat(extra).ToArray();
        var validity = diagnostics.Any(x => x.Validity == ComponentTangentValidity.Invalid) ? ComponentTangentValidity.Invalid : diagnostics.Any(x => x.Validity == ComponentTangentValidity.Warning) ? ComponentTangentValidity.Warning : ComponentTangentValidity.Valid;
        var l = left with { ArbSelfNPerM = arbLl, ArbCrossToOppositeWheelNPerM = arbLr, TotalSelfNPerM = left.SpringOnlyNPerM + left.BumpStopOnlyNPerM + arbLl, Validity = validity, Diagnostics = diagnostics };
        var r = right with { ArbSelfNPerM = arbRr, ArbCrossToOppositeWheelNPerM = arbRl, TotalSelfNPerM = right.SpringOnlyNPerM + right.BumpStopOnlyNPerM + arbRr, Validity = validity, Diagnostics = diagnostics };
        return new(l, r, new[,] { { l.TotalSelfNPerM, arbLr }, { arbRl, r.TotalSelfNPerM } }, diagnostics);
    }

    private static double WheelTangent(ElementEvaluation component, TravelDependentMotionRatio motion) => component.TangentRateNPerM * motion.MotionRatio * motion.MotionRatio + component.ForceN * motion.MotionRatioDerivativePerM;
    private static void ValidateMotion(TravelDependentMotionRatio motion)
    {
        if (!double.IsFinite(motion.MotionRatio) || !double.IsFinite(motion.MotionRatioDerivativePerM)) throw new ArgumentOutOfRangeException(nameof(motion), "Motion-ratio values must be finite.");
    }
    private static void AddAngleDiagnostic(double angle, string side, ICollection<ComponentTangentDiagnostic> diagnostics)
    {
        var cosine = Math.Abs(Math.Cos(angle));
        if (cosine < 1e-4) diagnostics.Add(new(ComponentTangentValidity.Invalid, $"{side}-endlink-singular", "End link is effectively horizontal; axial force is singular and no stiffness result is valid."));
        else if (cosine < 0.1) diagnostics.Add(new(ComponentTangentValidity.Warning, $"{side}-endlink-near-singular", "End link is near horizontal; axial-force reporting is poorly conditioned. Vertical virtual-work stiffness remains idealized."));
    }
}
