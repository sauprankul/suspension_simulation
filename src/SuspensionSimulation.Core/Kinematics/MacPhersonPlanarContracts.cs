using System.Numerics;
using SuspensionSimulation.Components;

namespace SuspensionSimulation.Kinematics;

/// <summary>
/// Status carried by every geometry result. Estimated means the constraint solution closed but
/// the quantity still relies on this deliberately planar, rigid-joint model.
/// </summary>
public enum KinematicValidity { Valid, Estimated, Unavailable, Invalid }

public sealed record KinematicDiagnostic(KinematicValidity Validity, string Code, string Message);

/// <summary>
/// First hard-point slice: a rigid upright in the vehicle Y-Z (front-view) plane. X coordinates
/// are preserved for provenance but are not constrained yet. This is intentionally not a 3D
/// steering/toe solver; when present, the tie rod closes the planar travel solve but cannot produce toe.
/// </summary>
public sealed record MacPhersonPlanarCornerConfig
{
    public required string Id { get; init; }
    public required Vector3 UpperStrutMountM { get; init; }
    public required Vector3 LowerArmInnerPivotM { get; init; }
    public required Vector3 LowerBallJointReferenceM { get; init; }
    public required Vector3 StrutLowerReferenceM { get; init; }
    public required Vector3 WheelCentreReferenceM { get; init; }
    public Vector3? TieRodInnerM { get; init; }
    public Vector3? TieRodOuterReferenceM { get; init; }
    /// <summary>Ground-plane Z used only by the front-view force-line construction.</summary>
    public double GroundPlaneZM { get; init; }
    public double SolverToleranceM { get; init; } = 1e-8;
}

public sealed record PlanarRollCentreDiagnostic(
    double? CentrePlaneHeightM,
    double? ForcePathVerticalPerLateralForce,
    KinematicValidity Validity,
    string Message);

public sealed record JackingDiagnostic(
    double? VerticalForcePerUnitLateralForce,
    KinematicValidity Validity,
    string Message);

public sealed record MacPhersonPlanarPose(
    string Id,
    double WheelJounceM,
    Vector3 WheelCentreM,
    Vector3 LowerBallJointM,
    Vector3 StrutLowerM,
    double UprightRotationRad,
    double StrutLengthM,
    double StrutCompressionM,
    TravelDependentMotionRatio? StrutMotionRatio,
    double? TieRodLengthResidualM,
    PlanarRollCentreDiagnostic RollCentre,
    JackingDiagnostic Jacking,
    IReadOnlyList<KinematicDiagnostic> Diagnostics,
    bool Converged);

/// <summary>
/// Bridges geometry travel to the existing component tangent contract. It is intentionally
/// separate from the four-strut load solver until a complete four-corner hard-point package exists.
/// </summary>
public static class GeometryTravelStiffness
{
    public static ComponentCornerTangentWheelStiffness Evaluate(
        string corner, Spring spring, BumpStop? bumpStop, MacPhersonPlanarPose pose)
    {
        ArgumentNullException.ThrowIfNull(pose);
        if (!pose.Converged || pose.StrutMotionRatio is null)
            throw new InvalidOperationException("A converged geometry pose with a valid local motion ratio is required.");
        return TangentWheelStiffnessCalculator.EvaluateCorner(corner, spring, bumpStop,
            pose.StrutCompressionM, pose.StrutMotionRatio);
    }
}
