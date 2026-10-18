using System.Collections.Immutable;
using System.Globalization;
using SuspensionSimulation.Design;

namespace SuspensionSimulation.App;

public sealed class ParameterDeclarationRow
{
    public string Id {get;set;}="parameter";public string Name {get;set;}="Parameter";public string Unit {get;set;}="kgf/mm";
    public string Minimum {get;set;}="";public string Maximum {get;set;}="";public string Description {get;set;}="";
    public string Choices {get;set;}="";
    public DefinitionParameter Read()=>string.IsNullOrWhiteSpace(Choices)?new(Id,Name,Unit,DesignerNumbers.Number(Minimum),DesignerNumbers.Number(Maximum),Description):DefinitionParameter.Choice(Id,Name,Choices.Split(';',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries).Select(raw=>{var split=raw.IndexOf('=');return split<0?new ParameterChoice(raw,raw):new ParameterChoice(raw[..split].Trim(),raw[(split+1)..].Trim());}).ToImmutableArray(),Description);
    public static ParameterDeclarationRow From(DefinitionParameter p)=>new(){Id=p.Id,Name=p.Name,Unit=p.Unit,Minimum=p.Choices is null?N(p.Minimum):"",Maximum=p.Choices is null?N(p.Maximum):"",Description=p.Description,Choices=p.Choices is {} choices?string.Join("; ",choices.Select(c=>c.Id+"="+c.Name)):""};
    internal static string N(double v)=>v.ToString("R",CultureInfo.InvariantCulture);
}
public sealed class ParameterPreviewRow
{
    public string ParameterId {get;set;}="";public string Value {get;set;}="";
    public string ChoiceId {get;set;}="";
    public ParameterValue Read()=>!string.IsNullOrWhiteSpace(ChoiceId)&&string.IsNullOrWhiteSpace(Value)?ParameterValue.Choice(ParameterId,ChoiceId):string.IsNullOrWhiteSpace(ChoiceId)?new(ParameterId,DesignerNumbers.Number(Value)):throw new ArgumentException("Preview needs a numeric value OR a named choice, not both.");
    public static ParameterPreviewRow From(ParameterValue v)=>new(){ParameterId=v.ParameterId,Value=v.ChoiceId is null?ParameterDeclarationRow.N(v.Value):"",ChoiceId=v.ChoiceId??""};
}
public sealed class ChildParameterRow
{
    public string Instance {get;set;}="";public string ParameterId {get;set;}="";public string Value {get;set;}="";public string FromOwnerParameter {get;set;}="";
    public string ChoiceId {get;set;}="";
    public ParameterAssignment Read()
    {
        var forwarded=!string.IsNullOrWhiteSpace(FromOwnerParameter);var literal=!string.IsNullOrWhiteSpace(Value);
        var choice=!string.IsNullOrWhiteSpace(ChoiceId);
        if((forwarded?1:0)+(literal?1:0)+(choice?1:0)!=1) throw new ArgumentException("Assign one number, named choice OR owner parameter: "+Instance+":"+ParameterId);
        return choice?ParameterAssignment.Choice(ParameterId,ChoiceId):new(ParameterId,literal?DesignerNumbers.Number(Value):null,forwarded?FromOwnerParameter:null);
    }
    public static ChildParameterRow From(string instance,ParameterAssignment p)=>new(){Instance=instance,ParameterId=p.ParameterId,Value=p.Value is {} v?ParameterDeclarationRow.N(v):"",FromOwnerParameter=p.FromParameterId??"",ChoiceId=p.ChoiceId??""};
}
public sealed class AdjustmentParameterRow
{
    public string AdjustmentId {get;set;}="";public string ParameterId {get;set;}="";
    public static AdjustmentParameterRow From(AdjustmentParameterBinding b)=>new(){AdjustmentId=b.AdjustmentId,ParameterId=b.ParameterId};
}
internal sealed record ParameterFieldRecovery(bool IsParameter,string ParameterId);
internal sealed record ComponentParameterRecovery(ParameterDeclarationRow[] Declarations,Dictionary<string,ParameterFieldRecovery> Fields,ParameterPreviewRow[] Preview,AdjustmentParameterRow[]? AdjustmentBindings=null);
internal sealed record AssemblyParameterRecovery(ParameterDeclarationRow[] Declarations,ChildParameterRow[] Assignments,ParameterPreviewRow[] Preview,EquipmentOptionRow[]? EquipmentOptions=null,EquipmentOptionParameterRow[]? OptionValues=null);
