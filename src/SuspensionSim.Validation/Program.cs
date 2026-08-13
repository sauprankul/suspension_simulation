using System.Text.Json;
using System.Text.Json.Serialization;

// Standalone reference harness. Its small equations are deliberately visible:
// total lateral transfer = m*g*ay*h / track.  Axle allocation follows the
// specified linear roll-stiffness distribution; longitudinal transfer is
// m*g*ax*h / wheelbase.  This is not a suspension travel or tire model.
return ValidationProgram.Run(args);

internal static class ValidationProgram
{
    private const double G = 9.80665;
    private const double Tolerance = 1e-8;

    internal static int Run(string[] args)
    {
        var root = FindRepositoryRoot();
        var fixture = Path.Combine(root, "data", "synthetic", "cayman");
        var chassis = Read<Chassis>(Path.Combine(fixture, "chassis.json"));
        var assembly = Read<Assembly>(Path.Combine(fixture, "assembly.json"));
        var cases = Read<LoadCases>(Path.Combine(fixture, "load_cases.json"));

        var failures = new List<string>();
        RunHandCheckFixtures(failures);
        var report = cases.Cases.Select(c => Solve(chassis, assembly, c)).ToArray();
        RunFixtureChecks(chassis, assembly, report, failures);

        Console.Error.WriteLine($"Loaded mass={chassis.TotalMassKg}, frontK={assembly.FrontRollStiffnessNMPerRad}, rearK={assembly.RearRollStiffnessNMPerRad}, cases={cases.Cases.Count}");

        Console.WriteLine(JsonSerializer.Serialize(new
        {
            fixture = "data/synthetic/cayman",
            solver_scope = "rigid quasi-static point contacts; no tire, travel, damping, or kinematics model",
            validity = "estimated",
            checks = failures.Count == 0 ? "PASS" : "FAIL",
            cases = report,
            failures
        }, JsonOptions));
        return failures.Count == 0 ? 0 : 1;
    }

    private static Result Solve(Chassis c, Assembly a, LoadCase input)
    {
        var weight = c.TotalMassKg * G;
        var frontStatic = weight * c.StaticFrontWeightFraction;
        var rearStatic = weight - frontStatic;
        var frontRsd = a.FrontRollStiffnessNMPerRad /
                       (a.FrontRollStiffnessNMPerRad + a.RearRollStiffnessNMPerRad);
        // Axle lateral transfer formula uses its own track after stiffness allocation.
        var totalMoment = c.TotalMassKg * G * input.AyG * c.CgHeightM;
        var frontTransfer = totalMoment * frontRsd / c.FrontTrackM;
        var rearTransfer = totalMoment * (1.0 - frontRsd) / c.RearTrackM;
        var longTransfer = c.TotalMassKg * G * input.AxG * c.CgHeightM / c.WheelbaseM;
        var fl = frontStatic / 2.0 - longTransfer / 2.0 - frontTransfer / 2.0;
        var fr = frontStatic / 2.0 - longTransfer / 2.0 + frontTransfer / 2.0;
        var rl = rearStatic / 2.0 + longTransfer / 2.0 - rearTransfer / 2.0;
        var rr = rearStatic / 2.0 + longTransfer / 2.0 + rearTransfer / 2.0;
        var lateralTotal = frontTransfer + rearTransfer;
        return new(input.Id, input.AxG, input.AyG, frontRsd,
            lateralTotal == 0 ? null : frontTransfer / lateralTotal,
            frontTransfer, rearTransfer, longTransfer, fl, fr, rl, rr, fl + fr + rl + rr);
    }

    private static void RunHandCheckFixtures(List<string> failures)
    {
        // symmetric: m=1000 kg, h=.5 m, tracks 2 m; 1g lateral => each axle
        // transfers 1225.83125 N with a 50/50 stiffness split.
        var c = new Chassis(1000, 2, 2, 2, .5, .5);
        var a = new Assembly(100, 100);
        var r = Solve(c, a, new LoadCase("symmetric_one_g", 0, 1));
        AssertNear(r.FrontLateralTransferN, 1000 * G * .5 * .5 / 2, "hand symmetric front lateral", failures);
        AssertNear(r.RearLateralTransferN, 1000 * G * .5 * .5 / 2, "hand symmetric rear lateral", failures);
        AssertNear(r.FrontRsd, .5, "hand symmetric RSD", failures);
        AssertNear(r.LltdFront, .5, "hand symmetric LLTD", failures);
        // Bars do no ideal heave work; this harness only allocates lateral load transfer.
        AssertNear(r.SumN, 1000 * G, "hand vertical equilibrium", failures);
    }

    private static void RunFixtureChecks(Chassis c, Assembly a, Result[] results, List<string> failures)
    {
        foreach (var r in results)
            AssertNear(r.SumN, c.TotalMassKg * G, $"{r.Id}: vertical equilibrium", failures);
        var staticCase = results.Single(r => r.Id == "static");
        AssertNear(staticCase.FlN, c.TotalMassKg * G * c.StaticFrontWeightFraction / 2, "static FL", failures);
        AssertNear(staticCase.FrN, staticCase.FlN, "static left/right symmetry front", failures);
        AssertNear(staticCase.RrN, staticCase.RlN, "static left/right symmetry rear", failures);
        if (staticCase.LltdFront is not null) failures.Add("static: LLTD must be undefined when lateral transfer is zero");
        var oneG = results.Single(r => r.Id == "one_g_left");
        AssertNear(oneG.FrontRsd, .5, "fixture RSD", failures);
        // Unequal tracks mean equal roll-stiffness allocation is not exactly 50% LLTD.
        var expectedLltd = (1 / c.FrontTrackM) / ((1 / c.FrontTrackM) + (1 / c.RearTrackM));
        AssertNear(oneG.LltdFront, expectedLltd, "fixture LLTD", failures);
        var accel = results.Single(r => r.Id == "one_g_accel");
        if (accel.RlN <= staticCase.RlN || accel.FlN >= staticCase.FlN)
            failures.Add("one_g_accel: expected rearward longitudinal transfer did not occur");
    }

    private static void AssertNear(double? actual, double expected, string name, List<string> failures)
    {
        if (actual is null || double.IsNaN(actual.Value) || Math.Abs(actual.Value - expected) > Tolerance * Math.Max(1, Math.Abs(expected)))
            failures.Add($"{name}: expected {expected:R}, got {(actual is null ? "undefined" : actual.Value.ToString("R"))}");
    }

    private static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions)
        ?? throw new InvalidDataException($"Could not read {path}");

    private static string FindRepositoryRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, "data", "synthetic", "cayman"))) return d.FullName;
        for (var d = new DirectoryInfo(Environment.CurrentDirectory); d is not null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, "data", "synthetic", "cayman"))) return d.FullName;
        throw new DirectoryNotFoundException("Repository root containing data/synthetic/cayman was not found.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };

    private sealed class Chassis
    {
        [JsonPropertyName("totalMassKg")] public double TotalMassKg { get; init; }
        [JsonPropertyName("wheelbaseM")] public double WheelbaseM { get; init; }
        [JsonPropertyName("frontTrackM")] public double FrontTrackM { get; init; }
        [JsonPropertyName("rearTrackM")] public double RearTrackM { get; init; }
        [JsonPropertyName("staticFrontWeightFraction")] public double StaticFrontWeightFraction { get; init; }
        [JsonPropertyName("cgHeightM")] public double CgHeightM { get; init; }
        public Chassis() { }
        public Chassis(double massKg, double wheelbaseM, double frontTrackM, double rearTrackM,
            double staticFrontWeightFraction, double cgHeightM) =>
            (TotalMassKg, WheelbaseM, FrontTrackM, RearTrackM, StaticFrontWeightFraction, CgHeightM) =
            (massKg, wheelbaseM, frontTrackM, rearTrackM, staticFrontWeightFraction, cgHeightM);
    }
    private sealed record Assembly(
        [property: JsonPropertyName("front_roll_stiffness_n_m_per_rad")] double FrontRollStiffnessNMPerRad,
        [property: JsonPropertyName("rear_roll_stiffness_n_m_per_rad")] double RearRollStiffnessNMPerRad);
    private sealed record LoadCases([property: JsonPropertyName("cases")] List<LoadCase> Cases);
    private sealed record LoadCase(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("ax_g")] double AxG,
        [property: JsonPropertyName("ay_g")] double AyG);
    private sealed record Result(string Id, double AxG, double AyG, double FrontRsd, double? LltdFront,
        double FrontLateralTransferN, double RearLateralTransferN, double LongitudinalTransferN,
        double FlN, double FrN, double RlN, double RrN, double SumN);
}
