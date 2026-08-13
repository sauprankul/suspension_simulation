using SuspensionSimulation.Components;
using SuspensionSimulation.Configuration;
using SuspensionSimulation.Core;
using SuspensionSimulation.Solvers;
using SuspensionSimulation.Core.Dashboard;
using SuspensionSimulation.Metrics;
using SuspensionSimulation.Results;
using SuspensionSimulation.Core.RuntimeParameters;
using SuspensionSimulation.Kinematics;
using SuspensionSimulation.Solvers.Kinematics;
using System.Numerics;
using SuspensionSimulation.Core.Kinematics;

var failures = new List<string>();
Run("committed synthetic analytic fixture catalogue validates", () => SuspensionSimulation.Tests.TestdataFixtureHarness.Verify());
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
    Equal(e.LeftEndLinkVerticalForceN, e.LeftEndLinkAxialForceN, 1e-9);
});
Run("corner tangent stiffness includes motion-ratio curvature and bump stop", () =>
{
    var spring = new Spring(new SpringConfig { Id = "s", LinearRateNPerM = 100_000 });
    var stop = new BumpStop(new BumpStopConfig { Id = "bs", EngagementGapM = 0.01, ForceCurve = [new(0, 0), new(0.01, 20_000)] });
    var stiffness = TangentWheelStiffnessCalculator.EvaluateCorner("FL", spring, stop, 0.02, new TravelDependentMotionRatio(0.8, 0.5));
    Equal(65_000, stiffness.SpringOnlyNPerM, 1e-9); Equal(1_290_000, stiffness.BumpStopOnlyNPerM, 1e-9);
});
Run("tubular ARB tangent matrix is the virtual-work Hessian", () =>
{
    var corner = TangentWheelStiffnessCalculator.EvaluateCorner("FL", new Spring(new SpringConfig { Id = "s", LinearRateNPerM = 100_000 }), null, 0, new(1));
    var bar = TubularBar(); var result = TangentWheelStiffnessCalculator.WithTubularArb(corner, corner with { Corner = "FR" }, bar);
    var tangent = bar.WheelTangent();
    Equal(100_000 + tangent.LeftSelfNPerM, result.TotalStiffnessMatrixNPerM[0, 0], 1e-8);
    Equal(tangent.CrossNPerM, result.TotalStiffnessMatrixNPerM[0, 1], 1e-8);
    Equal(result.TotalStiffnessMatrixNPerM[0, 1], result.TotalStiffnessMatrixNPerM[1, 0], 1e-12);
});
Run("tubular bar produces the direct pair tangent only for symmetric geometry", () =>
{
    var symmetric = TubularBar().WheelTangent();
    if (symmetric.LeftSelfNPerM <= 0 || symmetric.RightSelfNPerM <= 0 || symmetric.CrossNPerM >= 0)
        throw new Exception("Tubular geometry must expose a physically signed coupled tangent matrix.");
    var asymmetric = new TubularAntiRollBar(new TubularAntiRollBarConfig
    {
        Id = "asym", TorsionLengthM = 0.8, OutsideDiameterM = .02, InsideDiameterM = 0, ShearModulusPa = 79.3e9,
        Left = new(.15, .25, 0, 1), Right = new(.15, .25, 0, .8)
    }).WheelTangent();
    if (asymmetric.SymmetricEffectiveWheelPairTangentNPerM is not null || asymmetric.Validity != "warning") throw new Exception("Asymmetric geometry must retain the coupled matrix.");
});
Run("geometry-derived spring tangent follows virtual work", () =>
{
    var tangent = GeometryDerivedSpringCalculator.Evaluate(new SpringGeometryDerivative(new(0, 0, 2), new(0, 0, .8)), 1000, 50_000, .2);
    Equal(.8, tangent.MotionRatio, 1e-12); Equal(32_200, tangent.WheelTangentNPerM, 1e-9);
});
Run("planar roll-center force path preserves moment closure", () =>
{
    var result = PlanarRollCenterForcePathCalculator.Evaluate(.05, 4000, 1.6);
    Equal(200, result.GeometricRollMomentNm, 1e-12); Equal(250, result.PlanarEquivalentLeftRightLoadDifferenceN, 1e-12);
    if (!result.Validity.Contains("planar")) throw new Exception("Force-path limitation must be explicit.");
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
Run("corner stiffness separates spring, coupled ARB, and active stop", () =>
{
    var inactive = CornerStiffnessCalculator.Evaluate(80_000, 15_000, 50_000, false);
    Equal(95_000, inactive.TotalLocalTangentNPerM, 1e-10);
    var active = CornerStiffnessCalculator.Evaluate(80_000, 15_000, 50_000, true);
    Equal(145_000, active.TotalLocalTangentNPerM, 1e-10);
    if (!active.Warning.Contains("coupled", StringComparison.Ordinal)) throw new Exception("ARB coupling warning must remain visible.");
});
Run("solver exports four local tangent wheel-stiffness decompositions", () =>
{
    var result = new FourStrutQuasiStaticSolver().Solve(SymmetricPackage(), new LoadCase("one-g", 0, 1, 0));
    if (result.CornerTangentStiffnesses.Count != 4) throw new Exception("Exactly one local tangent record per corner is required.");
    foreach (var corner in result.CornerTangentStiffnesses)
    {
        Equal(100_000, corner.SpringOnlyNPerM, 1e-10);
        Equal(20_000, corner.ArbSelfNPerM, 1e-10);
        Equal(-20_000, corner.ArbCrossToOppositeWheelNPerM, 1e-10);
        Equal(0, corner.BumpStopOnlyNPerM, 1e-10);
        Equal(120_000, corner.TotalSelfNPerM, 1e-10);
        if (!corner.PerturbationConvention.Contains("opposite wheel", StringComparison.Ordinal)) throw new Exception("ARB tangent convention must be exported.");
        if (corner.Diagnostics.Count == 0) throw new Exception("Coupled-ARB diagnostic must be exported.");
    }
});
Run("solver includes active bump-stop tangent once", () =>
{
    var source = SymmetricPackage();
    var package = source with { Springs = source.Springs with { Corners = source.Springs.Corners.Select(c => c with { BumpStopGapM = 0, BumpStopRateNPerM = 50_000 }).ToArray() } };
    var result = new FourStrutQuasiStaticSolver().Solve(package, new LoadCase("one-g", 0, 1, 0));
    var active = result.CornerTangentStiffnesses.Single(c => c.Corner == "FR");
    Equal(50_000, active.BumpStopOnlyNPerM, 1e-10);
    Equal(170_000, active.TotalSelfNPerM, 1e-10);
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
Run("negative lateral G maps and geometry stiffness remains render-safe", () =>
{
    var mapped = GCircleMapper.Map(0.25, -1.25, 100, axisSweepCapG: 2, totalGCap: 2);
    if (mapped.X >= 0 || mapped.Y <= 0) throw new Exception("Left-half G-circle mapping must retain negative lateral and positive longitudinal screen signs.");
    var directory = Path.Combine("data", "synthetic", "cayman");
    var package = SyntheticFixtureLoader.LoadFourStrutPackage(directory);
    var geometry = FourCornerMacPhersonGeometryLoader.Load(directory);
    var state = new FourStrutQuasiStaticSolver().Solve(package, new LoadCase("negative-lateral", 0.25, -1.25, 0));
    var result = new FourCornerGeometryStiffnessSolver().Solve(geometry, state, CaymanGeometryComponents(directory), package.AntiRollBars);
    if (result.Corners.Count != 4) throw new Exception("Negative lateral state must return all four geometry stiffness records, not crash rendering.");
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
Run("geometry-backed Cayman loader uses hard-point travel without scalar fallback", () =>
{
    var fixture = GeometryBackedCaymanPackageLoader.Load(Path.Combine("data", "synthetic", "cayman"));
    if (fixture.ReferencePoses.Count != 4 || fixture.ReferencePoses.Any(x => !x.Converged || x.StrutMotionRatio is null)) throw new Exception("Expected four converged geometry poses.");
    if (!fixture.Package.Springs.Notes!.Contains("no scalar", StringComparison.Ordinal)) throw new Exception("Geometry loader must state no scalar fallback.");
    var scalar = SyntheticFixtureLoader.LoadFourStrutPackage(Path.Combine("data", "synthetic", "cayman"));
    if (fixture.Package.Springs.Corners.Zip(scalar.Springs.Corners).All(x => Math.Abs(x.First.WheelRateNPerM - x.Second.WheelRateNPerM) < 1e-12)) throw new Exception("Geometry-backed wheel rates must not silently retain scalar motion ratios.");
});
Run("geometry-backed tubular ARB state recomputes dashboard RSD and RCB", () =>
{
    var directory = Path.Combine("data", "synthetic", "cayman");
    var geometry = GeometryBackedCaymanPackageLoader.Load(directory);
    var setup = ModularSetupLoader.Load(directory);
    var solver = new FourStrutQuasiStaticSolver();
    var reference = solver.Solve(geometry.Package, new LoadCase("reference-travel", 0, 1, 0));

    // This is a geometry change, not a legacy N/m override: shortening the
    // front bar arm increases its derived tubular wheel tangent.
    var frontSource = setup.AntiRollBars.Single(x => x.Id == setup.Assembly.FrontAntiRollBarId);
    var frontTubular = frontSource.Tubular!;
    var editedSource = TubularArbEditor.ApplySymmetricInput(frontSource, new SymmetricTubularArbInput(
        frontTubular.OutsideDiameterM, frontTubular.InsideDiameterM, frontTubular.ShearModulusPa,
        frontTubular.TorsionLengthM, frontTubular.Left.LeverArmM * .80, frontTubular.Left.EndLinkLengthM,
        frontTubular.Left.EndLinkAngleFromVerticalRad, frontTubular.Left.WheelToEndLinkVerticalMotionRatio));
    var editedFront = AntiRollBarModel.FromConfig("front", editedSource, "synthetic geometry edit");
    if (editedFront.IsLegacyDirectRate || editedFront.InstalledWheelPairRateNPerM != 0)
        throw new Exception("Geometry state must not introduce a legacy direct-rate field.");
    var changedPackage = geometry.Package with
    {
        AntiRollBars = geometry.Package.AntiRollBars.Select(bar => bar.Axle == "front" ? editedFront : bar).ToArray()
    };
    var changed = solver.Solve(changedPackage, new LoadCase("changed-arb-geometry", 0, 1, 0));
    var referenceFrontK = reference.Axles.Single(x => x.Axle == "front").TangentRollStiffnessNmPerRad;
    var changedFrontK = changed.Axles.Single(x => x.Axle == "front").TangentRollStiffnessNmPerRad;
    if (changedFrontK <= referenceFrontK) throw new Exception("Shorter geometry-backed front ARB arm did not increase current tangent roll stiffness.");
    if (Math.Abs(changed.RollStiffnessDistributionFront - reference.RollStiffnessDistributionFront) < 1e-6)
        throw new Exception("Dashboard RSD did not change with modeled tubular ARB geometry.");
    Equal(100 * (changed.RollStiffnessDistributionFront - changed.StaticFrontWeightFraction), changed.RollCoupleBiasPercentagePoints, 1e-10);
    if (Math.Abs(changed.RollCoupleBiasPercentagePoints - reference.RollCoupleBiasPercentagePoints) < 1e-6)
        throw new Exception("Dashboard RCB did not recompute from current tangent roll stiffness.");
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
Run("pure longitudinal acceleration preserves RSD with constant tangents", () =>
{
    var source = SymmetricPackage(); // Linear springs, zero-rate stops, and fixed direct ARB tangent.
    var solver = new FourStrutQuasiStaticSolver();
    var staticCase = solver.Solve(source, new LoadCase("static", 0, 0, 0));
    var acceleration = solver.Solve(source, new LoadCase("pure-acceleration", 1.0, 0, 0));
    Equal(staticCase.RollStiffnessDistributionFront, acceleration.RollStiffnessDistributionFront, 1e-12);
    Equal(staticCase.RollCoupleBiasPercentagePoints, acceleration.RollCoupleBiasPercentagePoints, 1e-10);
    if (Math.Abs(acceleration.PitchRad) < 1e-12) throw new Exception("Hand-check must exercise longitudinal pitch while holding tangents constant.");
    var front = acceleration.TangentRollStiffnesses!.Single(x => x.Axle == "front");
    var rear = acceleration.TangentRollStiffnesses!.Single(x => x.Axle == "rear");
    Equal(0, front.BumpStopNmPerRad, 1e-12); Equal(0, rear.BumpStopNmPerRad, 1e-12);
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
Run("synthetic Cayman package uses tubular ARB geometry rather than primary direct rates", () =>
{
    var package = SyntheticFixtureLoader.LoadFourStrutPackage(Path.Combine("data", "synthetic", "cayman"));
    if (package.AntiRollBars.Any(bar => bar.IsLegacyDirectRate || bar.Tubular is null)) throw new Exception("Synthetic vehicle package must stitch tubular ARB geometry.");
    foreach (var bar in package.AntiRollBars)
    {
        var tangent = bar.TangentMatrix();
        if (tangent.LeftSelfNPerM <= 0 || tangent.RightSelfNPerM <= 0 || tangent.LeftCrossNPerM >= 0) throw new Exception("Tubular ARB tangent matrix was not physical.");
    }
});
Run("geometry-backed ARBs reject a misleading direct-rate runtime override", () =>
{
    var package = SyntheticFixtureLoader.LoadFourStrutPackage(Path.Combine("data", "synthetic", "cayman"));
    try { _ = VehiclePackageOverrides.Apply(package, new RuntimeParameterOverrides(FrontArbWheelPairRateNPerM: 20_000)); }
    catch (InvalidOperationException) { return; }
    throw new Exception("A tubular ARB must not silently accept a direct N/m override.");
});
Run("tubular ARB editor changes physical tube geometry rather than a direct rate", () =>
{
    var setup = ModularSetupLoader.Load(Path.Combine("data", "synthetic", "cayman"));
    var source = setup.AntiRollBars.Single(x => x.Id == setup.Assembly.FrontAntiRollBarId);
    var before = AntiRollBarModel.FromConfig("front", source, "test").TangentMatrix().LeftSelfNPerM;
    var edited = TubularArbEditor.ApplySymmetricInput(source, new SymmetricTubularArbInput(.022, 0, 79.3e9, .80, .150, .250, 0, .510));
    var after = AntiRollBarModel.FromConfig("front", edited, "test").TangentMatrix().LeftSelfNPerM;
    if (after <= before || edited.ModelKind != AntiRollBarModelKind.TubularGeometry) throw new Exception("Physical tubular edit did not update the derived tangent.");
});
Run("planar MacPherson hard points reproduce reference pose and derive travel motion ratio", () =>
{
    var config = PlanarStrutFixture(); var solver = new MacPhersonPlanarSolver();
    var reference = solver.Solve(config, 0); if (!reference.Converged) throw new Exception("Reference hard-point pose did not converge.");
    Equal(0, reference.StrutCompressionM, 1e-8);
    var bump = solver.Solve(config, 0.01); if (!bump.Converged || bump.StrutMotionRatio is null) throw new Exception("Bump pose or local motion ratio was unavailable.");
    if (Math.Abs(bump.StrutMotionRatio.MotionRatio) <= 1e-5) throw new Exception("Fixture must produce a non-zero local strut motion ratio.");
    if (bump.RollCentre.Validity == KinematicValidity.Invalid) throw new Exception("Roll-centre diagnostic must not be invalid for a converged fixture.");
    var spring = new Spring(new SpringConfig { Id = "spring", LinearRateNPerM = 100_000 });
    var stiffness = GeometryTravelStiffness.Evaluate("FL", spring, null, bump);
    var value = spring.EvaluateCompression(bump.StrutCompressionM);
    Equal(value.TangentRateNPerM * bump.StrutMotionRatio.MotionRatio * bump.StrutMotionRatio.MotionRatio + value.ForceN * bump.StrutMotionRatio.MotionRatioDerivativePerM, stiffness.SpringOnlyNPerM, 2);
});
Run("planar MacPherson marks tie-rod and jacking scope instead of inventing a 3D result", () =>
{
    var pose = new MacPhersonPlanarSolver().Solve(PlanarStrutFixture(), 0.01);
    if (!pose.Diagnostics.Any(d => d.Code == "tie-rod-planar-constraint")) throw new Exception("Tie rod must remain an explicit planar-only constraint until 3D steering is solved.");
    if (pose.Jacking.Validity == KinematicValidity.Valid) throw new Exception("Jacking must not be presented as a fully validated chassis force result.");
});
Run("solved strut travel changes local motion ratio and tubular ARB tangent", () =>
{
    var solver = new MacPhersonPlanarSolver(); var reference = solver.Solve(PlanarStrutFixture(), 0.01); var bump = solver.Solve(PlanarStrutFixture(), 0.10);
    if (reference.StrutMotionRatio is null || bump.StrutMotionRatio is null) throw new Exception($"Fixture travel motion ratios were unavailable ({reference.Converged}/{reference.StrutMotionRatio is not null}, {bump.Converged}/{bump.StrutMotionRatio is not null}).");
    if (Math.Abs(reference.StrutMotionRatio.MotionRatio - bump.StrutMotionRatio.MotionRatio) < 1e-5) throw new Exception($"Hard-point travel must change this fixture's local motion ratio ({reference.StrutMotionRatio.MotionRatio}, {bump.StrutMotionRatio.MotionRatio}).");
    var bar = TubularBar();
    var referenceTangent = bar.WheelTangent(reference.StrutMotionRatio.MotionRatio, reference.StrutMotionRatio.MotionRatio);
    var bumpTangent = bar.WheelTangent(bump.StrutMotionRatio.MotionRatio, bump.StrutMotionRatio.MotionRatio);
    if (Math.Abs(referenceTangent.LeftSelfNPerM - bumpTangent.LeftSelfNPerM) < 1e-3) throw new Exception("ARB tangent must follow supplied solved travel derivatives.");
});
Run("dashboard keeps legacy direct ARB inputs hidden by default", () =>
{
    var xaml = File.ReadAllText(Path.Combine("src", "SuspensionSimulation.App", "MainWindow.xaml"));
    if (!xaml.Contains("x:Name=\"LegacyDirectArbPanel\" Visibility=\"Collapsed\"", StringComparison.Ordinal)) throw new Exception("Legacy ARB panel must be collapsed by default.");
    if (!xaml.Contains("Enable legacy direct-rate import mode", StringComparison.Ordinal)) throw new Exception("Legacy mode must be explicitly opt-in.");
    if (xaml.Contains("Front ARB local tangent", StringComparison.Ordinal) || xaml.Contains("Rear ARB local tangent", StringComparison.Ordinal)) throw new Exception("Primary UI must expose geometry parameters, not a direct tangent control.");
});
Run("Cayman dashboard stiffness path is geometry-backed rather than unavailable", () =>
{
    var directory = Path.Combine("data", "synthetic", "cayman");
    var package = SyntheticFixtureLoader.LoadFourStrutPackage(directory);
    var load = new FourStrutQuasiStaticSolver().Solve(package, new LoadCase("dashboard-test", 0, 0.7, 0));
    var hardPoints = FourCornerMacPhersonGeometryLoader.Load(directory);
    var modular = ModularSetupLoader.Load(directory);
    var components = modular.Assembly.Corners.Select(c => new GeometryCornerComponents(c.Corner.ToString(),
        new Spring(modular.Springs.Single(x => x.Id == c.SpringId)), new BumpStop(modular.BumpStops.Single(x => x.Id == c.BumpStopId)))).ToArray();
    var result = new FourCornerGeometryStiffnessSolver().Solve(hardPoints, load, components, package.AntiRollBars);
    if (!result.IsGeometryBacked || result.Corners.Count != 4 || result.Corners.Any(x => !double.IsFinite(x.TotalSelfNPerM)))
        throw new Exception("Cayman dashboard path must return four finite geometry-backed tangent results.");
    if (result.Corners.Any(x => !x.Diagnostics.Any(d => d.Code == "arb-travel-scaled-from-strut")))
        throw new Exception("Cayman dashboard path must identify its solved travel-to-ARB mapping.");
});
Run("geometry-backed RSD and RCB respond to solved travel and ARB end-link angle", () =>
{
    var directory = Path.Combine("data", "synthetic", "cayman"); var package = SyntheticFixtureLoader.LoadFourStrutPackage(directory);
    var geometry = FourCornerMacPhersonGeometryLoader.Load(directory); var modular = ModularSetupLoader.Load(directory); var components = CaymanGeometryComponents(directory);
    var loadSolver = new FourStrutQuasiStaticSolver(); var geometrySolver = new FourCornerGeometryStiffnessSolver();
    var lowTravel = geometrySolver.Solve(geometry, loadSolver.Solve(package, new LoadCase("low", 0, .15, 0)), components, package.AntiRollBars).Balance;
    var highTravel = geometrySolver.Solve(geometry, loadSolver.Solve(package, new LoadCase("high", 0, .70, 0)), components, package.AntiRollBars).Balance;
    if (lowTravel is null || highTravel is null) throw new Exception("Geometry-backed balances must be available.");
    if (Math.Abs(lowTravel.FrontRollStiffnessDistribution - highTravel.FrontRollStiffnessDistribution) < 1e-7 || Math.Abs(lowTravel.RollCoupleBiasPercentagePoints - highTravel.RollCoupleBiasPercentagePoints) < 1e-5)
        throw new Exception("Solved travel must update current geometry-backed RSD and RCB.");
    var sourceFront = modular.AntiRollBars.Single(x => x.Id == modular.Assembly.FrontAntiRollBarId); var tube = sourceFront.Tubular!;
    // Change a fully modeled attachment state (arm length); this recomputes the
    // tubular virtual-work tangent and therefore displayed RSD/RCB.
    var armConfig = sourceFront with { Tubular = tube with { Left = tube.Left with { LeverArmM = tube.Left.LeverArmM * .85 }, Right = tube.Right with { LeverArmM = tube.Right.LeverArmM * .85 } } };
    var armPackage = package with { AntiRollBars = package.AntiRollBars.Select(x => x.Axle == "front" ? AntiRollBarModel.FromConfig("front", armConfig, "test") : x).ToArray() };
    var armChanged = geometrySolver.Solve(geometry, loadSolver.Solve(armPackage, new LoadCase("arm", 0, .70, 0)), components, armPackage.AntiRollBars).Balance;
    if (armChanged is null || Math.Abs(highTravel.FrontRollStiffnessDistribution - armChanged.FrontRollStiffnessDistribution) < 1e-7 || Math.Abs(highTravel.RollCoupleBiasPercentagePoints - armChanged.RollCoupleBiasPercentagePoints) < 1e-5)
        throw new Exception("Modeled tubular attachment geometry must recompute current RSD and RCB.");
    // A non-vertical link angle needs an explicit 3D bar-end travel derivative;
    // do not emit misleading balance metrics from an inferred direct rate.
    var angledConfig = sourceFront with { Tubular = tube with { Left = tube.Left with { EndLinkAngleFromVerticalRad = .45 }, Right = tube.Right with { EndLinkAngleFromVerticalRad = .45 } } };
    var angledPackage = package with { AntiRollBars = package.AntiRollBars.Select(x => x.Axle == "front" ? AntiRollBarModel.FromConfig("front", angledConfig, "test") : x).ToArray() };
    try { _ = geometrySolver.Solve(geometry, loadSolver.Solve(angledPackage, new LoadCase("angle", 0, .70, 0)), components, angledPackage.AntiRollBars); }
    catch (InvalidOperationException) { return; }
    throw new Exception("Non-vertical ARB-angle state must be rejected until explicit 3D bar-end travel is supplied.");
});
Run("pure longitudinal linear no-stop geometry RSD preserves the reference roll tangent", () =>
{
    var directory = Path.Combine("data", "synthetic", "cayman"); var package = SyntheticFixtureLoader.LoadFourStrutPackage(directory);
    var geometry = FourCornerMacPhersonGeometryLoader.Load(directory); var components = CaymanGeometryComponents(directory);
    var loadSolver = new FourStrutQuasiStaticSolver(); var tangentSolver = new FourCornerGeometryStiffnessSolver();
    var reference = tangentSolver.Solve(geometry, loadSolver.Solve(package, new LoadCase("reference", 0, 0, 0)), components, package.AntiRollBars);
    var braking = tangentSolver.Solve(geometry, loadSolver.Solve(package, new LoadCase("braking", -1.2, 0, 0)), components, package.AntiRollBars);
    if (reference.Balance is null || braking.Balance is null) throw new Exception("Reference and braking tangent balances must solve.");
    Equal(reference.Balance.FrontRollStiffnessDistribution, braking.Balance.FrontRollStiffnessDistribution, 1e-10);
    if (!braking.Diagnostics.Any(x => x.Code == "pure-longitudinal-reference-roll-tangent")) throw new Exception("Pure-longitudinal reference-tangent convention must be disclosed.");
});
Run("dashboard render failure preserves the prior plot and records a diagnostic", () =>
{
    var source = File.ReadAllText(Path.Combine("src", "SuspensionSimulation.App", "MainWindow.xaml.cs"));
    if (!source.Contains("previous valid plot retained", StringComparison.Ordinal) || !source.Contains("App.Log($\"Render failed", StringComparison.Ordinal)) throw new Exception("Render failure recovery must retain the prior plot and write a diagnostic.");
    var marker = source.IndexOf("catch (Exception exception)", StringComparison.Ordinal);
    var recovery = source[marker..Math.Min(source.Length, marker + 700)];
    if (recovery.Contains("CarCanvas.Children.Clear", StringComparison.Ordinal) || recovery.Contains("MetricsGrid.ItemsSource = null", StringComparison.Ordinal)) throw new Exception("Render recovery must not blank the dashboard.");
});

Console.WriteLine($"{40 - failures.Count}/40 regression checks passed.");
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
    Left = new ArbSideGeometry(0.15, 0.25, 0, 1),
    Right = new ArbSideGeometry(0.15, 0.25, 0, 1)
});

static GeometryCornerComponents[] CaymanGeometryComponents(string directory)
{
    var setup = ModularSetupLoader.Load(directory);
    return setup.Assembly.Corners.Select(c => new GeometryCornerComponents(c.Corner.ToString(),
        new Spring(setup.Springs.Single(x => x.Id == c.SpringId)), new BumpStop(setup.BumpStops.Single(x => x.Id == c.BumpStopId)))).ToArray();
}

static MacPhersonPlanarCornerConfig PlanarStrutFixture() => new()
{
    Id = "planar-fixture",
    LowerArmInnerPivotM = new Vector3(0, 0.00f, 0.00f),
    LowerBallJointReferenceM = new Vector3(0, 1.00f, 0.00f),
    StrutLowerReferenceM = new Vector3(0, 1.00f, 1.00f),
    UpperStrutMountM = new Vector3(0, 0.00f, 2.00f),
    WheelCentreReferenceM = new Vector3(0, 1.00f, 0.50f),
    TieRodInnerM = new Vector3(0, 0.20f, 0.65f),
    TieRodOuterReferenceM = new Vector3(0, 0.98f, 0.55f),
    GroundPlaneZM = 0
};
