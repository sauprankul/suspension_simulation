using System.Numerics;

namespace SuspensionSimulation.Kinematics;

public sealed record TubularArbMechanism3DConfig(
    string Id,
    Vector3 AxisStart,
    Vector3 AxisEnd,
    Vector3 LeftArmEndReference,
    Vector3 RightArmEndReference,
    Vector3 LeftLowerJointReference,
    Vector3 RightLowerJointReference,
    double OutsideDiameterM,
    double InsideDiameterM,
    double ShearModulusPa,
    double TorsionLengthM,
    string Provenance);

public sealed record ArbSideState3D(
    double ArmAngleRad,
    Vector3 ArmEnd,
    Vector3 LowerJoint,
    Vector3 EndLinkUnit,
    double EndLinkLengthM,
    double ConstraintResidualM,
    double ToggleDerivativeMPerRad,
    string LimitClassification);

public sealed record TubularArbMechanismState3D(
    string Id,
    ArbSideState3D Left,
    ArbSideState3D Right,
    double PolarSecondMomentM4,
    double TorsionalStiffnessNmPerRad,
    double BarTwistRad,
    double BarTorqueNm,
    double StoredEnergyJ,
    double LeftWheelReactionN,
    double RightWheelReactionN,
    double[,] WheelTangentNPerM,
    string Validity,
    IReadOnlyList<string> Diagnostics);
