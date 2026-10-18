using System.Collections.ObjectModel;
using System.Collections.Immutable;
using System.Windows;
using System.Windows.Controls;
using SuspensionSimulation.Design;

namespace SuspensionSimulation.App;

internal sealed class AssemblySlotRow
{
    public string Id {get;set;}="equipment";public string Name {get;set;}="Equipment";public string ChildInstance {get;set;}="";
    public string CompatibleAssetIds {get;set;}="";public string Description {get;set;}="Explicitly compatible published hardware. Source properties are read-only; declared parameters are configured by the immediate importing assembly.";
    public string ParameterId {get;set;}="";
    public EquipmentSlot Read()=>new(Id,Name,ChildInstance,[..CompatibleAssetIds.Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries)],Description) {ParameterId=string.IsNullOrWhiteSpace(ParameterId)?null:ParameterId};
    public static AssemblySlotRow From(EquipmentSlot s)=>new(){Id=s.Id,Name=s.Name,ChildInstance=s.InstanceId,CompatibleAssetIds=string.Join(",",s.CompatibleAssetIds),Description=s.Description,ParameterId=s.ParameterId??""};
}
public sealed class EquipmentOptionRow
{
    public string SlotId {get;set;}="";public string ChoiceId {get;set;}="";public string AssetId {get;set;}="";public string Revision {get;set;}="";
}
public sealed class EquipmentOptionParameterRow
{
    public string SlotId {get;set;}="";public string EquipmentChoice {get;set;}="";public string ParameterId {get;set;}="";
    public string Value {get;set;}="";public string ChoiceId {get;set;}="";public string FromOwnerParameter {get;set;}="";
    public ParameterAssignment Read()=>new ChildParameterRow {Instance=SlotId+"/"+EquipmentChoice,ParameterId=ParameterId,Value=Value,ChoiceId=ChoiceId,FromOwnerParameter=FromOwnerParameter}.Read();
}
internal sealed class AssemblyExposureRow
{
    public string ComponentPath {get;set;}="";public string Name {get;set;}="Adjustments";public string AdjustmentIds {get;set;}="*";
    public SetupAdjustmentExposure Read()=>new(ComponentPath,Name,AdjustmentIds.Trim()=="*"?null:[..AdjustmentIds.Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries)]);
    public static AssemblyExposureRow From(SetupAdjustmentExposure e)=>new(){ComponentPath=e.InstancePath,Name=e.Name,AdjustmentIds=e.AdjustmentIds is {} ids?string.Join(",",ids):"*"};
}
public partial class MainWindow
{
    private readonly ObservableCollection<AssemblySlotRow> _assemblySlots=new();
    private readonly ObservableCollection<AssemblyExposureRow> _assemblyExposures=new();
    private readonly ObservableCollection<EquipmentOptionRow> _assemblyEquipmentOptions=new();
    private readonly ObservableCollection<EquipmentOptionParameterRow> _assemblyEquipmentOptionValues=new();
    private void AddAssemblyGarageContracts(TabControl tabs)
    {
        void Grid<T>(string title,ObservableCollection<T> rows,string help)
        {
            var panel=new DockPanel {Background=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(17,20,25))};var text=new TextBlock {Text=help,Foreground=System.Windows.Media.Brushes.White,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(8)};DockPanel.SetDock(text,Dock.Top);panel.Children.Add(text);
            var legacy=title=="Legacy exposure metadata";var grid=new DataGrid {ItemsSource=rows,AutoGenerateColumns=true,CanUserAddRows=!legacy,CanUserDeleteRows=!legacy,IsReadOnly=legacy,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto};grid.CellEditEnding+=(_,_)=>Dispatcher.BeginInvoke(ScheduleAssemblyDraft);
            _assemblyGrids.Add(grid);panel.Children.Add(grid);tabs.Items.Add(new TabItem {Header=title,Content=panel});
        }
        Grid("Owner equipment slots",_assemblySlots,"An owner slot replaces only one direct child. Set ParameterId to this assembly's declared choice parameter; blank keeps the child equipment fixed as a source property. Define named options and each option's child configuration on the next tabs. No descendant Garage picker is created.");
        Grid("Equipment options",_assemblyEquipmentOptions,"Map each slot choice ID to a compatible child definition. Blank revision follows latest compatible source; an exact revision pins it. Choice IDs match the owner's declaration. Fixed slots may retain one source option.");
        Grid("Option configurations",_assemblyEquipmentOptionValues,"Configure each option's direct-child parameters using one numeric Value, child ChoiceId, or FromOwnerParameter. EquipmentChoice identifies the slot option; ChoiceId configures the imported child's named parameter.");
        Grid("Legacy exposure metadata",_assemblyExposures,"Compatibility metadata only. New mechanical controls use component adjustment bindings and explicit forwarding through every assembly. Garage v2 has no descendant adjuster or equipment controls.");
        _assemblySlots.CollectionChanged+=(_,_)=>ScheduleAssemblyDraft();_assemblyExposures.CollectionChanged+=(_,_)=>ScheduleAssemblyDraft();
        _assemblyEquipmentOptions.CollectionChanged+=(_,_)=>ScheduleAssemblyDraft();_assemblyEquipmentOptionValues.CollectionChanged+=(_,_)=>ScheduleAssemblyDraft();
    }
    private void FillAssemblyGarageContracts(AssemblyDefinition? definition,AssemblySlotRow[]? slots=null,AssemblyExposureRow[]? exposures=null)
    {
        _assemblySlots.Clear();foreach(var row in slots??(definition?.EquipmentSlots??[]).Select(AssemblySlotRow.From).ToArray()) _assemblySlots.Add(row);
        _assemblyExposures.Clear();foreach(var row in exposures??(definition?.SetupAdjustments??[]).Select(AssemblyExposureRow.From).ToArray()) _assemblyExposures.Add(row);
        _assemblyEquipmentOptions.Clear();_assemblyEquipmentOptionValues.Clear();
        foreach(var slot in definition?.EquipmentSlots??[]) foreach(var option in slot.Options??[])
        {
            _assemblyEquipmentOptions.Add(new(){SlotId=slot.Id,ChoiceId=option.ChoiceId,AssetId=option.Definition.AssetId,Revision=option.Definition.RevisionId??""});
            foreach(var value in option.ParameterValues) _assemblyEquipmentOptionValues.Add(new(){SlotId=slot.Id,EquipmentChoice=option.ChoiceId,ParameterId=value.ParameterId,Value=value.Value is {} n?ParameterDeclarationRow.N(n):"",ChoiceId=value.ChoiceId??"",FromOwnerParameter=value.FromParameterId??""});
        }
    }
    private EquipmentSlot ReadOwnerEquipmentSlot(AssemblySlotRow row)
    {
        var options=_assemblyEquipmentOptions.Where(o=>o.SlotId==row.Id).Select(o=>new EquipmentOption(o.ChoiceId,new(o.AssetId,string.IsNullOrWhiteSpace(o.Revision)?null:o.Revision),[.._assemblyEquipmentOptionValues.Where(v=>v.SlotId==row.Id&&v.EquipmentChoice==o.ChoiceId).Select(v=>v.Read())])).ToArray();
        return row.Read() with {Options=options.Length>0?options.ToImmutableArray():_assemblyMetadataSeed?.ConfigurationVersion==2||_assemblyMetadataSeed is null?[]:null};
    }
}
