using System.Collections.Immutable;

namespace SuspensionSimulation.Design;

/// <summary>Immutable owner properties. Length is undeformed axle-reference-plane spacing; EI/GJ are rigidities, not mounting-point N/mm rates.</summary>
public sealed record ChassisSpecification(double LengthM,double FrontAxleXM,double FrontOverhangM,double RearOverhangM,
    double RailHalfWidthM,double RailHeightM,double RackSetbackM,double RackHeightM,
    double VerticalBendingRigidityNm2,double LateralBendingRigidityNm2,double TorsionalRigidityNm2);
public enum ChassisSection { Front, Rear, Rack }
public sealed record ChassisAxleAttachment(string InstanceId,ChassisSection Section,DesignVector ReferencePointM);
public sealed record ChassisGroundAttachment(string BodyPath,ChassisSection Section);
public sealed record ChassisInstallation(string ComponentPath,double ReferenceFrontAxleXM,double ReferenceLengthM,DesignVector ReferenceRackMountM,
    ImmutableArray<ChassisAxleAttachment> AxleAttachments,ImmutableArray<ChassisGroundAttachment> GroundAttachments);

public static class ChassisAuthoring
{
    public const string Sources="https://journals.sagepub.com/doi/10.1177/0036850420927204 ; https://www.extrica.com/article/21171 ; https://newsroom.toyota.eu/world-premiere-of-all-new-gr-86/ ; https://s3.amazonaws.com/subarumedia.iconicweb.com/mediasite/attachments/22MY_BRZ_Release_-_FINAL_UPDATED.pdf";
    public const string EstimateNote="Estimated equivalent beam, not measured GR86/BRZ shell data. EI_y=4.62e6 N m² uses the published light-van beam scale; EI_z=6e6 N m² is a generic lateral estimate. GJ=2.95e6 N m² gives about 20000 N m/deg at 2.575m, in the generic BIW scale. Simply-supported central-load conversion is K=48EI/L³; pure end-couple conversion is Ktheta=EI/L or GJ/L. Manufacturer releases establish relative increases only. Axial extension/compression and shear are constrained; three constant-curvature bend/twist modes omit local shell deformation and higher modes.";
    public static ChassisSpecification Estimated(double lengthM=2.575,double frontAxleXM=1.21025)=>new(lengthM,frontAxleXM,.8,.89,.5,.18,.16,.29,4.62e6,6e6,2.95e6);
    public static void Validate(ChassisSpecification s)
    {
        var values=new[]{s.LengthM,s.FrontAxleXM,s.FrontOverhangM,s.RearOverhangM,s.RailHalfWidthM,s.RailHeightM,s.RackSetbackM,s.RackHeightM,s.VerticalBendingRigidityNm2,s.LateralBendingRigidityNm2,s.TorsionalRigidityNm2};
        if(values.Any(v=>!double.IsFinite(v))||s.LengthM<=0||s.LengthM>20||s.FrontOverhangM<0||s.RearOverhangM<0||s.RailHalfWidthM<=0||s.RailHeightM<0||s.RackHeightM<0||s.RackSetbackM<=0||s.RackSetbackM>=s.LengthM||new[]{s.VerticalBendingRigidityNm2,s.LateralBendingRigidityNm2,s.TorsionalRigidityNm2}.Any(k=>k<=0||!double.IsFinite(k/s.LengthM))) throw new ArgumentException("Chassis needs finite positive length/rigidities and a rack crossmember behind the front axle.");
    }
    public static ComponentDefinition Create(RevisionIdentity identity,ChassisSpecification s,ComponentMass? mass=null)
    {
        Validate(s);var points=new List<NamedDesignPoint>();var edges=new List<VisualEdge>();
        void Point(string id,double px,double py,double pz)=>points.Add(new(id,id.Replace('-',' '),new(px,py,pz)));
        void Edge(string id,string a,string b)=>edges.Add(new(id,a,b));
        var rear=s.FrontAxleXM-s.LengthM;var stations=new[]{rear-s.RearOverhangM,rear,rear+s.LengthM*.25,rear+s.LengthM*.5,rear+s.LengthM*.75,s.FrontAxleXM-s.RackSetbackM,s.FrontAxleXM,s.FrontAxleXM+s.FrontOverhangM}.Distinct().Order().ToArray();
        for(var i=0;i<stations.Length;i++) foreach(var (side,sign) in new[]{("left",1),("right",-1)})
        {Point(side+"-rail-"+i,stations[i],sign*s.RailHalfWidthM,s.RailHeightM);if(i>0) Edge(side+"-rail-segment-"+i,side+"-rail-"+(i-1),side+"-rail-"+i);}
        foreach(var (id,px,pz) in new[]{("front-axle",s.FrontAxleXM,s.RailHeightM),("rear-axle",rear,s.RailHeightM),("rack-crossmember",s.FrontAxleXM-s.RackSetbackM,s.RackHeightM)})
        {Point(id+"-left",px,s.RailHalfWidthM,pz);Point(id+"-right",px,-s.RailHalfWidthM,pz);Point(id,px,0,pz);Edge(id,id+"-left",id+"-right");}
        var rackStation=Array.IndexOf(stations,s.FrontAxleXM-s.RackSetbackM);foreach(var side in new[]{"left","right"}) Edge("rack-crossmember-"+side+"-support","rack-crossmember-"+side,side+"-rail-"+rackStation);
        var x=new DesignVector(1,0,0);var z=new DesignVector(0,0,1);var frames=new[]{new NamedDesignFrame("front-axle","Front axle reference plane",new(new(s.FrontAxleXM,0,0),z,x)),new NamedDesignFrame("rear-axle","Rear axle reference plane",new(new(rear,0,0),z,x)),new NamedDesignFrame("rack-mount","Rack mounting crossmember",new(new(s.FrontAxleXM-s.RackSetbackM,0,s.RackHeightM),z,x))};
        return new(identity,ComponentKind.Chassis,[..points],[..frames],[..frames.Select(f=>new AttachmentPort(f.Id,f.Id,f.Name,[JointKind.Fixed]))],[],mass) {Chassis=s,VisualEdges=[..edges],AdjustmentParameters=[]};
    }
}

/// <summary>Compile axle positions from the source chassis length. Root installation records contain reference coordinates, never a second chosen wheelbase.</summary>
public static class ChassisAssemblyGeometry
{
    public static ResolvedAssemblyGraph Place(ResolvedAssemblyGraph graph)
    {
        if(graph.Root.Vehicle?.Chassis is not {} install) return graph;
        var body=graph.Bodies.SingleOrDefault(b=>b.Path==install.ComponentPath)??throw new ArgumentException("Chassis installation component is missing.");var s=body.Component.Chassis??throw new ArgumentException("Chassis installation needs a chassis component.");
        if(body.Transform!=DesignTransform.Identity) throw new ArgumentException("The chassis installation uses the root chassis frame; move the complete assembly instead.");
        if(!double.IsFinite(install.ReferenceFrontAxleXM)||!double.IsFinite(install.ReferenceLengthM)||install.ReferenceLengthM<=0||!install.ReferenceRackMountM.IsFinite||install.AxleAttachments.IsDefaultOrEmpty||install.GroundAttachments.IsDefaultOrEmpty||install.AxleAttachments.Select(a=>a.InstanceId).Distinct().Count()!=install.AxleAttachments.Length||install.GroundAttachments.Select(a=>a.BodyPath).Distinct().Count()!=install.GroundAttachments.Length) throw new ArgumentException("Invalid chassis installation references.");
        foreach(var a in install.AxleAttachments) if(!graph.Root.Instances.Any(i=>i.Id==a.InstanceId)||a.InstanceId==install.ComponentPath||a.Section is not (ChassisSection.Front or ChassisSection.Rear)||!a.ReferencePointM.IsFinite) throw new ArgumentException("Chassis axle attachment must name a direct child, an axle section and finite source datum.");
        if(!graph.GroundedBodies.Where(p=>p!=install.ComponentPath).ToHashSet(StringComparer.Ordinal).SetEquals(install.GroundAttachments.Select(a=>a.BodyPath))||install.GroundAttachments.Any(a=>!Enum.IsDefined(a.Section))) throw new ArgumentException("Every chassis-relative mounting bracket must attach to exactly one beam section.");
        DesignVector Delta(string path)
        {
            var a=install.AxleAttachments.SingleOrDefault(a=>path==a.InstanceId||path.StartsWith(a.InstanceId+"/",StringComparison.Ordinal));if(a is null) return new(0,0,0);
            var declared=graph.Root.Instances.Single(i=>i.Id==a.InstanceId);var current=DesignTransform.From(declared.Placement).Point(a.ReferencePointM);
            var delta=new DesignVector((a.Section==ChassisSection.Front?s.FrontAxleXM:s.FrontAxleXM-s.LengthM)-current.X,0,0);
            if(path is "front/guide" or "front/rack") delta+=new DesignVector(s.FrontAxleXM-s.RackSetbackM-install.ReferenceRackMountM.X-delta.X,0,s.RackHeightM-install.ReferenceRackMountM.Z);
            return delta;
        }
        ResolvedPort Port(ResolvedPort p)=>p with {Frame=p.Frame with {OriginM=p.Frame.OriginM+Delta(p.BodyPath)}};
        return graph with {Bodies=[..graph.Bodies.Select(b=>b with {Transform=b.Transform with {Origin=b.Transform.Origin+Delta(b.Path)}})],Joints=[..graph.Joints.Select(j=>j with {First=Port(j.First),Second=Port(j.Second)})],
            Exports=graph.Exports.ToImmutableDictionary(p=>p.Key,p=>Port(p.Value),StringComparer.Ordinal),Interfaces=graph.Interfaces.ToImmutableDictionary(p=>p.Key,p=>Port(p.Value),StringComparer.Ordinal),
            ElasticElements=[..graph.ElasticElements.Select(e=>e with {First=Port(e.First),Second=Port(e.Second),Transform=e.Transform with {Origin=e.Transform.Origin+Delta(e.Path)},FirstReference=e.FirstReference with {OriginM=e.FirstReference.OriginM+Delta(e.Path)},SecondReference=e.SecondReference with {OriginM=e.SecondReference.OriginM+Delta(e.Path)}})]};
    }
}
