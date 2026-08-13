namespace SuspensionSimulation.Core.Dashboard;

/// <summary>
/// Screen-space mapping for the Load Lab G-circle. The circle is intentionally
/// capped at a configurable magnitude: inputs beyond the cap are clamped, never
/// silently re-scaled. Inputs retain the canonical vehicle meaning (+X forward longitudinal,
/// +Y left lateral); they are rendered as +longitudinal downward and +lateral right. Screen Y
/// increases downward, hence the positive Y mapping. These display signs are independent of
/// load-transfer sign conventions inside the solver.
/// </summary>
public static class GCircleMapper
{
    /// <summary>
    /// Limits each requested acceleration component to the sweep cap, then limits their vector
    /// magnitude to the total-G cap. This order makes the rectangular sweep boundary and the
    /// circular resultant boundary both explicit rather than silently treating them as one cap.
    /// </summary>
    public static GCirclePoint Map(double longitudinalG, double lateralG, double radiusPixels,
        double axisSweepCapG, double totalGCap)
    {
        if (!double.IsFinite(axisSweepCapG) || axisSweepCapG <= 0) throw new ArgumentOutOfRangeException(nameof(axisSweepCapG));
        if (!double.IsFinite(totalGCap) || totalGCap <= 0) throw new ArgumentOutOfRangeException(nameof(totalGCap));
        var longitudinal = Math.Clamp(longitudinalG, -axisSweepCapG, axisSweepCapG);
        var lateral = Math.Clamp(lateralG, -axisSweepCapG, axisSweepCapG);
        var magnitude = Math.Sqrt(longitudinal * longitudinal + lateral * lateral);
        var factor = magnitude <= totalGCap || magnitude == 0 ? 1.0 : totalGCap / magnitude;
        longitudinal *= factor; lateral *= factor;
        return new GCirclePoint(
            X: lateral / axisSweepCapG * radiusPixels,
            Y: longitudinal / axisSweepCapG * radiusPixels,
            DisplayLongitudinalG: longitudinal,
            DisplayLateralG: lateral,
            WasClamped: factor < 1 || longitudinal != longitudinalG || lateral != lateralG);
    }

    /// <summary>Compatibility overload: a single cap bounds both the axis sweep and total magnitude.</summary>
    public static GCirclePoint Map(double longitudinalG, double lateralG, double radiusPixels, double maximumG = 2.0)
    {
        if (!double.IsFinite(longitudinalG) || !double.IsFinite(lateralG)) throw new ArgumentOutOfRangeException(nameof(longitudinalG), "G inputs must be finite.");
        if (!double.IsFinite(radiusPixels) || radiusPixels <= 0) throw new ArgumentOutOfRangeException(nameof(radiusPixels));
        return Map(longitudinalG, lateralG, radiusPixels, maximumG, maximumG);
    }
}

public sealed record GCirclePoint(double X, double Y, double DisplayLongitudinalG, double DisplayLateralG, bool WasClamped);

/// <summary>
/// Local tangent stiffness display for a corner. The ARB term is the derivative
/// for one-wheel motion while its opposite wheel is held fixed; it is not an
/// independent wheel spring because the bar force couples both corners.
/// </summary>
public sealed record CornerWheelStiffness(double SpringWheelRateNPerM,
    double ArbDifferentialContributionNPerM, double ActiveBumpStopRateNPerM,
    double TotalLocalTangentNPerM, string Validity, string Warning);

public static class CornerStiffnessCalculator
{
    public static CornerWheelStiffness Evaluate(double springWheelRateNPerM,
        double arbDifferentialContributionNPerM, double bumpStopRateNPerM, bool bumpStopActive)
    {
        if (!double.IsFinite(springWheelRateNPerM) || springWheelRateNPerM < 0) throw new ArgumentOutOfRangeException(nameof(springWheelRateNPerM));
        if (!double.IsFinite(arbDifferentialContributionNPerM) || arbDifferentialContributionNPerM < 0) throw new ArgumentOutOfRangeException(nameof(arbDifferentialContributionNPerM));
        if (!double.IsFinite(bumpStopRateNPerM) || bumpStopRateNPerM < 0) throw new ArgumentOutOfRangeException(nameof(bumpStopRateNPerM));
        var activeStop = bumpStopActive ? bumpStopRateNPerM : 0;
        return new(springWheelRateNPerM, arbDifferentialContributionNPerM, activeStop,
            springWheelRateNPerM + arbDifferentialContributionNPerM + activeStop,
            "estimated", "ARB contribution is a coupled differential local tangent (opposite wheel held fixed).");
    }
}

public sealed record DashboardMetric(string Id, string Label, string Unit, string Value, bool IsSelected, string Validity)
{
    // Solver-backed metric callers do not need a selection state; tables own it.
    public DashboardMetric(string id, string label, string unit, string value, string validity)
        : this(id, label, unit, value, true, validity) { }
}

/// <summary>Preserves caller order and rejects unknown/duplicate IDs rather than hiding selection mistakes.</summary>
public static class MetricSelection
{
    public static IReadOnlyList<DashboardMetric> SelectedOnly(IEnumerable<DashboardMetric> metrics) =>
        metrics.Where(x => x.IsSelected).ToArray();

    public static IReadOnlyList<DashboardMetric> ApplySelection(IEnumerable<DashboardMetric> metrics, IEnumerable<string> selectedIds)
    {
        var desired = selectedIds.ToHashSet(StringComparer.Ordinal);
        var available = metrics.ToArray();
        var known = available.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        var unknown = desired.Where(x => !known.Contains(x)).ToArray();
        if (unknown.Length > 0) throw new InvalidDataException($"Unknown dashboard metric IDs: {string.Join(", ", unknown)}.");
        return available.Select(x => x with { IsSelected = desired.Contains(x.Id) }).ToArray();
    }
}

public sealed record TireGlyph(string Corner, double XFraction, double YFraction, double NormalLoadN, bool IsLifted);

/// <summary>
/// UI-independent data contract for the Phase 14 shell. WPF, WinUI, or export code
/// can render it without owning mechanical calculations.
/// </summary>
public sealed record LoadLabDashboardState(
    IReadOnlyList<TireGlyph> Tires,
    GCirclePoint GCircle,
    IReadOnlyList<DashboardMetric> SidebarConfiguration,
    IReadOnlyList<DashboardMetric> MetricsTable,
    IReadOnlyList<string> Assumptions);
