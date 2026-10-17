using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using SuspensionSimulation.Components;

namespace SuspensionSimulation.Design;

public sealed record TwinsTopologySeed(AssemblyDefinition Vehicle,double RackTravelAt450DegreesM,string SourceSha256);

/// <summary>Explicit synthetic front/rear mechanisms. No alignment targets or legacy rear damper aliases.</summary>
public static class TwinsTopologyCatalog
{
    public const string VehicleId="synthetic-twins-explicit-v1";
    public static TwinsTopologySeed Seed(DesignAssetLibrary library,string sourceFile)
    {
        var bytes=File.ReadAllBytes(sourceFile);var hash=Convert.ToHexStringLower(SHA256.HashData(bytes));using var json=JsonDocument.Parse(bytes);var root=json.RootElement;
        if(root.GetProperty("schema").GetString()!="suspension-sim/explicit-twins-topology/v1"||root.GetProperty("provenance").GetString()!="synthetic") throw new InvalidDataException("Unsupported explicit topology source.");
        var travel=root.GetProperty("rackTravelAt450DegreesM").GetDouble();if(!double.IsFinite(travel)||travel<=0) throw new InvalidDataException("Invalid rack travel.");
        var prior=library.LoadAssembly(VehicleId);
        if(prior is not null)
        {
            var graph=new AssemblyGraphResolver(library).Compile(new(prior.Identity.AssetId,prior.Identity.RevisionId));var specification=graph.Components.Single(x=>x.Kind==ComponentKind.Rack).Rack??throw new InvalidDataException("Saved rack specification is missing.");
            var savedHash=prior.Identity.Provenance.Source?.Split(' ').LastOrDefault();
            if(savedHash?.Length!=64||!savedHash.All(Uri.IsHexDigit)) throw new InvalidDataException("Saved topology source hash is missing; open its saved assembly directly.");
            return new(prior,specification.TravelAtLockM,savedHash);
        }
        RevisionIdentity Identity(string id,string name)=>new("twins-"+id,"draft",name,new(DataProvenance.Synthetic,root.GetProperty("note").GetString()!,"design-topology.json SHA-256 "+hash));
        static DesignVector Unit(DesignVector p)=>p.Length>1e-9?p/p.Length:throw new ArgumentException("Degenerate topology axis.");
        DesignVector Point(JsonElement section,string name) {var a=section.GetProperty(name).EnumerateArray().Select(x=>x.GetDouble()).ToArray();if(a.Length!=3) throw new InvalidDataException("Expected XYZ point.");var p=new DesignVector(a[0],a[1],a[2]);if(!p.IsFinite) throw new InvalidDataException("Invalid point.");return p;}
        static DesignFrame Frame(DesignVector origin,DesignVector z)
        {z=Unit(z);var candidate=Math.Abs(z.X)<.9?new DesignVector(1,0,0):new DesignVector(0,1,0);return new(origin,z,Unit(candidate-z*z.Dot(candidate)));}
        ComponentDefinition Part(string id,ComponentKind kind,DesignVector origin,(string Id,DesignVector Point)[] points,(string Id,string Point,JointKind Kind,DesignVector Axis)[] ports,(string A,string B)[] edges,AxialRodGauge? gauge=null)
        {
            var definition=new ComponentDefinition(Identity(id,id),kind,[..points.Select(p=>new NamedDesignPoint(p.Id,p.Id,p.Point-origin))],
                [..ports.Select(p=>new NamedDesignFrame(p.Id,p.Id,Frame(points.Single(x=>x.Id==p.Point).Point-origin,p.Axis)))],
                [..ports.Select(p=>new AttachmentPort(p.Id,p.Id,p.Id,[p.Kind]))],[],null)
                {VisualEdges=[..edges.Select((e,i)=>new VisualEdge("edge-"+i,e.A,e.B))],AxialGauge=gauge,Rack=kind==ComponentKind.Rack?new(travel,450):null};
            return library.LoadComponent(definition.Identity.AssetId)??library.Publish(definition,null);
        }
        ComponentInstance Instance(string id,ComponentDefinition component,DesignVector origin)=>new(id,new(component.Identity.AssetId),new(AssemblyAuthoring.IdentityPlacement.Frame with {OriginM=origin}));
        AssemblyDefinition Publish(AssemblyDefinition a)=>library.LoadAssembly(a.Identity.AssetId)??library.Publish(a,null);
        var z=new DesignVector(0,0,1);var x=new DesignVector(1,0,0);var y=new DesignVector(0,1,0);var zero=new DesignVector(0,0,0);
        // Rods are real component instances. Axial spin is a declared coordinate gauge, never an off-axis mounting shortcut.
        ComponentInstance Rod(string id,DesignVector a,DesignVector b)
        {
            var rod=Part(id,ComponentKind.RigidLink,a,[("inner",a),("outer",b)],[("inner","inner",JointKind.Spherical,z),("outer","outer",JointKind.Spherical,z)],[("inner","outer")],new("inner","outer"));
            return Instance(id,rod,a);
        }
        var f=root.GetProperty("front");var fw=Point(f,"wheel");var flf=Point(f,"lowerFront");var flr=Point(f,"lowerRear");var fl=(flf+flr)/2;var fb=Point(f,"lowerBall");var ft=Point(f,"strutTop");var fs=Point(f,"strutLower");var fti=Point(f,"tieInner");var fto=Point(f,"tieOuter");var fa=Unit(flr-flf);var strut=Unit(ft-fs);
        var fc=Part("front-chassis",ComponentKind.Structure,zero,[("lower",fl),("strut-top",ft)],[("lower","lower",JointKind.Revolute,fa),("strut-top","strut-top",JointKind.Spherical,z)],[("lower","strut-top")]);
        var farm=Part("front-arm",ComponentKind.Wishbone,fl,[("front",flf),("rear",flr),("inner",fl),("ball",fb)],[("inner","inner",JointKind.Revolute,fa),("ball","ball",JointKind.Spherical,z)],[("front","rear"),("front","ball"),("rear","ball")]);
        var fu=Part("front-upright",ComponentKind.Upright,fw,[("wheel",fw),("lower",fb),("strut",fs),("tie",fto),("arb",Point(f,"arbLower"))],[("lower","lower",JointKind.Spherical,z),("strut","strut",JointKind.Prismatic,strut),("tie","tie",JointKind.Spherical,z),("wheel","wheel",JointKind.Revolute,y)],[("lower","strut"),("strut","tie"),("strut","wheel"),("strut","arb")]);
        var shaft=Part("front-shaft",ComponentKind.Connector,ft,[("top",ft),("slider",fs)],[("top","top",JointKind.Spherical,z),("slider","slider",JointKind.Prismatic,strut)],[("top","slider")]);
        var frontCorner=Publish(new(Identity("front-corner","Synthetic strut corner"),[Instance("chassis",fc,zero),Instance("lower",farm,fl),Instance("upright",fu,fw),Instance("shaft",shaft,ft)],
            [new("lower-hinge",JointKind.Revolute,new("chassis","lower"),new("lower","inner")),new("lower-ball",JointKind.Spherical,new("lower","ball"),new("upright","lower")),
             new("top-bearing",JointKind.Spherical,new("chassis","strut-top"),new("shaft","top")),new("strut-stroke",JointKind.Prismatic,new("shaft","slider"),new("upright","strut"))],
            [new("tie",new("upright","tie")),new("wheel",new("upright","wheel"))]));
        var rackCenter=new DesignVector(fti.X,0,fti.Z);var rackRight=new DesignVector(fti.X,-fti.Y,fti.Z);
        var guide=Part("rack-guide",ComponentKind.Structure,rackCenter,[("origin",rackCenter),("axis",rackCenter+y*.1)],[("slider","origin",JointKind.Prismatic,y)],[("origin","axis")]);
        var rack=Part("rack",ComponentKind.Rack,rackCenter,[("origin",rackCenter),("left",fti),("right",rackRight)],[("slider","origin",JointKind.Prismatic,y),("left","left",JointKind.Spherical,z),("right","right",JointKind.Spherical,z)],[("left","right")]);
        var tieLeft=Rod("left-tie",fti,fto);var tieRight=Rod("right-tie",rackRight,new(fto.X,-fto.Y,fto.Z));
        var leftPlacement=AssemblyAuthoring.IdentityPlacement;var rightPlacement=leftPlacement with {Reflection=new(zero,y)};
        var front=Publish(new(Identity("front-axle","Synthetic front axle with translating rack"),[new AssemblyInstance("left",new(frontCorner.Identity.AssetId),leftPlacement),new AssemblyInstance("right",new(frontCorner.Identity.AssetId),rightPlacement),Instance("guide",guide,rackCenter),Instance("rack",rack,rackCenter),tieLeft,tieRight],
            [new("rack-slide",JointKind.Prismatic,new("guide","slider"),new("rack","slider")),new("left-inner",JointKind.Spherical,new("rack","left"),new("left-tie","inner")),new("left-outer",JointKind.Spherical,new("left-tie","outer"),new("left","tie")),
             new("right-inner",JointKind.Spherical,new("rack","right"),new("right-tie","inner")),new("right-outer",JointKind.Spherical,new("right-tie","outer"),new("right","tie"))],
            [new("left-wheel",new("left","wheel")),new("right-wheel",new("right","wheel"))]) {GroundedInstances=["guide","left/chassis","right/chassis"]});
        var r=root.GetProperty("rear");var rw=Point(r,"wheel");var ri=Point(r,"lowerInner");var rb=Point(r,"lowerBall");var ruf=Point(r,"upperFront");var rur=Point(r,"upperRear");var ru=(ruf+rur)/2;var rub=Point(r,"upperBall");var ra=Unit(Point(r,"lowerAxis"));var rua=Unit(rur-ruf);var lowerSecond=Unit(z-ra*ra.Dot(z));
        var rti=Point(r,"toeInner");var rto=Point(r,"toeOuter");var rxi=Point(r,"tractionInner");var rxo=Point(r,"tractionOuter");var shockTop=Point(r,"shockUpper");var shockBottom=Point(r,"shockLower");
        var rc=Part("rear-chassis",ComponentKind.Structure,zero,[("lower",ri),("upper",ru),("toe",rti),("traction",rxi),("shock-upper",shockTop)],[("lower","lower",JointKind.Universal,ra),("upper","upper",JointKind.Revolute,rua),("toe","toe",JointKind.Spherical,z),("traction","traction",JointKind.Spherical,z),("shock-upper","shock-upper",JointKind.Spherical,z)],[("lower","upper"),("lower","toe"),("lower","traction"),("upper","shock-upper")]);
        var rl=Part("rear-lower",ComponentKind.RigidLink,ri,[("inner",ri),("ball",rb),("shock-lower",shockBottom),("arb",Point(r,"arbLower"))],[("inner","inner",JointKind.Universal,lowerSecond),("ball","ball",JointKind.Spherical,z),("shock-lower","shock-lower",JointKind.Spherical,z)],[("inner","ball"),("inner","shock-lower"),("shock-lower","arb")]);
        var upper=Part("rear-upper",ComponentKind.Wishbone,ru,[("front",ruf),("rear",rur),("inner",ru),("ball",rub)],[("inner","inner",JointKind.Revolute,rua),("ball","ball",JointKind.Spherical,z)],[("front","rear"),("front","ball"),("rear","ball")]);
        var rearUpright=Part("rear-upright",ComponentKind.Upright,rw,[("wheel",rw),("lower",rb),("upper",rub),("toe",rto),("traction",rxo)],[("lower","lower",JointKind.Spherical,z),("upper","upper",JointKind.Spherical,z),("toe","toe",JointKind.Spherical,z),("traction","traction",JointKind.Spherical,z),("wheel","wheel",JointKind.Revolute,y)],[("lower","upper"),("upper","wheel"),("lower","traction"),("traction","toe")]);
        var rear=Publish(new(Identity("rear-corner","Synthetic rear upper/lower/toe/traction mechanism"),[Instance("chassis",rc,zero),Instance("lower",rl,ri),Instance("upper",upper,ru),Instance("upright",rearUpright,rw),Rod("rear-toe",rti,rto),Rod("rear-traction",rxi,rxo)],
            [new("lower-universal",JointKind.Universal,new("chassis","lower"),new("lower","inner")),new("upper-hinge",JointKind.Revolute,new("chassis","upper"),new("upper","inner")),new("lower-ball",JointKind.Spherical,new("lower","ball"),new("upright","lower")),new("upper-ball",JointKind.Spherical,new("upper","ball"),new("upright","upper")),
             new("toe-inner",JointKind.Spherical,new("chassis","toe"),new("rear-toe","inner")),new("toe-outer",JointKind.Spherical,new("rear-toe","outer"),new("upright","toe")),new("traction-inner",JointKind.Spherical,new("chassis","traction"),new("rear-traction","inner")),new("traction-outer",JointKind.Spherical,new("rear-traction","outer"),new("upright","traction"))],
            [new("wheel",new("upright","wheel")),new("shock-upper",new("chassis","shock-upper")),new("shock-lower",new("lower","shock-lower"))]) {GroundedInstances=["chassis"]});
        var vehicle=Publish(new(Identity("vehicle","Synthetic GR86/BRZ explicit suspension" ) with {AssetId=VehicleId},[new AssemblyInstance("front",new(front.Identity.AssetId),leftPlacement),new AssemblyInstance("rear-left",new(rear.Identity.AssetId),leftPlacement),new AssemblyInstance("rear-right",new(rear.Identity.AssetId),rightPlacement)],[],[])
            {GroundedInstances=["front/guide","front/left/chassis","front/right/chassis","rear-left/chassis","rear-right/chassis"]});
        return new(vehicle,travel,hash);
    }
}
