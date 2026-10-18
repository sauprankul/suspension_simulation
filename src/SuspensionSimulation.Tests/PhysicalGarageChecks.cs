using System.Collections.Immutable;
using SuspensionSimulation.Design;
using SuspensionSimulation.Solvers.Design;
using SuspensionSimulation.Solvers.HeadToHead;

namespace SuspensionSimulation.Tests;

internal static class PhysicalGarageChecks
{
    internal static void Verify()
    {
        var repository=AppContext.BaseDirectory;while(!Directory.Exists(Path.Combine(repository,"data"))) repository=Directory.GetParent(repository)?.FullName??throw new Exception("Repository missing.");
        var folder=Path.Combine(Path.GetTempPath(),"physical-garage-"+Guid.NewGuid().ToString("N"));
        try
        {
            var library=new DesignAssetLibrary(Path.Combine(folder,"design"));var garage=new GarageLibrary(Path.Combine(folder,"garage"));
            new ReferenceGarageCatalog(Path.Combine(repository,"data/reference/stock-86-brz/reference-sheet.json")).Seed(garage);
            var catalog=new PhysicalGarageCatalog(library,repository);catalog.Migrate(garage);catalog.Migrate(garage);
            if(garage.List().Any(e=>e.Revision!=2||e.Configuration is null)) throw new Exception("Migration must append once: "+DesignJson.Serialize(catalog.MigrationConflicts));
            var resolver=new GarageSetupResolver(library);var stock=garage.Load(ReferenceGarageCatalog.Gr86Id)!;var brz=garage.Load(ReferenceGarageCatalog.BrzId)!;
            var graph=resolver.Resolve(stock.Configuration!);var graphB=resolver.Resolve(brz.Configuration!);
            if(graph.Root.ConfigurationVersion!=2||graph.EquipmentSlots.Length!=18||!graph.SetupAdjustments.IsEmpty||graphB.Root.Identity.AssetId==graph.Root.Identity.AssetId||!stock.Configuration!.Equipment.IsEmpty||!stock.Configuration.Adjustments.IsEmpty||stock.Configuration.MechanicalValues is not null) throw new Exception("The complete-car scoped interface did not replace descendant overrides.");
            if(DesignJson.Serialize(catalog.Migrate(stock))!=DesignJson.Serialize(stock.Configuration)) throw new Exception("Scoped migration is not idempotent.");
            var owned=stock.Configuration.ParameterValues!.Value;
            if(owned.Count(v=>v.ParameterId.EndsWith(".rate",StringComparison.Ordinal))!=4||owned.Count(v=>v.ParameterId.EndsWith(".shear",StringComparison.Ordinal))!=2||owned.Count(v=>v.ChoiceId is not null)!=18) throw new Exception("Stock root lost explicit spring/bar or equipment values.");
            GarageSetup Number(GarageSetup s,string id,double value)=>s with {ParameterValues=[..s.ParameterValues!.Value.Select(v=>v.ParameterId==id?new ParameterValue(id,value):v)]};
            GarageSetup Choice(GarageSetup s,string id,string value)=>s with {ParameterValues=[..s.ParameterValues!.Value.Select(v=>v.ParameterId==id?ParameterValue.Choice(id,value):v)]};
            var custom=garage.Duplicate(stock);var setup=custom.Configuration!;
            setup=Choice(setup,"equipment.FL-damper","example-adjustable-front-damper");setup=Choice(setup,"front.left.equipment.top-mount","example-camber-plate");
            setup=Number(setup,"FL-damper.adjustment.perch",.75);setup=Number(setup,"front.left.top-mount.adjustment.camber-slot",.6);
            setup=setup with {Mass=setup.Mass with {AddedPayloads=[new("ballast",20,new(-.8,0,.2))]}};
            var configured=resolver.Resolve(setup);
            if(Math.Abs(GraphAdjustments.ThreadedOffset(configured,"FL-damper","perch")-.01)>1e-12||configured.ParameterAdjustments.Length!=2) throw new Exception("Scoped mechanical positions did not move their actual hardware.");
            var alignment=Choice(stock.Configuration,"front.equipment.left-tie","example-left-tie");alignment=Choice(alignment,"rear-left.equipment.lower","example-rear-lower");alignment=Choice(alignment,"rear-left.equipment.toe","example-rear-toe");
            alignment=Number(alignment,"front.left-tie.adjustment.length",.6);alignment=Number(alignment,"rear-left.lower.adjustment.length",.6);alignment=Number(alignment,"rear-left.rear-toe.adjustment.length",.6);
            var neutral=new TwinsKinematics(graph).Solve(FourCornerTravel.Zero,0);var aligned=new TwinsKinematics(resolver.Resolve(alignment)).Solve(FourCornerTravel.Zero,0);
            if(Math.Abs(aligned.Wheels[0].HeadingRad-neutral.Wheels[0].HeadingRad)<1e-4||Math.Abs(aligned.Wheels[2].CamberRad-neutral.Wheels[2].CamberRad)<1e-4||Math.Abs(aligned.Wheels[2].HeadingRad-neutral.Wheels[2].HeadingRad)<1e-4||Math.Abs(aligned.Wheels[1].HeadingRad-neutral.Wheels[1].HeadingRad)>1e-7||Math.Abs(aligned.Wheels[3].CamberRad-neutral.Wheels[3].CamberRad)>1e-7) throw new Exception("Scoped alignment settings did not independently affect the actual selected corners.");
            setup=Number(setup,"FL-spring.rate",owned.Single(v=>v.ParameterId=="FL-spring.rate").Value*1.4);
            custom=garage.Save(custom with {Configuration=setup,Name="My physical car"});var reopened=new GarageLibrary(garage.DirectoryPath).Load(custom.Id)!;configured=resolver.Resolve(reopened.Configuration!);
            if(reopened.Configuration!.Mass.AddedPayloads.Single().MassKg!=20||DesignJson.Serialize(garage.Load(stock.Id)!.Configuration!.ParameterValues)!=DesignJson.Serialize(owned)||garage.Load(brz.Id)!.Revision!=2) throw new Exception("Configuration leaked between saved cars.");
            var source=new AssemblyGraphResolver(library).Compile(setup.RootAssembly,[],setup.ParameterValues!.Value);var bundle=AssemblyGraphResolver.Export(source);var replay=GarageSetupResolver.Freeze(AssemblyGraphResolver.OpenBundle(DesignJson.Deserialize<PortableDesignBundle>(DesignJson.Serialize(bundle))),reopened.Configuration);
            var zero=new double[new ConstraintMechanism(configured).CoordinateCount];
            if(DesignJson.Serialize(new InstalledMechanics(new ConstraintMechanism(configured)).Evaluate(zero,false))!=DesignJson.Serialize(new InstalledMechanics(new ConstraintMechanism(replay)).Evaluate(zero,false))) throw new Exception("Frozen configured hardware/settings did not replay exact force/energy.");
            var imported=library.ImportClone(bundle);var importedGraph=new AssemblyGraphResolver(library).Compile(new(imported.Identity.AssetId),[],setup.ParameterValues.Value);
            if(importedGraph.EquipmentSlots.Length!=18||importedGraph.ParameterAdjustments.Length!=2) throw new Exception("Portable independent clone lost scoped hardware/settings.");
            void Reject(GarageSetup invalid) {try {resolver.Resolve(invalid);}catch(Exception error) when(error is ArgumentException or InvalidOperationException) {return;}throw new Exception("Forbidden configuration was accepted.");}
            Reject(setup with {Equipment=[new("FL-damper","FL-damper",new("example-adjustable-front-damper"))]});Reject(setup with {Adjustments=[new ContinuousAdjustmentSelection("FL-damper","perch",.5)]});
            Reject(Choice(setup,"equipment.FL-damper","unapproved-part"));Reject(Number(setup,"FL-damper.adjustment.perch",1.01));
            Reject(setup with {ParameterValues=setup.ParameterValues.Value.Add(new("front.left.top-mount.material",95))});Reject(setup with {ParameterValues=setup.ParameterValues.Value.RemoveAt(0)});Reject(setup with {ParameterValues=setup.ParameterValues.Value.Add(setup.ParameterValues.Value[0])});
            Reject(setup with {Mass=setup.Mass with {Policy=MassAccountingPolicy.ComponentSum}});Reject(setup with {Mass=setup.Mass with {Policy=MassAccountingPolicy.ComponentSum,AggregateBaseMassKg=null}});
            VerifyLegacyMigration(library,catalog,stock);
            var mountId=configured.Bodies.Single(b=>b.Path=="front/left/top-mount").Component.Identity.AssetId;var mount=library.LoadComponent(mountId)!;
            library.Publish(mount with {Adjustments=[],AdjustmentParameters=[],Parameters=null},mount.Identity.RevisionId);Reject(setup);
        }
        finally {if(Directory.Exists(folder)) Directory.Delete(folder,true);}
    }
    private static void VerifyLegacyMigration(DesignAssetLibrary library,PhysicalGarageCatalog catalog,GarageEntry stock)
    {
        var legacyRoot=catalog.Models.Gr86;var original=new AssemblyGraphResolver(library).Compile(new(legacyRoot.Identity.AssetId));
        var mechanical=GarageMechanicalConfiguration.Capture(original).Select(v=>v is InstanceSpringRate s&&s.InstancePath=="FL-spring"?s with {RateKgfPerMm=s.RateKgfPerMm*1.2}:v is InstanceTorsionSection t?t with {ShearModulusPa=t.ShearModulusPa*.8}:v).ToImmutableArray();
        var legacy=stock.Configuration! with {RootAssembly=new(legacyRoot.Identity.AssetId),ParameterValues=null,MechanicalValues=mechanical,
            Equipment=[new("FL-damper","FL-damper",new("example-adjustable-front-damper")),new("front/left/top-mount","top-mount",new("example-camber-plate"))],
            Adjustments=[new ContinuousAdjustmentSelection("FL-damper","perch",.75),new ContinuousAdjustmentSelection("front/left/top-mount","camber-slot",.6)]};
        var before=new GarageSetupResolver(library).Resolve(legacy);var migrated=catalog.Migrate(stock with {Configuration=legacy});var after=new GarageSetupResolver(library).Resolve(migrated);
        if(!migrated.Adjustments.IsEmpty||!migrated.Equipment.IsEmpty||migrated.MechanicalValues is not null||migrated.ParameterValues is null) throw new Exception("Legacy descendant arrays were retained after migration.");
        foreach(var body in before.Bodies)
        {var next=after.Bodies.Single(b=>b.Path==body.Path);foreach(var point in body.Component.Points) if((point.PositionM-next.Component.Points.Single(p=>p.Id==point.Id).PositionM).Length>1e-12) throw new Exception("Migration changed accepted installed geometry: "+body.Path+":"+point.Id);}
        foreach(var element in before.ElasticElements)
        {var next=after.ElasticElements.Single(e=>e.Path==element.Path);if(DesignJson.Serialize(element.Component.Behavior)!=DesignJson.Serialize(next.Component.Behavior)) throw new Exception("Migration changed configured force law: "+element.Path);}
        if(library.LoadAssembly(legacyRoot.Identity.AssetId)!.Identity.RevisionId!=legacyRoot.Identity.RevisionId||library.LoadComponent("example-camber-plate")!.AdjustmentParameters is not null) throw new Exception("Migration rewrote original source assets.");
        var oldDamper=library.LoadComponent("example-adjustable-front-damper")!;var revised=library.Publish(oldDamper with {Identity=oldDamper.Identity with {Name=oldDamper.Identity.Name+" revised"}},oldDamper.Identity.RevisionId);
        var pinned=legacy with {Equipment=[..legacy.Equipment.Select(e=>e.InstancePath=="FL-damper"?e with {Component=e.Component with {RevisionId=oldDamper.Identity.RevisionId}}:e)]};
        var pinnedSetup=catalog.Migrate(stock with {Configuration=pinned});var pinnedGraph=new GarageSetupResolver(library).Resolve(pinnedSetup);
        if(pinnedGraph.EquipmentSlots.Single(s=>s.InstancePath=="FL-damper").Selected.RevisionId is null) throw new Exception("Pinned legacy hardware became a latest reference.");
        library.Publish(oldDamper,revised.Identity.RevisionId);
        var frozen=AssemblyGraphResolver.ExportConfigured(new AssemblyGraphResolver(library).Compile(legacy.RootAssembly,legacy.Equipment));_=GarageSetupResolver.Freeze(AssemblyGraphResolver.OpenBundle(frozen),legacy);
    }
}
