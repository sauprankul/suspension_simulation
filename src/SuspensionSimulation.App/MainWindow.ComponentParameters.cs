using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using SuspensionSimulation.Design;

namespace SuspensionSimulation.App;

public partial class MainWindow
{
    private readonly ObservableCollection<ParameterDeclarationRow> _componentParameters=new();
    private readonly ObservableCollection<ParameterPreviewRow> _componentParameterPreview=new();
    private readonly ObservableCollection<AdjustmentParameterRow> _componentAdjustmentBindings=new();
    private readonly TextBox _componentAdjustmentContracts=new(){IsReadOnly=true,TextWrapping=TextWrapping.Wrap,Height=100,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    private readonly Dictionary<string,(StackPanel Panel,CheckBox Mode,TextBox Reference)> _componentParameterFields=new();
    private readonly List<DataGrid> _componentParameterGrids=new();
    private TabItem? _componentParametersTab;
    private void ResetComponentParameters() {_componentParameters.Clear();_componentParameterPreview.Clear();_componentAdjustmentBindings.Clear();_componentAdjustmentContracts.Text="No authored adjusters in the current preview.";foreach(var field in _componentParameterFields.Values) {field.Mode.IsChecked=false;field.Reference.Text="";}}
    private static double ParameterFieldScale(string id)=>id=="shear"?1e9:id is "outside" or "inside"?.001:1;
    private void AddMechanicalSourceChoice(Panel parent,string id)
    {
        if(id is not ("rate" or "shear" or "outside" or "inside")) return;
        var panel=new StackPanel();var mode=new CheckBox {Content="Parameter supplied by immediate importer"};var reference=new TextBox {ToolTip="Local parameter ID declared on the Parameters tab."};panel.Children.Add(mode);panel.Children.Add(reference);parent.Children.Add(panel);
        _componentParameterFields[id]=(panel,mode,reference);
        void Refresh() {_physicalFields[id].IsEnabled=mode.IsChecked!=true;reference.Visibility=mode.IsChecked==true?Visibility.Visible:Visibility.Collapsed;ScheduleComponentDraft();}
        mode.Checked+=(_,_)=>Refresh();mode.Unchecked+=(_,_)=>Refresh();reference.TextChanged+=(_,_)=>ScheduleComponentDraft();Refresh();
    }
    private void AddComponentParameterTools(TabControl tabs)
    {
        var panel=new StackPanel {Margin=new Thickness(8),Background=System.Windows.Media.Brushes.Transparent};panel.SetValue(System.Windows.Documents.TextElement.ForegroundProperty,System.Windows.Media.Brushes.White);
        panel.Children.Add(new TextBlock {Text="Owner declarations contain bounds or named choices, never a selected value. For choices enter id=Name; id2=Name and leave numeric bounds blank. Numeric units: kgf/mm, Pa, m; adjuster position uses fraction (0–1). Bump-stop material stays a component property.",TextWrapping=TextWrapping.Wrap});
        Grid(_componentParameters,true,200);
        panel.Children.Add(new TextBlock {Text="Adjustment bindings",FontWeight=FontWeights.Bold,Margin=new Thickness(0,10,0,4)});panel.Children.Add(new TextBlock {Text="Bind an adjustment ID to an owner parameter. Continuous paths use fraction; named holes use matching choice IDs. The source reference below defines nominal geometry, never a selected parameter default. Unbound adjustments retain that fixed source position.",TextWrapping=TextWrapping.Wrap});panel.Children.Add(_componentAdjustmentContracts);Grid(_componentAdjustmentBindings,true,130);
        panel.Children.Add(new TextBlock {Text="Temporary preview importer",FontWeight=FontWeights.Bold,Margin=new Thickness(0,12,0,4)});
        panel.Children.Add(new TextBlock {Text="Values below are inspection inputs only. They remain in this workspace draft and are never published as owner defaults.",TextWrapping=TextWrapping.Wrap});
        var refresh=new Button {Content="Refresh preview parameter rows"};refresh.Click+=(_,_)=>ComponentAction(()=>RefreshComponentPreviewRows(false));panel.Children.Add(refresh);
        var midpoint=new Button {Content="Fill temporary preview: midpoints / first named choices"};midpoint.Click+=(_,_)=>ComponentAction(()=>RefreshComponentPreviewRows(true));panel.Children.Add(midpoint);Grid(_componentParameterPreview,false,160);
        _componentParametersTab=new TabItem {Header="Parameters",Content=new ScrollViewer {Background=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(17,20,25)),Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}};tabs.Items.Add(_componentParametersTab);
        _componentParameters.CollectionChanged+=(_,_)=>ScheduleComponentDraft();_componentParameterPreview.CollectionChanged+=(_,_)=>ScheduleComponentDraft();_componentAdjustmentBindings.CollectionChanged+=(_,_)=>ScheduleComponentDraft();
        void Grid<T>(ObservableCollection<T> rows,bool add,double height) {var grid=new DataGrid {ItemsSource=rows,AutoGenerateColumns=true,CanUserAddRows=add,CanUserDeleteRows=add,Height=height};grid.CellEditEnding+=(_,_)=>Dispatcher.BeginInvoke(ScheduleComponentDraft);_componentParameterGrids.Add(grid);panel.Children.Add(grid);}
    }
    private void CommitComponentParameters() {foreach(var g in _componentParameterGrids) {g.CommitEdit();g.CommitEdit();}}
    private void RefreshComponentPreviewRows(bool midpoints)
    {
        CommitComponentParameters();var definitions=_componentParameters.Select(p=>p.Read()).ToArray();DefinitionParameters.Validate(definitions);var previous=_componentParameterPreview.ToDictionary(p=>p.ParameterId,StringComparer.Ordinal);
        _componentParameterPreview.Clear();foreach(var p in definitions) _componentParameterPreview.Add(midpoints?ParameterPreviewRow.From(p.Choices is {} choices?ParameterValue.Choice(p.Id,choices[0].Id):new(p.Id,p.Minimum/2+p.Maximum/2)):previous.GetValueOrDefault(p.Id)??new(){ParameterId=p.Id});
    }
    private double ComponentMechanicalNumber(string id)
    {
        if(_componentParameterFields.TryGetValue(id,out var field)&&field.Mode.IsChecked==true)
        {
            CommitComponentParameters();var values=DefinitionParameters.Resolve(_componentParameters.Select(p=>p.Read()),_componentParameterPreview.Select(p=>p.Read()));
            return values.SingleOrDefault(v=>v.ParameterId==field.Reference.Text)?.Value/ParameterFieldScale(id)??throw new ArgumentException("Enter an explicit temporary preview value for "+field.Reference.Text+" on the Parameters tab.");
        }
        return DesignerNumbers.Number(_physicalFields[id].Text);
    }
    private ComponentDefinition ApplyComponentParameterTemplate(ComponentDefinition component)
    {
        CommitComponentParameters();var declarations=_componentParameters.Select(p=>p.Read()).ToImmutableArray();
        component=component with {Parameters=declarations.IsEmpty?null:declarations,AdjustmentParameters=[.._componentAdjustmentBindings.Where(b=>!string.IsNullOrWhiteSpace(b.ParameterId)).Select(b=>new AdjustmentParameterBinding(b.AdjustmentId,b.ParameterId))]};
        var active=component.Kind==ComponentKind.Spring?new[]{"rate"}:component.Kind==ComponentKind.AntiRollBar?new[]{"shear","outside","inside"}:[];
        var hasParameters=active.Any(id=>_componentParameterFields[id].Mode.IsChecked==true);
        if(!hasParameters) return component with {MechanicalTemplate=null};
        NumericDefinition Number(string id)=>_componentParameterFields[id].Mode.IsChecked==true?new NumericParameterReference(_componentParameterFields[id].Reference.Text):new NumericProperty(DesignerNumbers.Number(_physicalFields[id].Text)*ParameterFieldScale(id));
        var source=_componentMetadataSeed?.MechanicalTemplate;
        var template=component.Behavior switch
        {
            CompressionSpringBehavior s=>(MechanicalTemplate)new SpringMechanicalTemplate(source?.FirstPortId??s.FirstPortId,source?.SecondPortId??s.SecondPortId,Number("rate"),s.FreeLengthM,s.BlockLengthM),
            TorsionBarBehavior b=>new TorsionMechanicalTemplate(source?.FirstPortId??b.FirstPortId,source?.SecondPortId??b.SecondPortId,source is TorsionMechanicalTemplate old?old.UnitAxis:b.UnitAxis,Number("shear"),Number("outside"),Number("inside"),b.TorsionLengthM,b.RestTwistRad,b.PrescribedRateNmPerRad),
            _=>throw new ArgumentException("Scalar parameter bindings currently support springs and torsion sections only.")
        };
        return component with {Behavior=null,MechanicalTemplate=template,Parameters=declarations};
    }
    private void FillComponentScopedParameters(ComponentDefinition component)
    {
        _componentParameters.Clear();foreach(var p in component.Parameters??[]) _componentParameters.Add(ParameterDeclarationRow.From(p));
        _componentAdjustmentBindings.Clear();foreach(var binding in component.AdjustmentParameters??[]) _componentAdjustmentBindings.Add(AdjustmentParameterRow.From(binding));
        _componentAdjustmentContracts.Text=string.Join("\n",(component.Adjustments??[]).Select(a=>a.Id+" · "+a.Name+" · "+(a.Path is DiscreteAdjustmentPath holes?string.Join("; ",holes.Choices.Select(c=>c.Id+"="+c.Name)):"fraction 0–1")+" · "+(a.Default.ChoiceId??ParameterDeclarationRow.N(a.Default.Fraction??0))+" reference/fixed setting"));
        foreach(var field in _componentParameterFields.Values) {field.Mode.IsChecked=false;field.Reference.Text="";}
        void Fill(string id,NumericDefinition n) {if(n is NumericProperty p) _physicalFields[id].Text=ParameterDeclarationRow.N(p.Value/ParameterFieldScale(id));else if(n is NumericParameterReference r) {_componentParameterFields[id].Mode.IsChecked=true;_componentParameterFields[id].Reference.Text=r.ParameterId;}}
        if(component.MechanicalTemplate is SpringMechanicalTemplate s) {Fill("rate",s.RateKgfPerMm);_physicalFields["free"].Text=ParameterDeclarationRow.N(s.FreeLengthM*1000);_physicalFields["block"].Text=ParameterDeclarationRow.N(s.BlockLengthM*1000);}
        if(component.MechanicalTemplate is TorsionMechanicalTemplate b) {Fill("shear",b.ShearModulusPa);Fill("outside",b.OutsideDiameterM);Fill("inside",b.InsideDiameterM);_physicalFields["torsionLength"].Text=ParameterDeclarationRow.N(b.TorsionLengthM*1000);_physicalFields["restTwist"].Text=ParameterDeclarationRow.N(b.RestTwistRad*180/Math.PI);_physicalFields["effectiveBar"].Text=b.PrescribedRateNmPerRad is {} rate?ParameterDeclarationRow.N(rate):"";}
        var previous=_componentParameterPreview.ToDictionary(p=>p.ParameterId,StringComparer.Ordinal);_componentParameterPreview.Clear();foreach(var p in component.Parameters??[]) _componentParameterPreview.Add(previous.GetValueOrDefault(p.Id)??new(){ParameterId=p.Id});
    }
    private ComponentParameterRecovery CaptureComponentParameters()=>new(_componentParameters.ToArray(),_componentParameterFields.ToDictionary(p=>p.Key,p=>new ParameterFieldRecovery(p.Value.Mode.IsChecked==true,p.Value.Reference.Text)),_componentParameterPreview.ToArray(),_componentAdjustmentBindings.ToArray());
    private void RestoreComponentParameters(ComponentParameterRecovery? raw)
    {
        if(raw is null) {if(_componentMetadataSeed is {} c) FillComponentScopedParameters(c);return;}
        _componentParameters.Clear();foreach(var p in raw.Declarations) _componentParameters.Add(p);_componentParameterPreview.Clear();foreach(var p in raw.Preview) _componentParameterPreview.Add(p);
        _componentAdjustmentBindings.Clear();foreach(var b in raw.AdjustmentBindings??[]) _componentAdjustmentBindings.Add(b);
        foreach(var (id,state) in raw.Fields) if(_componentParameterFields.TryGetValue(id,out var field)) {field.Mode.IsChecked=state.IsParameter;field.Reference.Text=state.ParameterId;}
    }
}
