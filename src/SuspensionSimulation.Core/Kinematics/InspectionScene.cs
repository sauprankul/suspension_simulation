using System.Collections.Immutable;
using System.Numerics;

namespace SuspensionSimulation.Kinematics;

public enum SceneSource { Solved, FixedChassis, VisualEstimate, UnconstrainedReference }
public sealed record InspectionPoint(string Id, string Label, string Corner, Vector3 PositionM, SceneSource Source);
public sealed record InspectionSegment(string Id, string Label, string Corner, string Layer,
    Vector3 StartM, Vector3 EndM, SceneSource Source, bool ActiveLimit = false);
public sealed record InspectionWheel(string Corner, Vector3 CenterM, Quaternion Orientation,
    double RadiusM, double WidthM, double GroundGapM, SceneSource Source);
public sealed record InspectionSpringStroke(string Corner,Vector3 UpperMountM,Vector3 LowerMountM,Vector3 MarkerM,
    double CurrentLengthM,double MinimumLengthM,double MaximumLengthM,double BumpRemainingM,double ReboundRemainingM);
public sealed record InspectionScene(string StateId, string PackageFingerprint, string Validity,
    ImmutableArray<InspectionPoint> Points, ImmutableArray<InspectionSegment> Segments,
    ImmutableArray<InspectionWheel> Wheels, ImmutableArray<string> Diagnostics)
{
    public ImmutableArray<InspectionSpringStroke> SpringStrokes {get;init;}=[];
}

/// <summary>SI, right handed: X forward, Y left, Z up. Body rotates about static CG, X then Y.</summary>
public sealed record InspectionBodyPose(double HeaveCompressionM, double RollRad, double PitchRad, double CgHeightM)
{
    public Quaternion Rotation => Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)PitchRad)
        * Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)RollRad));
    public Vector3 ToWorld(Vector3 bodyPointM)
    {
        var pivot=new Vector3(0,0,(float)CgHeightM);
        return pivot+Vector3.Transform(bodyPointM-pivot,Rotation)-new Vector3(0,0,(float)HeaveCompressionM);
    }
}
