using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using SuspensionSimulation.Design;

namespace SuspensionSimulation.App;

public partial class MainWindow
{
    private readonly ObservableCollection<ParameterDeclarationRow> _assemblyParameters=new();
    private readonly ObservableCollection<ChildParameterRow> _assemblyParameterAssignments=new();
    private readonly ObservableCollection<ParameterPreviewRow> _assemblyParameterPreview=new();
    private readonly List<DataGrid> _assemblyParameterGrids=new();
    private TabItem? _assemblyParametersTab;
    private readonly TextBox _assemblyChildProperties=new(){IsReadOnly=true,TextWrapping=TextWrapping.Wrap,Height=150,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    private string? _assemblyParameterOwner;
    private void AddAssemblyParameterTools(TabControl tabs)
    {
        var panel=new StackPanel {Margin=new Thickness(8),Background=System.Windows.Media.Brushes.Transparent};panel.SetValue(System.Windows.Documents.TextElement.ForegroundProperty,System.Windows.Media.Brushes.White);
        panel.Children.Add(new TextBlock {Text="Declare numeric ranges or named choices without selecting values. Choices use id=Name; id2=Name. Configure only direct child instances; forwarding is explicit at every boundary. Imported properties are read-only.",TextWrapping=TextWrapping.Wrap});
        panel.Children.Add(new TextBlock {Text="This assembly's declarations",FontWeight=FontWeights.Bold});Grid(_assemblyParameters,true,170);
        panel.Children.Add(new TextBlock {Text="Direct-child assignments",FontWeight=FontWeights.Bold,Margin=new Thickness(0,10,0,4)});
        panel.Children.Add(new TextBlock {Text="Enter one numeric Value, ChoiceId OR FromOwnerParameter. Instance is a direct child ID, not a descendant path.",TextWrapping=TextWrapping.Wrap});
        var refresh=new Button {Content="Inspect direct child contracts / add missing assignment rows"};refresh.Click+=(_,_)=>AssemblyAction(RefreshAssemblyChildParameters);panel.Children.Add(refresh);panel.Children.Add(_assemblyChildProperties);Grid(_assemblyParameterAssignments,true,170);
        panel.Children.Add(new TextBlock {Text="Temporary preview importer",FontWeight=FontWeights.Bold,Margin=new Thickness(0,10,0,4)});
        panel.Children.Add(new TextBlock {Text="Concrete values here configure this assembly only for inspection. They stay in the workspace draft and exported inspection context; publication never stores them as owner defaults. Pa / m / kgf/mm are SI-contract units.",TextWrapping=TextWrapping.Wrap});
        var rows=new Button {Content="Refresh temporary preview rows"};rows.Click+=(_,_)=>AssemblyAction(()=>RefreshAssemblyPreviewRows(false));panel.Children.Add(rows);
        var mid=new Button {Content="Fill temporary preview: midpoints / first named choices"};mid.Click+=(_,_)=>AssemblyAction(()=>RefreshAssemblyPreviewRows(true));panel.Children.Add(mid);Grid(_assemblyParameterPreview,false,140);
        _assemblyParametersTab=new TabItem {Header="Parameters / imports",Content=new ScrollViewer {Background=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(17,20,25)),Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}};tabs.Items.Add(_assemblyParametersTab);
        _assemblyParameters.CollectionChanged+=(_,_)=>ScheduleAssemblyDraft();_assemblyParameterAssignments.CollectionChanged+=(_,_)=>ScheduleAssemblyDraft();_assemblyParameterPreview.CollectionChanged+=(_,_)=>ScheduleAssemblyDraft();
        void Grid<T>(ObservableCollection<T> data,bool add,double height) {var grid=new DataGrid {ItemsSource=data,AutoGenerateColumns=true,CanUserAddRows=add,CanUserDeleteRows=add,Height=height};grid.CellEditEnding+=(_,_)=>Dispatcher.BeginInvoke(ScheduleAssemblyDraft);_assemblyParameterGrids.Add(grid);panel.Children.Add(grid);}
    }
    private void CommitAssemblyParameters() {foreach(var g in _assemblyParameterGrids) {g.CommitEdit();g.CommitEdit();}}
    private ImmutableArray<ParameterValue> ReadAssemblyPreviewValues()
    {CommitAssemblyParameters();return DefinitionParameters.Resolve(_assemblyParameters.Select(p=>p.Read()),_assemblyParameterPreview.Select(p=>p.Read()));}
    private void RefreshAssemblyPreviewRows(bool midpoints)
    {
        CommitAssemblyParameters();var definitions=_assemblyParameters.Select(p=>p.Read()).ToArray();DefinitionParameters.Validate(definitions);var previous=_assemblyParameterPreview.ToDictionary(p=>p.ParameterId,StringComparer.Ordinal);_assemblyParameterOwner=_assemblyId;
        _assemblyParameterPreview.Clear();foreach(var p in definitions) _assemblyParameterPreview.Add(midpoints?ParameterPreviewRow.From(p.Choices is {} choices?ParameterValue.Choice(p.Id,choices[0].Id):new(p.Id,p.Minimum/2+p.Maximum/2)):previous.GetValueOrDefault(p.Id)??new(){ParameterId=p.Id});
    }
    private void RefreshAssemblyChildParameters()
    {
        foreach(var grid in _assemblyGrids) {grid.CommitEdit();grid.CommitEdit();}CommitAssemblyParameters();var text=new List<string>();
        foreach(var row in _assemblyInstances)
        {
            var child=row.Read();IEnumerable<DefinitionParameter> parameters;
            if(child is ComponentInstance)
            {
                var component=_designLibrary!.LoadComponent(child.Definition.AssetId,child.Definition.RevisionId)??throw new ArgumentException("Missing child component.");parameters=component.Parameters??[];
                string Input(NumericDefinition n,string unit)=>n is NumericProperty p?$"{p.Value:R} {unit} (property)":n is NumericParameterReference r?$"parameter {r.ParameterId}":"unsupported";
                var description=component.MechanicalTemplate switch {SpringMechanicalTemplate s=>"rate: "+Input(s.RateKgfPerMm,"kgf/mm"),TorsionMechanicalTemplate b=>"G: "+Input(b.ShearModulusPa,"Pa")+"; OD: "+Input(b.OutsideDiameterM,"m")+"; ID: "+Input(b.InsideDiameterM,"m"),_=>component.Behavior switch {CompressionSpringBehavior s=>$"Fixed spring rate {s.RateKgfPerMm:R} kgf/mm",TorsionBarBehavior b=>$"Fixed torsion section G {b.ShearModulusPa:R} Pa, OD {b.OutsideDiameterM:R} m, ID {b.InsideDiameterM:R} m",_=>component.Identity.Name}};
                if(component.Chassis is {} chassis) description=ChassisDescription(chassis);
                text.Add(child.Id+" · component source (read only)\n"+description);
            }
            else {var assembly=_designLibrary!.LoadAssembly(child.Definition.AssetId,child.Definition.RevisionId)??throw new ArgumentException("Missing child assembly.");parameters=assembly.Parameters??[];text.Add(child.Id+" · assembly "+assembly.Identity.Name+" (source properties read only)");}
            foreach(var p in parameters)
            {
                text.Add(p.Choices is {} choices?$"  {p.Id}: {p.Name} · "+string.Join("; ",choices.Select(c=>c.Id+"="+c.Name)):$"  {p.Id}: {p.Name} [{p.Unit}], {p.Minimum:R}…{p.Maximum:R}");
                if(!_assemblyParameterAssignments.Any(a=>a.Instance==child.Id&&a.ParameterId==p.Id)) _assemblyParameterAssignments.Add(new(){Instance=child.Id,ParameterId=p.Id});
            }
        }
        _assemblyChildProperties.Text=string.Join("\n",text);
    }
    private PhysicalInstance ConfigureAssemblyChild(PhysicalInstance instance)
    {
        var values=_assemblyParameterAssignments.Where(p=>p.Instance==instance.Id).Select(p=>p.Read()).ToImmutableArray();return instance with {ParameterValues=values.IsEmpty?null:values};
    }
    private void FillAssemblyScopedParameters(AssemblyDefinition? definition)
    {
        var old=_assemblyParameterOwner==definition?.Identity.AssetId?_assemblyParameterPreview.ToDictionary(p=>p.ParameterId,StringComparer.Ordinal):new Dictionary<string,ParameterPreviewRow>();_assemblyParameterOwner=definition?.Identity.AssetId;
        _assemblyParameters.Clear();foreach(var p in definition?.Parameters??[]) _assemblyParameters.Add(ParameterDeclarationRow.From(p));
        _assemblyParameterAssignments.Clear();foreach(var instance in definition?.Instances??[]) foreach(var p in instance.ParameterValues??[]) _assemblyParameterAssignments.Add(ChildParameterRow.From(instance.Id,p));
        _assemblyParameterPreview.Clear();foreach(var p in definition?.Parameters??[]) _assemblyParameterPreview.Add(old.GetValueOrDefault(p.Id)??new(){ParameterId=p.Id});_assemblyChildProperties.Text="Use Inspect direct child contracts to read properties and declared interfaces.";
    }
    private AssemblyParameterRecovery CaptureAssemblyParameters()=>new(_assemblyParameters.ToArray(),_assemblyParameterAssignments.ToArray(),_assemblyParameterPreview.ToArray(),_assemblyEquipmentOptions.ToArray(),_assemblyEquipmentOptionValues.ToArray());
    private void RestoreAssemblyParameters(AssemblyParameterRecovery? raw)
    {
        if(raw is null) {FillAssemblyScopedParameters(_assemblyMetadataSeed);return;}_assemblyParameterOwner=_assemblyId;
        _assemblyParameters.Clear();foreach(var p in raw.Declarations) _assemblyParameters.Add(p);_assemblyParameterAssignments.Clear();foreach(var p in raw.Assignments) _assemblyParameterAssignments.Add(p);_assemblyParameterPreview.Clear();foreach(var p in raw.Preview) _assemblyParameterPreview.Add(p);
        if(raw.EquipmentOptions is {} options) {_assemblyEquipmentOptions.Clear();foreach(var option in options) _assemblyEquipmentOptions.Add(option);}
        if(raw.OptionValues is {} values) {_assemblyEquipmentOptionValues.Clear();foreach(var value in values) _assemblyEquipmentOptionValues.Add(value);}
    }
}
