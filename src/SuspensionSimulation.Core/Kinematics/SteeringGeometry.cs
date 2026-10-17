using System.Numerics;
using System.Text.Json;

namespace SuspensionSimulation.Kinematics;

public sealed record SteeringGeometry
{
    public double RackCenterXM {get;init;}=1.05025;
    public double RackCenterYM {get;init;}
    public double RackCenterZM {get;init;}=.290;
    public double InnerHalfSpacingM {get;init;}=.340;
    public double OuterOffsetXM {get;init;}=-.050;
    public double OuterOffsetYM {get;init;}=-.060;
    public double OuterOffsetZM {get;init;}=-.040;
    public double RackTravelAtLockM {get;init;}=.010;
    public double SteeringWheelLockDegrees {get;init;}=450;
    public double FrontStaticCamberDegrees {get;init;}
    public double RearStaticCamberDegrees {get;init;}
    public double FrontStaticTotalToeDegrees {get;init;}
    public double RearStaticTotalToeDegrees {get;init;}
    public string Provenance {get;init;}="estimated; original synthetic rack/pickups, estimated rack travel; wheel-plane alignment calibration";
    public static SteeringGeometry Load(string directory)
    {
        var path=Path.Combine(directory,"steering.json");
        var value=File.Exists(path)?JsonSerializer.Deserialize<SteeringGeometry>(File.ReadAllText(path),new JsonSerializerOptions {PropertyNameCaseInsensitive=true})!:new();
        value.Validate();return value;
    }
    public void Validate()
    {
        var values=new[]{RackCenterXM,RackCenterYM,RackCenterZM,InnerHalfSpacingM,OuterOffsetXM,OuterOffsetYM,OuterOffsetZM,RackTravelAtLockM,SteeringWheelLockDegrees,FrontStaticCamberDegrees,RearStaticCamberDegrees,FrontStaticTotalToeDegrees,RearStaticTotalToeDegrees};
        if(values.Any(x=>!double.IsFinite(x))||InnerHalfSpacingM<=0||RackTravelAtLockM<=0||SteeringWheelLockDegrees<=0||
            Math.Abs(FrontStaticCamberDegrees)>15||Math.Abs(RearStaticCamberDegrees)>15||Math.Abs(FrontStaticTotalToeDegrees)>10||Math.Abs(RearStaticTotalToeDegrees)>10)
            throw new ArgumentException("Rack dimensions must be finite with positive spacing/travel/lock. Static camber is limited to ±15°, total toe to ±10°.");
    }
    public double RackDisplacementM(double steeringWheelDegrees)
    {
        if(!double.IsFinite(steeringWheelDegrees)||Math.Abs(steeringWheelDegrees)>SteeringWheelLockDegrees+1e-8) throw new ArgumentOutOfRangeException(nameof(steeringWheelDegrees));
        return -RackTravelAtLockM*steeringWheelDegrees/SteeringWheelLockDegrees;
    }
    public MacPhersonCorner3D Apply(MacPhersonCorner3D source)
    {
        var side=source.Id=="FL"?1:-1;
        return source with
        {
            TieRodInner=new((float)RackCenterXM,(float)(RackCenterYM+side*InnerHalfSpacingM),(float)RackCenterZM),
            Upright=source.Upright with {TieRodOuter=new Vector3(
                (float)(Math.Round(source.Upright.WheelCenter.X,6)+OuterOffsetXM),
                (float)(Math.Round(source.Upright.WheelCenter.Y,6)+side*OuterOffsetYM),
                (float)(Math.Round(source.Upright.WheelCenter.Z,6)+OuterOffsetZM))}
        };
    }
    public Quaternion WheelReference(string corner)
    {
        var side=corner.EndsWith("L")?1:-1;var front=corner.StartsWith("F");
        var camber=(front?FrontStaticCamberDegrees:RearStaticCamberDegrees)*Math.PI/180;
        var toe=(front?FrontStaticTotalToeDegrees:RearStaticTotalToeDegrees)*Math.PI/180;
        return Quaternion.CreateFromAxisAngle(Vector3.UnitZ,(float)(-side*toe/2))*Quaternion.CreateFromAxisAngle(Vector3.UnitX,(float)(-side*camber));
    }
}

public sealed record WheelAlignment(string Corner,double HeadingDegrees,double CamberDegrees,double ToeInDegrees,double BumpToeDegrees);
public sealed record AxleAlignment(double MeanCamberDegrees,double TotalToeDegrees,double ThrustDegrees);
public sealed record SteeringInspection(double SteeringWheelDegrees,double RackDisplacementM,SteeringGeometry Configuration,
    IReadOnlyList<WheelAlignment> Wheels,AxleAlignment Front,AxleAlignment Rear);
public static class AlignmentMeasurement
{
    public static (double Heading,double Camber) Measure(string corner,Quaternion orientation)
    {
        var axle=Vector3.Normalize(Vector3.Transform(Vector3.UnitY,orientation));
        return (Math.Atan2(-axle.X,axle.Y)*180/Math.PI,
            -(corner.EndsWith("L")?1:-1)*Math.Atan2(axle.Z,Math.Sqrt(axle.X*axle.X+axle.Y*axle.Y))*180/Math.PI);
    }
    public static AxleAlignment Axle(WheelAlignment left,WheelAlignment right)
    {
        var a=left.HeadingDegrees*Math.PI/180;var b=right.HeadingDegrees*Math.PI/180;
        return new((left.CamberDegrees+right.CamberDegrees)/2,right.HeadingDegrees-left.HeadingDegrees,
            Math.Atan2(Math.Sin(a)+Math.Sin(b),Math.Cos(a)+Math.Cos(b))*180/Math.PI);
    }
}
