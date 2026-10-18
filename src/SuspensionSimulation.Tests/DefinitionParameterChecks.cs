using SuspensionSimulation.Design;
using SuspensionSimulation.Components;
using SuspensionSimulation.Solvers.Design;

namespace SuspensionSimulation.Tests;

internal static class DefinitionParameterChecks
{
    internal static void Verify()
    {
        var folder=Path.Combine(Path.GetTempPath(),"parameter-ownership-"+Guid.NewGuid().ToString("N"));
        try
        {
            var library=new DesignAssetLibrary(folder);var resolver=new AssemblyGraphResolver(library);
            RevisionIdentity Identity(string id)=>new(id,"draft",id,new(DataProvenance.Synthetic,"Ownership regression fixture",null));
            var placement=AssemblyAuthoring.IdentityPlacement;
            var support=library.Publish(ComponentAuthoring.Create(Identity("support"),ComponentKind.RigidLink,new(.3,0,0)),null);
            var spring=ComponentAuthoring.MakeCustom(support) with {Identity=Identity("spring"),Kind=ComponentKind.Spring,Behavior=null,
                Parameters=[new("rate","Spring rate","kgf/mm",1,10)],MechanicalTemplate=new SpringMechanicalTemplate("inner","outer",new NumericParameterReference("rate"),.3,.1)};
            spring=library.Publish(spring,null);
            var pack=new AssemblyDefinition(Identity("pack"),[new ComponentInstance("support",new(support.Identity.AssetId),placement),
                new ComponentInstance("spring",new(spring.Identity.AssetId),placement) {ParameterValues=[new("rate",FromParameterId:"stiffness")]}],
                [new("a",JointKind.Spherical,new("support","inner"),new("spring","inner")),new("b",JointKind.Spherical,new("support","outer"),new("spring","outer"))],[])
                {Parameters=[new("stiffness","Pack stiffness","kgf/mm",2,8)]};
            Reject(()=>library.Publish(pack,null)); // Own declarations never pick a default at publication.
            pack=library.Publish(pack,null,[new("stiffness",3)]);
            var axle=new AssemblyDefinition(Identity("axle"),[
                new AssemblyInstance("left",new(pack.Identity.AssetId),placement) {ParameterValues=[new("stiffness",FromParameterId:"leftRate")]},
                new AssemblyInstance("right",new(pack.Identity.AssetId),placement) {ParameterValues=[new("stiffness",6)]}],[],[])
                {Parameters=[new("leftRate","Left rate","kgf/mm",2,8)]};
            axle=library.Publish(axle,null,[new("leftRate",4)]);
            var car=new AssemblyDefinition(Identity("car"),[new AssemblyInstance("axle",new(axle.Identity.AssetId),placement) {ParameterValues=[new("leftRate",FromParameterId:"frontRate")]}],[],[])
                {Parameters=[new("frontRate","Front rate","kgf/mm",2,8)]};
            car=library.Publish(car,null,[new("frontRate",4)]);
            var graph=resolver.Compile(new(car.Identity.AssetId),[],[new("frontRate",4)]);
            double Rate(ResolvedAssemblyGraph g,string side)=>((CompressionSpringBehavior)g.ElasticElements.Single(e=>e.Path=="axle/"+side+"/spring").Component.Behavior!).RateKgfPerMm;
            if(Rate(graph,"left")!=4||Rate(graph,"right")!=6) throw new Exception("Immediate-owner forwarding/literal configuration changed.");
            var another=resolver.Compile(new(car.Identity.AssetId),[],[new("frontRate",7)]);
            if(Rate(another,"left")!=7||Rate(another,"right")!=6||Rate(graph,"left")!=4) throw new Exception("Configuration leaked between importers or overwrote a parent's literal.");
            Reject(()=>resolver.Compile(new(car.Identity.AssetId)));
            Reject(()=>resolver.Compile(new(car.Identity.AssetId),[],[new("frontRate",9)]));
            Reject(()=>resolver.Compile(new(car.Identity.AssetId),[],[new("frontRate",4),new("axle.right.stiffness",3)]));
            Reject(()=>resolver.CompileDraft(axle with {Instances=axle.Instances.SetItem(0,axle.Instances[0] with {ParameterValues=[new("stiffness",FromParameterId:"grandparent")]})},[new("leftRate",4)]));
            Reject(()=>resolver.CompileDraft(axle with {Parameters=[new("leftRate","Wrong unit","Pa",2,8)]},[new("leftRate",4)]));
            Reject(()=>resolver.CompileDraft(axle with {Parameters=[new("leftRate","Too wide","kgf/mm",1,9)]},[new("leftRate",4)]));
            Reject(()=>resolver.CompileDraft(pack with {Instances=pack.Instances.SetItem(1,pack.Instances[1] with {ParameterValues=[new("rate",3,"stiffness")]})},[new("stiffness",4)]));
            var propertySpring=spring with {Identity=Identity("fixed-spring"),Parameters=null,MechanicalTemplate=new SpringMechanicalTemplate("inner","outer",new NumericProperty(5),.3,.1)};
            propertySpring=library.Publish(propertySpring,null);
            Reject(()=>resolver.CompileDraft(pack with {Instances=pack.Instances.SetItem(1,pack.Instances[1] with {Definition=new(propertySpring.Identity.AssetId)})},[new("stiffness",4)]));
            var bundle=AssemblyGraphResolver.Export(graph);var replay=AssemblyGraphResolver.OpenBundle(DesignJson.Deserialize<PortableDesignBundle>(DesignJson.Serialize(bundle)));
            if(Rate(replay,"left")!=4||Rate(replay,"right")!=6||DesignJson.Serialize(replay.ParameterConfigurations)!=DesignJson.Serialize(graph.ParameterConfigurations)) throw new Exception("Frozen bundle lost parameter configuration chain.");
            var imported=library.ImportClone(bundle);var importedGraph=resolver.Compile(new(imported.Identity.AssetId),[],[new("frontRate",4)]);
            if(Rate(importedGraph,"left")!=4||Rate(importedGraph,"right")!=6) throw new Exception("Independent clone lost import configuration.");
            var grouped=AssemblyAuthoring.Group(axle,["left"],Identity("group"),"group");library.Publish(grouped.Child,null,[new("leftRate",4)]);
            var groupGraph=resolver.CompileDraft(grouped.Parent,[new("leftRate",4)]);
            if(((CompressionSpringBehavior)groupGraph.ElasticElements.Single(e=>e.Path=="group/left/spring").Component.Behavior!).RateKgfPerMm!=4) throw new Exception("Grouping lost forwarding across the new boundary.");
            var material=EstimatedBumperMaterials.Catalog().Single(m=>m.Id=="printed-tpu-95");
            var bumper=ElasticComponentFactory.ImportedBumper(Identity("bumper"),new("compression,force\n0,0\n10,100",CurveDisplacementUnit.Millimetres,CurveForceUnit.Newtons,CurveZero.Contact,.1,"Known test conditions","Measured curve fixture",DataProvenance.Synthetic)) with {BumperMaterial=material};
            bumper=library.Publish(bumper,null);
            if(DesignJson.Serialize(DesignJson.Deserialize<ComponentDefinition>(DesignJson.Serialize(bumper)).BumperMaterial)!=DesignJson.Serialize(material)) throw new Exception("Material property snapshot did not round-trip.");
            Reject(()=>MechanicalTemplates.Resolve(bumper,[new("material",95)]));
            Reject(()=>ComponentAuthoring.ValidateForPublish(bumper with {Parameters=[new("material","Material","Pa",1,100)]}));
            if(DesignJson.Serialize(spring).Contains("parameterValues",StringComparison.Ordinal)) throw new Exception("Source component stored its own selected configuration.");
        }
        finally {if(Directory.Exists(folder)) Directory.Delete(folder,true);}
    }
    private static void Reject(Action action)
    {try {action();}catch(ArgumentException) {return;}catch(InvalidOperationException) {return;}throw new Exception("Invalid ownership/configuration accepted.");}
}
