using System.Security.Cryptography;
using System.Text;
using SuspensionSimulation.Design;
using SuspensionSimulation.Reference;

namespace SuspensionSimulation.Solvers.HeadToHead;

public sealed record GarageEquipmentOption(string Id,string Name,string Role,double RateNPerM,double OutsideDiameterM,double InsideDiameterM);
public sealed record GarageAnalysisSource(string EntryId,int Revision,string EntrySha256,string CatalogSha256,ReferenceGarageEquipment Equipment);

/// <summary>Preserves the workbook conversion and declared legacy physics during Garage migration.</summary>
public sealed class ReferenceGarageCatalog
{
    public const string ModelId="stock-86-brz-reference-v1";
    public const string Gr86Id="86202200000000000000000000000001";
    public const string BrzId="86202200000000000000000000000002";
    public IReadOnlyList<GarageEquipmentOption> Options {get;}
    public string Fingerprint {get;}
    public ReferenceGarageCatalog(string referencePath)
    {
        Fingerprint=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(referencePath)));
        var document=Stock86BrzReferenceLoader.Load(referencePath);var options=new List<GarageEquipmentOption>();
        foreach(var setup in document.Setups)
        {
            options.Add(new(setup.Id+"/front-spring",setup.Name+" front spring","front-spring",setup.FrontSpringRateKgfPerMm*document.Constants.SpringRateNPerMPerKgfPerMm,0,0));
            options.Add(new(setup.Id+"/rear-spring",setup.Name+" rear spring","rear-spring",setup.RearSpringRateKgfPerMm*document.Constants.SpringRateNPerMPerKgfPerMm,0,0));
            options.Add(new(setup.Id+"/front-bar",setup.Name+" front bar","front-bar",0,setup.FrontArb.OutsideDiameterM,setup.FrontArb.InsideDiameterM));
            options.Add(new(setup.Id+"/rear-bar",setup.Name+" rear bar","rear-bar",0,setup.RearArb.OutsideDiameterM,setup.RearArb.InsideDiameterM));
        }
        Options=options.AsReadOnly();
    }
    public ReferenceGarageEquipment Stock(bool brz)
    {
        var id=brz?"stock-brz-2022":"stock-gr86-2022";
        return new(ModelId,id+"/front-spring",id+"/rear-spring",id+"/front-bar",id+"/rear-bar");
    }
    public void Seed(GarageLibrary library)
    {
        foreach(var brz in new[]{false,true})
        {
            var id=brz?BrzId:Gr86Id;
            if(library.Load(id) is null)
                library.Save(new(id,0,brz?"Stock BRZ":"Stock GR86","Reference-sheet equipment; synthetic geometry, estimated travel limits and provisional equilibrium.",false,null,Stock(brz)));
        }
    }
    public HeadToHeadSetupInput Resolve(GarageEntry entry)
    {
        if(entry.Archived) throw new InvalidOperationException("The selected Garage entry is archived.");
        var selected=entry.ReferenceEquipment??throw new InvalidOperationException("The selected Garage entry has no supported assembly/model.");
        if(selected.ModelId!=ModelId) throw new InvalidOperationException("Unsupported reference model.");
        GarageEquipmentOption Get(string id,string role)=>Options.SingleOrDefault(x=>x.Id==id && x.Role==role)
            ??throw new InvalidOperationException($"Missing or incompatible {role} equipment: {id}.");
        var front=Get(selected.FrontSpringId,"front-spring");var rear=Get(selected.RearSpringId,"rear-spring");
        var frontBar=Get(selected.FrontBarId,"front-bar");var rearBar=Get(selected.RearBarId,"rear-bar");
        return new(entry.Name,front.RateNPerM,rear.RateNPerM,frontBar.OutsideDiameterM,frontBar.InsideDiameterM,rearBar.OutsideDiameterM,rearBar.InsideDiameterM);
    }
    public GarageAnalysisSource Freeze(GarageEntry entry)
    {
        _=Resolve(entry);
        return new(entry.Id,entry.Revision,Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(DesignJson.Serialize(entry)))),Fingerprint,entry.ReferenceEquipment!);
    }
}
