using SuspensionSimulation.Components;
using SuspensionSimulation.Design;
using SuspensionSimulation.Solvers.HeadToHead;

namespace SuspensionSimulation.Solvers.Design;

public sealed class PhysicalGarageCatalog(DesignAssetLibrary library,string repository)
{
    public TwinsInstalledModels Models {get;}=TwinsInstalledCatalog.Seed(library,repository);
    public GarageSetup Create(string name,bool brz=false)=>new(new(Guid.NewGuid().ToString("N"),"draft",name,
        new(DataProvenance.Estimated,"Reference equipment with synthetic geometry and estimated installation dimensions/CG; total base mass includes the reference driver once.",null)),
        new((brz?Models.Brz:Models.Gr86).Identity.AssetId),[],[],new(MassAccountingPolicy.AggregateBase,1360.778,new(.0515,0,.46),[]));
    public GarageSetup Migrate(GarageEntry entry)
    {
        if(entry.Configuration is {} configured) return configured;
        var equipment=entry.ReferenceEquipment??throw new InvalidOperationException("Choose a physical root assembly for this entry.");
        if(equipment.ModelId!=ReferenceGarageCatalog.ModelId) throw new InvalidOperationException("Unsupported legacy vehicle model; explicit migration is required.");
        string Variant(string id,string role)=>id=="stock-gr86-2022/"+role?"gr86":id=="stock-brz-2022/"+role?"brz":throw new InvalidOperationException("Cannot migrate unknown equipment: "+id);
        var selections=new List<EquipmentSelection>();
        foreach(var axle in new[]{"front","rear"})
        {
            var spring=Variant(axle=="front"?equipment.FrontSpringId:equipment.RearSpringId,axle+"-spring");
            var bar=Variant(axle=="front"?equipment.FrontBarId:equipment.RearBarId,axle+"-bar");
            foreach(var side in new[]{"L","R"}) {var path=(axle=="front"?"F":"R")+side+"-spring";selections.Add(new(path,path,new("installed-"+spring+"-"+axle+"-spring")));}
            selections.Add(new(axle+"-bar",axle+"-bar",new("installed-"+bar+"-"+axle+"-bar-assembly")));
        }
        return Create(entry.Name,entry.Id==ReferenceGarageCatalog.BrzId) with {Equipment=[..selections]};
    }
    public void Migrate(GarageLibrary garage)
    {
        foreach(var entry in garage.List().Where(e=>e.Configuration is null&&e.ReferenceEquipment is not null))
        {
            var setup=Migrate(entry);_=new GarageSetupResolver(library).Resolve(setup);
            garage.Save(entry with {Configuration=setup}); // Append; retain the original reference revision and provenance.
        }
    }
}
