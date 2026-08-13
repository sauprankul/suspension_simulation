using System.Text.Json;
using System.Text.Json.Serialization;

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

/// <summary>
/// Installed wheel-pair rate: force at one wheel divided by left-minus-right wheel compression.
/// It represents only differential motion and contributes no ideal heave stiffness.
/// </summary>
public sealed record AntiRollBarModel(string Axle, double InstalledWheelPairRateNPerM, string DataClassification, string? Notes);

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
