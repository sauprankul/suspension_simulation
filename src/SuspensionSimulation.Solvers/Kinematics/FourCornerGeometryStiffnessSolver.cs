using SuspensionSimulation.Components;
using SuspensionSimulation.Core;
using SuspensionSimulation.Kinematics;

namespace SuspensionSimulation.Solvers.Kinematics;

public sealed record GeometryCornerComponents(string Corner, Spring Spring, BumpStop? BumpStop);

public sealed record GeometryAxleTangentRollStiffness(
    string Axle,
    double SpringNmPerRad,
    double ArbNmPerRad,
    double BumpStopNmPerRad,
    double TotalNmPerRad,
    ComponentTangentValidity Validity,
    IReadOnlyList<ComponentTangentDiagnostic> Diagnostics);

public sealed record GeometryBackedBalance(
    GeometryAxleTangentRollStiffness Front,
    GeometryAxleTangentRollStiffness Rear,
    double FrontRollStiffnessDistribution,
    double StaticFrontWeightFraction,
    double RollCoupleBiasPercentagePoints,
    ComponentTangentValidity Validity);

public sealed record GeometryBackedStiffnessResult(
    IReadOnlyList<ComponentCornerTangentWheelStiffness> Corners,
    IReadOnlyDictionary<string, MacPhersonPlanarPose> Poses,
    GeometryBackedBalance? Balance,
    IReadOnlyList<KinematicDiagnostic> Diagnostics,
    bool IsGeometryBacked);

/// <summary>
/// Joins the load-solver's current wheel state to the vehicle's declared hard points. It does not
/// alter force equilibrium yet; it is the authoritative geometry/travel stiffness readout path.
/// </summary>
public sealed class FourCornerGeometryStiffnessSolver
{
    public GeometryBackedStiffnessResult Solve(FourCornerMacPhersonGeometry geometry, QuasiStaticResult loadState,
        IReadOnlyList<GeometryCornerComponents> components, IReadOnlyList<AntiRollBarModel> antiRollBars)
    {
        ArgumentNullException.ThrowIfNull(geometry); ArgumentNullException.ThrowIfNull(loadState); ArgumentNullException.ThrowIfNull(components); ArgumentNullException.ThrowIfNull(antiRollBars);
        var diagnostics = new List<KinematicDiagnostic>();
        var componentByCorner = components.ToDictionary(x => x.Corner, StringComparer.OrdinalIgnoreCase);
        var equilibriumByCorner = loadState.Corners.ToDictionary(x => x.Corner, StringComparer.OrdinalIgnoreCase);
        var poses = new Dictionary<string, MacPhersonPlanarPose>(StringComparer.OrdinalIgnoreCase);
        var baseStiffness = new Dictionary<string, ComponentCornerTangentWheelStiffness>(StringComparer.OrdinalIgnoreCase);
        var planar = new MacPhersonPlanarSolver();
        foreach (var (corner, config) in geometry.Corners)
        {
            if (!componentByCorner.TryGetValue(corner, out var component) || !equilibriumByCorner.TryGetValue(corner, out var state))
            {
                diagnostics.Add(new(KinematicValidity.Invalid, "missing-corner-component", $"{corner} is missing geometry component or load state."));
                continue;
            }
            // The load solver's reference-relative compression is the currently available wheel-jounce proxy.
            var pose = planar.Solve(config, state.RelativeCompressionM);
            poses[corner] = pose;
            if (!pose.Converged || pose.StrutMotionRatio is null)
            {
                diagnostics.AddRange(pose.Diagnostics);
                baseStiffness[corner] = Unavailable(corner, pose, "Hard-point travel solve did not produce a finite local motion ratio.");
                continue;
            }
            baseStiffness[corner] = GeometryTravelStiffness.Evaluate(corner, component.Spring, component.BumpStop, pose);
        }

        var bars = antiRollBars.ToDictionary(x => x.Axle, StringComparer.OrdinalIgnoreCase);
        foreach (var attachment in geometry.ArbAttachments.Values)
        {
            if (!baseStiffness.TryGetValue(attachment.LeftCorner, out var left) || !baseStiffness.TryGetValue(attachment.RightCorner, out var right) ||
                !poses.TryGetValue(attachment.LeftCorner, out var leftPose) || !poses.TryGetValue(attachment.RightCorner, out var rightPose) ||
                leftPose.StrutMotionRatio is null || rightPose.StrutMotionRatio is null || !bars.TryGetValue(attachment.Axle, out var bar))
            {
                diagnostics.Add(new(KinematicValidity.Unavailable, "arb-travel-unavailable", $"{attachment.Axle} ARB tangent unavailable because a corner travel solve or ARB is missing."));
                continue;
            }
            var referenceLeft = planar.Solve(geometry.Corners[attachment.LeftCorner], geometry.Expected.ReferenceJounceM).StrutMotionRatio;
            var referenceRight = planar.Solve(geometry.Corners[attachment.RightCorner], geometry.Expected.ReferenceJounceM).StrutMotionRatio;
            if (referenceLeft is null || referenceRight is null)
            {
                diagnostics.Add(new(KinematicValidity.Unavailable, "arb-reference-travel-unavailable", $"{attachment.Axle} ARB reference travel could not be solved."));
                continue;
            }
            // Attachment values are the independently owned reference measurements/estimates. Travel variation is
            // scaled by the solved local strut derivative until explicit ARB-link hard points are introduced.
            var leftLinkMotion = attachment.LeftReferenceWheelToEndLinkMotionRatio * leftPose.StrutMotionRatio.MotionRatio / referenceLeft.MotionRatio;
            var rightLinkMotion = attachment.RightReferenceWheelToEndLinkMotionRatio * rightPose.StrutMotionRatio.MotionRatio / referenceRight.MotionRatio;
            var matrix = bar.ModelKind == AntiRollBarModelKind.TubularGeometry && bar.Tubular is not null
                ? bar.Tubular.WheelTangent(leftLinkMotion, rightLinkMotion)
                : null;
            var leftArb = matrix?.LeftSelfNPerM ?? bar.TangentMatrix().LeftSelfNPerM;
            var rightArb = matrix?.RightSelfNPerM ?? bar.TangentMatrix().RightSelfNPerM;
            var cross = matrix?.CrossNPerM ?? bar.TangentMatrix().LeftCrossNPerM;
            var notice = new ComponentTangentDiagnostic(ComponentTangentValidity.Warning, "arb-travel-scaled-from-strut",
                "ARB end-link travel is scaled from solved strut motion ratio around its declared reference attachment; explicit ARB-link hard points are a later refinement.");
            baseStiffness[attachment.LeftCorner] = WithArb(left, leftArb, cross, notice);
            baseStiffness[attachment.RightCorner] = WithArb(right, rightArb, cross, notice);
        }
        var all = geometry.Corners.Keys.Select(corner => baseStiffness.TryGetValue(corner, out var item)
            ? item : Unavailable(corner, poses.TryGetValue(corner, out var pose) ? pose : null, "No stiffness result was produced.")).ToArray();
        var geometryBacked = all.All(x => x.Validity != ComponentTangentValidity.Invalid) && all.All(x => !double.IsNaN(x.TotalSelfNPerM));
        var balance = geometryBacked ? Balance(geometry, all, poses, loadState.StaticFrontWeightFraction) : null;
        return new(all, poses, balance, diagnostics, geometryBacked && balance is not null);
    }

    /// <summary>
    /// Roll tangent is q'Kq at the solved pose, q = [-yL, -yR] for unit body roll.
    /// ARB cross terms are retained rather than treating its diagonal as two independent springs.
    /// </summary>
    private static GeometryBackedBalance Balance(FourCornerMacPhersonGeometry geometry,
        IReadOnlyList<ComponentCornerTangentWheelStiffness> all, IReadOnlyDictionary<string, MacPhersonPlanarPose> poses, double staticFrontWeightFraction)
    {
        var byCorner = all.ToDictionary(x => x.Corner, StringComparer.OrdinalIgnoreCase);
        GeometryAxleTangentRollStiffness Axle(string axle)
        {
            var attachment = geometry.ArbAttachments[axle]; var left = byCorner[attachment.LeftCorner]; var right = byCorner[attachment.RightCorner];
            var yLeft = poses[attachment.LeftCorner].WheelCentreM.Y; var yRight = poses[attachment.RightCorner].WheelCentreM.Y;
            var spring = left.SpringOnlyNPerM * yLeft * yLeft + right.SpringOnlyNPerM * yRight * yRight;
            var stop = left.BumpStopOnlyNPerM * yLeft * yLeft + right.BumpStopOnlyNPerM * yRight * yRight;
            var arb = left.ArbSelfNPerM * yLeft * yLeft + (left.ArbCrossToOppositeWheelNPerM + right.ArbCrossToOppositeWheelNPerM) * yLeft * yRight + right.ArbSelfNPerM * yRight * yRight;
            var diagnostics = left.Diagnostics.Concat(right.Diagnostics).ToArray();
            var validity = diagnostics.Any(x => x.Validity == ComponentTangentValidity.Invalid) ? ComponentTangentValidity.Invalid : ComponentTangentValidity.Warning;
            return new(axle, spring, arb, stop, spring + arb + stop, validity, diagnostics);
        }
        var front = Axle("front"); var rear = Axle("rear"); var sum = front.TotalNmPerRad + rear.TotalNmPerRad;
        var rsd = sum > 0 ? front.TotalNmPerRad / sum : double.NaN;
        var validity = !double.IsFinite(rsd) || front.Validity == ComponentTangentValidity.Invalid || rear.Validity == ComponentTangentValidity.Invalid ? ComponentTangentValidity.Invalid : ComponentTangentValidity.Warning;
        return new(front, rear, rsd, staticFrontWeightFraction, 100 * (rsd - staticFrontWeightFraction), validity);
    }

    private static ComponentCornerTangentWheelStiffness WithArb(ComponentCornerTangentWheelStiffness source, double self, double cross, ComponentTangentDiagnostic diagnostic)
    {
        var all = source.Diagnostics.Append(diagnostic).ToArray();
        var validity = all.Any(x => x.Validity == ComponentTangentValidity.Invalid) ? ComponentTangentValidity.Invalid : ComponentTangentValidity.Warning;
        return source with { ArbSelfNPerM = self, ArbCrossToOppositeWheelNPerM = cross, TotalSelfNPerM = source.SpringOnlyNPerM + source.BumpStopOnlyNPerM + self, Validity = validity, Diagnostics = all };
    }
    private static ComponentCornerTangentWheelStiffness Unavailable(string corner, MacPhersonPlanarPose? pose, string message) => new(
        corner, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, ComponentTangentValidity.Invalid,
        [new(ComponentTangentValidity.Invalid, "geometry-travel-unavailable", message)], pose?.StrutCompressionM ?? double.NaN,
        "geometry travel solve unavailable");
}
