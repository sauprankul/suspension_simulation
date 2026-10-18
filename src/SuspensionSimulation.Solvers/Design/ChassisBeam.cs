using SuspensionSimulation.Design;

namespace SuspensionSimulation.Solvers.Design;

public sealed record ChassisFlexState(double TwistRad,double VerticalBendRad,double LateralBendRad)
{
    public static ChassisFlexState Zero {get;}=new(0,0,0);
    public double[] Values=>[TwistRad,VerticalBendRad,LateralBendRad];
}

/// <summary>Three constant material-curvature modes of an unshearable, inextensible beam. Exact exponential-map centerline integration retains arc length.</summary>
public sealed class ChassisBeam
{
    public ChassisSpecification Specification {get;}
    public ChassisInstallation Installation {get;}
    public ChassisFlexState State {get;private set;}=ChassisFlexState.Zero;
    private readonly Dictionary<string,ChassisSection> _mounts;
    public ChassisBeam(ResolvedAssemblyGraph graph)
    {
        Installation=graph.Root.Vehicle?.Chassis??throw new ArgumentException("No chassis installation.");
        Specification=graph.Bodies.Single(b=>b.Path==Installation.ComponentPath).Component.Chassis??throw new ArgumentException("No chassis source properties.");
        _mounts=Installation.GroundAttachments.ToDictionary(a=>a.BodyPath,a=>a.Section,StringComparer.Ordinal);
    }
    public void Set(ChassisFlexState state)
    {if(state.Values.Any(v=>!double.IsFinite(v)||Math.Abs(v)>.15)) throw new InvalidOperationException("Chassis curvature is outside the reduced elastic beam's ±0.15rad range.");State=state;}
    public double EnergyJ=>.5/Specification.LengthM*(Specification.TorsionalRigidityNm2*State.TwistRad*State.TwistRad+Specification.VerticalBendingRigidityNm2*State.VerticalBendRad*State.VerticalBendRad+Specification.LateralBendingRigidityNm2*State.LateralBendRad*State.LateralBendRad);
    private DesignVector Curvature=>new(State.TwistRad/Specification.LengthM,State.VerticalBendRad/Specification.LengthM,State.LateralBendRad/Specification.LengthM);
    private double MidX=>Specification.FrontAxleXM-Specification.LengthM/2;
    private DesignVector Center(double station)
    {
        var k=Curvature;var a=k.Length;var x=new DesignVector(1,0,0);DesignVector integral;
        if(a<1e-9) integral=x*station+k.Cross(x)*(station*station/2)+k.Cross(k.Cross(x))*(station*station*station/6);
        else {var n=k/a;var t=a*station;integral=x*(Math.Sin(t)/a)+n.Cross(x)*((1-Math.Cos(t))/a)+n*(n.Dot(x)*(station-Math.Sin(t)/a));}
        return new DesignVector(MidX,0,Specification.RailHeightM)+integral;
    }
    private double SectionStation(ChassisSection section)=>section==ChassisSection.Rack?Specification.LengthM/2-Specification.RackSetbackM:(section==ChassisSection.Front?1:-1)*Specification.LengthM/2;
    public DesignVector SectionDirection(ChassisSection section,DesignVector v)=>DesignTransform.Rotate(v,Curvature*SectionStation(section));
    public DesignVector SectionPoint(ChassisSection section,DesignVector p)
    {var station=SectionStation(section);var nominal=new DesignVector(MidX+station,0,Specification.RailHeightM);return Center(station)+SectionDirection(section,p-nominal);}
    public DesignVector MaterialPoint(DesignVector p)
    {
        var station=Math.Clamp(p.X-MidX,-Specification.LengthM/2,Specification.LengthM/2);var nominal=new DesignVector(MidX+station,0,Specification.RailHeightM);
        return Center(station)+DesignTransform.Rotate(p-nominal,Curvature*station);
    }
    public DesignVector ReferenceDatum(DesignVector p)
    {
        var fraction=(Installation.ReferenceFrontAxleXM-p.X)/Installation.ReferenceLengthM;
        return MaterialPoint(p with {X=Specification.FrontAxleXM-fraction*Specification.LengthM});
    }
    public DesignFrame GroundFrame(string path,DesignFrame f)
    {
        if(path==Installation.ComponentPath)
        {var station=Math.Clamp(f.OriginM.X-MidX,-Specification.LengthM/2,Specification.LengthM/2);return new(MaterialPoint(f.OriginM),DesignTransform.Rotate(f.Axis,Curvature*station),DesignTransform.Rotate(f.ReferenceDirection,Curvature*station));}
        return _mounts.TryGetValue(path,out var section)?new(SectionPoint(section,f.OriginM),SectionDirection(section,f.Axis),SectionDirection(section,f.ReferenceDirection)):throw new InvalidOperationException("A grounded vehicle body is not attached to the chassis: "+path);
    }
}
