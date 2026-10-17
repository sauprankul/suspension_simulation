using SuspensionSimulation.Components;

namespace SuspensionSimulation.Design;

public static class ElasticComponentFactory
{
    private static ComponentDefinition Axial(RevisionIdentity identity,ComponentKind kind,double length,MechanicalBehavior behavior,System.Collections.Immutable.ImmutableArray<GeometryAdjustment>? adjustments=null)
    {
        var a=new DesignVector(0,0,0);var b=new DesignVector(0,0,length);var frame=new DesignFrame(a,new(0,0,1),new(1,0,0));
        var joint=behavior is TorsionBarBehavior?JointKind.Fixed:JointKind.Spherical;
        var c=new ComponentDefinition(identity,kind,[new("first","First attachment",a),new("second","Second attachment",b)],
            [new("first","First attachment",frame),new("second","Second attachment",frame with {OriginM=b})],
            [new("first","first","first",[joint]),new("second","second","second",[joint])],[],null) {VisualEdges=[new("element","first","second")],Behavior=behavior,Adjustments=adjustments};
        ComponentAuthoring.ValidateForPublish(c);return c;
    }
    public static ComponentDefinition Spring(RevisionIdentity identity,double rateKgfPerMm,double freeLengthM,double blockLengthM)=>
        Axial(identity,ComponentKind.Spring,freeLengthM,new CompressionSpringBehavior("first","second",rateKgfPerMm,freeLengthM,blockLengthM));
    public static ComponentDefinition Damper(RevisionIdentity identity,AxialDamperBehavior behavior,System.Collections.Immutable.ImmutableArray<GeometryAdjustment>? adjustments=null)=>Axial(identity,ComponentKind.Damper,behavior.MaximumEyeLengthM,behavior with {FirstPortId="first",SecondPortId="second"},adjustments);
    public static ComponentDefinition Bar(RevisionIdentity identity,TorsionBarBehavior behavior)
    {
        var component=Axial(identity,ComponentKind.AntiRollBar,behavior.TorsionLengthM,behavior with {FirstPortId="first",SecondPortId="second"});
        var axis=behavior.UnitAxis;var reference=Math.Abs(axis.X)<.9?new DesignVector(1,0,0):new DesignVector(0,1,0);reference-=axis*reference.Dot(axis);reference/=reference.Length;
        var end=axis*behavior.TorsionLengthM;
        return component with {Points=component.Points.SetItem(1,component.Points[1] with {PositionM=end}),Frames=[new("first","First end",new(new(0,0,0),axis,reference)),new("second","Second end",new(end,axis,reference))]};
    }
}
