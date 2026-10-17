using System.Text.Json;
using System.Text.Json.Nodes;
using SuspensionSimulation.Components;
using SuspensionSimulation.Design;

namespace SuspensionSimulation.Tests;

internal static class DesignContractChecks
{
    private static readonly DesignFrame Origin = new(new(0,0,0),new(0,0,1),new(1,0,0));
    private static RevisionIdentity Identity(string id) => new(id,"revision-1",id,new(DataProvenance.Synthetic,"Contract test only",null));
    private static ComponentDefinition Link() => new(Identity("traction-arm"),ComponentKind.RigidLink,
        [new("inner","Inner",new(0,0,0)),new("outer","Outer",new(.35,.02,-.01))],
        [new("inner-frame","Inner",Origin),new("outer-frame","Outer",Origin with {OriginM=new(.35,.02,-.01)})],
        [new("inboard","inner-frame","chassis",[JointKind.Spherical]),new("outboard","outer-frame","knuckle",[JointKind.Spherical])],
        [new DrivingLength("nominal-length",.351),new MeasuredDistance("end-spacing","inner","outer")],null);
    private static GarageSetup Setup() => new(Identity("car"),new("vehicle"),[],
        [new ContinuousAdjustmentSelection("rear/right/toe","threaded-length",.5),new DiscreteAdjustmentSelection("front/left/mount","hole","inboard")],
        new(MassAccountingPolicy.AggregateBase,1200,new(0,0,.4),[new("driver",80,new(.2,.3,.5))]));

    internal static void RoundTrip()
    {
        var original=Link();DesignValidation.Validate(original);
        var json=DesignJson.Serialize(original);var copy=DesignJson.Deserialize<ComponentDefinition>(json);DesignValidation.Validate(copy);
        if(json!=DesignJson.Serialize(copy) || copy.Points[1].PositionM!=original.Points[1].PositionM || copy.Dimensions[1] is not MeasuredDistance)
            throw new Exception("Component contract lost identity, SI coordinates or dimension ownership.");
        var setup=Setup();DesignValidation.Validate(setup);
        var setupCopy=DesignJson.Deserialize<GarageSetup>(DesignJson.Serialize(setup));DesignValidation.Validate(setupCopy);
        if(setupCopy.Adjustments[0] is not ContinuousAdjustmentSelection {Fraction:.5} || setupCopy.Adjustments[1] is not DiscreteAdjustmentSelection {ChoiceId:"inboard"})
            throw new Exception("Continuous and discrete selections were mixed.");
        if(Math.Abs(DesignConventions.NewtonsPerMetrePerKgfPerMm*.001-9.80665)>1e-12)
            throw new Exception("New design units inherited the approximate workbook conversion.");
    }

    internal static void Ownership()
    {
        Reject<JsonException>(()=>DesignJson.Deserialize<GarageSetup>("{}"));
        var missingMass=JsonNode.Parse(DesignJson.Serialize(Setup()))!.AsObject();missingMass["mass"]=null;
        Reject<JsonException>(()=>DesignJson.Deserialize<GarageSetup>(missingMass.ToJsonString()));
        foreach(var key in new[]{"staticCamberDegrees","staticToeDegrees","linkLengthM","springRateNPerM","preloadN","overrides"})
        {
            var json=JsonNode.Parse(DesignJson.Serialize(Setup()))!.AsObject();json[key]=0;
            Reject<JsonException>(()=>DesignJson.Deserialize<GarageSetup>(json.ToJsonString()));
        }
        var badSelection=JsonNode.Parse(DesignJson.Serialize(Setup()))!.AsObject();
        badSelection["adjustments"]![0]!["hardPointOffsetM"]=.05;
        Reject<JsonException>(()=>DesignJson.Deserialize<GarageSetup>(badSelection.ToJsonString()));
        Reject<ArgumentException>(()=>DesignValidation.Validate(Setup() with {Adjustments=[new ContinuousAdjustmentSelection("rear/right/toe","length",1.01)]}));
        Reject<ArgumentException>(()=>DesignValidation.Validate(Setup() with {Mass=Setup().Mass with {Policy=MassAccountingPolicy.ComponentSum}}));
        DesignValidation.Validate(Setup() with {Mass=Setup().Mass with {Policy=MassAccountingPolicy.ComponentSum,AggregateBaseMassKg=null}});
    }

    internal static void Structure()
    {
        var link=Link();
        Reject<ArgumentException>(()=>DesignValidation.Validate(link with {Points=[link.Points[0],link.Points[0]]}));
        Reject<ArgumentException>(()=>DesignValidation.Validate(link with {Ports=[link.Ports[0] with {FrameId="missing"}]}));
        Reject<ArgumentException>(()=>DesignValidation.Validate(link with {Dimensions=[new MeasuredDistance("length","missing","outer")]}));
        Reject<ArgumentException>(()=>DesignValidation.Frame(Origin with {Axis=new(0,0,0)}));
        Reject<ArgumentException>(()=>DesignValidation.Frame(Origin with {ReferenceDirection=new(0,0,1)}));
        Reject<ArgumentException>(()=>DesignValidation.Frame(Origin with {OriginM=new(double.NaN,0,0)}));
        Reject<ArgumentException>(()=>DesignValidation.Validate(link with {Identity=link.Identity with {SchemaVersion="99"}}));
    }

    internal static void AssemblyAndManifest()
    {
        var placement=new InstancePlacement(Origin,new(new(0,0,0),new(0,1,0)));
        var assembly=new AssemblyDefinition(Identity("rear"),
            [new ComponentInstance("link",new("traction-arm"),placement),new AssemblyInstance("hub",new("hub-group"),new(Origin))],
            [new("outer-ball",JointKind.Spherical,new("link","outboard"),new("hub","ball"))],[new("inboard",new("link","inboard"))]);
        DesignValidation.Validate(assembly);
        var copy=DesignJson.Deserialize<AssemblyDefinition>(DesignJson.Serialize(assembly));DesignValidation.Validate(copy);
        if(copy.Instances[0] is not ComponentInstance || copy.Instances[1] is not AssemblyInstance || copy.Instances[0].Placement.Reflection is null)
            throw new Exception("Nested physical types or explicit reflection were lost.");
        Reject<ArgumentException>(()=>DesignValidation.Validate(assembly with {Instances=[new AssemblyInstance("self",new("rear"),new(Origin))]}));
        Reject<ArgumentException>(()=>DesignValidation.Validate(assembly with {Joints=[assembly.Joints[0] with {First=new("missing","port")}]}));
        var manifest=new FrozenResultManifest("rear",[new(AssetKind.Assembly,"rear","revision-1",new('a',64))]);
        DesignValidation.Validate(manifest);
        Reject<ArgumentException>(()=>DesignValidation.Validate(manifest with {Dependencies=[manifest.Dependencies[0] with {Sha256="bad"}]}));
        Reject<ArgumentException>(()=>DesignValidation.Validate(manifest with {Dependencies=[manifest.Dependencies[0],manifest.Dependencies[0] with {RevisionId="revision-2"}]}));
        Reject<ArgumentException>(()=>DesignValidation.Validate(manifest with {RootAssemblyId="missing"}));
    }

    private static void Reject<T>(Action action) where T:Exception
    {
        try {action();} catch(T) {return;}
        throw new Exception("Expected rejection: "+typeof(T).Name);
    }
}
