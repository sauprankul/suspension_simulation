using SuspensionSimulation.Design;
using SuspensionSimulation.Components;
using SuspensionSimulation.Solvers.Design;

namespace SuspensionSimulation.Tests;

internal static class ScopedAdjustmentChecks
{
    internal static void Verify()
    {
        var folder=Path.Combine(Path.GetTempPath(),"scoped-adjusters-"+Guid.NewGuid().ToString("N"));
        try
        {
            var library=new DesignAssetLibrary(folder);var resolver=new AssemblyGraphResolver(library);var placement=AssemblyAuthoring.IdentityPlacement;
            RevisionIdentity Identity(string id)=>new(id,"draft",id,new(DataProvenance.Synthetic,"Scoped mechanical interface regression",null));
            var fixedPart=library.Publish(ComponentAuthoring.MakeCustom(ComponentAuthoring.Create(Identity("fixed-link"),ComponentKind.RigidLink,new(.3,0,0))) with {AdjustmentParameters=[]},null);
            var datum=new DesignFrame(new(.3,0,0),new(1,0,0),new(0,1,0));
            var threaded=library.Publish(fixedPart with {Identity=Identity("threaded-link"),Parameters=[new("position","Thread position","fraction",0,1)],
                Adjustments=[new("thread","Thread",new ThreadedAdjustmentPath(datum,.2,.3,.4),new(.5),["outer"],["outer"],"Actual threaded end")],AdjustmentParameters=[new("thread","position")]},null);
            var holes=library.Publish(fixedPart with {Identity=Identity("holes-link"),Parameters=[DefinitionParameter.Choice("hole","Mounting hole",[new("short","Short"),new("long","Long")])],
                Adjustments=[new("hole","Actual mounting hole",new DiscreteAdjustmentPath([new("short","Short",datum),new("long","Long",datum with {OriginM=new(.4,0,0)})]),new(ChoiceId:"short"),["outer"],["outer"],"Actual discrete mounting positions")],AdjustmentParameters=[new("hole","hole")]},null);
            var root=new AssemblyDefinition(Identity("root"),[new ComponentInstance("link",new(fixedPart.Identity.AssetId),placement)],[],[]) {ConfigurationVersion=2,
                Parameters=[DefinitionParameter.Choice("hardware","Link hardware",[new("fixed","Fixed"),new("threaded","Threaded"),new("holes","Holes")]),new("lengthSetting","Length setting","fraction",0,1),DefinitionParameter.Choice("mount","Mounting hole",[new("short","Short"),new("long","Long")])],
                EquipmentSlots=[new("link","Link","link",[fixedPart.Identity.AssetId,threaded.Identity.AssetId,holes.Identity.AssetId],"Explicit owning-assembly choices") {ParameterId="hardware",Options=[new("fixed",new(fixedPart.Identity.AssetId),[]),new("threaded",new(threaded.Identity.AssetId),[new("position",FromParameterId:"lengthSetting")]),new("holes",new(holes.Identity.AssetId),[new("hole",FromParameterId:"mount")])]}]};
            ParameterValue[] Values(string hardware,double position=.75,string mount="long")=>[ParameterValue.Choice("hardware",hardware),new("lengthSetting",position),ParameterValue.Choice("mount",mount)];
            root=library.Publish(root,null,Values("fixed"));
            var slot=resolver.Compile(new(root.Identity.AssetId),[],Values("threaded"));
            Near(slot.Bodies.Single().Component.Points.Single(p=>p.Id=="outer").PositionM.X,.35);
            if(slot.ParameterAdjustments.Single().Selection.Fraction!=.75||slot.Components.Length!=3) throw new Exception("Bound positions or frozen approved hardware were lost.");
            var discrete=resolver.Compile(new(root.Identity.AssetId),[],Values("holes"));Near(discrete.Bodies.Single().Component.Points.Single(p=>p.Id=="outer").PositionM.X,.4);
            var fixedGraph=resolver.Compile(new(root.Identity.AssetId),[],Values("fixed"));Near(fixedGraph.Bodies.Single().Component.Points.Single(p=>p.Id=="outer").PositionM.X,.3);
            if(!fixedGraph.ActiveRootParameterIds.SequenceEqual(new[]{"hardware"})||!slot.ActiveRootParameterIds.ToHashSet().SetEquals(new[]{"hardware","lengthSetting"})||!discrete.ActiveRootParameterIds.ToHashSet().SetEquals(new[]{"hardware","mount"})) throw new Exception("Inactive alternate hardware settings were presented as active.");
            Reject(()=>resolver.Compile(new(root.Identity.AssetId),[new("link","link",new(threaded.Identity.AssetId))],Values("fixed")));
            Reject(()=>resolver.Compile(new(root.Identity.AssetId),[],Values("holes",mount:"fictional")));
            Reject(()=>resolver.Compile(new(root.Identity.AssetId),[],[new("hardware",1),new("lengthSetting",.5),ParameterValue.Choice("mount","long")]));
            Reject(()=>GraphAdjustments.Freeze(slot,[new ContinuousAdjustmentSelection("link","thread",.25)]));
            var frozen=GraphAdjustments.Freeze(slot,[]);Near(frozen.Bodies.Single().Component.Points.Single(p=>p.Id=="outer").PositionM.X,.35);
            var replay=AssemblyGraphResolver.OpenBundle(DesignJson.Deserialize<PortableDesignBundle>(DesignJson.Serialize(AssemblyGraphResolver.Export(slot))));Near(replay.Bodies.Single().Component.Points.Single(p=>p.Id=="outer").PositionM.X,.35);
            var clone=library.ImportClone(AssemblyGraphResolver.Export(discrete));var cloned=resolver.Compile(new(clone.Identity.AssetId),[],Values("threaded"));Near(cloned.Bodies.Single().Component.Points.Single(p=>p.Id=="outer").PositionM.X,.35);
            var wrapper=library.Publish(new AssemblyDefinition(Identity("legacy-wrapper"),[new AssemblyInstance("child",new(root.Identity.AssetId),placement) {ParameterValues=[..root.Parameters!.Value.Select(p=>new ParameterAssignment(p.Id,FromParameterId:p.Id))]}],[],[]) {Parameters=root.Parameters},null,Values("holes"));
            var wrapped=resolver.Compile(new(wrapper.Identity.AssetId),[],Values("holes"));var wrappedClone=library.ImportClone(AssemblyGraphResolver.Export(wrapped));Near(resolver.Compile(new(wrappedClone.Identity.AssetId),[],Values("threaded")).Bodies.Single().Component.Points.Single(p=>p.Id=="outer").PositionM.X,.35);
            var grouped=AssemblyAuthoring.Group(root,["link"],Identity("group"),"group");library.Publish(grouped.Child,null,Values("holes"));var groupGraph=resolver.CompileDraft(grouped.Parent,Values("holes"));Near(groupGraph.Bodies.Single().Component.Points.Single(p=>p.Id=="outer").PositionM.X,.4);
            var mirrored=AssemblyAuthoring.MirrorInstance(root,"link","mirror",new(new(0,0,0),new(0,1,0)));var mirrorGraph=resolver.CompileDraft(mirrored,Values("threaded"));if(mirrorGraph.ParameterAdjustments.Length!=2) throw new Exception("Mirroring lost the declared equipment/configuration interface.");
            var locked=root with {EquipmentSlots=[root.EquipmentSlots!.Value.Single() with {ParameterId=null}]};var lockedGraph=resolver.CompileDraft(locked,Values("holes"));if(lockedGraph.Bodies.Single().Component.Identity.AssetId!=fixedPart.Identity.AssetId) throw new Exception("An owner property was overridden by an unrelated choice value.");
            Reject(()=>library.Publish(holes with {Identity=Identity("bad-holes"),Parameters=[DefinitionParameter.Choice("hole","Hole",[new("fake","Fake")])]},null));
            var broken=root.EquipmentSlots!.Value.Single() with {Options=[..root.EquipmentSlots.Value.Single().Options!.Value.Select(o=>o.ChoiceId=="threaded"?o with {ParameterValues=[]}:o)]};
            Reject(()=>library.Publish(root with {Identity=Identity("bad-option"),EquipmentSlots=[broken]},null,Values("fixed")));
        }
        finally {if(Directory.Exists(folder)) Directory.Delete(folder,true);}
    }
    private static void Near(double value,double expected) {if(Math.Abs(value-expected)>1e-12) throw new Exception($"Expected {expected:R}, got {value:R}.");}
    private static void Reject(Action action) {try {action();}catch(ArgumentException) {return;}catch(InvalidOperationException) {return;}throw new Exception("Invalid scoped mechanical interface accepted.");}
}
