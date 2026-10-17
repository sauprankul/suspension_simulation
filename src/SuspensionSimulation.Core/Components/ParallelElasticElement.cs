using SuspensionSimulation.Configuration;

namespace SuspensionSimulation.Components;

public sealed record ParallelElasticCurvePoint(double TravelMm, double ForceN);

/// <summary>
/// An arbitrary conservative wheel-parallel element. Positive travel is wheel jounce (bump).
/// Force is the upward suspension force on the sprung body; equilibrium uses F(x)-F(0), so a
/// gas preload may be declared without moving the calibrated static reference.
/// </summary>
public sealed record ParallelElasticElementConfig : IVersionedConfiguration
{
    public required string Id { get; init; }
    public string SchemaVersion { get; init; } = "1.0";
    public DataProvenance Provenance { get; init; } = DataProvenance.Estimated;
    public string? SourceNote { get; init; }
    public required IReadOnlyList<Corner> Corners { get; init; }
    public required IReadOnlyList<ParallelElasticCurvePoint> ForceCurve { get; init; }
}

public interface IWheelParallelElasticElement
{
    string Id { get; }
    string Kind { get; }
    ElementEvaluation EvaluateWheelJounce(double wheelJounceM);
}

public sealed class CurveParallelElasticElement : IWheelParallelElasticElement
{
    private readonly PiecewiseLinearCurve _curve;
    private readonly double _referenceForceN;
    public string Id { get; }
    public string Kind => "arbitrary-force-curve";

    public CurveParallelElasticElement(ParallelElasticElementConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        Id = string.IsNullOrWhiteSpace(config.Id) ? throw new ArgumentException("An id is required.", nameof(config)) : config.Id;
        if(config.ForceCurve.Count<2 || config.ForceCurve[0].TravelMm>0 || config.ForceCurve[^1].TravelMm<0)
            throw new ArgumentException("A parallel force curve requires at least two points and must span static travel zero.",nameof(config));
        if(config.ForceCurve.Zip(config.ForceCurve.Skip(1),(a,b)=>b.ForceN<a.ForceN).Any(x=>x))
            throw new ArgumentException("A resisting parallel force curve must not contain negative tangent segments.",nameof(config));
        _curve = new(config.ForceCurve.Select(x => new CurvePoint(x.TravelMm/1000, x.ForceN)));
        _referenceForceN = _curve.Evaluate(0).Value;
    }

    public ElementEvaluation EvaluateWheelJounce(double wheelJounceM)
    {
        var value = _curve.Evaluate(wheelJounceM);
        return new(value.Value-_referenceForceN, value.Slope, true, value.IsExtrapolated);
    }
}

public sealed record ComponentTravelLimitConfig(
    string Id,
    IReadOnlyList<Corner> Corners,
    double ReboundComponentTravelMm,
    double CompressionComponentTravelMm,
    DataProvenance Provenance,
    string SourceNote);

public sealed record TravelLimitsConfig : IVersionedConfiguration
{
    public required string Id { get; init; }
    public string SchemaVersion { get; init; } = "1.0";
    public required IReadOnlyList<ComponentTravelLimitConfig> Limits { get; init; }
}

public sealed record GeometryDerivedWheelTravelLimit(
    string Corner,
    string SourceId,
    double MinimumJounceM,
    double MaximumJounceM,
    double ReboundComponentTravelM,
    double CompressionComponentTravelM,
    string Provenance)
{
    public WheelTravelConstraint Constraint=>new($"{SourceId}-{Corner}",MinimumJounceM,MaximumJounceM);
}
