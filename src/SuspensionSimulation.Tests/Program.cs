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
using SuspensionSimulation.Solvers.HeadToHead;
using SuspensionSimulation.Reference;
using System.Numerics;
using SuspensionSimulation.Core.Kinematics;

if (args.Contains("--write-viz-baseline"))
{
    SuspensionSimulation.Tests.VisualizerBaseline.Write();
    return;
}
if(args.Contains("--write-viz-limit-cases")) {SuspensionSimulation.Tests.VisualizerLimitCases.Write();return;}

var failures = new List<string>();
var totalChecks = 0;
Run("component templates drive rigid geometry and immutable revisions preserve history conflicts measurements and drafts",SuspensionSimulation.Tests.ComponentAuthoringChecks.Verify);
Run("stock Garage seeding equipment selection frozen revisions and reference solving are consistent",SuspensionSimulation.Tests.GarageAnalysisChecks.Verify);
Run("Garage named entries round-trip independently with revisions archive recovery and conflict protection",SuspensionSimulation.Tests.GarageChecks.Persistence);
Run("design contracts preserve SI geometry and separate driving dimensions from measurements",SuspensionSimulation.Tests.DesignContractChecks.RoundTrip);
Run("Garage contracts reject geometry alignment rate overrides and double mass accounting",SuspensionSimulation.Tests.DesignContractChecks.Ownership);
Run("design validation rejects duplicate IDs unresolved frames and invalid axes",SuspensionSimulation.Tests.DesignContractChecks.Structure);
Run("assembly contracts retain nested kinds mirrors and exact frozen dependency manifests",SuspensionSimulation.Tests.DesignContractChecks.AssemblyAndManifest);
Run("rigid travel stops enforce exact bounds and unilateral reactions, arrest loading and release on unloading",()=>SuspensionSimulation.Tests.HardStopChecks.Analytic(SymmetricPackage()));
Run("spring stroke markers preserve mount points and meet the upper mount exactly at bottom-out",SuspensionSimulation.Tests.HardStopChecks.Strokes);
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
Run("head-to-head dashboard exposes tubular dimensions and no direct ARB rate", () =>
{
    var xaml = File.ReadAllText(Path.Combine("src", "SuspensionSimulation.App", "MainWindow.xaml"));
    if (!xaml.Contains("Front bar OD (mm)", StringComparison.Ordinal) || !xaml.Contains("Front bar ID (mm)", StringComparison.Ordinal)) throw new Exception("Primary UI must expose tubular geometry.");
    if (xaml.Contains("direct rate", StringComparison.OrdinalIgnoreCase) || xaml.Contains("local tangent", StringComparison.OrdinalIgnoreCase)) throw new Exception("Primary UI must not expose a direct ARB rate/tangent input.");
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
    if (!source.Contains("previous valid plots retained", StringComparison.Ordinal) || !source.Contains("App.LogException($\"Head-to-head render failed", StringComparison.Ordinal)) throw new Exception("Render failure recovery must retain the prior plots and write a diagnostic.");
    var marker = source.IndexOf("catch (Exception exception)", StringComparison.Ordinal);
    var recovery = source[marker..Math.Min(source.Length, marker + 700)];
    if (recovery.Contains("ACarCanvas.Children.Clear", StringComparison.Ordinal) || recovery.Contains("BCarCanvas.Children.Clear", StringComparison.Ordinal) || recovery.Contains("ComparisonGrid.ItemsSource = null", StringComparison.Ordinal)) throw new Exception("Render recovery must not blank the dashboard.");
});

Run("desktop app creates timestamped project-local logs with reproducibility context", () =>
{
    var app = File.ReadAllText(Path.Combine("src", "SuspensionSimulation.App", "App.xaml.cs"));
    var window = File.ReadAllText(Path.Combine("src", "SuspensionSimulation.App", "MainWindow.xaml.cs"));
    if (!app.Contains("Path.Combine(RepositoryRoot, \"logs\"", StringComparison.Ordinal) || !app.Contains("run-{RunId}.log", StringComparison.Ordinal))
        throw new Exception("Each application run must use a unique project-local log file.");
    foreach (var marker in new[] { "run-manifest", "GitSnapshot()", "RuntimeInformation.FrameworkDescription", "LogException" })
        if (!app.Contains(marker, StringComparison.Ordinal)) throw new Exception($"Run log is missing reproducibility marker {marker}.");
    foreach (var marker in new[] { "stock-reference-derivation", "render-attempt", "packageFingerprint", "frontSpringKgfPerMm", "frontBarOdMm", "render-result", "TangentRollStiffnesses", "CaptureResult" })
        if (!window.Contains(marker, StringComparison.Ordinal)) throw new Exception($"Dashboard log is missing state marker {marker}.");
});

Run("stock-twins package retains reference-sheet envelope and declared topology", () =>
{
    var directory = Path.Combine("data", "synthetic", "gr86");
    var setup = ModularSetupLoader.Load(directory);
    Equal(1360.778, setup.Chassis.TotalMassKg, 1e-9);
    Equal(.5500000735, setup.Chassis.StaticFrontWeightFraction, 1e-12);
    Equal(2.57556, setup.Chassis.WheelbaseM, 1e-12);
    Equal(1.53292, setup.Chassis.FrontTrackM, 1e-12);
    Equal(1.5494, setup.Chassis.RearTrackM, 1e-12);
    var front = File.ReadAllText(Path.Combine(directory, "front-strut-hard-points.json"));
    var rear = File.ReadAllText(Path.Combine(directory, "rear-double-wishbone-hard-points.json"));
    if (!front.Contains("front-macpherson-3d", StringComparison.Ordinal) || !rear.Contains("rear-double-wishbone-multilink-3d", StringComparison.Ordinal))
        throw new Exception("GR86 package must explicitly retain front-strut and rear-multilink topology.");
    if (setup.AntiRollBars.Any(x => x.ModelKind != AntiRollBarModelKind.TubularGeometry || x.DirectInstalledWheelPairRateNPerM != 0))
        throw new Exception("GR86 head-to-head package must not contain a direct-rate ARB input.");
});

Run("stock GR86 and BRZ reference calculations independently reproduce workbook rows", () =>
{
    var reference = Stock86BrzReferenceLoader.Load(Path.Combine("data", "reference", "stock-86-brz", "reference-sheet.json"));
    foreach (var setup in reference.Setups)
    {
        var actual = Stock86BrzReferenceCalculator.Calculate(reference, setup); var expected = setup.Expected;
        Equal(expected.FrontSpringWheelRateKgfPerMm, actual.FrontSpringWheelRateKgfPerMm, 1e-9);
        Equal(expected.RearSpringWheelRateKgfPerMm, actual.RearSpringWheelRateKgfPerMm, 1e-9);
        Equal(expected.FrontRideFrequencyHz, actual.FrontRideFrequencyHz, 2e-9);
        Equal(expected.RearRideFrequencyHz, actual.RearRideFrequencyHz, 2e-9);
        Equal(expected.RearToFrontFrequencyRatio, actual.RearToFrontFrequencyRatio, 2e-9);
        if (actual.RideCharacter != expected.RideCharacter) throw new Exception($"{setup.Name} ride character mismatch.");
        Equal(expected.FrontArbRawRateKgfPerMm, actual.FrontArb.RawRateKgfPerMm, 2e-9);
        Equal(expected.RearArbRawRateKgfPerMm, actual.RearArb.RawRateKgfPerMm, 2e-9);
        Equal(expected.FrontArbWheelRateKgfPerMm, actual.FrontArb.WheelRateKgfPerMm, 2e-9);
        Equal(expected.RearArbWheelRateKgfPerMm, actual.RearArb.WheelRateKgfPerMm, 2e-9);
        Equal(expected.FrontRollStiffnessNmPerDeg, actual.FrontRollStiffnessNmPerDeg, 2e-6);
        Equal(expected.RearRollStiffnessNmPerDeg, actual.RearRollStiffnessNmPerDeg, 2e-6);
        Equal(expected.TotalRollStiffnessNmPerDeg, actual.TotalRollStiffnessNmPerDeg, 2e-6);
        Equal(expected.FrontRollStiffnessDistribution, actual.FrontRollStiffnessDistribution, 2e-10);
        Equal(expected.ArbFractionOfTotalRollStiffness, actual.ArbFractionOfTotalRollStiffness, 2e-10);
    }
});

Run("stock-twins 3D geometry reproduces reference static motion ratios", () =>
{
    var solver = new HeadToHeadComparisonSolver(Path.Combine("data", "synthetic", "gr86"));
    var fl = solver.TravelMaps["FL"].Evaluate(0); var rl = solver.TravelMaps["RL"].Evaluate(0);
    var frontSpring = fl.SpringCompressionPerWheelJounce; var rearSpring = rl.SpringCompressionPerWheelJounce;
    var frontArb = Math.Abs(fl.ArbLowerDerivativePerWheelJounce.Z); var rearArb = Math.Abs(rl.ArbLowerDerivativePerWheelJounce.Z);
    if (Math.Abs(frontSpring-.95) > .003 || Math.Abs(rearSpring-.78) > .003 || Math.Abs(frontArb-.92) > .005 || Math.Abs(rearArb-.55) > .005)
        throw new Exception($"Static ratios front spring={frontSpring:G8}, rear spring={rearSpring:G8}, front ARB={frontArb:G8}, rear ARB={rearArb:G8}.");
});

Run("stock GR86 and BRZ 3D head-to-head reproduces reference static roll balance", () =>
{
    var reference = Stock86BrzReferenceLoader.Load(Path.Combine("data", "reference", "stock-86-brz", "reference-sheet.json"));
    var directory = Path.Combine("data", "synthetic", "gr86"); var vehicle = ModularSetupLoader.Load(directory); var solver = new HeadToHeadComparisonSolver(directory);
    HeadToHeadSetupInput Input(StockSetupReference x) => new(x.Name,
        x.FrontSpringRateKgfPerMm*reference.Constants.SpringRateNPerMPerKgfPerMm,
        x.RearSpringRateKgfPerMm*reference.Constants.SpringRateNPerMPerKgfPerMm,
        x.FrontArb.OutsideDiameterM,x.FrontArb.InsideDiameterM,x.RearArb.OutsideDiameterM,x.RearArb.InsideDiameterM);
    var result=solver.Solve(vehicle,Input(reference.Setups[0]),Input(reference.Setups[1]),new LoadCase("static-reference",0,0,0));
    void Verify(HeadToHeadSideResult side,StockSetupReference source)
    {
        var expected=Stock86BrzReferenceCalculator.Calculate(reference,source); var toPerDegree=Math.PI/180;
        var front=side.State.TangentRollStiffnesses!.Single(x=>x.Axle=="front"); var rear=side.State.TangentRollStiffnesses!.Single(x=>x.Axle=="rear");
        var errors=new[]
        {
            Math.Abs(front.SpringNmPerRad*toPerDegree/expected.FrontSpringRollStiffnessNmPerDeg-1),
            Math.Abs(rear.SpringNmPerRad*toPerDegree/expected.RearSpringRollStiffnessNmPerDeg-1),
            Math.Abs(front.ArbNmPerRad*toPerDegree/expected.FrontArbRollStiffnessNmPerDeg-1),
            Math.Abs(rear.ArbNmPerRad*toPerDegree/expected.RearArbRollStiffnessNmPerDeg-1),
            Math.Abs(side.State.RollStiffnessDistributionFront/expected.FrontRollStiffnessDistribution-1)
        };
        if(errors.Max()>.015) throw new Exception($"{source.Name} static 3D mismatch: spring F/R={front.SpringNmPerRad*toPerDegree:G7}/{rear.SpringNmPerRad*toPerDegree:G7}, ARB F/R={front.ArbNmPerRad*toPerDegree:G7}/{rear.ArbNmPerRad*toPerDegree:G7}, RSD={side.State.RollStiffnessDistributionFront:G7}; max relative error={errors.Max():P3}.");
    }
    Verify(result.A,reference.Setups[0]); Verify(result.B,reference.Setups[1]);
});

Run("head-to-head identical setups produce exactly zero deltas", () =>
{
    var directory = Path.Combine("data", "synthetic", "gr86"); var setup = ModularSetupLoader.Load(directory);
    var input = new HeadToHeadSetupInput("same", 45_000, 50_000, .020, 0, .016, 0);
    var result = new HeadToHeadComparisonSolver(directory).Solve(setup, input, input, new LoadCase("shared", -.4, 1.1, 0));
    Equal(0, result.BMinusA.FrontRsdPercentagePoints, 1e-12);
    Equal(0, result.BMinusA.RcbPercentagePoints, 1e-12);
    foreach (var value in result.BMinusA.CornerLoadN.Values) Equal(0, value, 1e-12);
});

Run("head-to-head tubular diameter edit changes derived ARB and RCB", () =>
{
    var directory = Path.Combine("data", "synthetic", "gr86"); var setup = ModularSetupLoader.Load(directory);
    var a = new HeadToHeadSetupInput("A", 45_000, 50_000, .020, 0, .016, 0);
    var b = a with { Name = "B", FrontArbOutsideDiameterM = .022, FrontArbInsideDiameterM = .010 };
    var result = new HeadToHeadComparisonSolver(directory).Solve(setup, a, b, new LoadCase("shared", 0, 1, 0));
    if (Math.Abs(result.A.FrontArbRollStiffnessNmPerRad - result.B.FrontArbRollStiffnessNmPerRad) < 1)
        throw new Exception("Tubular OD/ID edit did not change the derived front ARB roll stiffness.");
    if (Math.Abs(result.BMinusA.RcbPercentagePoints) < 1e-6)
        throw new Exception("Derived tubular ARB change did not propagate to RCB.");
});

Run("head-to-head UI has one shared controller and two independent setups", () =>
{
    var xaml = File.ReadAllText(Path.Combine("src", "SuspensionSimulation.App", "MainWindow.xaml"));
    if (Count(xaml, "x:Name=\"GCircleCanvas\"") != 1) throw new Exception("Exactly one shared G-circle is required.");
    foreach(var marker in new[]{"STOCK GR86","STOCK BRZ","Front spring (kgf/mm)","REFERENCE ROW 7","REFERENCE ROW 8","FrontUnsprungMass","RearUnsprungMass"})
        if(!xaml.Contains(marker,StringComparison.Ordinal)) throw new Exception($"Stock reference UI is missing {marker}.");
    foreach (var name in new[] { "AFrontSpring", "ARearSpring", "AFrontOd", "AFrontId", "ARearOd", "ARearId", "BFrontSpring", "BRearSpring", "BFrontOd", "BFrontId", "BRearOd", "BRearId" })
        if (!xaml.Contains($"x:Name=\"{name}\"", StringComparison.Ordinal)) throw new Exception($"Missing head-to-head input {name}.");
});

Run("GR86 rigid travel is traced once into four continuous startup maps", () =>
{
    var directory = Path.Combine("data", "synthetic", "gr86");
    var solver = new HeadToHeadComparisonSolver(directory);
    if (!solver.HasPrecomputedGeometry || solver.TravelMaps.Count != 4)
        throw new Exception("The head-to-head solver must own exactly four precomputed corner travel maps.");
    foreach (var (corner, map) in solver.TravelMaps)
    {
        Equal(RigidCornerTravelMapBuilder3D.DefaultMinimumJounceM, map.MinimumJounceM, 1e-12);
        Equal(RigidCornerTravelMapBuilder3D.DefaultMaximumJounceM, map.MaximumJounceM, 1e-12);
        if (map.Samples.Count != 291) throw new Exception($"{corner} map has {map.Samples.Count} samples instead of 291.");
        if (map.MaximumConstraintResidualM >= 1e-4) throw new Exception($"{corner} map contains an unvalidated rigid pose ({map.MaximumConstraintResidualM:G6} m).");
    }
});

Run("precomputed GR86 branch covers the former combined-G solver failure", () =>
{
    var directory = Path.Combine("data", "synthetic", "gr86");
    var setup = ModularSetupLoader.Load(directory);
    var solver = new HeadToHeadComparisonSolver(directory);
    var frontRight = solver.TravelMaps["FR"].Evaluate(-.04658);
    if (frontRight.Validity != "precomputed-rigid-branch" || !double.IsFinite(frontRight.SpringCompressionPerWheelJounce))
        throw new Exception($"Former FR failure point was not covered by the precomputed branch ({frontRight.Validity}).");

    var a = new HeadToHeadSetupInput("A", 45_000, 50_000, .020, 0, .016, 0);
    var b = new HeadToHeadSetupInput("B", 50_000, 55_000, .022, .012, .017, .010);
    var result = solver.Solve(setup, a, b, new LoadCase("former-branch-failure", -.14805797101449256, -1.982608695652174, 0));
    if (!double.IsFinite(result.A.State.RollCoupleBiasPercentagePoints) || !double.IsFinite(result.B.State.RollCoupleBiasPercentagePoints))
        throw new Exception("The former branch-failure load state did not produce finite A/B RCB results.");
});

Run("stock GR86 and BRZ cover the combined-G travel failures captured in the run log", () =>
{
    var directory = Path.Combine("data", "synthetic", "gr86");
    var setup = ModularSetupLoader.Load(directory);
    var reference = Stock86BrzReferenceLoader.Load(Path.Combine("data", "reference", "stock-86-brz", "reference-sheet.json"));
    HeadToHeadSetupInput Input(StockSetupReference x) => new(x.Name,
        x.FrontSpringRateKgfPerMm*reference.Constants.SpringRateNPerMPerKgfPerMm,
        x.RearSpringRateKgfPerMm*reference.Constants.SpringRateNPerMPerKgfPerMm,
        x.FrontArb.OutsideDiameterM, x.FrontArb.InsideDiameterM,
        x.RearArb.OutsideDiameterM, x.RearArb.InsideDiameterM);
    var a = Input(reference.Setups.Single(x => x.Name.Contains("GR86", StringComparison.OrdinalIgnoreCase)));
    var b = Input(reference.Setups.Single(x => x.Name.Contains("BRZ", StringComparison.OrdinalIgnoreCase)));
    var solver = new HeadToHeadComparisonSolver(directory);

    var loggedFailures = new[]
    {
        new LoadCase("logged-110mm-travel", .7562898550724639, -1.5304347826086957, 0),
        new LoadCase("logged-86mm-travel", .8780289855072466, -.8869565217391304, 0)
    };
    foreach (var load in loggedFailures)
    {
        HeadToHeadComparisonResult result;
        try
        {
            result = solver.Solve(setup, a, b, load);
        }
        catch (Exception error)
        {
            throw new Exception($"Captured load '{load.Name}' still fails: {error.Message}", error);
        }
        if (!double.IsFinite(result.A.State.RollCoupleBiasPercentagePoints) || !double.IsFinite(result.B.State.RollCoupleBiasPercentagePoints))
            throw new Exception($"Captured load '{load.Name}' produced a non-finite result.");
    }
});

Run("head-to-head pointer motion reuses startup geometry and coalesces display refreshes", () =>
{
    var window = File.ReadAllText(Path.Combine("src", "SuspensionSimulation.App", "MainWindow.xaml.cs"));
    if (Count(window, "new HeadToHeadComparisonSolver(") != 1)
        throw new Exception("The 3D head-to-head solver must be constructed only once during startup.");
    foreach (var marker in new[] { "DispatcherTimer", "ScheduleRender()", ".Solve(vehicle" })
        if (!window.Contains(marker, StringComparison.Ordinal)) throw new Exception($"Pointer refresh path is missing {marker}.");
    if (!window.Contains("if (!_renderTimer.IsEnabled) _renderTimer.Start();", StringComparison.Ordinal))
        throw new Exception("Continuous pointer motion must be throttled, not indefinitely debounced.");
});

Run("parallel elastic curve preserves static reference and reports force tangent", () =>
{
    var config=new ParallelElasticElementConfig
    {
        Id="gas-spring",Corners=[Corner.FL],
        ForceCurve=[new(-100,1000),new(0,1500),new(100,3500)]
    };
    var element=new CurveParallelElasticElement(config);
    var reference=element.EvaluateWheelJounce(0); var bump=element.EvaluateWheelJounce(.050);
    Equal(0,reference.ForceN,1e-12);
    Equal(1000,bump.ForceN,1e-9);
    Equal(20_000,bump.TangentRateNPerM,1e-9);
});

Run("stock-twin unsprung masses are explicit and alter sprung-body equilibrium", () =>
{
    var directory=Path.Combine("data","synthetic","gr86"); var vehicle=ModularSetupLoader.Load(directory);
    Equal(35,vehicle.Chassis.FrontUnsprungMassPerCornerKg,1e-12); Equal(38,vehicle.Chassis.RearUnsprungMassPerCornerKg,1e-12);
    var reference=Stock86BrzReferenceLoader.Load(Path.Combine("data","reference","stock-86-brz","reference-sheet.json")); var stock=reference.Setups[0];
    var input=new HeadToHeadSetupInput(stock.Name,stock.FrontSpringRateKgfPerMm*9800,stock.RearSpringRateKgfPerMm*9800,stock.FrontArb.OutsideDiameterM,stock.FrontArb.InsideDiameterM,stock.RearArb.OutsideDiameterM,stock.RearArb.InsideDiameterM);
    var solver=new HeadToHeadComparisonSolver(directory); var withUnsprung=solver.Solve(vehicle,input,input,new LoadCase("one-g",0,1,0)).A.State;
    var allSprung=vehicle with { Chassis=vehicle.Chassis with { FrontUnsprungMassPerCornerKg=0,RearUnsprungMassPerCornerKg=0 } };
    var withoutUnsprung=solver.Solve(allSprung,input,input,new LoadCase("one-g",0,1,0)).A.State;
    if(Math.Abs(withUnsprung.RollRad)>=Math.Abs(withoutUnsprung.RollRad))
        throw new Exception("Removing declared unsprung mass should increase the inertia carried through the sprung suspension.");
});

Run("GR86 physical travel limits come from component stroke intersected with geometry", () =>
{
    var solver=new HeadToHeadComparisonSolver(Path.Combine("data","synthetic","gr86"));
    if(solver.PhysicalTravelLimits.Count!=4) throw new Exception("Expected one derived travel limit per corner.");
    foreach(var (corner,limit) in solver.PhysicalTravelLimits)
    {
        if(limit.MinimumJounceM>=0 || limit.MaximumJounceM<=0) throw new Exception($"{corner} travel window does not span static.");
        var map=solver.TravelMaps[corner];
        var rebound=map.Evaluate(limit.MinimumJounceM); var bump=map.Evaluate(limit.MaximumJounceM); var reference=map.Evaluate(0);
        Equal(limit.ReboundComponentTravelM, rebound.SpringLengthM-reference.SpringLengthM, 1e-8);
        Equal(limit.CompressionComponentTravelM, reference.SpringLengthM-bump.SpringLengthM, 1e-8);
        Equal(limit.MinimumJounceM,limit.Constraint.MinimumJounceM,0);Equal(limit.MaximumJounceM,limit.Constraint.MaximumJounceM,0);
    }
});

Run("geometry-derived top and bottom stops keep the stock comparison solvable around 2 G", () =>
{
    var directory=Path.Combine("data","synthetic","gr86"); var vehicle=ModularSetupLoader.Load(directory);
    var reference=Stock86BrzReferenceLoader.Load(Path.Combine("data","reference","stock-86-brz","reference-sheet.json"));
    HeadToHeadSetupInput Input(StockSetupReference x)=>new(x.Name,x.FrontSpringRateKgfPerMm*9800,x.RearSpringRateKgfPerMm*9800,x.FrontArb.OutsideDiameterM,x.FrontArb.InsideDiameterM,x.RearArb.OutsideDiameterM,x.RearArb.InsideDiameterM);
    var solver=new HeadToHeadComparisonSolver(directory); var a=Input(reference.Setups[0]); var b=Input(reference.Setups[1]);
    for(var degree=0;degree<360;degree+=15)
    {
        var angle=degree*Math.PI/180;
        var result=solver.Solve(vehicle,a,b,new LoadCase($"2g-{degree}",2*Math.Cos(angle),2*Math.Sin(angle),0));
        foreach(var side in new[]{result.A,result.B}) foreach(var corner in side.State.Corners)
        {
            var limit=solver.PhysicalTravelLimits[corner.Corner];
            if(corner.RelativeCompressionM<limit.MinimumJounceM-1e-10 || corner.RelativeCompressionM>limit.MaximumJounceM+1e-10)
                throw new Exception($"{degree} deg {side.Input.Name} {corner.Corner} penetrated its rigid travel stop ({corner.RelativeCompressionM:G6} m). ");
            if(!side.State.Converged || Math.Abs(side.State.VerticalResidualN)>1e-6 || Math.Abs(side.State.RollResidualNm)>1e-6 || Math.Abs(side.State.PitchResidualNm)>1e-6) throw new Exception("Rigid equilibrium did not close.");
        }
    }
});

Run("3D MacPherson rigid constraints close and continue through travel", () =>
{
    var solver = new RigidMechanism3DSolver(); var geometry = ThreeDimensionalStrutFixture("FL", 1);
    var reference = solver.Solve(geometry, 0); var travel = solver.SolveTravel(geometry, .010);
    if (!reference.Converged || reference.MaximumConstraintResidualM > 1e-6) throw new Exception($"3D strut reference pose did not close ({reference.MaximumConstraintResidualM:G4} m).");
    if (!travel.Pose.Converged || !double.IsFinite(travel.SpringCompressionPerWheelJounce) || travel.ArbLowerDerivativePerWheelJounce.Length() <= 0)
        throw new Exception($"3D strut travel continuation failed: converged={travel.Pose.Converged}, residual={travel.Pose.MaximumConstraintResidualM:G4}, ds={travel.SpringCompressionPerWheelJounce:G4}, arb={travel.ArbLowerDerivativePerWheelJounce}.");
});

Run("3D double wishbone rigid constraints close and continue through travel", () =>
{
    var solver = new RigidMechanism3DSolver(); var geometry = ThreeDimensionalRearFixture("RL", 1);
    var reference = solver.Solve(geometry, 0); var travel = solver.SolveTravel(geometry, .010);
    if (!reference.Converged || reference.MaximumConstraintResidualM > 1e-6) throw new Exception($"3D rear reference pose did not close ({reference.MaximumConstraintResidualM:G4} m).");
    if (!travel.Pose.Converged || !double.IsFinite(travel.SpringCompressionPerWheelJounce) || travel.ArbLowerDerivativePerWheelJounce.Length() <= 0)
        throw new Exception($"3D rear travel continuation failed: converged={travel.Pose.Converged}, residual={travel.Pose.MaximumConstraintResidualM:G4}, ds={travel.SpringCompressionPerWheelJounce:G4}, arb={travel.ArbLowerDerivativePerWheelJounce}.");
    if (travel.Pose.Topology != "double-wishbone-multilink-3d") throw new Exception("Rear topology must not be substituted with a strut.");
});

Run("committed GR86 hard points close as front strut and rear multilink mechanisms", () =>
{
    var package = Gr86HardPointPackageLoader.Load(Path.Combine("data", "synthetic", "gr86"));
    var solver = new RigidMechanism3DSolver();
    foreach (var corner in package.Front.Values)
    {
        var travel = solver.SolveTravel(corner, .005);
        if (!travel.Pose.Converged || !double.IsFinite(travel.SpringCompressionPerWheelJounce)) throw new Exception($"{corner.Id} committed front geometry did not continue: residual={travel.Pose.MaximumConstraintResidualM:G4}, iter={travel.Pose.Iterations}, ds={travel.SpringCompressionPerWheelJounce:G4}.");
    }
    foreach (var corner in package.Rear.Values)
    {
        var travel = solver.SolveTravel(corner, .005);
        if (!travel.Pose.Converged || !double.IsFinite(travel.SpringCompressionPerWheelJounce)) throw new Exception($"{corner.Id} committed rear geometry did not continue: residual={travel.Pose.MaximumConstraintResidualM:G4}, iter={travel.Pose.Iterations}, ds={travel.SpringCompressionPerWheelJounce:G4}.");
    }
});

Run("3D fixed-length ARB endlinks reject heave and produce a coupled energy Hessian", () =>
{
    var config = ThreeDimensionalArbFixture(); var solver = new TubularArbMechanism3DSolver();
    Vector3 Left(double jounce) => config.LeftLowerJointReference + new Vector3(0,0,(float)jounce);
    Vector3 Right(double jounce) => config.RightLowerJointReference + new Vector3(0,0,(float)jounce);
    var heave = solver.Evaluate(config, Left, Right, .010, .010);
    Equal(0, heave.BarTwistRad, 2e-6); Equal(0, heave.StoredEnergyJ, 1e-8);
    var roll = solver.Evaluate(config, Left, Right, .010, -.010);
    if (roll.Validity == "invalid" || roll.WheelTangentNPerM[0,0] <= 0 || roll.WheelTangentNPerM[1,1] <= 0 || roll.WheelTangentNPerM[0,1] >= 0)
        throw new Exception("3D ARB must expose a physical coupled wheel Hessian.");
    Equal(roll.WheelTangentNPerM[0,1], roll.WheelTangentNPerM[1,0], 1e-9);
    Vector3 ImpossibleLeft(double jounce) => config.LeftLowerJointReference + new Vector3(1,0,(float)jounce);
    var impossible = solver.Evaluate(config, ImpossibleLeft, Right, 0, 0);
    if (impossible.Validity != "invalid" || impossible.Left.ConstraintResidualM < .1)
        throw new Exception("An actually unreachable fixed-length endlink must remain invalid.");
});

Run("3D ARB tangent responds to solved endlink geometry and hollow tube ID", () =>
{
    var solver = new TubularArbMechanism3DSolver(); var vertical = ThreeDimensionalArbFixture();
    Vector3 Left(double jounce) => vertical.LeftLowerJointReference + new Vector3(0,0,(float)jounce);
    Vector3 Right(double jounce) => vertical.RightLowerJointReference + new Vector3(0,0,(float)jounce);
    var solid = solver.Evaluate(vertical, Left, Right, .008, -.008);
    var hollowConfig = vertical with { InsideDiameterM = .014 };
    var hollow = solver.Evaluate(hollowConfig, Left, Right, .008, -.008);
    if (hollow.TorsionalStiffnessNmPerRad >= solid.TorsionalStiffnessNmPerRad || hollow.WheelTangentNPerM[0,0] >= solid.WheelTangentNPerM[0,0])
        throw new Exception("Increasing tube ID must reduce torsional and wheel tangent stiffness.");
    var angled = vertical with
    {
        LeftLowerJointReference = vertical.LeftLowerJointReference + new Vector3(.06f,0,0),
        RightLowerJointReference = vertical.RightLowerJointReference + new Vector3(.06f,0,0)
    };
    Vector3 AngledLeft(double jounce) => angled.LeftLowerJointReference + new Vector3(0,0,(float)jounce);
    Vector3 AngledRight(double jounce) => angled.RightLowerJointReference + new Vector3(0,0,(float)jounce);
    var angledState = solver.Evaluate(angled, AngledLeft, AngledRight, .008, -.008);
    if (Math.Abs(angledState.WheelTangentNPerM[0,0]-solid.WheelTangentNPerM[0,0]) < 1)
        throw new Exception("Changing fixed-link geometry must change the derived wheel tangent.");
});

Run("historical free-travel visualizer cases retain their numerical baseline", SuspensionSimulation.Tests.VisualizerBaseline.Verify);
Run("full branch poses reconstruct rigid off-grid arms and repeat forward/reverse", SuspensionSimulation.Tests.InspectionChecks.Branches);
Run("accepted snapshots preserve A/B metrics final travel endlinks and FR top-out identity", SuspensionSimulation.Tests.InspectionChecks.Snapshots);
Run("inspection brackets bump engagement and rejects invalid tubes springs pre-lift and non-convergence", SuspensionSimulation.Tests.InspectionChecks.FailureAndBumpCases);
Run("rack steering preserves rigid constraints through lock and bump; rack height changes bump steer", SuspensionSimulation.Tests.SteeringChecks.Constraints);
Run("static alignment and live axle toe thrust use actual constrained wheel orientations", SuspensionSimulation.Tests.SteeringChecks.AlignmentAndSnapshots);
Run("cube orbit and viewing-axis roll preserve target distance and camera frame", SuspensionSimulation.Tests.SteeringChecks.Camera);
Run("verified limit fixture pairs switch actual active state in accepted scenes", SuspensionSimulation.Tests.VisualizerLimitCases.Verify);
Run("reference inspection includes four corners both bars and explicit unsupported topology", () =>
{
    var builder=new InspectionSceneBuilder("data/synthetic/gr86");var scene=builder.BuildReference(.5);
    if(scene.Wheels.Length!=4 || scene.Segments.Count(x=>x.Id.EndsWith(".axis"))!=2) throw new Exception("Missing wheels or bars.");
    if(scene.Segments.Select(x=>x.Id).Distinct().Count()!=scene.Segments.Length) throw new Exception("Component IDs must be stable and unique.");
    foreach(var id in new[]{"FL","FR","RL","RR"})
    {
        if(!scene.Segments.Any(x=>x.Id==id+".spring") || !scene.Segments.Any(x=>x.Id==id+".endlink")) throw new Exception("Missing spring or endlink.");
        var wheel=scene.Wheels.Single(x=>x.Corner==id);
        var axle=Vector3.Transform(Vector3.UnitY,wheel.Orientation);
        Equal(wheel.CenterM.Z-wheel.RadiusM*Math.Sqrt(1-axle.Z*axle.Z)-wheel.WidthM*.5*Math.Abs(axle.Z),wheel.GroundGapM,1e-7);
        if(Math.Sign(wheel.CenterM.Y)!=(id.EndsWith("L")?1:-1)) throw new Exception("Wrong corner placement.");
    }
    if(scene.Segments.Any(x=>x.Corner.StartsWith("R")&&x.Label.Contains("strut",StringComparison.OrdinalIgnoreCase))) throw new Exception("Rear alias must never create a strut.");
    if(scene.Segments.Count(x=>x.Source==SceneSource.UnconstrainedReference)!=2) throw new Exception("Trailing links must be explicit references.");
});
Run("visualizer frame agrees with compression signs and preserves rigid distances", () =>
{
    var p=new Vector3(1.2f,.7f,.3f);
    var pose=new InspectionBodyPose(.002,.00001,-.00002,.5);
    var dz=pose.ToWorld(p).Z-p.Z;
    Equal(-.002+p.Y*.00001-p.X*(-.00002),dz,1e-7);
    var q=new Vector3(-1.4f,-.7f,.6f);
    Equal(Vector3.Distance(p,q),Vector3.Distance(pose.ToWorld(p),pose.ToWorld(q)),1e-6);
    var geometry=Gr86HardPointPackageLoader.Load("data/synthetic/gr86");
    Equal(geometry.Front["FL"].Upright.WheelCenter.X,geometry.Front["FR"].Upright.WheelCenter.X,1e-8);
    Equal(geometry.Front["FL"].Upright.WheelCenter.Y,-geometry.Front["FR"].Upright.WheelCenter.Y,1e-8);
});
Run("rigid travel diagnostics distinguish loaded stops from an unloaded boundary", () =>
{
    var limit=new SuspensionSimulation.Components.GeometryDerivedWheelTravelLimit("FR","stroke",-.09,.05,.085,.05,"estimated");
    var rebound=CornerLimitDiagnostic.Create(limit,-.09,1.52,-1000,"rebound");
    if(!rebound.Active || rebound.Direction!="rebound" || !rebound.Message.Contains("topped out")) throw new Exception("Top-out must be named correctly.");
    Equal(-1000,rebound.ReactionForceN,1e-6);Equal(0,rebound.PenetrationM,0);
    if(!double.IsPositiveInfinity(rebound.TangentNPerM)||!double.IsPositiveInfinity(rebound.RollStiffnessContributionNmPerRad)) throw new Exception("Stop tangent must be infinite.");
    var boundary=CornerLimitDiagnostic.Create(limit,-.09,1.52);
    if(boundary.Active) throw new Exception("An unloaded boundary is not an active stop.");
    var bump=CornerLimitDiagnostic.Create(limit,.05,1.52,1000,"bump");
    if(bump.Direction!="bump" || !bump.Message.Contains("bottomed out")) throw new Exception("Bottom-out must be named correctly.");
    Equal(1000,bump.ReactionForceN,1e-6);Equal(0,bump.PenetrationM,0);
});
Console.WriteLine($"{totalChecks - failures.Count}/{totalChecks} regression checks passed.");
foreach (var failure in failures) Console.Error.WriteLine(failure);
Environment.ExitCode = failures.Count == 0 ? 0 : 1;

void Run(string name, Action test)
{
    totalChecks++;
    try { test(); Console.WriteLine($"PASS {name}"); }
    catch (Exception e) { failures.Add($"FAIL {name}: {e.Message}"); }
}
static void Equal(double expected, double actual, double tolerance)
{
    if (Math.Abs(expected - actual) > tolerance) throw new Exception($"Expected {expected:G17}, got {actual:G17} (tolerance {tolerance:G17}).");
}
static int Count(string text, string value)
{
    var count = 0; var index = 0;
    while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0) { count++; index += value.Length; }
    return count;
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

static MacPhersonCorner3D ThreeDimensionalStrutFixture(string id, float side)
{
    Vector3 Mirror(float x, float y, float z) => new(x, side*y, z);
    var upright = new RigidUprightPoints3D(Mirror(0, .75f, .31f), Mirror(0, .72f, .20f), Mirror(0, .69f, .50f),
        Mirror(-.05f, .69f, .27f), Mirror(0, .69f, .29f), Mirror(0, .68f, .32f), Mirror(.01f, .67f, .50f));
    return new(id, upright, Mirror(.13f,.30f,.18f), Mirror(-.14f,.31f,.19f), Mirror(-.02f,.56f,.80f), Mirror(-.16f,.34f,.29f), Mirror(-.02f,.56f,.80f));
}

static DoubleWishboneCorner3D ThreeDimensionalRearFixture(string id, float side)
{
    Vector3 Mirror(float x, float y, float z) => new(x, side*y, z);
    var upright = new RigidUprightPoints3D(Mirror(0,.77f,.31f), Mirror(0,.73f,.22f), Mirror(0,.70f,.50f),
        Mirror(-.06f,.71f,.31f), Mirror(0,.70f,.30f), Mirror(.02f,.59f,.25f), Mirror(-.02f,.63f,.26f));
    return new(id, upright, Mirror(.14f,.31f,.20f), Mirror(-.15f,.32f,.21f), Mirror(.12f,.40f,.52f), Mirror(-.13f,.41f,.53f),
        Mirror(-.17f,.35f,.30f), Mirror(.01f,.50f,.72f), Mirror(.02f,.59f,.25f), Mirror(-.02f,.63f,.26f));
}

static TubularArbMechanism3DConfig ThreeDimensionalArbFixture() => new(
    "3d-arb", new(0,-.5f,0), new(0,.5f,0), new(.15f,.5f,0), new(.15f,-.5f,0),
    new(.15f,.5f,-.25f), new(.15f,-.5f,-.25f), .020, 0, 79.3e9, 1.0, "analytic synthetic");
