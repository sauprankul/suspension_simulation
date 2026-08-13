using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using SuspensionSimulation.Components;
using SuspensionSimulation.Configuration;
using SuspensionSimulation.Core;
using SuspensionSimulation.Kinematics;

namespace SuspensionSimulation.Solvers.Kinematics;

/// <summary>
/// Explicit geometry-backed bridge for the synthetic Cayman fixture. If its hard-point file is present
/// but cannot produce a converged local pose/motion ratio, this loader fails: it never falls back to the
/// scalar assembly motion ratio. The returned package is directly consumable by FourStrutQuasiStaticSolver;
/// the poses are available to dashboard/report callers as provenance/readouts.
/// </summary>
public sealed record GeometryBackedCaymanPackage(VehiclePackage Package, IReadOnlyList<MacPhersonPlanarPose> ReferencePoses,
    IReadOnlyList<string> Warnings, string GeometrySourcePath);

public static class GeometryBackedCaymanPackageLoader
{
    private const string GeometryFile = "strut-hard-points.json";
    public static GeometryBackedCaymanPackage Load(string caymanDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caymanDirectory);
        var geometryPath = Path.Combine(caymanDirectory, GeometryFile);
        if (!File.Exists(geometryPath)) throw new FileNotFoundException("Geometry-backed package was requested but strut-hard-points.json is unavailable; scalar fallback is deliberately prohibited.", geometryPath);
        var hardpoints = ReadHardpoints(geometryPath);
        if (!string.Equals(hardpoints.Schema, "suspension-sim/four-corner-macpherson-hardpoints/v1", StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported hard-point schema '{hardpoints.Schema}'.");
        if (hardpoints.Corners.Count != 4) throw new InvalidDataException("Geometry-backed Cayman fixture requires exactly four hard-point corners.");
        var solver = new MacPhersonPlanarSolver();
        var poses = hardpoints.Corners.Select(c => solver.Solve(ToPlanar(c, hardpoints.GroundPlaneZM), hardpoints.ReferenceJounceM)).ToArray();
        var failed = poses.Where(x => !x.Converged || x.StrutMotionRatio is null).ToArray();
        if (failed.Length > 0) throw new InvalidDataException($"Geometry-backed package is unavailable: {string.Join(", ", failed.Select(x => x.Id))} lacks a converged pose and local motion ratio.");

        var modular = ModularSetupLoader.Load(caymanDirectory);
        var basePackage = SyntheticFixtureLoader.LoadFourStrutPackage(caymanDirectory);
        var springById = modular.Springs.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var stopById = modular.BumpStops.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var assemblyByCorner = modular.Assembly.Corners.ToDictionary(x => x.Corner.ToString(), StringComparer.Ordinal);
        var poseByCorner = poses.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var geometrySprings = basePackage.Springs.Corners.Select(corner =>
        {
            if (!assemblyByCorner.TryGetValue(corner.Corner, out var assembly) || !poseByCorner.TryGetValue(corner.Corner, out var pose))
                throw new InvalidDataException($"Missing assembly or geometry pose for {corner.Corner}.");
            var motion = pose.StrutMotionRatio!;
            var magnitude = Math.Abs(motion.MotionRatio);
            if (magnitude < 1e-8) throw new InvalidDataException($"{corner.Corner} geometry motion ratio is zero; wheel rate is undefined.");
            var spring = springById[assembly.SpringId]; var stop = stopById[assembly.BumpStopId];
            var stopSlope = FirstSlope(stop.ForceCurve);
            return corner with
            {
                WheelRateNPerM = spring.LinearRateNPerM * motion.MotionRatio * motion.MotionRatio,
                BumpStopGapM = stop.EngagementGapM / magnitude,
                BumpStopRateNPerM = stopSlope * motion.MotionRatio * motion.MotionRatio
            };
        }).ToArray();
        var warnings = poses.SelectMany(pose => pose.Diagnostics.Select(diagnostic =>
            $"{pose.Id}:{diagnostic.Code}: {diagnostic.Message}")).ToArray();
        var package = basePackage with
        {
            Springs = new SpringsModel(geometrySprings, "Synthetic geometry-backed", "Wheel rates and stop conversion derive from converged planar strut poses; no scalar assembly motion-ratio fallback."),
            Scope = basePackage.Scope with { SolverVersion = basePackage.Scope.SolverVersion + "+planar-geometry" }
        };
        return new(package, poses, warnings, geometryPath);
    }

    private static double FirstSlope(IReadOnlyList<CurvePoint> points) => points.Count < 2 ? 0 : (points[1].Y - points[0].Y) / (points[1].X - points[0].X);
    private static MacPhersonPlanarCornerConfig ToPlanar(HardpointCorner c, double groundZ) => new()
    {
        Id = c.Id, UpperStrutMountM = V(c.UpperStrutMount), LowerArmInnerPivotM = V(c.LowerArmInnerPivot),
        LowerBallJointReferenceM = V(c.LowerBallJoint), StrutLowerReferenceM = V(c.StrutLower), WheelCentreReferenceM = V(c.WheelCenter),
        TieRodInnerM = c.TieRodInner is null ? null : V(c.TieRodInner), TieRodOuterReferenceM = c.TieRodOuter is null ? null : V(c.TieRodOuter), GroundPlaneZM = groundZ
    };
    private static Vector3 V(double[] source)
    {
        if (source.Length != 3 || source.Any(x => !double.IsFinite(x))) throw new InvalidDataException("Each hard point must be a finite three-element SI coordinate array.");
        return new((float)source[0], (float)source[1], (float)source[2]);
    }
    private static HardpointFile ReadHardpoints(string path) => JsonSerializer.Deserialize<HardpointFile>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new InvalidDataException("Hard-point file is empty.");
    private sealed record HardpointFile(
        [property: JsonPropertyName("schema")] string Schema,
        [property: JsonPropertyName("reference_jounce_m")] double ReferenceJounceM,
        [property: JsonPropertyName("ground_plane_z_m")] double GroundPlaneZM,
        [property: JsonPropertyName("corners")] List<HardpointCorner> Corners);
    private sealed record HardpointCorner(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("upper_strut_mount")] double[] UpperStrutMount,
        [property: JsonPropertyName("lower_arm_inner_pivot")] double[] LowerArmInnerPivot,
        [property: JsonPropertyName("lower_ball_joint")] double[] LowerBallJoint,
        [property: JsonPropertyName("strut_lower")] double[] StrutLower,
        [property: JsonPropertyName("wheel_center")] double[] WheelCenter,
        [property: JsonPropertyName("tie_rod_inner")] double[]? TieRodInner,
        [property: JsonPropertyName("tie_rod_outer")] double[]? TieRodOuter);
}
