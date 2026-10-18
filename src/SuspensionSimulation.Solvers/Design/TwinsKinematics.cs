using System.Collections.Immutable;
using SuspensionSimulation.Design;

namespace SuspensionSimulation.Solvers.Design;

public sealed record FourCornerTravel(double FrontLeftM,double FrontRightM,double RearLeftM,double RearRightM)
{
    public double[] Values=>[FrontLeftM,FrontRightM,RearLeftM,RearRightM];
    public static FourCornerTravel Zero {get;}=new(0,0,0,0);
}
public sealed record WheelAlignment(string Corner,DesignVector CenterM,DesignVector Forward,DesignVector Outward,double CamberRad,double HeadingRad);
public sealed record AxleAlignment(double LeftCamberRad,double RightCamberRad,double HeadingDifferenceRad,double ThrustAngleRad);
public sealed record TwinsMotionState(FourCornerTravel Travel,double SteeringWheelDegrees,MechanismPose Pose,ImmutableArray<WheelAlignment> Wheels,AxleAlignment Front,AxleAlignment Rear)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] public ChassisFlexState? ChassisFlex {get;init;}
}

/// <summary>Topology-specific operating coordinates over the general rigid graph. Static alignment is an output.</summary>
public sealed class TwinsKinematics
{
    public ConstraintMechanism System {get;}
    public double RackTravelAt450DegreesM {get;}
    public static readonly string[] WheelPaths=["front/left/upright","front/right/upright","rear-left/upright","rear-right/upright"];
    private readonly DesignVector[] _neutralCenters;
    private readonly DesignVector _rackCenter;
    private readonly string[] _wheelBodyPaths,_wheelPointIds;
    public TwinsKinematics(ResolvedAssemblyGraph graph):this(graph,graph.Bodies.Single(x=>x.Component.Kind==ComponentKind.Rack).Component.Rack?.TravelAtLockM??throw new InvalidOperationException("Rack travel specification is missing.")) { }
    public TwinsKinematics(ResolvedAssemblyGraph graph,double rackTravelAt450DegreesM)
    {
        if(!double.IsFinite(rackTravelAt450DegreesM)||rackTravelAt450DegreesM<=0) throw new ArgumentException("Rack translation must be positive.");
        if(graph.Bodies.Single(x=>x.Component.Kind==ComponentKind.Rack).Component.Rack?.SteeringWheelLockDegrees!=450) throw new InvalidOperationException("This operating adapter requires the declared ±450 degree steering range.");
        System=new(graph);RackTravelAt450DegreesM=rackTravelAt450DegreesM;var zero=new double[System.CoordinateCount];
        var corners=new[]{"FL","FR","RL","RR"};_wheelBodyPaths=graph.Root.Vehicle is {} vehicle?corners.Select(c=>vehicle.Contacts.Single(p=>p.Corner==c).InstancePath).ToArray():WheelPaths;
        _wheelPointIds=graph.Root.Vehicle is {} v?corners.Select(c=>v.Contacts.Single(p=>p.Corner==c).CenterPointId).ToArray():["wheel","wheel","wheel","wheel"];
        _neutralCenters=_wheelBodyPaths.Select((p,i)=>System.Point(p,_wheelPointIds[i],zero)).ToArray();_rackCenter=System.Point("front/rack","origin",zero);
        var report=MechanismMobility.Inspect(graph,5);if((!report.IsClosed&&graph.AppliedAdjustments.IsEmpty&&graph.Root.Vehicle?.Chassis is null)||report.DegreesOfFreedom!=5||report.RedundantRows>0) throw new InvalidOperationException("Unsupported explicit vehicle mechanism: "+string.Join(" ",report.Diagnostics));
    }
    public TwinsMotionState Solve(FourCornerTravel travel,double steeringWheelDegrees,TwinsMotionState? seed=null,double tolerance=1e-10,ChassisFlexState? chassisFlex=null)
    {
        if(!double.IsFinite(steeringWheelDegrees)||Math.Abs(steeringWheelDegrees)>450||travel.Values.Any(x=>!double.IsFinite(x))) throw new ArgumentException("Steering range is ±450 degrees; travel must be finite.");
        var start=seed?.Travel.Values??[0d,0d,0d,0d];var initialSteer=seed?.SteeringWheelDegrees??0;var end=travel.Values;var flex=chassisFlex??ChassisFlexState.Zero;var startFlex=seed?.ChassisFlex??ChassisFlexState.Zero;
        if(System.Chassis is null&&flex!=ChassisFlexState.Zero) throw new ArgumentException("This historical rigid vehicle has no chassis flex coordinates.");
        var steps=Math.Max(1,Math.Max((int)Math.Ceiling(end.Zip(start,(a,b)=>Math.Abs(a-b)).Max()/.005),(int)Math.Ceiling(Math.Abs(steeringWheelDegrees-initialSteer)/25)));
        steps=Math.Max(steps,(int)Math.Ceiling(flex.Values.Zip(startFlex.Values,(a,b)=>Math.Abs(a-b)).Max()/.01));
        if(steps>200) throw new ArgumentException("Operating request is beyond this topology's supported continuation budget.");
        MechanismPose? pose=seed?.Pose;
        for(var step=1;step<=steps;step++)
        {
            var fraction=(double)step/steps;
            System.Chassis?.Set(new(startFlex.TwistRad+fraction*(flex.TwistRad-startFlex.TwistRad),startFlex.VerticalBendRad+fraction*(flex.VerticalBendRad-startFlex.VerticalBendRad),startFlex.LateralBendRad+fraction*(flex.LateralBendRad-startFlex.LateralBendRad)));
            var drivers=_wheelBodyPaths.Select((p,i)=>
            {
                var section=i<2?ChassisSection.Front:ChassisSection.Rear;var direction=System.Chassis?.SectionDirection(section,new(0,0,1))??new(0,0,1);var center=System.Chassis?.SectionPoint(section,_neutralCenters[i])??_neutralCenters[i];
                return new MechanismDriver(p,_wheelPointIds[i],direction,center.Dot(direction)+start[i]+fraction*(end[i]-start[i]));
            }).ToList();
            // Rearward steering arms require negative rack Y translation for positive left steering.
            var rackDirection=System.Chassis?.SectionDirection(ChassisSection.Rack,new(0,1,0))??new(0,1,0);var rackCenter=System.Chassis?.SectionPoint(ChassisSection.Rack,_rackCenter)??_rackCenter;
            drivers.Add(new("front/rack","origin",rackDirection,rackCenter.Dot(rackDirection)-(initialSteer+fraction*(steeringWheelDegrees-initialSteer))/450*RackTravelAt450DegreesM));
            pose=System.Solve(drivers,pose,tolerance);if(!pose.Converged) throw new InvalidOperationException($"Explicit mechanism continuation failed at fraction {fraction:0.###}; scaled residual {pose.MaximumScaledResidual:0.###E+0}. Last accepted pose retained.");
        }
        var wheels=_wheelBodyPaths.Select((path,i)=>
        {
            var forward=System.Direction(path,new(1,0,0),pose!.Coordinates);var outward=System.Direction(path,new(0,1,0),pose.Coordinates);
            var camber=-Math.Atan2(outward.Z,Math.Sqrt(outward.X*outward.X+outward.Y*outward.Y));var heading=Math.Atan2(forward.Y,forward.X);
            return new WheelAlignment(new[]{"FL","FR","RL","RR"}[i],System.Point(path,_wheelPointIds[i],pose.Coordinates),forward,outward,camber,heading);
        }).ToImmutableArray();
        static AxleAlignment Axle(WheelAlignment a,WheelAlignment b)=>new(a.CamberRad,b.CamberRad,a.HeadingRad-b.HeadingRad,Math.Atan2(Math.Sin(a.HeadingRad)+Math.Sin(b.HeadingRad),Math.Cos(a.HeadingRad)+Math.Cos(b.HeadingRad)));
        return new(travel,steeringWheelDegrees,pose!,wheels,Axle(wheels[0],wheels[1]),Axle(wheels[2],wheels[3])) {ChassisFlex=System.Chassis is null?null:flex};
    }
}
