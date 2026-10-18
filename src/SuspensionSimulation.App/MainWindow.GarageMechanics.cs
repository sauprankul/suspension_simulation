using System.Collections.Immutable;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using SuspensionSimulation.Design;
using SuspensionSimulation.Solvers.Design;

namespace SuspensionSimulation.App;

internal sealed record GarageMechanicalDraftRow(string Path,string Kind,string Rate,string G,string Od,string Id);

public partial class MainWindow
{
    private void OpenGarageVehicle()
    {
        var selected=_garageRoot.SelectedItem as AssemblyDefinition??throw new ArgumentException("Choose a vehicle assembly.");
        NavigateWorkspace(Workspace.AssemblyEditor);RefreshAssemblyCatalog();_assemblyExpectedDof.Text="5";LoadAssembly(selected);
    }
    private readonly Dictionary<string,TextBox> _garageMechanicalInputs=new();
    private GarageMechanicalDraftRow[] _garageMechanicalRows=[];
    private static string MechanicalNumber(double value)=>value.ToString("R",CultureInfo.InvariantCulture);
    private static GarageMechanicalDraftRow[] MechanicalDraft(IEnumerable<InstanceMechanicalValue> values)=>values.Select(v=>v switch
    {
        InstanceSpringRate s=>new GarageMechanicalDraftRow(s.InstancePath,"spring",MechanicalNumber(s.RateKgfPerMm),"","",""),
        InstanceTorsionSection b=>new GarageMechanicalDraftRow(b.InstancePath,"bar","",MechanicalNumber(b.ShearModulusPa/1e9),MechanicalNumber(b.OutsideDiameterM*1000),MechanicalNumber(b.InsideDiameterM*1000)),
        _=>throw new ArgumentException("Unsupported car mechanical configuration.")
    }).ToArray();
    private void FillGarageMechanical(ResolvedAssemblyGraph graph,GaragePhysicalDraft draft)
    {
        _garageMechanicalInputs.Clear();
        var defaults=MechanicalDraft(GarageMechanicalConfiguration.Capture(graph));
        var saved=draft.Mechanics??defaults;
        _garageMechanicalRows=saved.Concat(defaults.Where(d=>!saved.Any(s=>s.Path==d.Path))).ToArray();
        var panel=new StackPanel {Margin=new Thickness(4)};
        panel.Children.Add(new TextBlock {Text="These values belong to this saved car instance. Source part edits do not replace them. Springs use kgf/mm; bars use mm and effective shear modulus in GPa. Camber, toe and ride height remain calculated.",TextWrapping=TextWrapping.Wrap});
        foreach(var row in _garageMechanicalRows)
        {
            panel.Children.Add(new TextBlock {Text=row.Path,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,8,0,2)});
            void Field(string id,string label,string value) {panel.Children.Add(new TextBlock {Text=label});var input=new TextBox {Text=value,Margin=new Thickness(0,1,0,4)};panel.Children.Add(input);_garageMechanicalInputs[row.Path+":"+id]=input;}
            if(row.Kind=="spring") Field("rate","Spring rate (kgf/mm; positive)",row.Rate);
            else {Field("od","Bar OD (mm; positive)",row.Od);Field("id","Bar ID (mm; 0 ≤ ID < OD)",row.Id);Field("g","Effective shear modulus (GPa; positive)",row.G);}
        }
        var defaultsButton=new Button {Content="Copy values from selected hardware defaults",Margin=new Thickness(0,8,0,4)};
        defaultsButton.Click+=(_,_)=>{CachePhysicalGarageDraft();if(_garageSelected is {} entry) {_garagePhysicalDrafts[entry.Id]=DraftFor(entry) with {Mechanics=defaults};FillPhysicalGarage(entry);}};panel.Children.Add(defaultsButton);
        _garagePhysicalPanel.Children.Add(new Expander {Header="Car spring rates and torsion sections",IsExpanded=true,Content=panel});
    }
    private GarageMechanicalDraftRow[] CaptureMechanicalDraft()=>_garageMechanicalRows.Select(row=>
    {
        string Value(string id,string fallback)=>_garageMechanicalInputs.TryGetValue(row.Path+":"+id,out var field)?field.Text:fallback;
        return row with {Rate=Value("rate",row.Rate),G=Value("g",row.G),Od=Value("od",row.Od),Id=Value("id",row.Id)};
    }).ToArray();
    private static ImmutableArray<InstanceMechanicalValue> ReadMechanicalValues(GarageMechanicalDraftRow[] rows)=>[..rows.Select(row=>row.Kind switch
    {
        "spring"=>(InstanceMechanicalValue)new InstanceSpringRate(row.Path,GarageReadNumber(row.Rate,"Spring rate")),
        "bar"=>new InstanceTorsionSection(row.Path,GarageReadNumber(row.G,"Shear modulus")*1e9,GarageReadNumber(row.Od,"Bar OD")/1000,GarageReadNumber(row.Id,"Bar ID")/1000),
        _=>throw new ArgumentException("Unknown mechanical setting type.")
    })];
}
