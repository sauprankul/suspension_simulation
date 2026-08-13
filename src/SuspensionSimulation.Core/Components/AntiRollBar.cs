namespace SuspensionSimulation.Components;

public sealed record AntiRollBarConfig : Configuration.IVersionedConfiguration
{
    public required string Id { get; init; }
    public string SchemaVersion { get; init; } = "1.0";
    public DataProvenance Provenance { get; init; } = DataProvenance.Estimated;
    public string? SourceNote { get; init; }
    /// <summary>Installed force at one wheel per metre of left-minus-right wheel travel. Includes arms and links.</summary>
    public double DirectInstalledWheelPairRateNPerM { get; init; }
    public double PreloadDifferentialTravelM { get; init; }
}

public sealed record AntiRollBarEvaluation(double DifferentialTravelM, double LeftReactionN, double RightReactionN, double StoredEnergyJ);

/// <summary>Ideal bar: equal wheel heave has no reaction. Reactions oppose left-minus-right differential travel.</summary>
public sealed class AntiRollBar
{
    private readonly AntiRollBarConfig _config;
    public AntiRollBar(AntiRollBarConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        if (string.IsNullOrWhiteSpace(config.Id)) throw new ArgumentException("An id is required.", nameof(config));
        Guard.NonNegative(nameof(config.DirectInstalledWheelPairRateNPerM), config.DirectInstalledWheelPairRateNPerM);
    }
    public AntiRollBarEvaluation Evaluate(double leftWheelJounceM, double rightWheelJounceM)
    {
        if (!double.IsFinite(leftWheelJounceM) || !double.IsFinite(rightWheelJounceM)) throw new ArgumentOutOfRangeException(nameof(leftWheelJounceM));
        var differential = leftWheelJounceM - rightWheelJounceM - _config.PreloadDifferentialTravelM;
        var reaction = -_config.DirectInstalledWheelPairRateNPerM * differential;
        return new AntiRollBarEvaluation(differential, reaction, -reaction, 0.5 * _config.DirectInstalledWheelPairRateNPerM * differential * differential);
    }
}
