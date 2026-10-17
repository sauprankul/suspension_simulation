using SuspensionSimulation.Design;
using SuspensionSimulation.Solvers.HeadToHead;
using SuspensionSimulation.Configuration;
using SuspensionSimulation.Core;

namespace SuspensionSimulation.Tests;

internal static class GarageAnalysisChecks
{
    internal static void Verify()
    {
        var folder=Path.Combine(Path.GetTempPath(),"suspension-garage-analysis-"+Guid.NewGuid().ToString("N"));
        try
        {
            var catalog=new ReferenceGarageCatalog("data/reference/stock-86-brz/reference-sheet.json");var library=new GarageLibrary(folder);catalog.Seed(library);catalog.Seed(library);
            if(library.List().Count!=2) throw new Exception("Stock Garage seeding was not idempotent.");
            var a=library.Load(ReferenceGarageCatalog.Gr86Id)!;var b=library.Load(ReferenceGarageCatalog.BrzId)!;
            var ai=catalog.Resolve(a);var bi=catalog.Resolve(b);
            if(ai.FrontSpringNPerM!=28420 || ai.RearSpringNPerM!=39200 || bi.FrontSpringNPerM!=30380 || bi.RearSpringNPerM!=35280) throw new Exception("Stock reference equipment changed.");
            var custom=library.Duplicate(a);custom=library.Save(custom with {ReferenceEquipment=custom.ReferenceEquipment! with {FrontSpringId=b.ReferenceEquipment!.FrontSpringId}});
            if(catalog.Resolve(custom).FrontSpringNPerM!=bi.FrontSpringNPerM || catalog.Resolve(library.Load(a.Id)!).FrontSpringNPerM!=ai.FrontSpringNPerM) throw new Exception("Independent Garage equipment selection failed.");
            var before=catalog.Freeze(custom);var renamed=library.Save(custom with {Name="Custom"});var after=catalog.Freeze(renamed);
            if(before.EntrySha256==after.EntrySha256 || after.Revision!=before.Revision+1 || after.CatalogSha256.Length!=64) throw new Exception("Garage results are not revision-frozen.");
            var solver=new HeadToHeadComparisonSolver("data/synthetic/gr86");var vehicle=ModularSetupLoader.Load("data/synthetic/gr86");
            var result=solver.Solve(vehicle,ai,bi,new LoadCase("garage",0,0,0));
            if(!result.A.State.Converged || !result.B.State.Converged) throw new Exception("Stock Garage entries are not solvable by their reference adapter.");
            var archived=library.Save(a with {Archived=true});catalog.Seed(library);
            if(!library.Load(a.Id)!.Archived) throw new Exception("Seeding undid a user's archive.");
            Reject(()=>catalog.Resolve(archived));Reject(()=>catalog.Resolve(b with {ReferenceEquipment=b.ReferenceEquipment! with {FrontSpringId="missing"}}));
            Reject(()=>catalog.Resolve(b with {ReferenceEquipment=b.ReferenceEquipment! with {FrontSpringId=b.ReferenceEquipment.RearSpringId}}));
        }
        finally {Directory.Delete(folder,true);}
    }
    private static void Reject(Action action) {try {action();}catch(InvalidOperationException) {return;}throw new Exception("Invalid Garage source was accepted.");}
}
