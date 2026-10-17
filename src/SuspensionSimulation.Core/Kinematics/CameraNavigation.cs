using System.Numerics;

namespace SuspensionSimulation.Kinematics;

/// <summary>Camera-only navigation. Drag coordinates are screen pixels, Y downward.</summary>
public sealed record CameraFrame(Vector3 Position,Vector3 Target,Vector3 Up)
{
    public CameraFrame Orbit(float dx,float dy)
    {
        var forward=Vector3.Normalize(Target-Position);
        var right=Vector3.Normalize(Vector3.Cross(forward,Up));
        var up=Vector3.Normalize(Vector3.Cross(right,forward));
        var axis=-(up*dx+right*dy);
        if(axis.LengthSquared()<1e-12f) return this;
        var rotation=Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis),MathF.Sqrt(dx*dx+dy*dy)*.008f);
        return this with {Position=Target+Vector3.Transform(Position-Target,rotation),Up=Vector3.Normalize(Vector3.Transform(up,rotation))};
    }
    public CameraFrame Roll(float radians)
    {
        var forward=Vector3.Normalize(Target-Position);
        var up=Vector3.Normalize(Up-forward*Vector3.Dot(Up,forward));
        return this with {Up=Vector3.Normalize(Vector3.Transform(up,Quaternion.CreateFromAxisAngle(forward,radians)))};
    }
}
