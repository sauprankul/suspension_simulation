namespace SuspensionSimulation.Metrics;

/// <summary>
/// Keeps frequently conflated quantities distinct. Neither stiffness distribution nor LLTD is a tire-grip or understeer prediction.
/// All input loads are non-negative left/right load-transfer magnitudes for the same operating point.
/// </summary>
public sealed record BalanceSnapshot(
    double RollStiffnessDistributionFront,
    double LoadTransferDistributionFront,
    double StaticFrontMassFraction,
    double RollStiffnessMinusMassFraction);

public static class BalanceMetrics
{
    public static double RollStiffnessDistributionFront(double frontRollStiffnessNmPerRad, double rearRollStiffnessNmPerRad) =>
        Fraction(frontRollStiffnessNmPerRad, rearRollStiffnessNmPerRad, nameof(frontRollStiffnessNmPerRad), nameof(rearRollStiffnessNmPerRad));

    /// <summary>Front share of total lateral load transfer. Undefined (NaN) if no transfer exists.</summary>
    public static double LoadTransferDistributionFront(double frontTransferN, double rearTransferN) =>
        Fraction(frontTransferN, rearTransferN, nameof(frontTransferN), nameof(rearTransferN));

    public static BalanceSnapshot Create(double frontRollStiffnessNmPerRad, double rearRollStiffnessNmPerRad,
        double frontTransferN, double rearTransferN, double staticFrontMassFraction)
    {
        if (!double.IsFinite(staticFrontMassFraction) || staticFrontMassFraction is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(staticFrontMassFraction), "Must be a fraction in [0, 1].");
        var rsd = RollStiffnessDistributionFront(frontRollStiffnessNmPerRad, rearRollStiffnessNmPerRad);
        var lltd = LoadTransferDistributionFront(frontTransferN, rearTransferN);
        return new BalanceSnapshot(rsd, lltd, staticFrontMassFraction, double.IsNaN(rsd) ? double.NaN : rsd - staticFrontMassFraction);
    }

    private static double Fraction(double front, double rear, string frontName, string rearName)
    {
        if (double.IsNaN(front) || front < 0) throw new ArgumentOutOfRangeException(frontName, "Must be non-negative (positive infinity denotes a rigid constraint).");
        if (double.IsNaN(rear) || rear < 0) throw new ArgumentOutOfRangeException(rearName, "Must be non-negative (positive infinity denotes a rigid constraint).");
        if(double.IsPositiveInfinity(front)) return double.IsPositiveInfinity(rear)?double.NaN:1;
        if(double.IsPositiveInfinity(rear)) return 0;
        var sum = front + rear;
        return sum == 0 ? double.NaN : front / sum;
    }
}
