using System.Text.Json;
using System.Text.Json.Serialization;
using SuspensionSimulation.Components;

namespace SuspensionSimulation.Core;

/// <summary>Canonical vehicle axes: +X forward, +Y left, +Z up. All values are SI.</summary>
public sealed record ChassisModel(
    string Name,
    double MassKg,
    double WheelbaseM,
    double FrontTrackM,
    double RearTrackM,
    double CgHeightM,
    double StaticFrontWeightFraction,
    string DataClassification,
    string? Source);

public sealed record CornerSpring(
    string Corner,
    double WheelRateNPerM,
    double BumpStopGapM,
    double BumpStopRateNPerM);

public sealed record SpringsModel(IReadOnlyList<CornerSpring> Corners, string DataClassification, string? Notes);

public sealed record ArbTangentMatrix(double LeftSelfNPerM, double LeftCrossNPerM,
    double RightCrossNPerM, double RightSelfNPerM);

/// <summary>
/// An axle ARB model. Geometry-backed tubular bars are the normal package representation.
/// <see cref="InstalledWheelPairRateNPerM"/> is retained solely for explicit legacy/test imports.
/// </summary>
public sealed record AntiRollBarModel(string Axle, double InstalledWheelPairRateNPerM, string DataClassification, string? Notes)
{
    public AntiRollBarModelKind ModelKind { get; init; } = AntiRollBarModelKind.LegacyDirectWheelPairRate;
    public TubularAntiRollBar? Tubular { get; init; }
    public ArbTangentMatrix? GeometryDerivedTangent { get; init; }
    public bool IsLegacyDirectRate => ModelKind == AntiRollBarModelKind.LegacyDirectWheelPairRate && GeometryDerivedTangent is null;

    public static AntiRollBarModel FromGeometryDerivedTangent(string axle, ArbTangentMatrix tangent, string notes) =>
        new(axle, 0, "geometry-derived-3d", notes)
        {
            ModelKind = AntiRollBarModelKind.TubularGeometry,
            GeometryDerivedTangent = tangent
        };

    public static AntiRollBarModel FromConfig(string axle, AntiRollBarConfig config, string dataClassification)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(axle); ArgumentNullException.ThrowIfNull(config);
        if (config.ModelKind == AntiRollBarModelKind.TubularGeometry)
            return new AntiRollBarModel(axle, 0, dataClassification, config.SourceNote)
            {
                ModelKind = AntiRollBarModelKind.TubularGeometry,
                Tubular = AntiRollBar.CreateTubular(config)
            };
        if (config.ModelKind == AntiRollBarModelKind.LegacyDirectWheelPairRate)
            return new AntiRollBarModel(axle, config.DirectInstalledWheelPairRateNPerM, dataClassification,
                $"LEGACY direct wheel-pair rate: {config.SourceNote}")
            { ModelKind = AntiRollBarModelKind.LegacyDirectWheelPairRate };
        throw new InvalidDataException($"ARB '{config.Id}' has an unknown model kind.");
    }

    /// <summary>Returns the resisting wheel-load increment for the supplied wheel compressions.</summary>
    public (double LeftN, double RightN) ResistingWheelForces(double leftCompressionM, double rightCompressionM)
    {
        if (!double.IsFinite(leftCompressionM) || !double.IsFinite(rightCompressionM)) throw new ArgumentOutOfRangeException(nameof(leftCompressionM));
        if (GeometryDerivedTangent is { } derived)
            return (derived.LeftSelfNPerM*leftCompressionM + derived.LeftCrossNPerM*rightCompressionM,
                derived.RightCrossNPerM*leftCompressionM + derived.RightSelfNPerM*rightCompressionM);
        if (ModelKind == AntiRollBarModelKind.TubularGeometry)
        {
            if (Tubular is null) throw new InvalidOperationException($"Tubular ARB '{Axle}' has no geometry.");
            var reaction = Tubular.Evaluate(leftCompressionM, rightCompressionM);
            // The component reaction is force on the wheel. The chassis solver stores the opposing
            // suspension load increment, hence the sign reversal here.
            return (-reaction.LeftWheelReactionN, -reaction.RightWheelReactionN);
        }
        var differential = leftCompressionM - rightCompressionM;
        return (InstalledWheelPairRateNPerM * differential, -InstalledWheelPairRateNPerM * differential);
    }

    /// <summary>Local K where resisting wheel force is K times [left, right] compression.</summary>
    public ArbTangentMatrix TangentMatrix()
    {
        if (GeometryDerivedTangent is { } derived) return derived;
        if (ModelKind == AntiRollBarModelKind.LegacyDirectWheelPairRate)
            return new(InstalledWheelPairRateNPerM, -InstalledWheelPairRateNPerM,
                -InstalledWheelPairRateNPerM, InstalledWheelPairRateNPerM);
        if (Tubular is null) throw new InvalidOperationException($"Tubular ARB '{Axle}' has no geometry.");
        var tangent = Tubular.WheelTangent();
        if (string.Equals(tangent.Validity, "invalid", StringComparison.Ordinal))
            throw new InvalidOperationException($"Tubular ARB '{Axle}' wheel tangent is unavailable: {string.Join(" ", tangent.Warnings)}");
        return new(tangent.LeftSelfNPerM, tangent.CrossNPerM, tangent.CrossNPerM, tangent.RightSelfNPerM);
    }
}

public sealed record DampingModel(string DataClassification, string? Notes);

/// <summary>
/// Vehicle-axis acceleration input: positive longitudinal is +X (forward) and positive lateral is +Y (left).
/// Positive vertical is +Z (up), relative to the static-reference equilibrium. Display conventions are defined separately.
/// </summary>
public sealed record LoadCase(string Name, double LongitudinalG, double LateralG, double VerticalG);

public sealed record ModelScope(
    string SolverVersion,
    IReadOnlyList<string> IncludedAssumptions,
    IReadOnlyList<string> OmittedEffects);

public sealed record VehiclePackage(
    ChassisModel Chassis,
    SpringsModel Springs,
    IReadOnlyList<AntiRollBarModel> AntiRollBars,
    DampingModel Dampers,
    ModelScope Scope);

public static class ModelLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static T Read<T>(string path) where T : class =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options)
        ?? throw new InvalidDataException($"Could not deserialize {path} as {typeof(T).Name}.");

    public static VehiclePackage LoadPackage(string directory) => new(
        Read<ChassisModel>(Path.Combine(directory, "chassis.json")),
        Read<SpringsModel>(Path.Combine(directory, "springs.json")),
        [Read<AntiRollBarModel>(Path.Combine(directory, "arb-front.json")), Read<AntiRollBarModel>(Path.Combine(directory, "arb-rear.json"))],
        Read<DampingModel>(Path.Combine(directory, "dampers.json")),
        Read<ModelScope>(Path.Combine(directory, "model-scope.json")));
}
