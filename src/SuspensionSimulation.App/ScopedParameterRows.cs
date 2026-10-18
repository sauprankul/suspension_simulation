using System.Collections.Immutable;
using System.Globalization;
using SuspensionSimulation.Design;

namespace SuspensionSimulation.App;

public sealed class ParameterDeclarationRow
{
    public string Id {get;set;}="parameter";public string Name {get;set;}="Parameter";public string Unit {get;set;}="kgf/mm";
    public string Minimum {get;set;}="";public string Maximum {get;set;}="";public string Description {get;set;}="";
    public DefinitionParameter Read()=>new(Id,Name,Unit,DesignerNumbers.Number(Minimum),DesignerNumbers.Number(Maximum),Description);
    public static ParameterDeclarationRow From(DefinitionParameter p)=>new(){Id=p.Id,Name=p.Name,Unit=p.Unit,Minimum=N(p.Minimum),Maximum=N(p.Maximum),Description=p.Description};
    internal static string N(double v)=>v.ToString("R",CultureInfo.InvariantCulture);
}
public sealed class ParameterPreviewRow
{
    public string ParameterId {get;set;}="";public string Value {get;set;}="";
    public ParameterValue Read()=>new(ParameterId,DesignerNumbers.Number(Value));
    public static ParameterPreviewRow From(ParameterValue v)=>new(){ParameterId=v.ParameterId,Value=ParameterDeclarationRow.N(v.Value)};
}
public sealed class ChildParameterRow
{
    public string Instance {get;set;}="";public string ParameterId {get;set;}="";public string Value {get;set;}="";public string FromOwnerParameter {get;set;}="";
    public ParameterAssignment Read()
    {
        var forwarded=!string.IsNullOrWhiteSpace(FromOwnerParameter);var literal=!string.IsNullOrWhiteSpace(Value);
        if(forwarded==literal) throw new ArgumentException("Assign one literal value OR one owner parameter: "+Instance+":"+ParameterId);
        return new(ParameterId,literal?DesignerNumbers.Number(Value):null,forwarded?FromOwnerParameter:null);
    }
    public static ChildParameterRow From(string instance,ParameterAssignment p)=>new(){Instance=instance,ParameterId=p.ParameterId,Value=p.Value is {} v?ParameterDeclarationRow.N(v):"",FromOwnerParameter=p.FromParameterId??""};
}
internal sealed record ParameterFieldRecovery(bool IsParameter,string ParameterId);
internal sealed record ComponentParameterRecovery(ParameterDeclarationRow[] Declarations,Dictionary<string,ParameterFieldRecovery> Fields,ParameterPreviewRow[] Preview);
internal sealed record AssemblyParameterRecovery(ParameterDeclarationRow[] Declarations,ChildParameterRow[] Assignments,ParameterPreviewRow[] Preview);
