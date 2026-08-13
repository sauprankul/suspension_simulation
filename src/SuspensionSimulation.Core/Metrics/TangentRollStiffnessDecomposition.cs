namespace SuspensionSimulation.Metrics;

/// <summary>
/// Current operating-point tangent roll stiffness, separated by conservative element type.
/// Values are N·m/rad. The total is the only stiffness value used for RSD/RCB.
/// </summary>
public sealed record TangentRollStiffnessDecomposition(
    string Axle,
    double SpringNmPerRad,
    double ArbNmPerRad,
    double BumpStopNmPerRad,
    double TotalNmPerRad,
    string OperatingPoint,
    IReadOnlyList<string> Warnings);

public static class TangentRollStiffnessCalculator
{
    /// <summary>
    /// Calculates yᵀKy for small roll. `leftY/rightY` are contact-plane lateral coordinates,
    /// spring/stop rates are diagonal wheel-plane tangents, and ARB terms form its 2x2 Hessian.
    /// </summary>
    public static TangentRollStiffnessDecomposition FromWheelPlane(string axle, double leftY, double rightY,
        double leftSpringNPerM, double rightSpringNPerM, double leftStopNPerM, double rightStopNPerM,
        double arbLeftSelfNPerM, double arbLeftCrossNPerM, double arbRightCrossNPerM, double arbRightSelfNPerM,
        string operatingPoint, IEnumerable<string>? warnings = null)
    {
        var values = new[] { leftY, rightY, leftSpringNPerM, rightSpringNPerM, leftStopNPerM, rightStopNPerM, arbLeftSelfNPerM, arbLeftCrossNPerM, arbRightCrossNPerM, arbRightSelfNPerM };
        if (values.Any(x => !double.IsFinite(x))) throw new ArgumentOutOfRangeException(nameof(leftY), "Stiffness inputs must be finite.");
        if (leftSpringNPerM < 0 || rightSpringNPerM < 0 || leftStopNPerM < 0 || rightStopNPerM < 0) throw new ArgumentOutOfRangeException(nameof(leftSpringNPerM), "Spring and bump-stop tangents must be non-negative.");
        var spring = leftSpringNPerM * leftY * leftY + rightSpringNPerM * rightY * rightY;
        var stop = leftStopNPerM * leftY * leftY + rightStopNPerM * rightY * rightY;
        var arb = arbLeftSelfNPerM * leftY * leftY + (arbLeftCrossNPerM + arbRightCrossNPerM) * leftY * rightY + arbRightSelfNPerM * rightY * rightY;
        var total = spring + stop + arb;
        return new(axle, spring, arb, stop, total, operatingPoint, warnings?.ToArray() ?? []);
    }
}
