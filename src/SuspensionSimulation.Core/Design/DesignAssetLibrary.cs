using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SuspensionSimulation.Design;

/// <summary>Immutable revisions with one atomic head pointer and optimistic per-asset publication.</summary>
public sealed class DesignAssetLibrary(string root)
{
    public string Root {get;}=Path.GetFullPath(root);
    private static void Id(string id)
    {if(string.IsNullOrEmpty(id)||id.Length>120||!char.IsAsciiLetterOrDigit(id[0])||id.Any(x=>!char.IsAsciiLetterOrDigit(x)&&x is not '-' and not '_')) throw new ArgumentException("Invalid asset identity.");}
    private string Folder(string kind,string id) {Id(id);return Path.Combine(Root,kind,id);}
    private static string ReadText(string path)
    {using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);using var reader=new StreamReader(stream);return reader.ReadToEnd();}
    private string? Head(string kind,string id) {var path=Path.Combine(Folder(kind,id),"head");return File.Exists(path)?ReadText(path).Trim():null;}
    public ComponentDefinition? LoadComponent(string id,string? revision=null)=>Load<ComponentDefinition>("components",id,revision);
    public AssemblyDefinition? LoadAssembly(string id,string? revision=null)=>Load<AssemblyDefinition>("assemblies",id,revision);
    public IReadOnlyList<ComponentDefinition> Components()=>List("components",LoadComponent);
    public IReadOnlyList<AssemblyDefinition> Assemblies()=>List("assemblies",LoadAssembly);
    public IReadOnlyList<string> ComponentRevisions(string id)=>Revisions("components",id);
    public IReadOnlyList<string> AssemblyRevisions(string id)=>Revisions("assemblies",id);
    private IReadOnlyList<string> Revisions(string kind,string id)
    {var folder=Path.Combine(Folder(kind,id),"revisions");return Directory.Exists(folder)?Directory.GetFiles(folder,"*.json").OrderByDescending(File.GetLastWriteTimeUtc).Select(x=>Path.GetFileNameWithoutExtension(x)).ToArray():[];}
    private IReadOnlyList<T> List<T>(string kind,Func<string,string?,T?> load) where T:class=>!Directory.Exists(Path.Combine(Root,kind))?[]:
        Directory.GetDirectories(Path.Combine(Root,kind)).Select(Path.GetFileName).Select(id=>load(id!,null)).Where(x=>x is not null).Cast<T>().ToArray();
    private T? Load<T>(string kind,string id,string? revision)
    {
        revision??=Head(kind,id);if(revision is null) return default;
        if(revision.Length!=64||!revision.All(Uri.IsHexDigit)) throw new InvalidDataException("Invalid asset revision.");
        var value=DesignJson.Deserialize<T>(ReadText(Path.Combine(Folder(kind,id),"revisions",revision+".json")));
        var identity=Identity(value!);
        if(identity.AssetId!=id||identity.RevisionId!=revision||Hash(WithIdentity(value!,identity with {RevisionId=""}))!=revision) throw new InvalidDataException("Asset content hash mismatch.");
        return value;
    }
    public ComponentDefinition Publish(ComponentDefinition component,string? expectedRevision)
    {ComponentAuthoring.ValidateForPublish(component);return Publish("components",component,expectedRevision);}
    public AssemblyDefinition Publish(AssemblyDefinition assembly,string? expectedRevision)
    {DesignValidation.Validate(assembly);return Publish("assemblies",assembly,expectedRevision);}
    private T Publish<T>(string kind,T value,string? expectedRevision)
    {
        var identity=Identity(value!);var folder=Folder(kind,identity.AssetId);Directory.CreateDirectory(folder);
        using var gate=new FileStream(Path.Combine(folder,"publish.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        if(Head(kind,identity.AssetId)!=expectedRevision) throw new InvalidOperationException("This asset has a newer published revision. Reopen before publishing.");
        var revision=Hash(WithIdentity(value!,identity with {RevisionId=""}));var published=(T)WithIdentity(value!,identity with {RevisionId=revision});
        var revisions=Path.Combine(folder,"revisions");Directory.CreateDirectory(revisions);var path=Path.Combine(revisions,revision+".json");
        var json=Serialize(published!);
        if(File.Exists(path)) {if(ReadText(path)!=json) throw new InvalidDataException("Existing immutable revision has different content.");}
        else Atomic(path,json);
        Atomic(Path.Combine(folder,"head"),revision);return published;
    }
    private static RevisionIdentity Identity(object value)=>value switch {ComponentDefinition x=>x.Identity,AssemblyDefinition x=>x.Identity,_=>throw new ArgumentException("Unsupported asset type.")};
    private static object WithIdentity(object value,RevisionIdentity identity)=>value switch {ComponentDefinition x=>x with {Identity=identity},AssemblyDefinition x=>x with {Identity=identity},_=>throw new ArgumentException("Unsupported asset type.")};
    private static string Serialize(object value)=>value switch {ComponentDefinition x=>DesignJson.Serialize(x),AssemblyDefinition x=>DesignJson.Serialize(x),_=>throw new ArgumentException("Unsupported asset type.")};
    private static string Hash(object value)=>Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Serialize(value))));
    private static void Atomic(string path,string text)
    {
        var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {File.WriteAllText(temp,text);File.Move(temp,path,true);} finally {if(File.Exists(temp)) File.Delete(temp);}
    }
    public void SaveDraft(string id,string json)
    {Id(id);using var parsed=JsonDocument.Parse(json);var folder=Path.Combine(Root,"drafts");Directory.CreateDirectory(folder);Atomic(Path.Combine(folder,id+".json"),json);}
    public string? ReadDraft(string id) {Id(id);var path=Path.Combine(Root,"drafts",id+".json");return File.Exists(path)?ReadText(path):null;}
}
