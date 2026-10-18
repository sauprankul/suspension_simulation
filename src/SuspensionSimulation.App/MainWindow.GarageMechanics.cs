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
        var draft=_garageSelected is {} entry?DraftFor(entry):null;var preview=draft is null?[]:ReadGarageRootParameterValues(draft);
        NavigateWorkspace(Workspace.AssemblyEditor);RefreshAssemblyCatalog();_assemblyExpectedDof.Text="5";LoadAssembly(selected,preview);
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
        if(draft.RootParameters is not null)
        {
            _garageMechanicalRows=[];
            foreach(var body in graph.Bodies.Where(b=>b.Component.Chassis is not null)) _garagePhysicalPanel.Children.Add(new Expander {Header="Chassis properties (source owned; read only)",Foreground=System.Windows.Media.Brushes.White,Content=new TextBlock {Text=ChassisDescription(body.Component.Chassis!),Foreground=System.Windows.Media.Brushes.White,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(4)}});
            var readout=string.Join("\n",graph.ElasticElements.Where(e=>e.Component.Behavior is CompressionSpringBehavior or TorsionBarBehavior).Select(e=>e.Component.Behavior switch {CompressionSpringBehavior s=>$"{e.Path}: {s.RateKgfPerMm:0.######} kgf/mm",TorsionBarBehavior b=>$"{e.Path}: OD {b.OutsideDiameterM*1000:0.###} / ID {b.InsideDiameterM*1000:0.###} mm · G {b.ShearModulusPa/1e9:0.######} GPa",_=>""}));
            _garagePhysicalPanel.Children.Add(new Expander {Header="Resolved hardware values (read only here)",Foreground=System.Windows.Media.Brushes.White,Content=new TextBlock {Text=readout,Foreground=System.Windows.Media.Brushes.White,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(4)}});return;
        }
        var defaults=MechanicalDraft(GarageMechanicalConfiguration.Capture(graph));
        var saved=draft.Mechanics??defaults;
        _garageMechanicalRows=saved.Concat(defaults.Where(d=>!saved.Any(s=>s.Path==d.Path))).ToArray();
        var panel=new StackPanel {Margin=new Thickness(4)};panel.SetValue(System.Windows.Documents.TextElement.ForegroundProperty,System.Windows.Media.Brushes.White);
        panel.Children.Add(new TextBlock {Text="Legacy recovery snapshot only. These old descendant values are read-only. Reopen the migrated saved car to configure its explicit root parameters; the original raw draft is retained. Source properties cannot be overridden here.",TextWrapping=TextWrapping.Wrap});
        foreach(var row in _garageMechanicalRows)
        {
            panel.Children.Add(new TextBlock {Text=row.Path,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,8,0,2)});
            void Field(string id,string label,string value) {panel.Children.Add(new TextBlock {Text=label});var input=new TextBox {Text=value,IsReadOnly=true,Margin=new Thickness(0,1,0,4)};panel.Children.Add(input);_garageMechanicalInputs[row.Path+":"+id]=input;}
            if(row.Kind=="spring") Field("rate","Spring rate (kgf/mm; positive)",row.Rate);
            else {Field("od","Bar OD (mm; positive)",row.Od);Field("id","Bar ID (mm; 0 ≤ ID < OD)",row.Id);Field("g","Effective shear modulus (GPa; positive)",row.G);}
        }
        var savedButton=new Button {Content="Return to migrated saved car",Margin=new Thickness(0,8,0,4)};
        savedButton.Click+=(_,_)=>{if(_garageSelected is {} entry) {_designLibrary!.SaveDraft("garage-legacy-"+entry.Id+"-"+App.RunId,System.Text.Json.JsonSerializer.Serialize(DraftFor(entry)));_garagePhysicalDrafts.Remove(entry.Id);FillPhysicalGarage(entry);}};panel.Children.Add(savedButton);
        _garagePhysicalPanel.Children.Add(new Expander {Header="Legacy recovery values (read only)",Foreground=System.Windows.Media.Brushes.White,IsExpanded=true,Content=panel});
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
