using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using SuspensionSimulation.Design;

namespace SuspensionSimulation.App;

internal sealed class AssemblySlotRow
{
    public string Id {get;set;}="equipment";public string Name {get;set;}="Equipment";public string ChildInstance {get;set;}="";
    public string CompatibleAssetIds {get;set;}="";public string Description {get;set;}="Explicitly compatible published hardware. Source properties are read-only; declared parameters are configured by the immediate importing assembly.";
    public EquipmentSlot Read()=>new(Id,Name,ChildInstance,[..CompatibleAssetIds.Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries)],Description);
    public static AssemblySlotRow From(EquipmentSlot s)=>new(){Id=s.Id,Name=s.Name,ChildInstance=s.InstanceId,CompatibleAssetIds=string.Join(",",s.CompatibleAssetIds),Description=s.Description};
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
    private void AddAssemblyGarageContracts(TabControl tabs)
    {
        void Grid<T>(string title,ObservableCollection<T> rows,string help)
        {
            var panel=new DockPanel();var text=new TextBlock {Text=help,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(8)};DockPanel.SetDock(text,Dock.Top);panel.Children.Add(text);
            var grid=new DataGrid {ItemsSource=rows,AutoGenerateColumns=true,CanUserAddRows=true,CanUserDeleteRows=true,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto};grid.CellEditEnding+=(_,_)=>Dispatcher.BeginInvoke(ScheduleAssemblyDraft);
            _assemblyGrids.Add(grid);panel.Children.Add(grid);tabs.Items.Add(new TabItem {Header=title,Content=panel});
        }
        Grid("Garage equipment",_assemblySlots,"A slot replaces one direct child component or assembly. List compatible asset IDs separated by commas, including the default child's asset ID. Ports and mechanism are checked when selected; compatibility is an explicit assembly author decision.");
        Grid("Legacy adjusters",_assemblyExposures,"Legacy mechanical-adjuster exposure; migration to owner parameters and explicit forwarding remains pending. Existing paths, ranges and settings are retained for compatibility. New scalar parameters belong on Parameters / imports; this legacy table does not grant property overrides.");
        _assemblySlots.CollectionChanged+=(_,_)=>ScheduleAssemblyDraft();_assemblyExposures.CollectionChanged+=(_,_)=>ScheduleAssemblyDraft();
    }
    private void FillAssemblyGarageContracts(AssemblyDefinition? definition,AssemblySlotRow[]? slots=null,AssemblyExposureRow[]? exposures=null)
    {
        _assemblySlots.Clear();foreach(var row in slots??(definition?.EquipmentSlots??[]).Select(AssemblySlotRow.From).ToArray()) _assemblySlots.Add(row);
        _assemblyExposures.Clear();foreach(var row in exposures??(definition?.SetupAdjustments??[]).Select(AssemblyExposureRow.From).ToArray()) _assemblyExposures.Add(row);
    }
}
