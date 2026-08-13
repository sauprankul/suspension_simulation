using SuspensionSimulation.Components;

namespace SuspensionSimulation.Configuration;

public enum Corner { FL, FR, RL, RR }

public interface IVersionedConfiguration
{
    string SchemaVersion { get; }
}

public sealed record ChassisConfig : IVersionedConfiguration
{
    public required string Id { get; init; }
    public string SchemaVersion { get; init; } = "1.0";
    public DataProvenance Provenance { get; init; } = DataProvenance.Estimated;
    public string? SourceNote { get; init; }
    public double TotalMassKg { get; init; }
    public double WheelbaseM { get; init; }
    public double FrontTrackM { get; init; }
    public double RearTrackM { get; init; }
    /// <summary>CG position forward from rear axle and above ground.</summary>
    public double CgFromRearAxleM { get; init; }
    public double CgHeightM { get; init; }
    public double StaticFrontWeightFraction { get; init; }
}

public sealed record CornerAssemblyConfig
{
    public required Corner Corner { get; init; }
    public required string SpringId { get; init; }
    public required string DamperId { get; init; }
    public required string BumpStopId { get; init; }
    /// <summary>Spring/damper/stop displacement divided by wheel jounce. Must be explicitly set.</summary>
    public double MotionRatio { get; init; }
}

public sealed record SuspensionAssemblyConfig : IVersionedConfiguration
{
    public required string Id { get; init; }
    public string SchemaVersion { get; init; } = "1.0";
    public required string FrontAntiRollBarId { get; init; }
    public required string RearAntiRollBarId { get; init; }
    public required IReadOnlyList<CornerAssemblyConfig> Corners { get; init; }
}

/// <summary>Separately loaded component library. IDs are intentionally resolved only by the stitching layer.</summary>
public sealed record VehicleComponentLibrary(
    IReadOnlyList<SpringConfig> Springs,
    IReadOnlyList<AntiRollBarConfig> AntiRollBars,
    IReadOnlyList<DamperConfig> Dampers,
    IReadOnlyList<BumpStopConfig> BumpStops);
