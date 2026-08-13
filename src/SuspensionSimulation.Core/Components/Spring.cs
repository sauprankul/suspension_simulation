namespace SuspensionSimulation.Components;

public sealed record SpringConfig : Configuration.IVersionedConfiguration
{
    public required string Id { get; init; }
    public string SchemaVersion { get; init; } = "1.0";
    public DataProvenance Provenance { get; init; } = DataProvenance.Estimated;
    public string? SourceNote { get; init; }
    public double LinearRateNPerM { get; init; }
    public double PreloadN { get; init; }
    public IReadOnlyList<CurvePoint>? ForceCurve { get; init; }
}

/// <summary>Compression is positive; output is a positive resisting-force magnitude.</summary>
public sealed class Spring : ICompressionElement
{
    private readonly SpringConfig _config;
    private readonly PiecewiseLinearCurve? _curve;

    public Spring(SpringConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        if (string.IsNullOrWhiteSpace(config.Id)) throw new ArgumentException("An id is required.", nameof(config));
        Guard.NonNegative(nameof(config.LinearRateNPerM), config.LinearRateNPerM);
        Guard.NonNegative(nameof(config.PreloadN), config.PreloadN);
        if (config.ForceCurve is { Count: > 0 }) _curve = new PiecewiseLinearCurve(config.ForceCurve);
        if (_curve is null && config.LinearRateNPerM == 0) throw new ArgumentException("A linear rate or force curve is required.", nameof(config));
    }

    public ElementEvaluation EvaluateCompression(double compressionM)
    {
        if (!double.IsFinite(compressionM)) throw new ArgumentOutOfRangeException(nameof(compressionM));
        if (_curve is not null)
        {
            var result = _curve.Evaluate(compressionM);
            return new ElementEvaluation(Math.Max(0, result.Value + _config.PreloadN), Math.Max(0, result.Slope), compressionM >= 0, result.IsExtrapolated);
        }
        return new ElementEvaluation(Math.Max(0, _config.PreloadN + _config.LinearRateNPerM * compressionM), _config.LinearRateNPerM, compressionM >= 0);
    }
}
