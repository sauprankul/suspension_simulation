namespace SuspensionSimulation.Components;

public sealed record DamperConfig : Configuration.IVersionedConfiguration
{
    public required string Id { get; init; }
    public string SchemaVersion { get; init; } = "1.0";
    public DataProvenance Provenance { get; init; } = DataProvenance.Estimated;
    public string? SourceNote { get; init; }
    /// <summary>Positive compression velocity coefficient.</summary>
    public double CompressionDampingNPerMPerS { get; init; }
    /// <summary>Positive rebound velocity coefficient.</summary>
    public double ReboundDampingNPerMPerS { get; init; }
}

/// <summary>Linear placeholder. It does not affect quasi-static equilibrium because force is zero at zero velocity.</summary>
public sealed class Damper : IVelocityElement
{
    private readonly DamperConfig _config;
    public Damper(DamperConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        if (string.IsNullOrWhiteSpace(config.Id)) throw new ArgumentException("An id is required.", nameof(config));
        Guard.NonNegative(nameof(config.CompressionDampingNPerMPerS), config.CompressionDampingNPerMPerS);
        Guard.NonNegative(nameof(config.ReboundDampingNPerMPerS), config.ReboundDampingNPerMPerS);
    }
    public double EvaluateForce(double velocityMPerS)
    {
        if (!double.IsFinite(velocityMPerS)) throw new ArgumentOutOfRangeException(nameof(velocityMPerS));
        return velocityMPerS >= 0 ? -_config.CompressionDampingNPerMPerS * velocityMPerS : -_config.ReboundDampingNPerMPerS * velocityMPerS;
    }
}
