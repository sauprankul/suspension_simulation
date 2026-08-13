namespace SuspensionSimulation.Metrics;

public enum LoadLabMetricId
{
    RollStiffnessDistributionFront,
    LoadTransferDistributionFront,
    StaticFrontMassFraction,
    RollCoupleBiasPercentagePoints,
    FrontRollStiffnessNmPerRad,
    RearRollStiffnessNmPerRad,
    FrontLoadTransferN,
    RearLoadTransferN
}

/// <summary>Solver-independent input shape: the solver owns equilibrium, this class owns named metric presentation values.</summary>
public sealed record QuasiStaticMetricInput(
    double FrontRollStiffnessNmPerRad,
    double RearRollStiffnessNmPerRad,
    double FrontLoadTransferN,
    double RearLoadTransferN,
    double StaticFrontMassFraction);

public sealed record ComputedMetric(LoadLabMetricId Id, string Label, string Unit, double Value, bool IsDefined);

public static class SelectedMetricComputation
{
    public static IReadOnlyList<ComputedMetric> Compute(QuasiStaticMetricInput input, IEnumerable<LoadLabMetricId> selected)
    {
        ArgumentNullException.ThrowIfNull(input); ArgumentNullException.ThrowIfNull(selected);
        var snapshot = BalanceMetrics.Create(input.FrontRollStiffnessNmPerRad, input.RearRollStiffnessNmPerRad,
            input.FrontLoadTransferN, input.RearLoadTransferN, input.StaticFrontMassFraction);
        return selected.Distinct().Select(id => id switch
        {
            LoadLabMetricId.RollStiffnessDistributionFront => Percent(id, "Front RSD", snapshot.RollStiffnessDistributionFront),
            LoadLabMetricId.LoadTransferDistributionFront => Percent(id, "Front LLTD", snapshot.LoadTransferDistributionFront),
            LoadLabMetricId.StaticFrontMassFraction => Percent(id, "Static front mass fraction", snapshot.StaticFrontMassFraction),
            LoadLabMetricId.RollCoupleBiasPercentagePoints => new(id, "RSD − static front mass", "percentage points", 100 * snapshot.RollStiffnessMinusMassFraction, !double.IsNaN(snapshot.RollStiffnessMinusMassFraction)),
            LoadLabMetricId.FrontRollStiffnessNmPerRad => new(id, "Front roll stiffness", "N·m/rad", input.FrontRollStiffnessNmPerRad, true),
            LoadLabMetricId.RearRollStiffnessNmPerRad => new(id, "Rear roll stiffness", "N·m/rad", input.RearRollStiffnessNmPerRad, true),
            LoadLabMetricId.FrontLoadTransferN => new(id, "Front lateral load transfer", "N", input.FrontLoadTransferN, true),
            LoadLabMetricId.RearLoadTransferN => new(id, "Rear lateral load transfer", "N", input.RearLoadTransferN, true),
            _ => throw new ArgumentOutOfRangeException(nameof(selected), id, "Unknown metric.")
        }).ToArray();
    }
    private static ComputedMetric Percent(LoadLabMetricId id, string label, double fraction) => new(id, label, "%", 100 * fraction, !double.IsNaN(fraction));
}
