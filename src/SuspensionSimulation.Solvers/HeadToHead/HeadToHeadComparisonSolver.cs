using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Numerics;
using SuspensionSimulation.Components;
using SuspensionSimulation.Configuration;
using SuspensionSimulation.Core;
using SuspensionSimulation.Kinematics;
using SuspensionSimulation.Solvers.Kinematics;

namespace SuspensionSimulation.Solvers.HeadToHead;

public sealed record HeadToHeadSetupInput(
    string Name,
    double FrontSpringNPerM,
    double RearSpringNPerM,
    double FrontArbOutsideDiameterM,
    double FrontArbInsideDiameterM,
    double RearArbOutsideDiameterM,
    double RearArbInsideDiameterM);

public sealed record HeadToHeadSideResult(
    HeadToHeadSetupInput Input,
    QuasiStaticResult State,
    double FrontSpringRollStiffnessNmPerRad,
    double RearSpringRollStiffnessNmPerRad,
    double FrontArbRollStiffnessNmPerRad,
    double RearArbRollStiffnessNmPerRad,
    string InputHash,
    string Validity,
    IReadOnlyList<string> Diagnostics);

public sealed record HeadToHeadDelta(
    double FrontRsdPercentagePoints,
    double RcbPercentagePoints,
    double RollDegrees,
    double PitchDegrees,
    IReadOnlyDictionary<string, double> CornerLoadN);

public sealed record HeadToHeadComparisonResult(
    LoadCase SharedLoadCase,
    HeadToHeadSideResult A,
    HeadToHeadSideResult B,
    HeadToHeadDelta BMinusA,
    string Validity);

/// <summary>
/// Stable A/B orchestration boundary for the dashboard. Exact rigid corner mechanisms are traced
/// once at construction into continuous travel maps. Interactive solves query those maps, so mouse
/// motion cannot launch an independent nonlinear solve or jump to a different kinematic branch.
/// </summary>
public sealed class HeadToHeadComparisonSolver
{
    private readonly TubularArbMechanism3DConfig? _frontBar;
    private readonly TubularArbMechanism3DConfig? _rearBar;
    private readonly IReadOnlyDictionary<string,RigidCornerTravelMap3D>? _travelMaps;

    public bool HasPrecomputedGeometry => _travelMaps is not null;
    public IReadOnlyDictionary<string,RigidCornerTravelMap3D> TravelMaps => _travelMaps ?? throw new InvalidOperationException("No 3D package was supplied.");

    public HeadToHeadComparisonSolver() { }

    public HeadToHeadComparisonSolver(string packageDirectory)
    {
        var geometry = Gr86HardPointPackageLoader.Load(packageDirectory);
        _frontBar = TubularArbMechanism3DLoader.Load(Path.Combine(packageDirectory, "front-tubular-arb.json"));
        _rearBar = TubularArbMechanism3DLoader.Load(Path.Combine(packageDirectory, "rear-tubular-arb.json"));
        var builder=new RigidCornerTravelMapBuilder3D();
        var maps=geometry.Front.ToDictionary(x=>x.Key,x=>builder.Build(x.Value),StringComparer.OrdinalIgnoreCase);
        foreach(var pair in geometry.Rear) maps[pair.Key]=builder.Build(pair.Value);
        _travelMaps=maps;
    }

    public HeadToHeadComparisonResult Solve(ModularSetup vehicle, HeadToHeadSetupInput a, HeadToHeadSetupInput b, LoadCase sharedLoad)
    {
        ArgumentNullException.ThrowIfNull(vehicle); ArgumentNullException.ThrowIfNull(a); ArgumentNullException.ThrowIfNull(b); ArgumentNullException.ThrowIfNull(sharedLoad);
        var sideA = SolveSide(vehicle, a, sharedLoad);
        var sideB = SolveSide(vehicle, b, sharedLoad);
        var loadA = sideA.State.Corners.ToDictionary(x => x.Corner, x => x.NormalLoadN, StringComparer.OrdinalIgnoreCase);
        var loadB = sideB.State.Corners.ToDictionary(x => x.Corner, x => x.NormalLoadN, StringComparer.OrdinalIgnoreCase);
        var cornerDelta = loadA.Keys.ToDictionary(x => x, x => loadB[x] - loadA[x], StringComparer.OrdinalIgnoreCase);
        var delta = new HeadToHeadDelta(
            100 * (sideB.State.RollStiffnessDistributionFront - sideA.State.RollStiffnessDistributionFront),
            sideB.State.RollCoupleBiasPercentagePoints - sideA.State.RollCoupleBiasPercentagePoints,
            Degrees(sideB.State.RollRad - sideA.State.RollRad),
            Degrees(sideB.State.PitchRad - sideA.State.PitchRad), cornerDelta);
        var validity = sideA.Validity == "valid" && sideB.Validity == "valid" ? "valid" : "provisional";
        return new(sharedLoad, sideA, sideB, delta, validity);
    }

    private HeadToHeadSideResult SolveSide(ModularSetup vehicle, HeadToHeadSetupInput input, LoadCase load)
    {
        Validate(input);
        var package = BuildScalarPackage(vehicle, input);
        var state = new FourStrutQuasiStaticSolver().Solve(package, load);
        var geometryLive = _travelMaps is not null && _frontBar is not null && _rearBar is not null;
        if (geometryLive)
        {
            // One explicit local-tangent update. Repeated fixed-point iteration is not a substitute
            // for the pending nonlinear energy equilibrium and can jump kinematic branches.
            var evaluated = EvaluateGeometry(vehicle, input, state);
            package = BuildGeometryPackage(vehicle, evaluated);
            state = new FourStrutQuasiStaticSolver().Solve(package, load);
        }
        var front = state.TangentRollStiffnesses!.Single(x => x.Axle == "front");
        var rear = state.TangentRollStiffnesses!.Single(x => x.Axle == "rear");
        var diagnostics = new List<string>
        {
            "Supplied reference-sheet mass, tracks, stock springs, and flexible-arm ARB dimensions; shared hard points remain synthetic but are calibrated to its static motion ratios.",
            geometryLive
                ? "Startup-precomputed 3D front-strut and rear-multilink branches supply local spring derivatives; fixed-length 3D endlinks plus declared arm-bending/torsion compliance supply each ARB energy Hessian."
                : "PROVISIONAL FALLBACK: declared scalar spring/ARB attachment motion ratios are in use because no 3D package was supplied.",
            "PROVISIONAL: the whole-car equilibrium uses one explicit 3D local-tangent update; full nonlinear component force integration and force-path roll-center/jacking gates remain pending.",
            "Bump stops, tire physics, damping, compliance and aero are excluded."
        };
        if (!state.Converged) diagnostics.Add("Quasi-static equilibrium did not converge.");
        return new(input, state, front.SpringNmPerRad, rear.SpringNmPerRad, front.ArbNmPerRad, rear.ArbNmPerRad,
            Hash(input, load, vehicle.InputFingerprintSha256), geometryLive ? "provisional-3d-local-tangent-equilibrium" : "provisional-scalar-attachment-geometry", diagnostics);
    }

    private static VehiclePackage BuildScalarPackage(ModularSetup source, HeadToHeadSetupInput input)
    {
        var springRates = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["front"] = input.FrontSpringNPerM, ["rear"] = input.RearSpringNPerM };
        var corners = source.Assembly.Corners.Select(c =>
        {
            var axle = c.Corner is Corner.FL or Corner.FR ? "front" : "rear";
            return new CornerSpring(c.Corner.ToString(), springRates[axle] * c.MotionRatio * c.MotionRatio, 1.0, 0);
        }).ToArray();
        var frontConfig = EditBar(source.AntiRollBars.Single(x => x.Id == source.Assembly.FrontAntiRollBarId), input.FrontArbOutsideDiameterM, input.FrontArbInsideDiameterM);
        var rearConfig = EditBar(source.AntiRollBars.Single(x => x.Id == source.Assembly.RearAntiRollBarId), input.RearArbOutsideDiameterM, input.RearArbInsideDiameterM);
        var c = source.Chassis;
        return new VehiclePackage(
            new(c.Id, c.TotalMassKg, c.WheelbaseM, c.FrontTrackM, c.RearTrackM, c.CgHeightM, c.StaticFrontWeightFraction, "mixed public/synthetic", c.SourceNote),
            new(corners, "synthetic", "Component spring rates mapped through declared attachment motion ratios; 3D continuation pending."),
            [AntiRollBarModel.FromConfig("front", frontConfig, "synthetic geometry"), AntiRollBarModel.FromConfig("rear", rearConfig, "synthetic geometry")],
            new("excluded", "Quasi-static head-to-head."),
            new("head-to-head/0.1-provisional", ["rigid chassis", "flat road", "quasi-static", "linear springs", "tubular steel ARBs"],
                ["3D hard-point continuation in this executable slice", "bump stops", "tire physics", "damping/transients", "compliance", "aero"]));
    }

    private GeometryTangentEvaluation EvaluateGeometry(ModularSetup vehicle, HeadToHeadSetupInput input, QuasiStaticResult state)
    {
        var byCorner = state.Corners.ToDictionary(x => x.Corner, StringComparer.OrdinalIgnoreCase);
        var travel = _travelMaps!.ToDictionary(x=>x.Key,x=>x.Value.Evaluate(byCorner[x.Key].RelativeCompressionM),StringComparer.OrdinalIgnoreCase);
        if (travel.Values.Any(x => x.Validity != "precomputed-rigid-branch" || !double.IsFinite(x.SpringCompressionPerWheelJounce)))
        {
            var failed = string.Join(", ", travel.Where(x => x.Value.Validity != "precomputed-rigid-branch" || !double.IsFinite(x.Value.SpringCompressionPerWheelJounce))
                .Select(x => $"{x.Key} jounce={byCorner[x.Key].RelativeCompressionM:G4}m validity={x.Value.Validity} ({string.Join(" ",x.Value.Diagnostics)})"));
            throw new InvalidOperationException($"The requested state is outside a validated 3D travel branch ({failed}); no scalar geometry fallback is permitted.");
        }

        var rates = new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase);
        foreach (var corner in travel.Keys)
        {
            var front = corner is "FL" or "FR"; var rawRate = front ? input.FrontSpringNPerM : input.RearSpringNPerM;
            var derivative = travel[corner];
            // The fixture has no measured installed spring preload. Use the exact first-order
            // virtual-work term k*(ds/dx)^2; adding Fspring*d2s/dx2 without a calibrated static
            // component force would be less faithful than explicitly omitting that term.
            var tangent = rawRate*derivative.SpringCompressionPerWheelJounce*derivative.SpringCompressionPerWheelJounce;
            if (!double.IsFinite(tangent) || tangent <= 100) throw new InvalidOperationException($"{corner} geometry-derived spring tangent is non-physical ({tangent:G6} N/m).");
            rates[corner] = tangent;
        }

        TubularArbMechanismState3D EvaluateBar(TubularArbMechanism3DConfig source, bool front)
        {
            var left = front ? "FL" : "RL"; var right = front ? "FR" : "RR";
            var configured = source with
            {
                OutsideDiameterM = front ? input.FrontArbOutsideDiameterM : input.RearArbOutsideDiameterM,
                InsideDiameterM = front ? input.FrontArbInsideDiameterM : input.RearArbInsideDiameterM
            };
            var lj = byCorner[left].RelativeCompressionM; var rj = byCorner[right].RelativeCompressionM;
            var lp = travel[left].ArbLowerPointM; var rp = travel[right].ArbLowerPointM;
            var ld = travel[left].ArbLowerDerivativePerWheelJounce; var rd = travel[right].ArbLowerDerivativePerWheelJounce;
            Vector3 Left(double j) => lp+ld*(float)(j-lj); Vector3 Right(double j) => rp+rd*(float)(j-rj);
            return new TubularArbMechanism3DSolver().Evaluate(configured, Left, Right, lj, rj);
        }
        var frontBar = EvaluateBar(_frontBar!,true); var rearBar = EvaluateBar(_rearBar!,false);
        if (frontBar.Validity == "invalid" || rearBar.Validity == "invalid") throw new InvalidOperationException("A 3D ARB/endlink mechanism failed at the requested load state.");
        return new(rates, Matrix(frontBar), Matrix(rearBar), frontBar, rearBar);
    }

    private static VehiclePackage BuildGeometryPackage(ModularSetup source, GeometryTangentEvaluation evaluated)
    {
        var corners = source.Assembly.Corners.Select(c => new CornerSpring(c.Corner.ToString(), evaluated.CornerSpringTangentNPerM[c.Corner.ToString()],1,0)).ToArray();
        var c=source.Chassis;
        return new(new(c.Id,c.TotalMassKg,c.WheelbaseM,c.FrontTrackM,c.RearTrackM,c.CgHeightM,c.StaticFrontWeightFraction,"mixed public/synthetic",c.SourceNote),
            new(corners,"geometry-derived-3d","Local virtual-work tangent k*(ds/dx)^2. Preload-curvature term is withheld until installed spring force is calibrated."),
            [AntiRollBarModel.FromGeometryDerivedTangent("front",evaluated.FrontArb,"3D fixed-length endlink energy Hessian"), AntiRollBarModel.FromGeometryDerivedTangent("rear",evaluated.RearArb,"3D fixed-length endlink energy Hessian")],
            new("excluded","Quasi-static head-to-head."),
            new("head-to-head/0.2-3d-local-tangent",["rigid 3D front strut","rigid 3D rear multilink","geometry-derived spring tangent","3D tubular ARB/endlinks","iterated quasi-static local equilibrium"],
                ["spring preload-curvature term pending calibrated installed force","nonlinear force integration","force-path roll-center/jacking","bump stops","tire physics","damping/transients","compliance","aero"]));
    }

    private static ArbTangentMatrix Matrix(TubularArbMechanismState3D state) => new(state.WheelTangentNPerM[0,0],state.WheelTangentNPerM[0,1],state.WheelTangentNPerM[1,0],state.WheelTangentNPerM[1,1]);

    private static AntiRollBarConfig EditBar(AntiRollBarConfig source, double od, double id)
    {
        if (source.ModelKind != AntiRollBarModelKind.TubularGeometry || source.Tubular is null)
            throw new InvalidDataException($"Head-to-head ARB '{source.Id}' must be tubular geometry, not a direct wheel rate.");
        return source with { Tubular = source.Tubular with { OutsideDiameterM = od, InsideDiameterM = id } };
    }

    private static void Validate(HeadToHeadSetupInput input)
    {
        if (input.FrontSpringNPerM <= 0 || input.RearSpringNPerM <= 0) throw new ArgumentOutOfRangeException(nameof(input), "Spring rates must be positive.");
        ValidateTube(input.FrontArbOutsideDiameterM, input.FrontArbInsideDiameterM, "front");
        ValidateTube(input.RearArbOutsideDiameterM, input.RearArbInsideDiameterM, "rear");
    }

    private static void ValidateTube(double od, double id, string axle)
    {
        if (!double.IsFinite(od) || !double.IsFinite(id) || od <= 0 || id < 0 || id >= od)
            throw new ArgumentOutOfRangeException(axle, $"{axle} ARB requires OD > ID >= 0.");
    }

    private static string Hash(HeadToHeadSetupInput input, LoadCase load, string sourceHash)
    {
        var json = JsonSerializer.Serialize(new { input, load, sourceHash });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    private static double Degrees(double value) => value * 180 / Math.PI;
    private sealed record GeometryTangentEvaluation(IReadOnlyDictionary<string,double> CornerSpringTangentNPerM, ArbTangentMatrix FrontArb, ArbTangentMatrix RearArb,
        TubularArbMechanismState3D FrontArbState, TubularArbMechanismState3D RearArbState);
}
