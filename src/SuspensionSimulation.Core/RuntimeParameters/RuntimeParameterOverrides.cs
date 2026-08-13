namespace SuspensionSimulation.Core.RuntimeParameters;

/// <summary>
/// Immutable request for an A/B setup experiment. All values are absolute SI values,
/// not percentage adjustments. Null means retain the package value.
/// </summary>
public sealed record RuntimeParameterOverrides(
    double? FrontWheelRateNPerM = null,
    double? RearWheelRateNPerM = null,
    double? FrontArbWheelPairRateNPerM = null,
    double? RearArbWheelPairRateNPerM = null,
    double? FrontBumpStopGapM = null,
    double? RearBumpStopGapM = null,
    double? CgHeightM = null,
    double? WheelbaseM = null,
    double? FrontTrackM = null,
    double? RearTrackM = null,
    double? StaticFrontWeightFraction = null)
{
    public bool IsEmpty => this == new RuntimeParameterOverrides();
}

public sealed record AppliedOverride(string Parameter, double PreviousValue, double AppliedValue, string Unit);
public sealed record OverrideApplication(VehiclePackage Package, IReadOnlyList<AppliedOverride> Applied);

public static class VehiclePackageOverrides
{
    public static OverrideApplication Apply(VehiclePackage source, RuntimeParameterOverrides overrides)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(overrides);
        Validate(overrides);
        var applied = new List<AppliedOverride>();
        var chassis = source.Chassis;
        chassis = chassis with
        {
            CgHeightM = Choose(overrides.CgHeightM, chassis.CgHeightM, "cgHeightM", "m", applied),
            WheelbaseM = Choose(overrides.WheelbaseM, chassis.WheelbaseM, "wheelbaseM", "m", applied),
            FrontTrackM = Choose(overrides.FrontTrackM, chassis.FrontTrackM, "frontTrackM", "m", applied),
            RearTrackM = Choose(overrides.RearTrackM, chassis.RearTrackM, "rearTrackM", "m", applied),
            StaticFrontWeightFraction = Choose(overrides.StaticFrontWeightFraction, chassis.StaticFrontWeightFraction, "staticFrontWeightFraction", "fraction", applied)
        };

        var springs = source.Springs.Corners.Select(s =>
        {
            var replacement = IsFront(s.Corner) ? overrides.FrontWheelRateNPerM : overrides.RearWheelRateNPerM;
            return replacement is null ? s : s with { WheelRateNPerM = Choose(replacement, s.WheelRateNPerM, $"{s.Corner}.wheelRate", "N/m", applied) };
        }).ToArray();
        var bars = source.AntiRollBars.Select(bar =>
        {
            var replacement = string.Equals(bar.Axle, "front", StringComparison.OrdinalIgnoreCase)
                ? overrides.FrontArbWheelPairRateNPerM : string.Equals(bar.Axle, "rear", StringComparison.OrdinalIgnoreCase)
                ? overrides.RearArbWheelPairRateNPerM : null;
            if (replacement is not null && !bar.IsLegacyDirectRate)
                throw new InvalidOperationException($"{bar.Axle} ARB is geometry-backed. Change its tube/arm/end-link configuration rather than overriding a direct N/m rate.");
            return replacement is null ? bar : bar with { InstalledWheelPairRateNPerM = Choose(replacement, bar.InstalledWheelPairRateNPerM, $"{bar.Axle}.arbWheelPairRate", "N/m", applied) };
        }).ToArray();
        springs = springs.Select(s =>
        {
            var replacement = IsFront(s.Corner) ? overrides.FrontBumpStopGapM : overrides.RearBumpStopGapM;
            return replacement is null ? s : s with { BumpStopGapM = Choose(replacement, s.BumpStopGapM, $"{s.Corner}.bumpStopGap", "m wheel travel", applied) };
        }).ToArray();
        return new OverrideApplication(source with { Chassis = chassis, Springs = source.Springs with { Corners = springs }, AntiRollBars = bars }, applied);
    }

    private static bool IsFront(string corner) => corner is "FL" or "FR";
    private static double Choose(double? value, double oldValue, string name, string unit, ICollection<AppliedOverride> applied)
    {
        if (value is null) return oldValue;
        applied.Add(new AppliedOverride(name, oldValue, value.Value, unit));
        return value.Value;
    }

    private static void Validate(RuntimeParameterOverrides o)
    {
        Positive(o.FrontWheelRateNPerM, nameof(o.FrontWheelRateNPerM)); Positive(o.RearWheelRateNPerM, nameof(o.RearWheelRateNPerM));
        NonNegative(o.FrontArbWheelPairRateNPerM, nameof(o.FrontArbWheelPairRateNPerM)); NonNegative(o.RearArbWheelPairRateNPerM, nameof(o.RearArbWheelPairRateNPerM));
        NonNegative(o.FrontBumpStopGapM, nameof(o.FrontBumpStopGapM)); NonNegative(o.RearBumpStopGapM, nameof(o.RearBumpStopGapM));
        NonNegative(o.CgHeightM, nameof(o.CgHeightM)); Positive(o.WheelbaseM, nameof(o.WheelbaseM)); Positive(o.FrontTrackM, nameof(o.FrontTrackM)); Positive(o.RearTrackM, nameof(o.RearTrackM));
        if (o.StaticFrontWeightFraction is { } fraction && (!double.IsFinite(fraction) || fraction is <= 0 or >= 1)) throw new ArgumentOutOfRangeException(nameof(o.StaticFrontWeightFraction), "Must be finite and strictly between zero and one.");
    }
    private static void Positive(double? value, string name) { if (value is { } v && (!double.IsFinite(v) || v <= 0)) throw new ArgumentOutOfRangeException(name, "Must be finite and positive."); }
    private static void NonNegative(double? value, string name) { if (value is { } v && (!double.IsFinite(v) || v < 0)) throw new ArgumentOutOfRangeException(name, "Must be finite and non-negative."); }
}
