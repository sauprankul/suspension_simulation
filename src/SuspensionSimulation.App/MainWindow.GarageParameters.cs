using System.Collections.Immutable;
using System.Windows;
using System.Windows.Controls;
using SuspensionSimulation.Design;
using SuspensionSimulation.Solvers.Design;

namespace SuspensionSimulation.App;

public partial class MainWindow
{
    private readonly Dictionary<string,TextBox> _garageRootParameterInputs=new();
    private readonly Dictionary<string,ComboBox> _garageRootChoiceInputs=new();
    private readonly Dictionary<string,(TextBlock Label,string Title)> _garageRootParameterLabels=new();
    private const string InvalidGarageDisplay="display-invalid:";
    private readonly Dictionary<string,(double Scale,string OriginalCanonical,string OriginalDisplay)> _garageNumericDisplays=new();
    private static (double Scale,string Unit) GarageDisplayUnit(string unit)=>unit switch {"Pa"=>(1e9,"GPa"),"m"=>(.001,"mm"),"fraction"=>(.01,"%"),_=>(1,unit)};
    private static string GarageDisplayText(string canonical,double scale)
        =>canonical.StartsWith(InvalidGarageDisplay,StringComparison.Ordinal)?canonical[InvalidGarageDisplay.Length..]:double.TryParse(canonical,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var value)&&double.IsFinite(value)?(value/scale).ToString("G10",System.Globalization.CultureInfo.InvariantCulture):canonical;
    private void SetGarageContractValue(string id,double canonical)
        =>_garageRootParameterInputs[id].Text=(canonical/_garageNumericDisplays[id].Scale).ToString("G10",System.Globalization.CultureInfo.InvariantCulture);
    private GaragePhysicalDraft UpgradeGarageParameterDraft(GarageEntry entry,GaragePhysicalDraft raw)
    {
        if(raw.RootParameters is not null&&raw.Mechanics is {Length:0}) raw=raw with {Mechanics=null};
        var rawSource=_designLibrary!.LoadAssembly(raw.Root.AssetId,raw.Root.RevisionId)??throw new ArgumentException("The recovered assembly is missing.");
        if(raw.RootParameters is not null&&rawSource.ConfigurationVersion==2&&raw.Equipment.Length==0&&raw.Adjustments.Count==0&&raw.Mechanics is null) return raw;
        var saved=entry.Configuration??throw new ArgumentException("The saved car has no assembly.");
        var values=raw.RootParameters is null?(System.Collections.Immutable.ImmutableArray<ParameterValue>?)null:ReadGarageRootParameterValues(raw);
        var graph=new AssemblyGraphResolver(_designLibrary).Compile(raw.Root,raw.Equipment,values??[]);var adjustments=new List<AdjustmentSelection>();
        foreach(var value in raw.Adjustments)
        {
            var split=value.Key.LastIndexOf(':');if(split<1) throw new ArgumentException("Invalid recovered adjustment identity.");var path=value.Key[..split];var id=value.Key[(split+1)..];
            var a=graph.SetupAdjustments.SingleOrDefault(a=>a.InstancePath==path&&a.Definition.Id==id)?.Definition??throw new ArgumentException("Recovered adjustment is not declared by the source.");
            adjustments.Add(a.Path is DiscreteAdjustmentPath?new DiscreteAdjustmentSelection(path,id,value.Value):new ContinuousAdjustmentSelection(path,id,GarageReadNumber(value.Value,"Recovered adjustment")));
        }
        var legacy=saved with {RootAssembly=raw.Root,Equipment=[..raw.Equipment],Adjustments=[..adjustments],ParameterValues=values,MechanicalValues=raw.Mechanics is {} old?ReadMechanicalValues(old):null};
        var configured=_physicalGarageCatalog!.Migrate(entry with {Configuration=legacy});
        return raw with {Root=configured.RootAssembly,Equipment=configured.Equipment.ToArray(),Adjustments=[],Mechanics=null,RootParameters=configured.ParameterValues!.Value.ToDictionary(p=>p.ParameterId,GarageParameterText)};
    }
    private void FillGarageRootParameters(GaragePhysicalDraft draft,AssemblyDefinition? root)
    {
        _garageRootParameterInputs.Clear();_garageRootChoiceInputs.Clear();_garageRootParameterLabels.Clear();_garageNumericDisplays.Clear();if(draft.RootParameters is null&&(root?.Parameters??[]).IsEmpty) return;
        var panel=new StackPanel {Margin=new Thickness(4)};panel.SetValue(System.Windows.Documents.TextElement.ForegroundProperty,System.Windows.Media.Brushes.White);panel.Children.Add(new TextBlock {Text="Garage configures only parameters declared by this complete-car assembly. Descendant properties and bump-stop material are read-only. Each forwarding boundary is owned in Assembly Editor.",TextWrapping=TextWrapping.Wrap});
        foreach(var p in root?.Parameters??[])
        {
            var (scale,unit)=GarageDisplayUnit(p.Unit);var title=p.Choices is not null?p.Name:$"{p.Name} [{unit}] · {p.Minimum/scale:G6}…{p.Maximum/scale:G6}";var label=new TextBlock {Text=title,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,2)};_garageRootParameterLabels[p.Id]=(label,title);panel.Children.Add(label);
            var raw=draft.RootParameters?.GetValueOrDefault(p.Id,"")??"";
            if(p.Choices is {} choices)
            {
                var picker=new ComboBox {ItemsSource=choices,DisplayMemberPath="Name",SelectedValuePath="Id",IsEditable=true,IsTextSearchEnabled=false,ToolTip=p.Description+"\nRoot choice parameter ID: "+p.Id};picker.SelectedValue=raw;if(picker.SelectedItem is null) picker.Text=raw;
                _garageRootChoiceInputs[p.Id]=picker;panel.Children.Add(picker);
            }
            else {var shown=GarageDisplayText(raw,scale);_garageNumericDisplays[p.Id]=(scale,raw,shown);var box=new TextBox {Text=shown,ToolTip=p.Description+"\nDisplayed in "+unit+"; saved in "+p.Unit+". Root parameter ID: "+p.Id};_garageRootParameterInputs[p.Id]=box;panel.Children.Add(box);}
        }
        var unknown=(draft.RootParameters??[]).Keys.Except((root?.Parameters??[]).Select(p=>p.Id),StringComparer.Ordinal).ToArray();
        if(unknown.Length>0) panel.Children.Add(new TextBlock {Text="Recovered undeclared parameter values retained: "+string.Join(", ",unknown)+". Reopen the saved car or restore the original source before saving.",TextWrapping=TextWrapping.Wrap,Foreground=System.Windows.Media.Brushes.Gold});
        var apply=new Button {Content="Apply root configuration to hardware preview",Margin=new Thickness(0,8,0,4)};apply.Click+=(_,_)=>{if(_garageSelected is {} entry) {CachePhysicalGarageDraft();FillPhysicalGarage(entry);}};panel.Children.Add(apply);
        _garagePhysicalPanel.Children.Add(new Expander {Header="Complete-car configuration parameters",Foreground=System.Windows.Media.Brushes.White,IsExpanded=true,Content=panel});
    }
    private static string GarageParameterText(ParameterValue value)=>value.ChoiceId??ParameterDeclarationRow.N(value.Value);
    private void MarkGarageParameterActivity(ResolvedAssemblyGraph graph,bool savedPreview)
    {
        var active=graph.ActiveRootParameterIds.ToHashSet(StringComparer.Ordinal);
        foreach(var (id,row) in _garageRootParameterLabels)
        {
            row.Label.Text=row.Title+(active.Contains(id)?"":" · inactive with current hardware")+(savedPreview?" · activity from saved configuration":"");
            row.Label.ToolTip=active.Contains(id)?"Used by the current hardware preview.":"An approved alternative uses this explicit value. It is retained and editable; it currently has no effect on the selected hardware.";
        }
    }
    private Dictionary<string,string> CaptureGarageRootParameters()
    {
        var values=_garageSelected is {} entry?new Dictionary<string,string>(DraftFor(entry).RootParameters??[],StringComparer.Ordinal):new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var (id,input) in _garageRootParameterInputs)
        {
            var display=_garageNumericDisplays[id];
            values[id]=input.Text==display.OriginalDisplay?display.OriginalCanonical:double.TryParse(input.Text,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var shown)&&double.IsFinite(shown)&&double.IsFinite(shown*display.Scale)?ParameterDeclarationRow.N(shown*display.Scale):InvalidGarageDisplay+input.Text;
        }
        foreach(var (id,picker) in _garageRootChoiceInputs) values[id]=picker.SelectedItem is ParameterChoice choice&&(picker.Text==choice.Name||picker.Text==choice.Id)?choice.Id:picker.Text;
        return values;
    }
    private ImmutableArray<ParameterValue> ReadGarageRootParameterValues(GaragePhysicalDraft draft)
    {
        if(draft.RootParameters is null) return [];
        var root=_designLibrary!.LoadAssembly(draft.Root.AssetId,draft.Root.RevisionId)??throw new ArgumentException("The car assembly is missing.");
        var definitions=(root.Parameters??[]).ToDictionary(p=>p.Id,StringComparer.Ordinal);
        return DefinitionParameters.Resolve(definitions.Values,draft.RootParameters.Select(p=>definitions.TryGetValue(p.Key,out var definition)&&definition.Choices is not null?ParameterValue.Choice(p.Key,p.Value):new(p.Key,GarageReadNumber(p.Value,"Car parameter "+p.Key))));
    }
}
