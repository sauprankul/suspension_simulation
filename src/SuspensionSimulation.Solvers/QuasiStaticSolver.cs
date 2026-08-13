using SuspensionSimulation.Core;
using SuspensionSimulation.Components;

namespace SuspensionSimulation.Solvers;

public sealed record CornerResult(
    string Corner, double StaticLoadN, double DeltaLoadN, double NormalLoadN,
    double RelativeCompressionM, double BumpStopForceN, bool IsLifted);

public sealed record AxleBalance(string Axle, double TangentRollStiffnessNmPerRad,
    double LoadTransferN, double SpringLoadTransferN, double ArbLoadTransferN,
    double BumpStopLoadTransferN);

public sealed record QuasiStaticResult(
    LoadCase LoadCase, double HeaveCompressionM, double RollRad, double PitchRad,
    IReadOnlyList<CornerResult> Corners, IReadOnlyList<ComponentCornerTangentWheelStiffness> CornerTangentStiffnesses, IReadOnlyList<AxleBalance> Axles,
    double RollStiffnessDistributionFront, double LoadTransferDistributionFront,
    double StaticFrontWeightFraction, double RollCoupleBiasPercentagePoints, IReadOnlyList<string> Warnings,
    double VerticalResidualN, double RollResidualNm, double PitchResidualNm,
    int SolverIterations, bool Converged);

/// <summary>
/// Small-displacement, flat-road, rigid-body elastic equilibrium. It deliberately predicts
/// point-contact normal loads, not tire force, grip, roll-center/jacking, or transient behavior.
/// States are DELTAS from a static reference: bump-stop gaps therefore apply to incremental
/// compression only until a calibrated static operating point is implemented.
/// </summary>
public sealed class FourStrutQuasiStaticSolver
{
    public const double GravityMPerS2 = 9.80665;

    public QuasiStaticResult Solve(VehiclePackage package, LoadCase loadCase)
    {
        Validate(package);
        var geometry = Geometry(package.Chassis);
        var springByCorner = package.Springs.Corners.ToDictionary(c => c.Corner, StringComparer.OrdinalIgnoreCase);
        var bars = package.AntiRollBars.ToDictionary(b => b.Axle, StringComparer.OrdinalIgnoreCase);
        var baseLoads = StaticLoads(package.Chassis);
        var target = new[]
        {
            package.Chassis.MassKg * GravityMPerS2 * loadCase.VerticalG,
            package.Chassis.MassKg * GravityMPerS2 * loadCase.LateralG * package.Chassis.CgHeightM,
            -package.Chassis.MassKg * GravityMPerS2 * loadCase.LongitudinalG * package.Chassis.CgHeightM
        };

        var solve = SolveNewton(geometry, springByCorner, bars, target);
        var state = solve.State;
        var force = Forces(geometry, springByCorner, bars, state) with { Iterations = solve.Iterations };
        var resultants = Resultants(geometry, force.TotalDeltaLoads);
        var warnings = new List<string>();
        if (!solve.Converged) warnings.Add("Newton iteration limit reached; result is not valid for engineering interpretation.");
        var cornerResults = geometry.Select((g, i) => new CornerResult(
            g.Corner, baseLoads[g.Corner], force.TotalDeltaLoads[i], baseLoads[g.Corner] + force.TotalDeltaLoads[i],
            Compression(g, state), force.BumpStopForces[i], baseLoads[g.Corner] + force.TotalDeltaLoads[i] <= 0)).ToList();
        foreach (var corner in cornerResults.Where(c => c.IsLifted)) warnings.Add($"{corner.Corner} has non-positive normal load; tire lift-off is reported but not solved as an active constraint.");
        var activeStops = cornerResults.Where(c => c.BumpStopForceN > 0).Select(c => c.Corner).ToArray();
        if (activeStops.Length > 0) warnings.Add($"Active bump-stop branch: {string.Join(", ", activeStops)}. Tangent RSD reflects this engagement and can change from the linear baseline.");
        if (force.BumpStopForces.Any(f => f > 0)) warnings.Add("Bump-stop forces are relative to the uncalibrated static reference state.");
        warnings.Add("No tire model: normal-load changes are not traction, balance, or lap-time predictions.");
        warnings.AddRange(package.Scope.OmittedEffects.Select(x => $"Omitted: {x}"));

        var cornerStiffnesses = geometry.Select((g, i) => CornerTangent(g, i, force, springByCorner, bars, cornerResults[i], solve.Converged)).ToList();
        var front = Axle("front", [geometry[0], geometry[1]], [0, 1], force, springByCorner, bars);
        var rear = Axle("rear", [geometry[2], geometry[3]], [2, 3], force, springByCorner, bars);
        var kSum = front.TangentRollStiffnessNmPerRad + rear.TangentRollStiffnessNmPerRad;
        var ltSum = front.LoadTransferN + rear.LoadTransferN;
        return new(loadCase, state[0], state[1], state[2], cornerResults, cornerStiffnesses, [front, rear],
            kSum == 0 ? double.NaN : front.TangentRollStiffnessNmPerRad / kSum,
            Math.Abs(ltSum) < 1e-8 ? double.NaN : front.LoadTransferN / ltSum,
            package.Chassis.StaticFrontWeightFraction,
            100 * ((kSum == 0 ? double.NaN : front.TangentRollStiffnessNmPerRad / kSum) - package.Chassis.StaticFrontWeightFraction), warnings,
            resultants[0] - target[0], resultants[1] - target[1], resultants[2] - target[2], solve.Iterations, solve.Converged);
    }

    private static NewtonSolution SolveNewton(IReadOnlyList<CornerGeometry> g, IReadOnlyDictionary<string, CornerSpring> springs,
        IReadOnlyDictionary<string, AntiRollBarModel> bars, double[] target)
    {
        var q = new double[3];
        for (var iteration = 0; iteration < 32; iteration++)
        {
            var forces = Forces(g, springs, bars, q).TotalDeltaLoads;
            var r = Resultants(g, forces).Select((x, i) => x - target[i]).ToArray();
            if (NormInfinity(r) < 1e-6) return new(q, iteration, true);
            var j = Jacobian(g, springs, bars, q);
            var step = Solve3x3(j, r);
            for (var i = 0; i < 3; i++) q[i] -= step[i];
        }
        return new(q, 32, false);
    }

    private static ForceAssembly Forces(IReadOnlyList<CornerGeometry> g, IReadOnlyDictionary<string, CornerSpring> springs,
        IReadOnlyDictionary<string, AntiRollBarModel> bars, double[] q)
    {
        var delta = new double[4]; var springsOnly = new double[4]; var stops = new double[4]; var arb = new double[4]; var activeStops = new bool[4];
        for (var i = 0; i < 4; i++)
        {
            var s = springs[g[i].Corner]; var c = Compression(g[i], q);
            activeStops[i] = c > s.BumpStopGapM;
            stops[i] = activeStops[i] ? s.BumpStopRateNPerM * (c - s.BumpStopGapM) : 0;
            springsOnly[i] = s.WheelRateNPerM * c;
            delta[i] = springsOnly[i] + stops[i];
        }
        foreach (var axle in new[] { ("front", 0, 1), ("rear", 2, 3) })
        {
            var wheelForces = bars[axle.Item1].ResistingWheelForces(
                Compression(g[axle.Item2], q), Compression(g[axle.Item3], q));
            arb[axle.Item2] = wheelForces.LeftN;
            arb[axle.Item3] = wheelForces.RightN;
            delta[axle.Item2] += arb[axle.Item2];
            delta[axle.Item3] += arb[axle.Item3];
        }
        return new(delta, springsOnly, stops, arb, activeStops, 0);
    }

    private static double[,] Jacobian(IReadOnlyList<CornerGeometry> g, IReadOnlyDictionary<string, CornerSpring> springs,
        IReadOnlyDictionary<string, AntiRollBarModel> bars, double[] q)
    {
        var h = new[] { 1e-6, 1e-7, 1e-7 }; var j = new double[3, 3];
        for (var c = 0; c < 3; c++)
        {
            var plus = (double[])q.Clone(); var minus = (double[])q.Clone(); plus[c] += h[c]; minus[c] -= h[c];
            var rp = Resultants(g, Forces(g, springs, bars, plus).TotalDeltaLoads);
            var rm = Resultants(g, Forces(g, springs, bars, minus).TotalDeltaLoads);
            for (var r = 0; r < 3; r++) j[r, c] = (rp[r] - rm[r]) / (2 * h[c]);
        }
        return j;
    }

    private static AxleBalance Axle(string axle, IReadOnlyList<CornerGeometry> g, IReadOnlyList<int> indices,
        ForceAssembly force, IReadOnlyDictionary<string, CornerSpring> springs, IReadOnlyDictionary<string, AntiRollBarModel> bars)
    {
        var left = indices[0]; var right = indices[1];
        // A local tangent is recovered from force breakdown: spring rate plus stop rate if active.
        var tangent = 0.0;
        for (var i = 0; i < 2; i++)
        {
            var s = springs[g[i].Corner];
            var activeStop = force.ActiveBumpStops[indices[i]] ? s.BumpStopRateNPerM : 0;
            tangent += (s.WheelRateNPerM + activeStop) * g[i].Y * g[i].Y;
        }
        var arbTangent = bars[axle].TangentMatrix();
        tangent += arbTangent.LeftSelfNPerM * g[0].Y * g[0].Y
            + (arbTangent.LeftCrossNPerM + arbTangent.RightCrossNPerM) * g[0].Y * g[1].Y
            + arbTangent.RightSelfNPerM * g[1].Y * g[1].Y;
        var springLt = force.SpringOnlyDeltaLoads[left] - force.SpringOnlyDeltaLoads[right];
        var stopLt = force.BumpStopForces[left] - force.BumpStopForces[right];
        var arbLt = force.ArbDeltaLoads[left] - force.ArbDeltaLoads[right];
        return new(axle, tangent, -(force.TotalDeltaLoads[left] - force.TotalDeltaLoads[right]), -springLt, -arbLt, -stopLt);
    }

    private static ComponentCornerTangentWheelStiffness CornerTangent(CornerGeometry geometry, int index, ForceAssembly force,
        IReadOnlyDictionary<string, CornerSpring> springs, IReadOnlyDictionary<string, AntiRollBarModel> bars,
        CornerResult corner, bool converged)
    {
        var spring = springs[geometry.Corner];
        var springTangent = spring.WheelRateNPerM;
        var stopTangent = force.ActiveBumpStops[index] ? spring.BumpStopRateNPerM : 0;
        var axle = index < 2 ? "front" : "rear";
        var matrix = bars[axle].TangentMatrix();
        var arbTangent = index is 0 or 2 ? matrix.LeftSelfNPerM : matrix.RightSelfNPerM;
        var arbCross = index is 0 or 2 ? matrix.LeftCrossNPerM : matrix.RightCrossNPerM;
        var diagnostics = new List<ComponentTangentDiagnostic>
        {
            new(ComponentTangentValidity.Warning, "arb-coupled-local-tangent", "ARB self term is dF(corner)/d(jounce at that corner); its off-diagonal derivative to the opposite wheel is the negative ARB term. Do not sum corner totals for axle roll stiffness.")
        };
        if (force.ActiveBumpStops[index]) diagnostics.Add(new(ComponentTangentValidity.Valid, "bump-stop-active", "Bump-stop tangent is included exactly once in the total."));
        if (corner.IsLifted) diagnostics.Add(new(ComponentTangentValidity.Warning, "pre-lift-contact", "Pre-lift tangent only: normal load is non-positive and contact constraint is not solved."));
        if (!converged) diagnostics.Add(new(ComponentTangentValidity.Invalid, "equilibrium-not-converged", "Equilibrium did not converge."));
        var validity = diagnostics.Any(x => x.Validity == ComponentTangentValidity.Invalid) ? ComponentTangentValidity.Invalid : diagnostics.Any(x => x.Validity == ComponentTangentValidity.Warning) ? ComponentTangentValidity.Warning : ComponentTangentValidity.Valid;
        return new(geometry.Corner, springTangent, stopTangent, arbTangent, arbCross,
            springTangent + arbTangent + stopTangent, validity, diagnostics, corner.RelativeCompressionM,
            "positive wheel-centre jounce; opposite wheel on same axle held fixed");
    }

    private static double Compression(CornerGeometry g, double[] q) => q[0] - g.Y * q[1] + g.X * q[2];
    private static double[] Resultants(IReadOnlyList<CornerGeometry> g, double[] f) =>
    [f.Sum(), -g.Select((x, i) => x.Y * f[i]).Sum(), g.Select((x, i) => x.X * f[i]).Sum()];
    private static double NormInfinity(double[] v) => v.Max(Math.Abs);

    private static double[] Solve3x3(double[,] a, double[] b)
    {
        var m = new double[3, 4]; for (var r = 0; r < 3; r++) { for (var c = 0; c < 3; c++) m[r,c] = a[r,c]; m[r,3] = b[r]; }
        for (var p = 0; p < 3; p++)
        {
            var pivot = Enumerable.Range(p, 3 - p).OrderByDescending(r => Math.Abs(m[r,p])).First();
            if (Math.Abs(m[pivot,p]) < 1e-12) throw new InvalidOperationException("Suspension stiffness matrix is singular.");
            if (pivot != p) for (var c = p; c < 4; c++) (m[p,c], m[pivot,c]) = (m[pivot,c], m[p,c]);
            for (var r = p + 1; r < 3; r++) { var f = m[r,p] / m[p,p]; for (var c = p; c < 4; c++) m[r,c] -= f*m[p,c]; }
        }
        var x = new double[3]; for (var r = 2; r >= 0; r--) x[r] = (m[r,3] - Enumerable.Range(r + 1, 2-r).Sum(c => m[r,c]*x[c])) / m[r,r]; return x;
    }

    private static List<CornerGeometry> Geometry(ChassisModel c) =>
    [new("FL", c.WheelbaseM * (1 - c.StaticFrontWeightFraction), c.FrontTrackM / 2), new("FR", c.WheelbaseM * (1 - c.StaticFrontWeightFraction), -c.FrontTrackM / 2), new("RL", -c.WheelbaseM * c.StaticFrontWeightFraction, c.RearTrackM / 2), new("RR", -c.WheelbaseM * c.StaticFrontWeightFraction, -c.RearTrackM / 2)];
    private static Dictionary<string, double> StaticLoads(ChassisModel c) => new(StringComparer.OrdinalIgnoreCase) { ["FL"] = c.MassKg * GravityMPerS2*c.StaticFrontWeightFraction/2, ["FR"] = c.MassKg*GravityMPerS2*c.StaticFrontWeightFraction/2, ["RL"] = c.MassKg*GravityMPerS2*(1-c.StaticFrontWeightFraction)/2, ["RR"] = c.MassKg*GravityMPerS2*(1-c.StaticFrontWeightFraction)/2 };
    private static void Validate(VehiclePackage p)
    {
        if (p.Springs.Corners.Count != 4 || p.Springs.Corners.Select(x => x.Corner).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 4) throw new InvalidDataException("Exactly one spring per FL/FR/RL/RR corner is required.");
        if (p.AntiRollBars.Count != 2 || p.Chassis.MassKg <= 0 || p.Chassis.CgHeightM < 0 || p.Chassis.StaticFrontWeightFraction is <= 0 or >= 1) throw new InvalidDataException("Invalid chassis or ARB model.");
    }
    private sealed record CornerGeometry(string Corner, double X, double Y);
    private sealed record ForceAssembly(double[] TotalDeltaLoads, double[] SpringOnlyDeltaLoads,
        double[] BumpStopForces, double[] ArbDeltaLoads, bool[] ActiveBumpStops, int Iterations);
    private sealed record NewtonSolution(double[] State, int Iterations, bool Converged);
}
