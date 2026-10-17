using System.Collections.Immutable;
using SuspensionSimulation.Core;
using SuspensionSimulation.Kinematics;
using SuspensionSimulation.Configuration;

namespace SuspensionSimulation.Solvers.HeadToHead;

public sealed record InspectionBarPose(string Id,ArbSideState3D Left,ArbSideState3D Right,double TwistRad,double TorqueNm);
public sealed record InspectionSideSnapshot(HeadToHeadSetupInput Input,InspectionBodyPose Body,
    InspectionScene Scene,ImmutableDictionary<string,RigidCornerPose3D> Corners,
    ImmutableArray<InspectionBarPose> Bars,ImmutableDictionary<string,double> NormalLoadsN,
    double FrontRsd,double RcbPercentagePoints,HeadToHeadInspectionDiagnostics Diagnostics)
{
    public SteeringInspection? Steering {get;init;}
}
public sealed record AcceptedInspectionSnapshot(long Revision,string PackageFingerprint,LoadCase Load,
    ChassisConfig Chassis,InspectionSideSnapshot A,InspectionSideSnapshot B,double SolveMilliseconds)
{
    public GarageAnalysisSource? GarageA {get;init;}
    public GarageAnalysisSource? GarageB {get;init;}
}
