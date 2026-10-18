using System.Collections.Immutable;
using System.Windows;
using System.Windows.Controls;
using SuspensionSimulation.Design;
using SuspensionSimulation.Solvers.Design;

namespace SuspensionSimulation.App;

public partial class MainWindow
{
    private readonly Dictionary<string,TextBox> _garageRootParameterInputs=new();
    private GaragePhysicalDraft UpgradeGarageParameterDraft(GarageEntry entry,GaragePhysicalDraft raw)
    {
        if(raw.RootParameters is not null) return raw;
        var saved=entry.Configuration??throw new ArgumentException("The saved car has no assembly.");
        var legacy=saved with {RootAssembly=raw.Root,Equipment=[..raw.Equipment],Adjustments=[],ParameterValues=null,MechanicalValues=raw.Mechanics is {} old?ReadMechanicalValues(old):null};
        var configured=_physicalGarageCatalog!.Migrate(entry with {Configuration=legacy});
        return raw with {Root=configured.RootAssembly,Equipment=configured.Equipment.ToArray(),Mechanics=null,RootParameters=configured.ParameterValues!.Value.ToDictionary(p=>p.ParameterId,p=>ParameterDeclarationRow.N(p.Value))};
    }
    private void FillGarageRootParameters(GaragePhysicalDraft draft,AssemblyDefinition? root)
    {
        _garageRootParameterInputs.Clear();if(draft.RootParameters is null&&(root?.Parameters??[]).IsEmpty) return;
        var panel=new StackPanel {Margin=new Thickness(4)};panel.SetValue(System.Windows.Documents.TextElement.ForegroundProperty,System.Windows.Media.Brushes.White);panel.Children.Add(new TextBlock {Text="Garage configures only parameters declared by this complete-car assembly. Descendant properties and bump-stop material are read-only. Each forwarding boundary is owned in Assembly Editor.",TextWrapping=TextWrapping.Wrap});
        foreach(var p in root?.Parameters??[])
        {
            panel.Children.Add(new TextBlock {Text=$"{p.Name} [{p.Unit}] · {p.Minimum:R}…{p.Maximum:R}",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,2)});
            var box=new TextBox {Text=draft.RootParameters?.GetValueOrDefault(p.Id," ")??"",ToolTip=p.Description+"\nRoot parameter ID: "+p.Id};_garageRootParameterInputs[p.Id]=box;panel.Children.Add(box);
        }
        var apply=new Button {Content="Apply root configuration to hardware preview",Margin=new Thickness(0,8,0,4)};apply.Click+=(_,_)=>{if(_garageSelected is {} entry) {_garagePhysicalDrafts[entry.Id]=DraftFor(entry) with {RootParameters=CaptureGarageRootParameters()};FillPhysicalGarage(entry);}};panel.Children.Add(apply);
        _garagePhysicalPanel.Children.Add(new Expander {Header="Complete-car configuration parameters",Foreground=System.Windows.Media.Brushes.White,IsExpanded=true,Content=panel});
    }
    private Dictionary<string,string> CaptureGarageRootParameters()=>_garageRootParameterInputs.ToDictionary(p=>p.Key,p=>p.Value.Text,StringComparer.Ordinal);
    private ImmutableArray<ParameterValue> ReadGarageRootParameterValues(GaragePhysicalDraft draft)
    {
        if(draft.RootParameters is null) return [];
        var root=_designLibrary!.LoadAssembly(draft.Root.AssetId,draft.Root.RevisionId)??throw new ArgumentException("The car assembly is missing.");
        return DefinitionParameters.Resolve(root.Parameters??[],draft.RootParameters.Select(p=>new ParameterValue(p.Key,GarageReadNumber(p.Value,"Car parameter "+p.Key))));
    }
}
