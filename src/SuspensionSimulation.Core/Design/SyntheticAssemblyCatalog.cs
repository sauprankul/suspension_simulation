using System.Collections.Immutable;
using SuspensionSimulation.Components;

namespace SuspensionSimulation.Design;

/// <summary>Analytic mobility fixture, never claimed as measured GR86 hardware.</summary>
public static class SyntheticAssemblyCatalog
{
    public const string CornerId="synthetic-double-wishbone-corner";
    private static RevisionIdentity Identity(string id,string name)=>new(id,"draft",name,new(DataProvenance.Synthetic,"Synthetic analytic mechanism for authoring and mobility checks. Not a production vehicle.",null));
    private static readonly ImmutableArray<JointKind> All=[..Enum.GetValues<JointKind>()];
    public static AssemblyDefinition SeedCorner(DesignAssetLibrary library)
    {
        var existing=library.LoadAssembly(CornerId);if(existing is not null) return existing;
        ComponentDefinition Part(string id,ComponentKind kind,params (string Id,DesignVector Point,DesignVector Axis)[] ports)
        {
            var points=ports.Select(x=>new NamedDesignPoint(x.Id,x.Id,x.Point)).ToImmutableArray();
            var frames=ports.Select(x=>new NamedDesignFrame(x.Id,x.Id,new(x.Point,x.Axis,new(1,0,0)))).ToImmutableArray();
            var c=new ComponentDefinition(Identity(id,id),kind,points,frames,[..ports.Select(x=>new AttachmentPort(x.Id,x.Id,x.Id,All))],[],null)
                {VisualEdges=[..ports.Skip(1).Select(x=>new VisualEdge(x.Id,ports[0].Id,x.Id))]};
            return library.LoadComponent(id)??library.Publish(c,null);
        }
        var chassis=Part("analytic-chassis",ComponentKind.Structure,("lower",new(0,0,0),new(0,1,0)),("upper",new(0,0,.4),new(0,1,0)),("toe",new(0,-.2,.2),new(0,0,1)));
        var arm=Part("analytic-arm",ComponentKind.Wishbone,("inner",new(0,0,0),new(0,1,0)),("outer",new(.3,0,0),new(0,0,1)));
        var upright=Part("analytic-upright",ComponentKind.Upright,("lower",new(0,0,-.2),new(0,0,1)),("upper",new(0,0,.2),new(0,0,1)),("toe",new(-.1,.05,0),new(0,0,1)));
        InstancePlacement At(double x,double y,double z)=>new(AssemblyAuthoring.IdentityPlacement.Frame with {OriginM=new(x,y,z)});
        var a=new AssemblyDefinition(Identity(CornerId,"Synthetic double-wishbone corner"),
            [new ComponentInstance("chassis",new(chassis.Identity.AssetId),At(0,0,0)),new ComponentInstance("lower",new(arm.Identity.AssetId),At(0,0,0)),
             new ComponentInstance("upper",new(arm.Identity.AssetId),At(0,0,.4)),new ComponentInstance("upright",new(upright.Identity.AssetId),At(.3,0,.2))],
            [new("lower-hinge",JointKind.Revolute,new("chassis","lower"),new("lower","inner")),new("upper-hinge",JointKind.Revolute,new("chassis","upper"),new("upper","inner")),
             new("lower-ball",JointKind.Spherical,new("lower","outer"),new("upright","lower")),new("upper-ball",JointKind.Spherical,new("upper","outer"),new("upright","upper")),
             new("toe-length",JointKind.RigidDistance,new("chassis","toe"),new("upright","toe"))],
            [new("wheel",new("upright","lower"))]) {GroundedInstances=["chassis"]};
        return library.Publish(a,null);
    }
}
