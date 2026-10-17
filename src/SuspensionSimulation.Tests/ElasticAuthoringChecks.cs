using System.Collections.Immutable;
using SuspensionSimulation.Design;
using SuspensionSimulation.Components;
using SuspensionSimulation.Solvers.Design;

namespace SuspensionSimulation.Tests;

internal static class ElasticAuthoringChecks
{
    internal static void Verify()
    {
        var spring=new CompressionSpringBehavior("a","b",3,.3,.1);var state=ElasticLaws.Spring(spring,.2);
        Near(state.ForceN,2941.995,1e-8);Near(state.TangentNPerM,29419.95,1e-8);Near(state.EnergyJ,147.09975,1e-8);
        if(!ElasticLaws.Spring(spring,.31).Unseated||ElasticLaws.Spring(spring,.31).ForceN!=0) throw new Exception("A loose spring carries tension.");
        Reject(()=>ElasticLaws.Spring(spring,.099));if(!double.IsFinite(ElasticLaws.Spring(spring,.1).TangentNPerM)) throw new Exception("Coil bind used numeric infinity.");
        var damper=new AxialDamperBehavior("a","b",DamperFamily.Shock,.3,.5,1000,2000,0,new(.03,.07));
        Near(ElasticLaws.DamperForce(damper,.4,0),0,0);Near(ElasticLaws.DamperForce(damper,.4,.1),100,1e-10);Near(ElasticLaws.DamperForce(damper,.4,-.1),-200,1e-10);
        Near(ElasticLaws.SeatSeparation(damper,.4,.02),.28,1e-12);Reject(()=>ElasticLaws.DamperForce(damper,.501,0));
        var bar=new TorsionBarBehavior("a","b",new(0,1,0),79e9,.02,.01,1.1);
        Near(ElasticLaws.Bar(bar,.2,.2).EnergyJ,0,0);Near(ElasticLaws.Bar(bar,0,.1).TorqueNm,bar.StiffnessNmPerRad*.1,1e-10);
        Near(ElasticLaws.Bar(bar with {RestTwistRad=.05},0,.1).TorqueNm,bar.StiffnessNmPerRad*.05,1e-10);
        var curve=new BumpCurveBehavior("a","b",.4,[new(0,0),new(.01,100),new(.03,500)],CurveZero.Contact,"Synthetic analytic curve",new string('a',64));
        var bump=ElasticLaws.Bump(curve,.38);Near(bump.ForceN,300,1e-8);Near(bump.EnergyJ,2.5,1e-8);Near(bump.TangentNPerM,20000,1e-8);Reject(()=>ElasticLaws.Bump(curve,.36));
        var datum=AssemblyAuthoring.IdentityPlacement.Frame;var line=new StraightAdjustmentPath(datum,datum with {OriginM=new(.02,0,0)},PathOrientation.HoldStart);
        Near(AdjustmentPaths.Evaluate(line,new(.25)).OriginM.X,.005,1e-12);Reject(()=>AdjustmentPaths.Evaluate(line,new(1.01)));
        var arc=new ArcAdjustmentPath(new(0,0,0),new(0,0,1),datum with {OriginM=new(.1,0,0)},Math.PI/2,PathOrientation.FollowPath);
        var end=AdjustmentPaths.Evaluate(arc,new(1));Near(end.OriginM.Y,.1,1e-12);Near(end.ReferenceDirection.Y,1,1e-12);
        var discrete=new DiscreteAdjustmentPath([new("inner","Inner",datum),new("outer","Outer",datum with {OriginM=new(.02,0,0)})]);
        Near(AdjustmentPaths.Evaluate(discrete,new(ChoiceId:"outer")).OriginM.X,.02,1e-12);Reject(()=>AdjustmentPaths.Evaluate(discrete,new(.5)));
        var measured=new MeasuredAdjustmentPath([datum,datum with {OriginM=new(.01,0,0)},datum with {OriginM=new(.01,.03,0)}],PathOrientation.HoldStart);
        var middle=AdjustmentPaths.Evaluate(measured,new(.5));Near(middle.OriginM.X,.01,1e-12);Near(middle.OriginM.Y,.01,1e-12);
        var thread=new ThreadedAdjustmentPath(datum,.2,.3,.4,.0015);Near(AdjustmentPaths.Evaluate(thread,new(1)).OriginM.Z,.1,1e-12);
        var eccentric=new EccentricAdjustmentPath(new(0,0,0),new(0,0,1),datum with {OriginM=new(.005,0,0)},0,Math.PI,PathOrientation.HoldStart);Near(AdjustmentPaths.Evaluate(eccentric,new(1)).OriginM.X,-.005,1e-12);
        var identity=new RevisionIdentity("adjustable-link","draft","Adjustable link",new(DataProvenance.Synthetic,"Synthetic physical adjustment tests",null));
        var component=ComponentAuthoring.Create(identity,ComponentKind.RigidLink,new(.3,0,0)) with {Adjustments=[new("thread","Effective length",new ThreadedAdjustmentPath(datum with {Axis=new(1,0,0),ReferenceDirection=new(0,1,0)},.2,.3,.4),new(.5),["outer"],["outer"],"Physical threaded rod; locks before motion.")]};
        var adjusted=AdjustmentPaths.Freeze(component,new Dictionary<string,AdjustmentSetting>{{"thread",new(1)}});
        Near(adjusted.Points.Single(p=>p.Id=="outer").PositionM.X,.4,1e-12);Near(component.Points.Single(p=>p.Id=="outer").PositionM.X,.3,1e-12);Reject(()=>ComponentAuthoring.ValidateForPublish(adjusted));
        var folder=Path.Combine(Path.GetTempPath(),"elastic-authoring-"+Guid.NewGuid().ToString("N"));
        try
        {
            var library=new DesignAssetLibrary(folder);var rigid=library.Publish(component with {Adjustments=null},null);
            var force=ComponentAuthoring.MakeCustom(ComponentAuthoring.Create(identity with {AssetId="spring"},ComponentKind.RigidLink,new(.3,0,0))) with {Kind=ComponentKind.Spring,Behavior=new CompressionSpringBehavior("inner","outer",3,.3,.1)};
            force=library.Publish(force,null);var a=new AssemblyDefinition(identity with {AssetId="spring-assembly"},[new ComponentInstance("support",new(rigid.Identity.AssetId),AssemblyAuthoring.IdentityPlacement),new ComponentInstance("spring",new(force.Identity.AssetId),AssemblyAuthoring.IdentityPlacement)],
                [new("a",JointKind.Spherical,new("support","inner"),new("spring","inner")),new("b",JointKind.Spherical,new("support","outer"),new("spring","outer"))],[]) {GroundedInstances=["support"]};
            var graph=new AssemblyGraphResolver(library).CompileDraft(a);
            if(graph.Bodies.Length!=1||graph.ElasticElements.Length!=1||graph.Joints.Length!=0||graph.Components.Length!=2) throw new Exception("Spring ends became a rigid welded body or lost provenance.");
            Reject(()=>new AssemblyGraphResolver(library).CompileDraft(a with {Joints=a.Joints.RemoveAt(1)}));
            var roundTrip=DesignJson.Deserialize<ComponentDefinition>(DesignJson.Serialize(force));MechanicalValidation.Validate(roundTrip);
        }
        finally {Directory.Delete(folder,true);}
    }
    private static void Near(double a,double b,double tolerance) {if(Math.Abs(a-b)>tolerance) throw new Exception($"Expected {b:R}, got {a:R}.");}
    private static void Reject(Action action) {try {action();}catch(ArgumentException) {return;}catch(InvalidOperationException) {return;}throw new Exception("Invalid mechanical request accepted.");}
}
