using System.Text.Json;
using System.Numerics;
using SuspensionSimulation.Components;
using SuspensionSimulation.Configuration;
using SuspensionSimulation.Kinematics;
using SuspensionSimulation.Solvers.Kinematics;

namespace SuspensionSimulation.Tests;

/// <summary>Loads the committed synthetic fixture catalogue and checks its analytic component laws.</summary>
internal static class TestdataFixtureHarness
{
    private static readonly string[] FixtureNames =
    ["spring-linear", "spring-piecewise", "arb-direct", "arb-tubular", "bump-stop", "damper-placeholder",
     "chassis-equilibrium", "motion-ratio", "tangent-decomposition", "dashboard-g-circle"];

    public static void Verify()
    {
        var root = FindRoot();
        foreach (var name in FixtureNames)
        {
            var directory = Path.Combine(root, "data", "testdata", name);
            if (!File.Exists(Path.Combine(directory, "fixture.json")) || !File.Exists(Path.Combine(directory, "README.md")))
                throw new Exception($"Synthetic fixture '{name}' must include fixture.json and README.md.");
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "fixture.json")));
            if (json.RootElement.GetProperty("validity").GetString() != "verified-analytic")
                throw new Exception($"Synthetic fixture '{name}' must declare verified-analytic validity.");
        }

        var linear = new Spring(new SpringConfig { Id = "linear", LinearRateNPerM = 100_000, PreloadN = 200 });
        Equal(1200, linear.EvaluateCompression(.01).ForceN);
        var piecewise = new Spring(new SpringConfig { Id = "piecewise", ForceCurve = [new CurvePoint(0,0), new CurvePoint(.01,1000), new CurvePoint(.02,3000)] });
        Equal(2000, piecewise.EvaluateCompression(.015).ForceN);
        Equal(200_000, piecewise.EvaluateCompression(.015).TangentRateNPerM);
        var bar = new AntiRollBar(new AntiRollBarConfig { Id = "bar", DirectInstalledWheelPairRateNPerM = 10_000 });
        var barValue = bar.Evaluate(.02, .01); Equal(-100, barValue.LeftReactionN); Equal(100, barValue.RightReactionN); Equal(.5, barValue.StoredEnergyJ);
        var stop = new BumpStop(new BumpStopConfig { Id = "stop", EngagementGapM = .02, ForceCurve = [new CurvePoint(0,0), new CurvePoint(.01,1000)] });
        Equal(500, stop.EvaluateCompression(.025).ForceN); Equal(0, stop.EvaluateCompression(.015).ForceN);
        var damper = new Damper(new DamperConfig { Id = "damper", CompressionDampingNPerMPerS = 2000, ReboundDampingNPerMPerS = 3000 });
        Equal(-1000, damper.EvaluateForce(.5)); Equal(1500, damper.EvaluateForce(-.5)); Equal(0, damper.EvaluateForce(0));
        VerifyGeometryBackedFixtures(root);
        VerifyCaymanFourCornerGeometry(root);
    }

    private static void VerifyCaymanFourCornerGeometry(string root)
    {
        var directory = Path.Combine(root, "data", "synthetic", "cayman");
        // This is the geometry-backed package entry point the dashboard uses;
        // it rejects scalar assembly motion-ratio fallback by construction.
        var dashboardPath = GeometryBackedCaymanPackageLoader.Load(directory);
        if (dashboardPath.ReferencePoses.Count != 4) throw new Exception("Dashboard setup path did not load all four hard-point corners.");
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "strut-hard-points.json")));
        using var expected = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "strut-geometry-expected.json")));
        var corners = fixture.RootElement.GetProperty("corners");
        if (corners.GetArrayLength() != 4) throw new Exception("Cayman geometry fixture requires exactly FL/FR/RL/RR hard-point records.");
        var configurations = corners.EnumerateArray().Select(ToPlanarConfig).ToArray();
        var solver = new MacPhersonPlanarSolver(); var travel = expected.RootElement.GetProperty("travel_check_m").GetDouble();
        var tolerance = expected.RootElement.GetProperty("max_reference_constraint_residual_m").GetDouble();
        foreach (var configuration in configurations)
        {
            var reference = solver.Solve(configuration, 0); var bump = solver.Solve(configuration, travel); var rebound = solver.Solve(configuration, -travel);
            if (!reference.Converged || !bump.Converged || !rebound.Converged) throw new Exception($"{configuration.Id} synthetic hard-point continuation did not converge.");
            if (Math.Abs(reference.StrutCompressionM) > tolerance || Math.Abs(reference.TieRodLengthResidualM ?? double.PositiveInfinity) > tolerance)
                throw new Exception($"{configuration.Id} did not close at its declared reference pose.");
            if (bump.StrutMotionRatio is null || rebound.StrutMotionRatio is null || Math.Abs(bump.StrutMotionRatio.MotionRatio) < 1e-5)
                throw new Exception($"{configuration.Id} must produce finite, non-zero geometry-backed travel motion ratios.");
        }
        Mirror(configurations.Single(x => x.Id == "FL"), configurations.Single(x => x.Id == "FR"));
        Mirror(configurations.Single(x => x.Id == "RL"), configurations.Single(x => x.Id == "RR"));
    }

    private static MacPhersonPlanarCornerConfig ToPlanarConfig(JsonElement element) => new()
    {
        Id = element.GetProperty("id").GetString()!,
        UpperStrutMountM = Vector(element.GetProperty("upper_strut_mount")),
        LowerArmInnerPivotM = Vector(element.GetProperty("lower_arm_inner_pivot")),
        LowerBallJointReferenceM = Vector(element.GetProperty("lower_ball_joint")),
        StrutLowerReferenceM = Vector(element.GetProperty("strut_lower")),
        WheelCentreReferenceM = Vector(element.GetProperty("wheel_center")),
        TieRodInnerM = Vector(element.GetProperty("tie_rod_inner")),
        TieRodOuterReferenceM = Vector(element.GetProperty("tie_rod_outer")),
        GroundPlaneZM = 0
    };

    private static Vector3 Vector(JsonElement point) => new(point[0].GetSingle(), point[1].GetSingle(), point[2].GetSingle());

    private static void Mirror(MacPhersonPlanarCornerConfig left, MacPhersonPlanarCornerConfig right)
    {
        foreach (var pair in new[] { (left.UpperStrutMountM, right.UpperStrutMountM), (left.LowerArmInnerPivotM, right.LowerArmInnerPivotM), (left.LowerBallJointReferenceM, right.LowerBallJointReferenceM), (left.StrutLowerReferenceM, right.StrutLowerReferenceM), (left.WheelCentreReferenceM, right.WheelCentreReferenceM), (left.TieRodInnerM!.Value, right.TieRodInnerM!.Value), (left.TieRodOuterReferenceM!.Value, right.TieRodOuterReferenceM!.Value) })
        {
            Equal(pair.Item1.X, pair.Item2.X); Equal(pair.Item1.Z, pair.Item2.Z); Equal(pair.Item1.Y, -pair.Item2.Y);
        }
    }

    private static void VerifyGeometryBackedFixtures(string root)
    {
        var caymanPath = Path.Combine(root, "data", "synthetic", "cayman", "tubular-arbs.geometry.json");
        var strutPath = Path.Combine(root, "data", "synthetic", "strut", "constraints.json");
        using var cayman = JsonDocument.Parse(File.ReadAllText(caymanPath));
        var bar = cayman.RootElement.GetProperty("bars")[0];
        RequireTubularOnly(bar, "Cayman tubular ARB fixture");
        var od = bar.GetProperty("outside_diameter_m").GetDouble();
        var id = bar.GetProperty("inside_diameter_m").GetDouble();
        var expectedPolar = bar.GetProperty("expected_polar_second_moment_m4").GetDouble();
        Equal(expectedPolar, Math.PI / 32 * (Math.Pow(od, 4) - Math.Pow(id, 4)));

        using var strut = JsonDocument.Parse(File.ReadAllText(strutPath));
        var geometryArb = strut.RootElement.GetProperty("geometry_backed_components").GetProperty("tubular_arb");
        RequireTubularOnly(geometryArb, "strut geometry-backed ARB fixture");
        var motionRatio = strut.RootElement.GetProperty("geometry_backed_components").GetProperty("spring_motion_ratio_target").GetDouble();
        if (motionRatio <= 0 || !double.IsFinite(motionRatio)) throw new Exception("Strut geometry fixture must declare a finite positive motion-ratio target.");
        using var expected = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "data", "synthetic", "strut", "expected-results.json")));
        Equal(motionRatio, expected.RootElement.GetProperty("expected_component_motion_ratio").GetDouble());
        var rc = expected.RootElement.GetProperty("expected_planar_roll_center_height_m").GetDouble();
        var jacking = expected.RootElement.GetProperty("expected_jacking_force_per_unit_lateral_force").GetDouble();
        Equal(jacking, rc / .80); // half-track is deliberately declared in this fixture convention.
    }

    private static void RequireTubularOnly(JsonElement bar, string context)
    {
        if (bar.GetProperty("rate_mode").GetString() != "tubular_geometry") throw new Exception($"{context} must select tubular_geometry.");
        if (bar.TryGetProperty("direct_installed_wheel_pair_rate_n_per_m", out _))
            throw new Exception($"{context} must reject a silent direct-rate fallback.");
        foreach (var property in new[] { "outside_diameter_m", "inside_diameter_m", "shear_modulus_pa", "torsion_length_m", "left", "right" })
            if (!bar.TryGetProperty(property, out _)) throw new Exception($"{context} is missing required tubular input '{property}'.");
    }

    private static void Equal(double expected, double actual)
    {
        if (Math.Abs(expected - actual) > 1e-9 * Math.Max(1, Math.Abs(expected)))
            throw new Exception($"Expected {expected:R}, got {actual:R}.");
    }

    private static string FindRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, "data", "testdata"))) return d.FullName;
        for (var d = new DirectoryInfo(Environment.CurrentDirectory); d is not null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, "data", "testdata"))) return d.FullName;
        throw new DirectoryNotFoundException("Repository root containing data/testdata was not found.");
    }
}
