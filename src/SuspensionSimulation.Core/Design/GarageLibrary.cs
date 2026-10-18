namespace SuspensionSimulation.Design;

/// <summary>Saved Garage drafts; structural validity never implies a runnable physical assembly.</summary>
public sealed record GarageEntry(string Id,int Revision,string Name,string Notes,bool Archived,GarageSetup? Configuration,
    ReferenceGarageEquipment? ReferenceEquipment=null);

/// <summary>Explicit migration adapter for the accepted reference model, not a new assembly solve.</summary>
public sealed record ReferenceGarageEquipment(string ModelId,string FrontSpringId,string RearSpringId,string FrontBarId,string RearBarId);

public sealed class GarageLibrary(string directory)
{
    public string DirectoryPath {get;}=Path.GetFullPath(directory);
    private string Revisions(string id)
    {
        if(!Guid.TryParseExact(id,"N",out _)) throw new ArgumentException("Invalid Garage identity.");
        return Path.Combine(DirectoryPath,id,"revisions");
    }
    public GarageEntry? Load(string id)
    {
        var folder=Revisions(id);if(!Directory.Exists(folder)) return null;
        var files=Directory.GetFiles(folder,"*.json").OrderDescending(StringComparer.Ordinal).ToArray();
        if(files.Length==0) return null;
        var entry=DesignJson.Deserialize<GarageEntry>(File.ReadAllText(files[0]));
        if(entry.Id!=id || Path.GetFileNameWithoutExtension(files[0])!=entry.Revision.ToString("D8")) throw new InvalidDataException("Garage revision identity mismatch.");
        return entry;
    }
    public IReadOnlyList<GarageEntry> List()=>!Directory.Exists(DirectoryPath)?[]:
        Directory.GetDirectories(DirectoryPath).Select(Path.GetFileName).Where(x=>Guid.TryParseExact(x,"N",out _))
            .Select(x=>Load(x!)!).Where(x=>x is not null).OrderBy(x=>x.Name,StringComparer.OrdinalIgnoreCase).ToArray();
    public GarageEntry Create(string name,string notes="")=>Save(new(Guid.NewGuid().ToString("N"),0,name,notes,false,null));
    public GarageEntry Duplicate(GarageEntry entry)
    {
        var id=Guid.NewGuid().ToString("N");var name=entry.Name+" copy";
        return Save(entry with {Id=id,Revision=0,Name=name,Archived=false,Configuration=entry.Configuration is {} setup?setup with {Identity=setup.Identity with {AssetId=id,RevisionId="draft",Name=name}}:null});
    }
    public GarageEntry Save(GarageEntry draft)
    {
        if(string.IsNullOrWhiteSpace(draft.Name) || draft.Name.Trim().Length>120 || draft.Notes is null) throw new ArgumentException("Enter a name of 1–120 characters and valid notes.");
        if(draft.Configuration is {} setup)
        {
            DesignValidation.Validate(setup);
            if(setup.ParameterValues is null) throw new ArgumentException("A new saved car must configure its imported assembly's parameters. Migrate or reopen this legacy car first; descendant property overrides cannot be saved.");
        }
        var current=Load(draft.Id);
        if((current?.Revision??0)!=draft.Revision) throw new InvalidOperationException("This Garage entry has a newer saved revision. Reopen it before saving.");
        var saved=draft with {Revision=checked(draft.Revision+1),Name=draft.Name.Trim()};
        var folder=Revisions(saved.Id);Directory.CreateDirectory(folder);
        var target=Path.Combine(folder,saved.Revision.ToString("D8")+".json");var temp=Path.Combine(folder,Guid.NewGuid().ToString("N")+".tmp");
        try {File.WriteAllText(temp,DesignJson.Serialize(saved));File.Move(temp,target,overwrite:false);}
        catch(IOException) when(File.Exists(target)) {throw new InvalidOperationException("Another session saved this revision first. Reopen the entry.");}
        finally {if(File.Exists(temp)) File.Delete(temp);}
        return saved;
    }
}
