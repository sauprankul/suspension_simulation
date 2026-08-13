namespace SuspensionSimulation.Core.Kinematics;

public sealed record Vector3Si(double X, double Y, double Z)
{
    public double Dot(Vector3Si other) => X * other.X + Y * other.Y + Z * other.Z;
    public double Magnitude => Math.Sqrt(Dot(this));
    public Vector3Si Unit()
    {
        if (!double.IsFinite(Magnitude) || Magnitude <= 1e-12) throw new InvalidOperationException("A non-zero finite vector is required.");
        return new(X / Magnitude, Y / Magnitude, Z / Magnitude);
    }
}

/// <summary>Local derivative supplied by a hard-point solver or measurement, never inferred from static points alone.</summary>
public sealed record SpringGeometryDerivative(Vector3Si SpringAxisUnit, Vector3Si RelativeSpringAttachmentDerivativePerWheelJounce);
public sealed record GeometryDerivedSpringTangent(double MotionRatio, double MotionRatioDerivativePerM, double WheelTangentNPerM, string Validity, IReadOnlyList<string> Warnings);

public static class GeometryDerivedSpringCalculator
{
    public static GeometryDerivedSpringTangent Evaluate(SpringGeometryDerivative derivative, double componentForceN, double componentTangentNPerM, double motionRatioDerivativePerM = 0)
    {
        ArgumentNullException.ThrowIfNull(derivative);
        if (!double.IsFinite(componentForceN) || !double.IsFinite(componentTangentNPerM) || componentTangentNPerM < 0 || !double.IsFinite(motionRatioDerivativePerM)) throw new ArgumentOutOfRangeException(nameof(componentTangentNPerM));
        var motionRatio = derivative.SpringAxisUnit.Unit().Dot(derivative.RelativeSpringAttachmentDerivativePerWheelJounce);
        return new(motionRatio, motionRatioDerivativePerM, componentTangentNPerM * motionRatio * motionRatio + componentForceN * motionRatioDerivativePerM,
            "local-only", ["Requires a kinematic derivative from a hard-point solver or measurement; static geometry alone is insufficient."]);
    }
}

/// <summary>Planar force-path output, not a complete roll-center migration, tire, or jacking model.</summary>
public sealed record RollCenterJackingResult(double RollCenterHeightM, double LateralForceN, double GeometricRollMomentNm,
    double PlanarEquivalentLeftRightLoadDifferenceN, string Validity, IReadOnlyList<string> Warnings);

public static class PlanarRollCenterForcePathCalculator
{
    /// <summary>For planar axle force path: moment=Fy*h, equivalent vertical load difference=2*moment/track.</summary>
    public static RollCenterJackingResult Evaluate(double rollCenterHeightM, double lateralForceN, double trackM)
    {
        if (!double.IsFinite(rollCenterHeightM) || !double.IsFinite(lateralForceN) || !double.IsFinite(trackM) || trackM <= 0) throw new ArgumentOutOfRangeException(nameof(trackM));
        var moment = lateralForceN * rollCenterHeightM;
        return new(rollCenterHeightM, lateralForceN, moment, 2 * moment / trackM, "planar-force-path-only",
            ["Not a full jacking prediction: contact-patch force paths, roll-center migration, compliance, and tire effects are excluded."]);
    }
}
