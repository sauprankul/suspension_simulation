using SuspensionSimulation.Components;
using SuspensionSimulation.Configuration;
using SuspensionSimulation.Core;
using SuspensionSimulation.Solvers;
using SuspensionSimulation.Core.Dashboard;
using SuspensionSimulation.Metrics;
using SuspensionSimulation.Results;
using SuspensionSimulation.Core.RuntimeParameters;

var failures = new List<string>();
Run("ideal ARB rejects equal heave", () =>
{
    var bar = new AntiRollBar(new AntiRollBarConfig { Id = "test", DirectInstalledWheelPairRateNPerM = 10_000 });
    var e = bar.Evaluate(0.01, 0.01);
    Equal(0, e.LeftReactionN, 1e-12); Equal(0, e.RightReactionN, 1e-12);
});
Run("tubular ARB derives GJ over L and rejects symmetric heave", () =>
{
    var bar = TubularBar();
    // Solid 20 mm bar: J = pi*d^4/32, k = GJ/L.
    var expectedJ = Math.PI * Math.Pow(0.020, 4) / 32;
    Equal(expectedJ, bar.Properties.PolarSecondMomentM4, 1e-18);
    Equal(79.3e9 * expectedJ / 0.8, bar.Properties.TorsionalStiffnessNmPerRad, 1e-8);
    var heave = bar.Evaluate(0.01, 0.01);
    Equal(0, heave.BarTwistRad, 1e-12); Equal(0, heave.LeftWheelReactionN, 1e-9); Equal(0, heave.RightWheelReactionN, 1e-9);
});
Run("tubular ARB wheel reactions satisfy virtual work", () =>
{
    var bar = TubularBar();
    var e = bar.Evaluate(0.010, -0.010);
    // F_left*dwl + F_right*dwr equals negative dU for this linear conservative bar.
    var displacement = 1e-7;
    var advanced = bar.Evaluate(0.010 + displacement, -0.010);
    var dEnergyByDwLeft = (advanced.StoredEnergyJ - e.StoredEnergyJ) / displacement;
    Equal(-dEnergyByDwLeft, e.LeftWheelReactionN, 0.02);
    Equal(-e.LeftEndLinkVerticalForceN, e.RightEndLinkVerticalForceN, 1e-9);
    Equal(e.LeftEndLinkVerticalForceN / Math.Cos(0.2), e.LeftEndLinkAxialForceN, 1e-9);
});
Run("symmetric linear axle split is 50/50", () =>
{
    var p = SymmetricPackage();
    var r = new FourStrutQuasiStaticSolver().Solve(p, new LoadCase("one-g-left", 0, 1, 0));
    Equal(0.5, r.RollStiffnessDistributionFront, 1e-10);
    Equal(0.5, r.LoadTransferDistributionFront, 1e-10);
    Equal(0, r.RollCoupleBiasPercentagePoints, 1e-10);
    Equal(0, r.VerticalResidualN, 1e-6); Equal(0, r.RollResidualNm, 1e-6); Equal(0, r.PitchResidualNm, 1e-6);
});
Run("zero acceleration preserves reference loads", () =>
{
    var r = new FourStrutQuasiStaticSolver().Solve(SymmetricPackage(), new LoadCase("static", 0, 0, 0));
    foreach (var corner in r.Corners) Equal(0, corner.DeltaLoadN, 1e-8);
    if (!double.IsNaN(r.LoadTransferDistributionFront)) throw new Exception("LLTD must be undefined at zero lateral load transfer.");
});
Run("static attitude and relative ride heights are zero", () =>
{
    var r = new FourStrutQuasiStaticSolver().Solve(SymmetricPackage(), new LoadCase("static", 0, 0, 0));
    Equal(0, r.RollRad, 1e-12); Equal(0, r.PitchRad, 1e-12);
    foreach (var corner in r.Corners) Equal(0, corner.RelativeCompressionM, 1e-12);
});
Run("G-circle maps axes and clamps at two G", () =>
{
    var point = GCircleMapper.Map(3, 4, 100, 2);
    // The compatibility overload applies the same value as the per-axis sweep
    // cap and the total-G cap: axis values clip first, then the resultant clips.
    Equal(70.71067811865475, point.X, 1e-10); Equal(70.71067811865475, point.Y, 1e-10);
    Equal(1.414213562373095, point.DisplayLongitudinalG, 1e-10); Equal(1.414213562373095, point.DisplayLateralG, 1e-10);
    if (!point.WasClamped) throw new Exception("Expected a 5G vector to clamp to 2G.");
});
Run("G-circle applies component sweep before total-G limit", () =>
{
    var point = GCircleMapper.Map(4, 3, 100, 3, 4);
    Equal(2.8284271247461903, point.DisplayLongitudinalG, 1e-10); // component capped to (3,3), then vector-capped to 4G
    Equal(2.8284271247461903, point.DisplayLateralG, 1e-10);
    Equal(94.28090415820634, point.X, 1e-10); Equal(94.28090415820634, point.Y, 1e-10);
    if (!point.WasClamped) throw new Exception("Expected both requested caps to report clamping.");
});
Run("G-circle applies separate axis sweep and total-G caps", () =>
{
    // First clamp the 3G longitudinal request to 2G; then clamp the resulting
    // sqrt(2^2 + 2^2) vector to a 2.5G resultant limit.
    var point = GCircleMapper.Map(3, 2, 100, axisSweepCapG: 2, totalGCap: 2.5);
    Equal(88.38834764831844, point.X, 1e-10);
    Equal(88.38834764831844, point.Y, 1e-10);
    Equal(1.7677669529663689, point.DisplayLongitudinalG, 1e-10);
    Equal(1.7677669529663689, point.DisplayLateralG, 1e-10);
    if (!point.WasClamped) throw new Exception("Expected both cap stages to be observable as a clamped result.");
});
Run("metric selection retains selected metrics only", () =>
{
    var metrics = new[]
    {
        new DashboardMetric("lltd", "LLTD front", "%", "50", true, "estimated"),
        new DashboardMetric("roll", "Roll", "deg", "0.9", false, "estimated")
    };
    var selected = MetricSelection.SelectedOnly(metrics);
    if (selected.Count != 1 || selected[0].Id != "lltd") throw new Exception("Selected-only table was incorrect.");
    var changed = MetricSelection.ApplySelection(metrics, ["roll"]);
    if (changed[0].IsSelected || !changed[1].IsSelected) throw new Exception("Metric selection update was incorrect.");
});
Run("balance metrics retain separate RSD, LLTD, and mass fraction", () =>
{
    var metrics = BalanceMetrics.Create(60_000, 40_000, 300, 100, 0.45);
    Equal(0.6, metrics.RollStiffnessDistributionFront, 1e-12);
    Equal(0.75, metrics.LoadTransferDistributionFront, 1e-12);
    Equal(0.15, metrics.RollStiffnessMinusMassFraction, 1e-12);
    if (!double.IsNaN(BalanceMetrics.LoadTransferDistributionFront(0, 0))) throw new Exception("Zero load transfer must be undefined.");
});
Run("modular synthetic setup loads with deterministic fingerprint", () =>
{
    var setup = ModularSetupLoader.Load(Path.Combine("data", "synthetic", "cayman"));
    if (setup.Assembly.Corners.Count != 4 || setup.InputFingerprintSha256.Length != 64) throw new Exception("Invalid loaded setup.");
    if (setup.Chassis.Provenance is not (DataProvenance.Synthetic or DataProvenance.PublicSource)) throw new Exception("Chassis-envelope provenance was lost.");
});
Run("RCB equals front RSD minus static front fraction in percentage points", () =>
{
    var p = SymmetricPackage() with { Chassis = SymmetricPackage().Chassis with { StaticFrontWeightFraction = 0.45 } };
    var r = new FourStrutQuasiStaticSolver().Solve(p, new LoadCase("one-g-left", 0, 1, 0));
    Equal(5, r.RollCoupleBiasPercentagePoints, 1e-10);
});
Run("stitched fixture RCB reconstructs from reported inputs", () =>
{
    var p = SyntheticFixtureLoader.LoadFourStrutPackage(Path.Combine("data", "synthetic", "cayman"));
    var r = new FourStrutQuasiStaticSolver().Solve(p, new LoadCase("one-g-left", 0, 1, 0));
    Equal(100 * (r.RollStiffnessDistributionFront - r.StaticFrontWeightFraction), r.RollCoupleBiasPercentagePoints, 1e-10);
});
Run("linear combined cases retain constant RSD and raw negative normal loads", () =>
{
    var package = SyntheticFixtureLoader.LoadFourStrutPackage(Path.Combine("data", "synthetic", "cayman"));
    var solver = new FourStrutQuasiStaticSolver();
    var baseCase = solver.Solve(package, new LoadCase("one-g", 0, 1, 0));
    var combined = solver.Solve(package, new LoadCase("combined", 1, 1.2, 0));
    Equal(baseCase.RollStiffnessDistributionFront, combined.RollStiffnessDistributionFront, 1e-10);
    if (!combined.Corners.Any(c => c.NormalLoadN < 0 && c.IsLifted)) throw new Exception("The pre-lift solver must retain and mark raw negative normal loads.");
    if (!combined.Converged) throw new Exception("Combined linear case should converge.");
});
Run("lateral transfer satisfies the hand-check roll-moment closure", () =>
{
    var package = SyntheticFixtureLoader.LoadFourStrutPackage(Path.Combine("data", "synthetic", "cayman"));
    var r = new FourStrutQuasiStaticSolver().Solve(package, new LoadCase("one-g", 0, 1, 0));
    var front = r.Axles.Single(x => x.Axle == "front"); var rear = r.Axles.Single(x => x.Axle == "rear");
    var expected = package.Chassis.MassKg * FourStrutQuasiStaticSolver.GravityMPerS2 * package.Chassis.CgHeightM;
    Equal(expected, (front.LoadTransferN * package.Chassis.FrontTrackM + rear.LoadTransferN * package.Chassis.RearTrackM) / 2, 1e-6);
});
Run("result error takes precedence over warning", () =>
{
    var status = AnalysisResult.DeriveStatus([new("warning", DiagnosticSeverity.Warning, "w"), new("error", DiagnosticSeverity.Error, "e")]);
    if (status != ResultStatus.Invalid) throw new Exception("An error must make a result invalid.");
});
Run("runtime overrides change requested axle values and retain baseline", () =>
{
    var source = SymmetricPackage();
    var application = VehiclePackageOverrides.Apply(source, new RuntimeParameterOverrides(
        FrontWheelRateNPerM: 120_000, RearArbWheelPairRateNPerM: 25_000, CgHeightM: 0.55));
    var package = application.Package;
    Equal(120_000, package.Springs.Corners.Single(x => x.Corner == "FL").WheelRateNPerM, 0);
    Equal(100_000, package.Springs.Corners.Single(x => x.Corner == "RL").WheelRateNPerM, 0);
    Equal(20_000, package.AntiRollBars.Single(x => x.Axle == "front").InstalledWheelPairRateNPerM, 0);
    Equal(25_000, package.AntiRollBars.Single(x => x.Axle == "rear").InstalledWheelPairRateNPerM, 0);
    Equal(0.5, source.Chassis.CgHeightM, 0); Equal(0.55, package.Chassis.CgHeightM, 0);
    if (application.Applied.Count != 4) throw new Exception("Expected an audit entry for each modified physical input.");
});
Run("selected metric computation leaves zero-transfer LLTD undefined", () =>
{
    var metrics = SelectedMetricComputation.Compute(new QuasiStaticMetricInput(60_000, 40_000, 0, 0, 0.45),
        [LoadLabMetricId.RollStiffnessDistributionFront, LoadLabMetricId.LoadTransferDistributionFront, LoadLabMetricId.RollCoupleBiasPercentagePoints]);
    Equal(60, metrics[0].Value, 1e-12);
    if (metrics[1].IsDefined) throw new Exception("LLTD must be undefined where no lateral transfer exists.");
    Equal(15, metrics[2].Value, 1e-12);
});
Run("bump-stop gap overrides apply per axle and retain source", () =>
{
    var source = SymmetricPackage();
    var applied = VehiclePackageOverrides.Apply(source, new RuntimeParameterOverrides(FrontBumpStopGapM: 0.012, RearBumpStopGapM: 0.025));
    Equal(0.012, applied.Package.Springs.Corners.Single(x => x.Corner == "FL").BumpStopGapM, 0);
    Equal(0.012, applied.Package.Springs.Corners.Single(x => x.Corner == "FR").BumpStopGapM, 0);
    Equal(0.025, applied.Package.Springs.Corners.Single(x => x.Corner == "RL").BumpStopGapM, 0);
    Equal(1, source.Springs.Corners.Single(x => x.Corner == "FL").BumpStopGapM, 0);
    if (applied.Applied.Count != 4) throw new Exception("Expected one bump-stop-gap audit entry per corner.");
});
Run("reference-relative readouts use documented roll and pitch signs", () =>
{
    var readout = ReferenceRelativeRideReadouts.Create(0.010, 0.020, 0.010, 1.2, 1.3, 1.6, 1.5);
    var fl = readout.Corners.Single(x => x.Corner == "FL"); var fr = readout.Corners.Single(x => x.Corner == "FR");
    Equal(0.010 - 0.8 * 0.020 + 1.2 * 0.010, fl.ReferenceRelativeCompressionM, 1e-12);
    Equal(0.010 + 0.8 * 0.020 + 1.2 * 0.010, fr.ReferenceRelativeCompressionM, 1e-12);
    Equal(-fl.ReferenceRelativeCompressionM, fl.ReferenceRelativeWheelVerticalPositionM, 1e-12);
    if (!readout.Semantics.Contains("not kinematic", StringComparison.Ordinal)) throw new Exception("Readout limitation must be explicit.");
});
Run("synthetic Cayman bump-stop placeholders are zero-rate", () =>
{
    var package = SyntheticFixtureLoader.LoadFourStrutPackage(Path.Combine("data", "synthetic", "cayman"));
    if (package.Springs.Corners.Any(c => c.BumpStopRateNPerM != 0)) throw new Exception("Synthetic fixture must not add bump-stop stiffness yet.");
    var result = new FourStrutQuasiStaticSolver().Solve(package, new LoadCase("high-lateral", 0, 2, 0));
    if (result.Corners.Any(c => c.BumpStopForceN != 0)) throw new Exception("Zero-rate placeholder must generate zero bump-stop force.");
});

Console.WriteLine($"{20 - failures.Count}/20 regression checks passed.");
foreach (var failure in failures) Console.Error.WriteLine(failure);
Environment.ExitCode = failures.Count == 0 ? 0 : 1;

void Run(string name, Action test)
{
    try { test(); Console.WriteLine($"PASS {name}"); }
    catch (Exception e) { failures.Add($"FAIL {name}: {e.Message}"); }
}
static void Equal(double expected, double actual, double tolerance)
{
    if (Math.Abs(expected - actual) > tolerance) throw new Exception($"Expected {expected:G17}, got {actual:G17} (tolerance {tolerance:G17}).");
}
static VehiclePackage SymmetricPackage() => new(
    new ChassisModel("symmetric", 1000, 2.5, 1.5, 1.5, 0.5, 0.5, "Synthetic", null),
    new SpringsModel([new("FL", 100_000, 1, 0), new("FR", 100_000, 1, 0), new("RL", 100_000, 1, 0), new("RR", 100_000, 1, 0)], "Synthetic", null),
    [new("front", 20_000, "Synthetic", null), new("rear", 20_000, "Synthetic", null)],
    new DampingModel("Synthetic", null), new ModelScope("test", [], []));

static TubularAntiRollBar TubularBar() => new(new TubularAntiRollBarConfig
{
    Id = "test-tube", TorsionLengthM = 0.8, OutsideDiameterM = 0.020, InsideDiameterM = 0,
    ShearModulusPa = 79.3e9,
    Left = new ArbSideGeometry(0.15, 0.25, 0.2, 1),
    Right = new ArbSideGeometry(0.15, 0.25, 0.2, 1)
});
