namespace SuspensionSimulation.Components;

/// <summary>
/// A direct wheel-pair rate has no physical bar geometry behind it. It remains only for
/// legacy imports and analytic tests; new vehicle packages use <see cref="TubularGeometry"/>.
/// </summary>
public enum AntiRollBarModelKind { LegacyDirectWheelPairRate, TubularGeometry }

public sealed record AntiRollBarConfig : Configuration.IVersionedConfiguration
{
    public required string Id { get; init; }
    public string SchemaVersion { get; init; } = "1.0";
    public DataProvenance Provenance { get; init; } = DataProvenance.Estimated;
    public string? SourceNote { get; init; }
    public AntiRollBarModelKind ModelKind { get; init; } = AntiRollBarModelKind.LegacyDirectWheelPairRate;
    /// <summary>Full bar geometry for the normal vehicle-model path.</summary>
    public TubularAntiRollBarConfig? Tubular { get; init; }
    /// <summary>LEGACY/import-only installed force at one wheel per metre of left-minus-right wheel travel.</summary>
    public double DirectInstalledWheelPairRateNPerM { get; init; }
    public double PreloadDifferentialTravelM { get; init; }
}

public sealed record AntiRollBarEvaluation(double DifferentialTravelM, double LeftReactionN, double RightReactionN, double StoredEnergyJ);

/// <summary>Ideal legacy bar: equal wheel heave has no reaction. Reactions oppose left-minus-right differential travel.</summary>
public sealed class AntiRollBar
{
    private readonly AntiRollBarConfig _config;
    public AntiRollBar(AntiRollBarConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        if (string.IsNullOrWhiteSpace(config.Id)) throw new ArgumentException("An id is required.", nameof(config));
        if (config.ModelKind == AntiRollBarModelKind.LegacyDirectWheelPairRate)
            Guard.NonNegative(nameof(config.DirectInstalledWheelPairRateNPerM), config.DirectInstalledWheelPairRateNPerM);
        else if (config.ModelKind == AntiRollBarModelKind.TubularGeometry)
            _ = CreateTubular(config);
        else throw new ArgumentOutOfRangeException(nameof(config.ModelKind));
    }
    public AntiRollBarEvaluation Evaluate(double leftWheelJounceM, double rightWheelJounceM)
    {
        if (_config.ModelKind != AntiRollBarModelKind.LegacyDirectWheelPairRate)
            throw new InvalidOperationException("AntiRollBar.Evaluate is legacy direct-rate only. Use TubularAntiRollBar or a stitched vehicle package for a geometry-backed bar.");
        if (!double.IsFinite(leftWheelJounceM) || !double.IsFinite(rightWheelJounceM)) throw new ArgumentOutOfRangeException(nameof(leftWheelJounceM));
        var differential = leftWheelJounceM - rightWheelJounceM - _config.PreloadDifferentialTravelM;
        var reaction = -_config.DirectInstalledWheelPairRateNPerM * differential;
        return new AntiRollBarEvaluation(differential, reaction, -reaction, 0.5 * _config.DirectInstalledWheelPairRateNPerM * differential * differential);
    }

    internal static TubularAntiRollBar CreateTubular(AntiRollBarConfig config)
    {
        if (config.Tubular is null) throw new InvalidDataException($"ARB '{config.Id}' is tubular but has no tubular geometry.");
        return new TubularAntiRollBar(config.Tubular with
        {
            Id = config.Id,
            SchemaVersion = config.SchemaVersion,
            Provenance = config.Provenance,
            SourceNote = config.SourceNote
        });
    }
}
