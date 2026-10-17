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
            if(garage.List().Any(e=>e.Revision!=2||e.Configuration is null)) throw new Exception("Physical migration did not append exactly once.");
            var resolver=new GarageSetupResolver(library);var stock=garage.Load(ReferenceGarageCatalog.Gr86Id)!;var brz=garage.Load(ReferenceGarageCatalog.BrzId)!;
            var graph=resolver.Resolve(stock.Configuration!);var graphB=resolver.Resolve(brz.Configuration!);
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
            var source=new AssemblyGraphResolver(library).Compile(stock.Configuration!.RootAssembly);var exported=AssemblyGraphResolver.Export(source);
            var imported=library.ImportClone(exported);var importedGraph=new AssemblyGraphResolver(library).Compile(new(imported.Identity.AssetId));
            if(importedGraph.EquipmentSlots.Length!=18) throw new Exception("Independent import lost or invalidated equipment interfaces.");
            var mount=library.LoadComponent("example-camber-plate")!;library.Publish(mount with {Adjustments=[]},mount.Identity.RevisionId);
            Reject(setup); // Saved settings must not silently disappear after a hardware revision.
        }
        finally {if(Directory.Exists(folder)) Directory.Delete(folder,true);}
    }
}
