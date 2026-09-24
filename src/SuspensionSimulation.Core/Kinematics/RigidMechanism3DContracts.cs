using System.Numerics;

namespace SuspensionSimulation.Kinematics;

/// <summary>Canonical SI frame: +X forward, +Y left, +Z up.</summary>
public sealed record RigidUprightPoints3D(
    Vector3 WheelCenter,
    Vector3 LowerBallJoint,
    Vector3 UpperBallJoint,
    Vector3 TieRodOuter,
    Vector3 StrutLower,
    Vector3 SpringLower,
    Vector3 ArbLinkLower);

public sealed record MacPhersonCorner3D(
    string Id,
    RigidUprightPoints3D Upright,
    Vector3 LowerArmFrontPivot,
    Vector3 LowerArmRearPivot,
    Vector3 StrutTopMount,
    Vector3 TieRodInner,
    Vector3 SpringUpper);

public sealed record DoubleWishboneCorner3D(
    string Id,
    RigidUprightPoints3D Upright,
    Vector3 LowerArmFrontPivot,
    Vector3 LowerArmRearPivot,
    Vector3 UpperArmFrontPivot,
    Vector3 UpperArmRearPivot,
    Vector3 ToeLinkInner,
    Vector3 SpringUpper,
    Vector3 SpringLowerOnLowerArm,
    Vector3 ArbLinkLowerOnLowerArm);

public sealed record RigidPoseState3D(Vector3 TranslationM, Vector3 RotationVectorRad);

public sealed record RigidCornerPose3D(
    string Id,
    double WheelJounceM,
    RigidPoseState3D UprightPose,
    IReadOnlyDictionary<string, Vector3> SolvedPoints,
    double SpringLengthM,
    double MaximumConstraintResidualM,
    int Iterations,
    bool Converged,
    string Topology,
    IReadOnlyList<string> Diagnostics);

public sealed record RigidCornerTravelDerivative3D(
    RigidCornerPose3D Pose,
    double SpringCompressionPerWheelJounce,
    double SpringCompressionSecondDerivativePerM,
    Vector3 ArbLowerDerivativePerWheelJounce,
    string Validity,
    IReadOnlyList<string> Diagnostics);
