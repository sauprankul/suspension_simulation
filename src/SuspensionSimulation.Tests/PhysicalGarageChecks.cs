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
            var legacy=new ReferenceGarageCatalog(Path.Combine(repository,"data/reference/stock-86-brz/reference-sheet.json"));legacy.Seed(garage);
            var catalog=new PhysicalGarageCatalog(library,repository);catalog.Migrate(garage);catalog.Migrate(garage);
            if(garage.List().Any(e=>e.Revision!=2||e.Configuration is null)) throw new Exception("Physical migration did not append exactly once: "+DesignJson.Serialize(catalog.MigrationConflicts));
            var resolver=new GarageSetupResolver(library);var stock=garage.Load(ReferenceGarageCatalog.Gr86Id)!;var brz=garage.Load(ReferenceGarageCatalog.BrzId)!;
            var graph=resolver.Resolve(stock.Configuration!);var graphB=resolver.Resolve(brz.Configuration!);
            VerifyCarOwnedValues(library,garage,catalog,stock,graph,resolver);
            if(graph.EquipmentSlots.Length!=18||graph.SetupAdjustments.Length!=0||graphB.Root.Identity.AssetId==graph.Root.Identity.AssetId) throw new Exception("Stock hardware setup exposure failed.");
            var custom=garage.Duplicate(stock);var setup=custom.Configuration! with {Equipment=[..custom.Configuration!.Equipment,new("FL-damper","FL-damper",new("example-adjustable-front-damper")),new("front/left/top-mount","top-mount",new("example-camber-plate"))],
                Adjustments=[new ContinuousAdjustmentSelection("FL-damper","perch",.75),new ContinuousAdjustmentSelection("front/left/top-mount","camber-slot",.6)],Mass=custom.Configuration.Mass with {AddedPayloads=[new("ballast",20,new(-.8,0,.2))]}};
            var configured=resolver.Resolve(setup);if(configured.SetupAdjustments.Length!=2||Math.Abs(GraphAdjustments.ThreadedOffset(configured,"FL-damper","perch")-.01)>1e-12) throw new Exception("Installed adjusters did not resolve from selected hardware.");
            var alignment=stock.Configuration! with {Equipment=[..stock.Configuration!.Equipment,new("front/left-tie","left-tie",new("example-left-tie")),new("rear-left/lower","lower",new("example-rear-lower")),new("rear-left/rear-toe","toe",new("example-rear-toe"))],
                Adjustments=[new ContinuousAdjustmentSelection("front/left-tie","length",.6),new ContinuousAdjustmentSelection("rear-left/lower","length",.6),new ContinuousAdjustmentSelection("rear-left/rear-toe","length",.6)]};
            var neutral=new TwinsKinematics(graph).Solve(FourCornerTravel.Zero,0);var aligned=new TwinsKinematics(resolver.Resolve(alignment)).Solve(FourCornerTravel.Zero,0);
            if(Math.Abs(aligned.Wheels[0].HeadingRad-neutral.Wheels[0].HeadingRad)<1e-4||Math.Abs(aligned.Wheels[2].CamberRad-neutral.Wheels[2].CamberRad)<1e-4||Math.Abs(aligned.Wheels[2].HeadingRad-neutral.Wheels[2].HeadingRad)<1e-4||Math.Abs(aligned.Wheels[1].HeadingRad-neutral.Wheels[1].HeadingRad)>1e-7||Math.Abs(aligned.Wheels[3].CamberRad-neutral.Wheels[3].CamberRad)>1e-7) throw new Exception("Hardware alignment adjustment did not change the selected front/rear corners independently.");
            custom=garage.Save(custom with {Configuration=setup,Name="My physical car"});
            var reopened=new GarageLibrary(garage.DirectoryPath).Load(custom.Id)!;
            if(reopened.Configuration!.Mass.AddedPayloads.Single().MassKg!=20||garage.Load(stock.Id)!.Configuration!.Adjustments.Length!=0||garage.Load(brz.Id)!.Revision!=2) throw new Exception("Garage setup changes leaked across entries.");
            void Reject(GarageSetup invalid)
            {try {resolver.Resolve(invalid);}catch(Exception error) when(error is ArgumentException or InvalidOperationException) {return;}throw new Exception("Invalid or unexposed Garage override was accepted.");}
            Reject(setup with {Equipment=[new("FL-damper","FL-damper",new("installed-gr86-front-spring"))]});
            Reject(setup with {Equipment=[new("front/left/upright","not-a-slot",new("twins-front-upright"))]});
            Reject(stock.Configuration! with {Adjustments=[new ContinuousAdjustmentSelection("front/left/top-mount","camber-slot",.5)]});
            Reject(setup with {Adjustments=[new ContinuousAdjustmentSelection("FL-damper","perch",1.01)]});
            Reject(setup with {Mass=setup.Mass with {Policy=MassAccountingPolicy.ComponentSum}});
            Reject(setup with {Mass=setup.Mass with {Policy=MassAccountingPolicy.ComponentSum,AggregateBaseMassKg=null}});
            var source=new AssemblyGraphResolver(library).Compile(stock.Configuration!.RootAssembly,[],stock.Configuration.ParameterValues??[]);var exported=AssemblyGraphResolver.Export(source);
            var imported=library.ImportClone(exported);var importedGraph=new AssemblyGraphResolver(library).Compile(new(imported.Identity.AssetId),[],stock.Configuration.ParameterValues??[]);
            if(importedGraph.EquipmentSlots.Length!=18) throw new Exception("Independent import lost or invalidated equipment interfaces.");
            var mount=library.LoadComponent("example-camber-plate")!;library.Publish(mount with {Adjustments=[]},mount.Identity.RevisionId);
            Reject(setup); // Saved settings must not silently disappear after a hardware revision.
        }
        finally {if(Directory.Exists(folder)) Directory.Delete(folder,true);}
    }
    private static void VerifyCarOwnedValues(DesignAssetLibrary library,GarageLibrary garage,PhysicalGarageCatalog catalog,GarageEntry stock,ResolvedAssemblyGraph graph,GarageSetupResolver resolver)
    {
        var owned=stock.Configuration!.ParameterValues??throw new Exception("Migrated car has no root configuration.");
        if(owned.Length!=10||stock.Configuration.MechanicalValues is not null) throw new Exception("Migration did not replace blanket overrides with four spring and six bar parameters.");
        if(DesignJson.Serialize(catalog.Migrate(stock))!=DesignJson.Serialize(stock.Configuration)) throw new Exception("Scoped migration is not idempotent.");
        var legacy=stock.Configuration with {RootAssembly=new(catalog.Models.Gr86.Identity.AssetId),Equipment=[],ParameterValues=null,MechanicalValues=GarageMechanicalConfiguration.Capture(graph)};
        var migrated=catalog.Migrate(stock with {Configuration=legacy});
        if(!migrated.ParameterValues!.Value.SequenceEqual(owned)||migrated.MechanicalValues is not null) throw new Exception("Legacy saved settings were not preserved.");
        var editedLegacy=legacy with {MechanicalValues=[..legacy.MechanicalValues!.Value.Select(v=>v switch {InstanceSpringRate s when s.InstancePath=="FL-spring"=>s with {RateKgfPerMm=s.RateKgfPerMm*1.2},InstanceTorsionSection t=>t with {ShearModulusPa=t.ShearModulusPa*.8},_=>v})]};
        var migratedEdited=resolver.Resolve(catalog.Migrate(stock with {Configuration=editedLegacy}));
        if(((CompressionSpringBehavior)migratedEdited.ElasticElements.Single(e=>e.Path=="FL-spring").Component.Behavior!).RateKgfPerMm!=((CompressionSpringBehavior)graph.ElasticElements.Single(e=>e.Path=="FL-spring").Component.Behavior!).RateKgfPerMm*1.2||
            migratedEdited.ElasticElements.Where(e=>e.Component.Behavior is TorsionBarBehavior).Any(e=>((TorsionBarBehavior)e.Component.Behavior!).ShearModulusPa!=editedLegacy.MechanicalValues!.Value.OfType<InstanceTorsionSection>().Single(v=>v.InstancePath==e.Path).ShearModulusPa)) throw new Exception("Edited legacy settings reverted to source properties during migration.");
        var values=owned.Select(v=>v.ParameterId=="FL-spring.rate"?v with {Value=v.Value*1.4}:v.ParameterId.EndsWith(".outside",StringComparison.Ordinal)?v with {Value=v.Value*1.05}:v).ToImmutableArray();
        var duplicate=garage.Duplicate(stock);duplicate=garage.Save(duplicate with {Configuration=duplicate.Configuration! with {ParameterValues=values}});
        var reopened=new GarageLibrary(garage.DirectoryPath).Load(duplicate.Id)!;var configured=resolver.Resolve(reopened.Configuration!);
        var spring=(CompressionSpringBehavior)configured.ElasticElements.Single(e=>e.Path=="FL-spring").Component.Behavior!;
        var original=(CompressionSpringBehavior)graph.ElasticElements.Single(e=>e.Path=="FL-spring").Component.Behavior!;
        if(spring.RateKgfPerMm!=original.RateKgfPerMm*1.4||DesignJson.Serialize(garage.Load(stock.Id)!.Configuration!.ParameterValues)!=DesignJson.Serialize(owned)) throw new Exception("Car values leaked across entries or were not persisted.");
        var springDefinition=library.LoadComponent(configured.ElasticElements.Single(e=>e.Path=="FL-spring").Component.Identity.AssetId)!;
        var beforeRevision=springDefinition.Identity.RevisionId;
        if(springDefinition.Behavior is not null||springDefinition.MechanicalTemplate is not SpringMechanicalTemplate {RateKgfPerMm:NumericParameterReference}) throw new Exception("Migration baked a car value into its component.");
        var bundle=AssemblyGraphResolver.ExportConfigured(new AssemblyGraphResolver(library).Compile(reopened.Configuration!.RootAssembly,reopened.Configuration.Equipment,values));
        var frozen=DesignJson.Deserialize<GarageSetup>(DesignJson.Serialize(reopened.Configuration));
        var replay=GarageSetupResolver.Freeze(AssemblyGraphResolver.OpenBundle(bundle),frozen);
        var zero=new double[new ConstraintMechanism(configured).CoordinateCount];
        if(DesignJson.Serialize(new InstalledMechanics(new ConstraintMechanism(configured)).Evaluate(zero,false))!=DesignJson.Serialize(new InstalledMechanics(new ConstraintMechanism(replay)).Evaluate(zero,false))) throw new Exception("Frozen car configuration changed force/energy replay.");
        var refrozen=GraphAdjustments.Freeze(configured,configured.AppliedAdjustments.Select(a=>a.Selection.ChoiceId is {} id?(AdjustmentSelection)new DiscreteAdjustmentSelection(a.InstancePath,a.AdjustmentId,id):new ContinuousAdjustmentSelection(a.InstancePath,a.AdjustmentId,a.Selection.Fraction??0)));
        if(((CompressionSpringBehavior)refrozen.ElasticElements.Single(e=>e.Path=="FL-spring").Component.Behavior!).RateKgfPerMm!=spring.RateKgfPerMm) throw new Exception("Adjustment/solver construction discarded car-owned values.");
        foreach(var invalid in new[]{values.RemoveAt(0),values.Add(values[0]),values.SetItem(0,new ParameterValue("missing",1)),values.SetItem(0,values[0] with {Value=double.NaN}),values.Select(v=>v.ParameterId.EndsWith(".inside",StringComparison.Ordinal)?v with {Value=1}:v).ToImmutableArray()})
        {try {resolver.Resolve(frozen with {ParameterValues=invalid});throw new Exception("Invalid car parameter configuration accepted.");}catch(ArgumentException) { }}
        try {resolver.Resolve(frozen with {MechanicalValues=GarageMechanicalConfiguration.Capture(graph)});throw new Exception("Legacy property overrides accepted on scoped hierarchy.");}catch(ArgumentException) { }
        // Turning a parameter into a property is an interface conflict, not an implicit override.
        library.Publish(springDefinition with {Parameters=null,MechanicalTemplate=null,Behavior=original with {RateKgfPerMm=1}},beforeRevision);
        try {resolver.Resolve(reopened.Configuration);throw new Exception("Parameter-to-property change silently retained an override.");}catch(ArgumentException) { }
        var current=library.LoadComponent(springDefinition.Identity.AssetId)!;library.Publish(springDefinition,current.Identity.RevisionId);
    }
}
