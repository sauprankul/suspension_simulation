using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using SuspensionSimulation.Components;

namespace SuspensionSimulation.Design;

public sealed record TwinsInstalledModels(AssemblyDefinition Gr86,AssemblyDefinition Brz);

/// <summary>Reference equipment in an explicitly estimated physical installation, separate from the legacy adapter.</summary>
public static class TwinsInstalledCatalog
{
    public static TwinsInstalledModels Seed(DesignAssetLibrary library,string repository)
    {
        var topology=TwinsTopologyCatalog.Seed(library,Path.Combine(repository,"data/synthetic/gr86/design-topology.json"));
        var estimateBytes=File.ReadAllBytes(Path.Combine(repository,"data/synthetic/gr86/design-equipment-estimates.json"));using var estimates=JsonDocument.Parse(estimateBytes);
        var referenceBytes=File.ReadAllBytes(Path.Combine(repository,"data/reference/stock-86-brz/reference-sheet.json"));using var reference=JsonDocument.Parse(referenceBytes);
        var source="Topology "+topology.SourceSha256+"; installation "+Convert.ToHexStringLower(SHA256.HashData(estimateBytes))+"; reference "+Convert.ToHexStringLower(SHA256.HashData(referenceBytes));
        var note=estimates.RootElement.GetProperty("note").GetString()!;
        RevisionIdentity Identity(string id,string name)=>new("installed-"+id,"draft",name,new(DataProvenance.Estimated,note,source));
        var zero=new DesignVector(0,0,0);var x=new DesignVector(1,0,0);var y=new DesignVector(0,1,0);var z=new DesignVector(0,0,1);
        ComponentDefinition Save(ComponentDefinition c)=>library.LoadComponent(c.Identity.AssetId)??library.Publish(c,null);
        AssemblyDefinition SaveAssembly(AssemblyDefinition a)=>library.LoadAssembly(a.Identity.AssetId)??library.Publish(a,null);
        ComponentInstance Instance(string id,ComponentDefinition c,DesignVector? origin=null)=>new(id,new(c.Identity.AssetId),new(AssemblyAuthoring.IdentityPlacement.Frame with {OriginM=origin??zero}));
        ComponentDefinition Anchor(string assetId,string portId,string pointId,JointKind joint,DesignVector? pointOffset=null,DesignVector? axis=null)
        {
            var c=library.LoadComponent(assetId)??throw new InvalidOperationException("Missing physical base "+assetId);
            if(c.Ports.Any(p=>p.Id==portId)) return c;
            var point=c.Points.Single(p=>p.Id==pointId).PositionM+(pointOffset??zero);var frameId=portId+"-frame";
            var next=c with {Frames=c.Frames.Add(new(frameId,portId,new(point,axis??z,x))),Ports=c.Ports.Add(new(portId,frameId,portId,[joint]))};
            return library.Publish(next,c.Identity.RevisionId);
        }
        Anchor("twins-front-upright","damper-lower","strut",JointKind.Spherical);Anchor("twins-front-upright","arb","arb",JointKind.Spherical);
        Anchor("twins-rear-lower","arb","arb",JointKind.Spherical);Anchor("twins-front-chassis","topmount-base","strut-top",JointKind.Fixed,z*.025);
        Anchor("twins-front-upright","wheel-fixed","wheel",JointKind.Fixed,axis:y);Anchor("twins-rear-upright","wheel-fixed","wheel",JointKind.Fixed,axis:y);
        var wheelData=estimates.RootElement.GetProperty("wheel");var radius=wheelData.GetProperty("rollingRadiusM").GetDouble();var width=wheelData.GetProperty("widthM").GetDouble();
        var wheel=Save(new(Identity("wheel","Estimated rigid wheel/tire contact envelope; spin omitted"),ComponentKind.Wheel,[new("center","Wheel center",zero),new("radius","Rolling-radius datum",z*radius)],
            [new("hub","Hub interface",new(zero,y,x))],[new("hub","hub","hub",[JointKind.Fixed])],[],null) {Wheel=new(radius,width),VisualEdges=[new("radius","center","radius")]});
        var mount=Save(new(Identity("top-mount","Fixed upper mount (estimated stack)"),ComponentKind.TopMount,
            [new("base","Chassis mounting face",zero),new("bearing","Strut bearing",new(0,0,-.025)),new("bolt","Mounting bolt",new(.05,0,0))],
            [new("base","Chassis interface",new(zero,z,x)),new("bearing","Strut bearing",new(new(0,0,-.025),z,x))],
            [new("base","base","chassis",[JointKind.Fixed]),new("bearing","bearing","strut-bearing",[JointKind.Spherical])],[],null) {VisualEdges=[new("stack","base","bearing"),new("flange","base","bolt")]});
        var oldCorner=library.LoadAssembly("twins-front-corner")!;var mountPosition=library.LoadComponent("twins-front-shaft")!.Points.Single(p=>p.Id=="top").PositionM;
        var shaftInstance=oldCorner.Instances.Single(i=>i.Id=="shaft");mountPosition=DesignTransform.From(shaftInstance.Placement).Point(mountPosition)+z*.025;
        var frontWheelPosition=DesignTransform.From(oldCorner.Instances.Single(i=>i.Id=="upright").Placement).Origin;
        var frontCorner=SaveAssembly(oldCorner with {Identity=Identity("front-corner-v2","Installed strut corner"),Instances=oldCorner.Instances.Add(Instance("top-mount",mount,mountPosition)).Add(Instance("wheel",wheel,frontWheelPosition)),
            Joints=oldCorner.Joints.Select(j=>j.Id=="top-bearing"?j with {First=new("top-mount","bearing")}:j).Append(new("topmount-fix",JointKind.Fixed,new("chassis","topmount-base"),new("top-mount","base"))).Append(new("wheel-fixed",JointKind.Fixed,new("upright","wheel-fixed"),new("wheel","hub"))).ToImmutableArray(),
            Ports=[..oldCorner.Ports,new("shock-upper",new("shaft","top")),new("shock-lower",new("upright","damper-lower")),new("arb",new("upright","arb"))]});
        var oldAxle=library.LoadAssembly("twins-front-axle")!;
        var front=SaveAssembly(oldAxle with {Identity=Identity("front-axle-v2","Installed front rack and struts"),Instances=[..oldAxle.Instances.Select(i=>i is AssemblyInstance?i with {Definition=new(frontCorner.Identity.AssetId)}:i)],
            Ports=[..oldAxle.Ports,new("left-shock-upper",new("left","shock-upper")),new("left-shock-lower",new("left","shock-lower")),new("right-shock-upper",new("right","shock-upper")),new("right-shock-lower",new("right","shock-lower")),new("left-arb",new("left","arb")),new("right-arb",new("right","arb"))]});
        var oldRear=library.LoadAssembly("twins-rear-corner")!;
        var rearWheelPosition=DesignTransform.From(oldRear.Instances.Single(i=>i.Id=="upright").Placement).Origin;
        var rear=SaveAssembly(oldRear with {Identity=Identity("rear-corner-v2","Installed rear upper/lower/toe/traction corner"),Instances=oldRear.Instances.Add(Instance("wheel",wheel,rearWheelPosition)),Joints=oldRear.Joints.Add(new("wheel-fixed",JointKind.Fixed,new("upright","wheel-fixed"),new("wheel","hub"))),Ports=oldRear.Ports.Add(new("arb",new("lower","arb")))});
        ComponentInstance Rod(string id,DesignVector a,DesignVector b)
        {
            var delta=b-a;var c=Save(new(Identity(id,id),ComponentKind.RigidLink,[new("inner","Inner ball",zero),new("outer","Outer ball",delta)],
                [new("inner","Inner ball",new(zero,z,x)),new("outer","Outer ball",new(delta,z,x))],[new("inner","inner","inner",[JointKind.Spherical]),new("outer","outer","outer",[JointKind.Spherical])],[],null)
                {VisualEdges=[new("rod","inner","outer")],AxialGauge=new("inner","outer")});return Instance(id,c,a);
        }
        AssemblyDefinition BarAssembly(string axle,string variant,JsonElement dimensions)
        {
            var frontAxle=axle=="front";var positionX=frontAxle?1.06025:-1.523;var halfSpan=frontAxle?.43:.5;var endpointX=frontAxle?1.22025:-1.390;var endpointY=frontAxle?.565:.539;
            var height=estimates.RootElement.GetProperty(axle).GetProperty("barHeightM").GetDouble();var left=new DesignVector(positionX,halfSpan,height);var right=new DesignVector(positionX,-halfSpan,height);
            var endLeft=new DesignVector(endpointX,endpointY,height);var endRight=new DesignVector(endpointX,-endpointY,height);
            ComponentDefinition Arm(string side,DesignVector hinge,DesignVector end)=>Save(new(Identity(axle+"-bar-arm-"+side,axle+" rigid bar arm "+side),ComponentKind.RigidLink,[new("hinge","Bearing",zero),new("end","Endlink ball",end-hinge)],
                [new("hinge","Bearing",new(zero,y,x)),new("end","Endlink ball",new(end-hinge,z,x))],[new("hinge","hinge","bearing",[JointKind.Revolute,JointKind.Fixed]),new("end","end","endlink",[JointKind.Spherical])],[],null) {VisualEdges=[new("rigid-arm","hinge","end")]});
            var armLeft=Arm("left",left,endLeft);var armRight=Arm("right",right,endRight);
            var housing=Save(new(Identity(axle+"-bar-housing",axle+" rigid bar bearings"),ComponentKind.Structure,[new("left","Left bearing",left),new("right","Right bearing",right)],
                [new("left","Left bearing",new(left,y,x)),new("right","Right bearing",new(right,y,x))],[new("left","left","bearing",[JointKind.Revolute]),new("right","right","bearing",[JointKind.Revolute])],[],null) {VisualEdges=[new("axis","left","right")]});
            var behavior=new TorsionBarBehavior("first","second",y,dimensions.GetProperty("shearModulusPa").GetDouble(),dimensions.GetProperty("outsideDiameterM").GetDouble(),dimensions.GetProperty("insideDiameterM").GetDouble(),dimensions.GetProperty("torsionLengthM").GetDouble());
            var coupler=Save(new(Identity(variant+"-"+axle+"-bar",variant+" "+axle+" torsion section"),ComponentKind.AntiRollBar,[new("first","Left torsion end",left),new("second","Right torsion end",right)],
                [new("first","Left torsion end",new(left,y,x)),new("second","Right torsion end",new(right,y,x))],[new("first","first","torsion-end",[JointKind.Fixed]),new("second","second","torsion-end",[JointKind.Fixed])],[],null) {VisualEdges=[new("torsion","first","second")],Behavior=behavior});
            var lowerLeft=frontAxle?new DesignVector(1.22025,.565,.4):new DesignVector(-1.39,.539,.26);var lowerRight=new DesignVector(lowerLeft.X,-lowerLeft.Y,lowerLeft.Z);
            var linkLeft=Rod(axle+"-endlink-left",endLeft,lowerLeft);var linkRight=Rod(axle+"-endlink-right",endRight,lowerRight);
            return SaveAssembly(new(Identity(variant+"-"+axle+"-bar-assembly",variant+" "+axle+" torsion-only bar"),[Instance("housing",housing),Instance("left-arm",armLeft,left),Instance("right-arm",armRight,right),linkLeft,linkRight,Instance("coupler",coupler)],
                [new("left-bearing",JointKind.Revolute,new("housing","left"),new("left-arm","hinge")),new("right-bearing",JointKind.Revolute,new("housing","right"),new("right-arm","hinge")),
                 new("left-torsion",JointKind.Fixed,new("left-arm","hinge"),new("coupler","first")),new("right-torsion",JointKind.Fixed,new("right-arm","hinge"),new("coupler","second")),
                 new("left-link",JointKind.Spherical,new("left-arm","end"),new(linkLeft.Id,"inner")),new("right-link",JointKind.Spherical,new("right-arm","end"),new(linkRight.Id,"inner"))],
                [new("left-lower",new(linkLeft.Id,"outer")),new("right-lower",new(linkRight.Id,"outer"))]));
        }
        AssemblyDefinition Vehicle(JsonElement setup,string variant)
        {
            var existing=library.LoadAssembly("installed-stock-"+variant+"-v2");if(existing is not null) return existing;
            var frontBar=BarAssembly("front",variant,setup.GetProperty("frontArb"));var rearBar=BarAssembly("rear",variant,setup.GetProperty("rearArb"));
            var baseVehicle=topology.Vehicle;var instances=baseVehicle.Instances.Select(i=>i.Id=="front"?i with {Definition=new(front.Identity.AssetId)}:i with {Definition=new(rear.Identity.AssetId)}).ToList();
            instances.Add(new AssemblyInstance("front-bar",new(frontBar.Identity.AssetId),AssemblyAuthoring.IdentityPlacement));instances.Add(new AssemblyInstance("rear-bar",new(rearBar.Identity.AssetId),AssemblyAuthoring.IdentityPlacement));
            var joints=new List<AssemblyJoint>
            {
                new("front-bar-left",JointKind.Spherical,new("front-bar","left-lower"),new("front","left-arb")),new("front-bar-right",JointKind.Spherical,new("front-bar","right-lower"),new("front","right-arb")),
                new("rear-bar-left",JointKind.Spherical,new("rear-bar","left-lower"),new("rear-left","arb")),new("rear-bar-right",JointKind.Spherical,new("rear-bar","right-lower"),new("rear-right","arb"))
            };
            var seats=new List<SpringSeatBinding>();
            foreach(var axle in new[]{"front","rear"})
            {
                var e=estimates.RootElement.GetProperty(axle);double D(string field)=>e.GetProperty(field).GetDouble();
                var spring=Save(ElasticComponentFactory.Spring(Identity(variant+"-"+axle+"-spring",variant+" "+axle+" spring: reference rate, estimated dimensions"),setup.GetProperty(axle+"SpringRateKgfPerMm").GetDouble(),D("springFreeM"),D("springBlockM")));
                var damper=Save(ElasticComponentFactory.Damper(Identity(axle+"-damper",axle+" "+(axle=="front"?"strut":"shock")+": estimated stroke/seats; no damping data"),new("first","second",axle=="front"?DamperFamily.Strut:DamperFamily.Shock,D("damperMinimumM"),D("damperMaximumM"),0,0,0,new(D("upperSeatInsetM"),D("lowerSeatInsetM")))));
                foreach(var side in new[]{"left","right"})
                {
                    var corner=(axle=="front"?"F":"R")+(side=="left"?"L":"R");var springId=corner+"-spring";var damperId=corner+"-damper";instances.Add(Instance(springId,spring));instances.Add(Instance(damperId,damper));
                    var upper=axle=="front"?new InstancePort("front",side+"-shock-upper"):new InstancePort("rear-"+side,"shock-upper");var lower=axle=="front"?new InstancePort("front",side+"-shock-lower"):new InstancePort("rear-"+side,"shock-lower");
                    foreach(var id in new[]{springId,damperId}) {joints.Add(new(id+"-upper",JointKind.Spherical,new(id,"first"),upper));joints.Add(new(id+"-lower",JointKind.Spherical,new(id,"second"),lower));}
                    seats.Add(new(springId,damperId));
                }
            }
            return SaveAssembly(new(Identity("stock-"+variant+"-v2",variant.ToUpperInvariant()+" reference equipment / synthetic installation"),[..instances],[..joints],[])
                {GroundedInstances=[..baseVehicle.GroundedInstances,"front-bar/housing","rear-bar/housing"],SpringSeats=[..seats],Vehicle=new(
                    [new("FL","front/left/wheel","center"),new("FR","front/right/wheel","center"),new("RL","rear-left/wheel","center"),new("RR","rear-right/wheel","center")],
                    [new("front-left","Estimated front-left chassis datum",new(.9,.3,.15)),new("front-right","Estimated front-right chassis datum",new(.9,-.3,.15)),new("rear-left","Estimated rear-left chassis datum",new(-.9,.3,.15)),new("rear-right","Estimated rear-right chassis datum",new(-.9,-.3,.15))])});
        }
        var setups=reference.RootElement.GetProperty("setups").EnumerateArray().ToArray();return TwinsGarageHardware.Configure(library,new(Vehicle(setups.Single(s=>s.GetProperty("id").GetString()=="stock-gr86-2022"),"gr86"),Vehicle(setups.Single(s=>s.GetProperty("id").GetString()=="stock-brz-2022"),"brz")));
    }
}
