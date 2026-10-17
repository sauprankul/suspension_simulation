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
public sealed record TwinsMotionState(FourCornerTravel Travel,double SteeringWheelDegrees,MechanismPose Pose,ImmutableArray<WheelAlignment> Wheels,AxleAlignment Front,AxleAlignment Rear);

/// <summary>Topology-specific operating coordinates over the general rigid graph. Static alignment is an output.</summary>
public sealed class TwinsKinematics
{
    public ConstraintMechanism System {get;}
    public double RackTravelAt450DegreesM {get;}
    public static readonly string[] WheelPaths=["front/left/upright","front/right/upright","rear-left/upright","rear-right/upright"];
    private readonly double[] _neutralHeights;
    private readonly double _rackNeutral;
    public TwinsKinematics(ResolvedAssemblyGraph graph):this(graph,graph.Components.Single(x=>x.Kind==ComponentKind.Rack).Rack?.TravelAtLockM??throw new InvalidOperationException("Rack travel specification is missing.")) { }
    public TwinsKinematics(ResolvedAssemblyGraph graph,double rackTravelAt450DegreesM)
    {
        if(!double.IsFinite(rackTravelAt450DegreesM)||rackTravelAt450DegreesM<=0) throw new ArgumentException("Rack translation must be positive.");
        if(graph.Components.Single(x=>x.Kind==ComponentKind.Rack).Rack?.SteeringWheelLockDegrees!=450) throw new InvalidOperationException("This operating adapter requires the declared ±450 degree steering range.");
        System=new(graph);RackTravelAt450DegreesM=rackTravelAt450DegreesM;var zero=new double[System.CoordinateCount];
        _neutralHeights=WheelPaths.Select(p=>System.Point(p,"wheel",zero).Z).ToArray();_rackNeutral=System.Point("front/rack","origin",zero).Y;
        var report=MechanismMobility.Inspect(graph,5);if(!report.IsClosed||report.DegreesOfFreedom!=5||report.RedundantRows>0) throw new InvalidOperationException("Unsupported explicit vehicle mechanism: "+string.Join(" ",report.Diagnostics));
    }
    public TwinsMotionState Solve(FourCornerTravel travel,double steeringWheelDegrees,TwinsMotionState? seed=null)
    {
        if(!double.IsFinite(steeringWheelDegrees)||Math.Abs(steeringWheelDegrees)>450||travel.Values.Any(x=>!double.IsFinite(x))) throw new ArgumentException("Steering range is ±450 degrees; travel must be finite.");
        var start=seed?.Travel.Values??[0d,0d,0d,0d];var initialSteer=seed?.SteeringWheelDegrees??0;var end=travel.Values;
        var steps=Math.Max(1,Math.Max((int)Math.Ceiling(end.Zip(start,(a,b)=>Math.Abs(a-b)).Max()/.005),(int)Math.Ceiling(Math.Abs(steeringWheelDegrees-initialSteer)/25)));
        if(steps>200) throw new ArgumentException("Operating request is beyond this topology's supported continuation budget.");
        MechanismPose? pose=seed?.Pose;
        for(var step=1;step<=steps;step++)
        {
            var fraction=(double)step/steps;
            var drivers=WheelPaths.Select((p,i)=>new MechanismDriver(p,"wheel",new(0,0,1),_neutralHeights[i]+start[i]+fraction*(end[i]-start[i]))).ToList();
            // Rearward steering arms require negative rack Y translation for positive left steering.
            drivers.Add(new("front/rack","origin",new(0,1,0),_rackNeutral-(initialSteer+fraction*(steeringWheelDegrees-initialSteer))/450*RackTravelAt450DegreesM));
            pose=System.Solve(drivers,pose);if(!pose.Converged) throw new InvalidOperationException($"Explicit mechanism continuation failed at fraction {fraction:0.###}; scaled residual {pose.MaximumScaledResidual:0.###E+0}. Last accepted pose retained.");
        }
        var wheels=WheelPaths.Select((path,i)=>
        {
            var forward=System.Direction(path,new(1,0,0),pose!.Coordinates);var outward=System.Direction(path,new(0,1,0),pose.Coordinates);
            var camber=-Math.Atan2(outward.Z,Math.Sqrt(outward.X*outward.X+outward.Y*outward.Y));var heading=Math.Atan2(forward.Y,forward.X);
            return new WheelAlignment(new[]{"FL","FR","RL","RR"}[i],System.Point(path,"wheel",pose.Coordinates),forward,outward,camber,heading);
        }).ToImmutableArray();
        static AxleAlignment Axle(WheelAlignment a,WheelAlignment b)=>new(a.CamberRad,b.CamberRad,a.HeadingRad-b.HeadingRad,Math.Atan2(Math.Sin(a.HeadingRad)+Math.Sin(b.HeadingRad),Math.Cos(a.HeadingRad)+Math.Cos(b.HeadingRad)));
        return new(travel,steeringWheelDegrees,pose!,wheels,Axle(wheels[0],wheels[1]),Axle(wheels[2],wheels[3]));
    }
}
