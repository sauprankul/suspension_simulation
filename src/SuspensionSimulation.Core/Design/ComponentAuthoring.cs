using System.Collections.Immutable;

namespace SuspensionSimulation.Design;

public sealed record RigidTemplateSpec(double LengthM,double WidthM,double HeightM);
public sealed record VisualEdge(string Id,string StartPointId,string EndPointId);
public sealed record PointMeasurement(double DistanceM,DesignVector DeltaM);

public static class ComponentAuthoring
{
    public static readonly ComponentKind[] RigidTemplates=[ComponentKind.RigidLink,ComponentKind.Wishbone,ComponentKind.Upright,ComponentKind.TopMount];
    public static ComponentDefinition Create(RevisionIdentity identity,ComponentKind kind,RigidTemplateSpec spec)
    {
        if(!RigidTemplates.Contains(kind)) throw new ArgumentException("Unsupported rigid template.");
        if(!double.IsFinite(spec.LengthM) || spec.LengthM<=0 || (kind!=ComponentKind.RigidLink &&
            (!double.IsFinite(spec.WidthM)||spec.WidthM<=0||!double.IsFinite(spec.HeightM)||spec.HeightM<=0))) throw new ArgumentException("Template dimensions must be finite and positive.");
        var points=new List<NamedDesignPoint>();var frames=new List<NamedDesignFrame>();var ports=new List<AttachmentPort>();var edges=new List<VisualEdge>();
        void Point(string id,DesignVector p) => points.Add(new(id,id,p));
        void Port(string id,string point,JointKind joint,DesignVector? axis=null)
        {var p=points.Single(x=>x.Id==point).PositionM;frames.Add(new(id,id,new(p,axis??new(0,0,1),new(1,0,0))));ports.Add(new(id,id,id,[joint]));}
        void Edge(string a,string b)=>edges.Add(new(a+"-"+b,a,b));
        var l=spec.LengthM;var w=spec.WidthM;var h=spec.HeightM;
        switch(kind)
        {
            case ComponentKind.RigidLink:
                Point("inner",new(0,0,0));Point("outer",new(l,0,0));Port("inner","inner",JointKind.Spherical);Port("outer","outer",JointKind.Spherical);Edge("inner","outer");break;
            case ComponentKind.Wishbone:
                Point("pivot-front",new(0,-w/2,0));Point("pivot-rear",new(0,w/2,0));Point("pivot-center",new(0,0,0));Point("ball",new(l,0,h));Point("spring-seat",new(.6*l,0,.6*h));
                Port("inboard","pivot-center",JointKind.Revolute,new(0,1,0));Port("outboard","ball",JointKind.Spherical);Port("spring-seat","spring-seat",JointKind.Spherical);
                Edge("pivot-front","ball");Edge("pivot-rear","ball");Edge("pivot-front","pivot-rear");break;
            case ComponentKind.Upright:
                Point("hub",new(0,0,0));Point("lower",new(0,0,-h/2));Point("upper",new(0,0,h/2));Point("steering",new(-l,w,0));
                Port("hub","hub",JointKind.Revolute,new(0,1,0));Port("lower","lower",JointKind.Spherical);Port("upper","upper",JointKind.Spherical);Port("steering","steering",JointKind.Spherical);
                Edge("lower","upper");Edge("hub","steering");break;
            case ComponentKind.TopMount:
                Point("bolt-left",new(0,-w/2,0));Point("bolt-right",new(0,w/2,0));Point("chassis",new(0,0,0));Point("bearing",new(0,0,h));Point("slot-start",new(-l/2,0,h));Point("slot-end",new(l/2,0,h));
                Port("chassis","chassis",JointKind.Fixed);Port("bearing","bearing",JointKind.Spherical);Edge("bolt-left","bolt-right");Edge("slot-start","slot-end");Edge("chassis","bearing");break;
        }
        ImmutableArray<DesignDimension> dimensions=kind==ComponentKind.RigidLink?[new DrivingLength("length",l),new MeasuredDistance("endpoint-distance","inner","outer")]:
            [new DrivingLength("length",l),new DrivingLength("width",w),new DrivingLength("height",h)];
        return new(identity,kind,[..points],[..frames],[..ports],dimensions,null) {Template=spec,VisualEdges=[..edges]};
    }
    public static ComponentDefinition MakeCustom(ComponentDefinition component)=>component with
    {Template=null,Dimensions=[..component.VisualEdges.Select(x=>(DesignDimension)new MeasuredDistance(x.Id,x.StartPointId,x.EndPointId))]};
    public static void ValidateForPublish(ComponentDefinition component)
    {
        DesignValidation.Validate(component);
        if(component.Points.Length<2) throw new ArgumentException("A rigid component needs at least two named points.");
        if(component.Template is {} spec)
        {
            var expected=Create(component.Identity,component.Kind,spec);
            if(!component.Points.SequenceEqual(expected.Points)||!component.Frames.SequenceEqual(expected.Frames)||!component.Dimensions.SequenceEqual(expected.Dimensions))
                throw new ArgumentException("Template geometry must match its driving dimensions. Make it custom before editing coordinates.");
        }
        else if(component.Dimensions.Any(x=>x is DrivingLength)) throw new ArgumentException("Custom geometry uses measured distances; unsupported driving dimensions cannot be published.");
        foreach(var edge in component.VisualEdges)
            if(!component.Points.Any(x=>x.Id==edge.StartPointId)||!component.Points.Any(x=>x.Id==edge.EndPointId)) throw new ArgumentException("Visual edge has an unresolved point.");
    }
    public static PointMeasurement Measure(DesignVector first,DesignVector second,DesignFrame? frame=null)
    {
        if(!first.IsFinite||!second.IsFinite) throw new ArgumentException("Measurement coordinates must be finite.");
        var delta=second-first;var distance=delta.Length;
        if(frame is not null)
        {
            DesignValidation.Frame(frame);var z=frame.Axis;var x=frame.ReferenceDirection;
            var y=new DesignVector(z.Y*x.Z-z.Z*x.Y,z.Z*x.X-z.X*x.Z,z.X*x.Y-z.Y*x.X);
            delta=new(delta.Dot(x),delta.Dot(y),delta.Dot(z));
        }
        return new(distance,delta);
    }
}
