using System.Security.Cryptography;
using System.Text;
using SuspensionSimulation.Components;

namespace SuspensionSimulation.Configuration;

/// <summary>One complete, independently stored setup. Component values remain in their source files.</summary>
public sealed record ModularSetup(
    ChassisConfig Chassis,
    IReadOnlyList<SpringConfig> Springs,
    IReadOnlyList<AntiRollBarConfig> AntiRollBars,
    IReadOnlyList<DamperConfig> Dampers,
    IReadOnlyList<BumpStopConfig> BumpStops,
    SuspensionAssemblyConfig Assembly,
    string InputFingerprintSha256);

public static class ModularSetupLoader
{
    public static ModularSetup Load(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var files = new[] { "chassis.json", "springs.json", "anti-roll-bars.json", "dampers.json", "bump-stops.json", "suspension-assembly.json" };
        var paths = files.Select(x => Path.Combine(directory, x)).ToArray();
        foreach (var path in paths) if (!File.Exists(path)) throw new FileNotFoundException("Required modular setup file is missing.", path);
        var setup = new ModularSetup(
            JsonConfigurationLoader.Load<ChassisConfig>(paths[0]),
            JsonConfigurationLoader.Load<List<SpringConfig>>(paths[1]),
            JsonConfigurationLoader.Load<List<AntiRollBarConfig>>(paths[2]),
            JsonConfigurationLoader.Load<List<DamperConfig>>(paths[3]),
            JsonConfigurationLoader.Load<List<BumpStopConfig>>(paths[4]),
            JsonConfigurationLoader.Load<SuspensionAssemblyConfig>(paths[5]),
            Fingerprint(paths));
        Validate(setup);
        return setup;
    }

    private static string Fingerprint(IEnumerable<string> paths)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var path in paths.OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(Path.GetFileName(path) + "\n"));
            hash.AppendData(File.ReadAllBytes(path));
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void Validate(ModularSetup setup)
    {
        if (setup.Chassis.TotalMassKg <= 0 || setup.Chassis.WheelbaseM <= 0 || setup.Chassis.FrontTrackM <= 0 || setup.Chassis.RearTrackM <= 0)
            throw new InvalidDataException("Chassis mass, wheelbase, and tracks must be positive.");
        if (setup.Chassis.StaticFrontWeightFraction is <= 0 or >= 1)
            throw new InvalidDataException("Static front weight fraction must be strictly between zero and one.");
        RequireUnique(setup.Springs.Select(x => x.Id), "spring"); RequireUnique(setup.AntiRollBars.Select(x => x.Id), "ARB");
        RequireUnique(setup.Dampers.Select(x => x.Id), "damper"); RequireUnique(setup.BumpStops.Select(x => x.Id), "bump stop");
        if (setup.Assembly.Corners.Count != 4 || setup.Assembly.Corners.Select(x => x.Corner).Distinct().Count() != 4)
            throw new InvalidDataException("Assembly must contain exactly one FL, FR, RL, and RR corner.");
        var springs = setup.Springs.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        var dampers = setup.Dampers.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        var stops = setup.BumpStops.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var corner in setup.Assembly.Corners)
        {
            if (corner.MotionRatio <= 0 || !double.IsFinite(corner.MotionRatio)) throw new InvalidDataException($"{corner.Corner} has an invalid motion ratio.");
            if (!springs.Contains(corner.SpringId) || !dampers.Contains(corner.DamperId) || !stops.Contains(corner.BumpStopId))
                throw new InvalidDataException($"{corner.Corner} references a missing component.");
        }
        var bars = setup.AntiRollBars.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        if (!bars.Contains(setup.Assembly.FrontAntiRollBarId) || !bars.Contains(setup.Assembly.RearAntiRollBarId))
            throw new InvalidDataException("Assembly references a missing anti-roll bar.");
    }

    private static void RequireUnique(IEnumerable<string> ids, string label)
    {
        if (ids.Any(string.IsNullOrWhiteSpace) || ids.Distinct(StringComparer.Ordinal).Count() != ids.Count())
            throw new InvalidDataException($"Every {label} must have a unique non-empty id.");
    }
}
