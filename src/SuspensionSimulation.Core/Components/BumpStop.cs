namespace SuspensionSimulation.Components;

public sealed record BumpStopConfig : Configuration.IVersionedConfiguration
{
    public required string Id { get; init; }
    public string SchemaVersion { get; init; } = "1.0";
    public DataProvenance Provenance { get; init; } = DataProvenance.Estimated;
    public string? SourceNote { get; init; }
    /// <summary>Suspension compression required before the stop begins compressing.</summary>
    public double EngagementGapM { get; init; }
    public double PreloadN { get; init; }
    public required IReadOnlyList<CurvePoint> ForceCurve { get; init; }
}

public sealed class BumpStop : ICompressionElement
{
    private readonly BumpStopConfig _config;
    private readonly PiecewiseLinearCurve _curve;
    public BumpStop(BumpStopConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        if (string.IsNullOrWhiteSpace(config.Id)) throw new ArgumentException("An id is required.", nameof(config));
        Guard.NonNegative(nameof(config.EngagementGapM), config.EngagementGapM);
        Guard.NonNegative(nameof(config.PreloadN), config.PreloadN);
        _curve = new PiecewiseLinearCurve(config.ForceCurve ?? throw new ArgumentException("A force curve is required.", nameof(config)));
        if (_curve.Points[0].X != 0) throw new ArgumentException("Bump-stop force curve must begin at zero stop compression.", nameof(config));
    }

    public ElementEvaluation EvaluateCompression(double suspensionCompressionM)
    {
        if (!double.IsFinite(suspensionCompressionM)) throw new ArgumentOutOfRangeException(nameof(suspensionCompressionM));
        var stopCompression = suspensionCompressionM - _config.EngagementGapM;
        if (stopCompression < 0) return new ElementEvaluation(0, 0, false);
        var curve = _curve.Evaluate(stopCompression);
        return new ElementEvaluation(Math.Max(0, curve.Value + _config.PreloadN), Math.Max(0, curve.Slope), true, curve.IsExtrapolated);
    }
}
