using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using SuspensionSimulation.Design;
using SuspensionSimulation.Solvers.Design;

namespace SuspensionSimulation.App;

internal sealed record GarageHardwareChoice(string Id,string Name);
internal sealed record GaragePhysicalDraft(AssetReference Root,EquipmentSelection[] Equipment,Dictionary<string,string> Adjustments,
    string Policy,string BaseKg,string CgX,string CgY,string CgZ,GaragePayloadRow[] Payloads,GarageMechanicalDraftRow[]? Mechanics=null,Dictionary<string,string>? RootParameters=null);
internal sealed record GarageWorkspaceRecovery(string? SelectedId,Dictionary<string,GaragePhysicalDraft> Drafts);
internal sealed class GaragePayloadRow
{
    public string Id {get;set;}="ballast";public string Kg {get;set;}="20";
    public string Xmm {get;set;}="0";public string Ymm {get;set;}="0";public string Zmm {get;set;}="0";
}

public partial class MainWindow
{
    private PhysicalGarageCatalog? _physicalGarageCatalog;
    private readonly StackPanel _garagePhysicalPanel=new(){Margin=new Thickness(0,8,0,12)};
    private readonly ComboBox _garageRoot=new(){DisplayMemberPath="Identity.Name",SelectedValuePath="Identity.AssetId"};
    private readonly Dictionary<string,GaragePhysicalDraft> _garagePhysicalDrafts=new();
    private readonly Dictionary<string,ComboBox> _garagePhysicalEquipment=new();
    private readonly Dictionary<string,(ResolvedSetupAdjustment Definition,Control Input)> _garagePhysicalAdjustments=new();
    private readonly TextBox _garageBaseMass=new(),_garageCgX=new(),_garageCgY=new(),_garageCgZ=new();
    private readonly ComboBox _garageMassPolicy=new(){ItemsSource=Enum.GetNames<MassAccountingPolicy>()};
    private readonly ObservableCollection<GaragePayloadRow> _garagePayloads=new();
    private DataGrid? _garagePayloadGrid;
    private ResolvedAssemblyGraph? _garagePhysicalGraph;
    private bool _garagePhysicalUpdating;
    private readonly TextBlock _garagePhysicalReadout=new(){TextWrapping=TextWrapping.Wrap,MinHeight=100,Margin=new Thickness(0,8,0,0)};

    private void InitializePhysicalGarage(StackPanel editor)
    {
        var proof=Environment.GetCommandLineArgs().Any(x=>x.Contains("proof",StringComparison.Ordinal));
        _designLibrary??=new DesignAssetLibrary(Path.Combine(App.RepositoryRoot,proof?"artifacts/component-designer/"+App.RunId:"library/design"));
        _physicalGarageCatalog=new(_designLibrary,App.RepositoryRoot);
        _physicalGarageCatalog.Migrate(_garage!);
        if(_assemblyReady) RefreshAssemblyCatalog(); // Stock complete cars are seeded after the editor is built.
        _garageRoot.ToolTip="Imported complete-car assembly. Garage configures only its declared root scalar parameters. Source properties and bump-stop material are read-only. Existing mechanical adjusters retain their legacy exposure model until their ownership migration.";
        editor.Children.Insert(6,_garagePhysicalPanel);
        _garageRoot.SelectionChanged+=(_,_)=>
        {
            if(_garagePhysicalUpdating||_garageSelected is null||_garageRoot.SelectedValue is not string id) return;
            CachePhysicalGarageDraft();var previous=_garagePhysicalDrafts.GetValueOrDefault(_garageSelected.Id)??new GaragePhysicalDraft(new(id),[],[],nameof(MassAccountingPolicy.AggregateBase),"1360.778","51.5","0","460",[]);
            _garagePhysicalDrafts[_garageSelected.Id]=previous with {Root=new(id),Equipment=[],Adjustments=[],Mechanics=null,RootParameters=new()};
            FillPhysicalGarage(_garageSelected);
        };
    }
    private static string GarageAdjustmentKey(string path,string id)=>path+":"+id;
    private static string GarageNumber(double value)=>value.ToString("0.#########",CultureInfo.InvariantCulture);
    private static double GarageReadNumber(string text,string label)
        =>double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out var value)&&double.IsFinite(value)?value:throw new ArgumentException(label+" must be a finite number.");
    private GaragePhysicalDraft DraftFor(GarageEntry entry)
    {
        if(_garagePhysicalDrafts.TryGetValue(entry.Id,out var raw)) return raw;
        var setup=entry.Configuration??_physicalGarageCatalog!.Migrate(entry);
        return new(setup.RootAssembly,setup.Equipment.ToArray(),setup.Adjustments.ToDictionary(a=>GarageAdjustmentKey(a.InstancePath,a.AdjustmentId),a=>a switch
        {ContinuousAdjustmentSelection c=>GarageNumber(c.Fraction),DiscreteAdjustmentSelection d=>d.ChoiceId,_=>""}),
            setup.Mass.Policy.ToString(),setup.Mass.AggregateBaseMassKg is {} kg?GarageNumber(kg):"",GarageNumber(setup.Mass.BaseCenterOfMassM.X*1000),GarageNumber(setup.Mass.BaseCenterOfMassM.Y*1000),GarageNumber(setup.Mass.BaseCenterOfMassM.Z*1000),
            setup.Mass.AddedPayloads.Select(p=>new GaragePayloadRow {Id=p.Id,Kg=GarageNumber(p.MassKg),Xmm=GarageNumber(p.PositionM.X*1000),Ymm=GarageNumber(p.PositionM.Y*1000),Zmm=GarageNumber(p.PositionM.Z*1000)}).ToArray(),setup.MechanicalValues is {} mechanics?MechanicalDraft(mechanics):null,setup.ParameterValues is {} values?values.ToDictionary(p=>p.ParameterId,p=>ParameterDeclarationRow.N(p.Value)):null);
    }
    private void CachePhysicalGarageDraft()
    {
        if(_garagePhysicalUpdating||_garageSelected is not {} entry) return;
        if(_garagePhysicalGraph is null) {if(_garageRootParameterInputs.Count>0) _garagePhysicalDrafts[entry.Id]=DraftFor(entry) with {RootParameters=CaptureGarageRootParameters()};return;}
        _garagePayloadGrid?.CommitEdit();_garagePayloadGrid?.CommitEdit();
        var before=DraftFor(entry);var equipment=before.Equipment.ToList();
        foreach(var slot in _garagePhysicalGraph.EquipmentSlots)
        {
            var key=GarageAdjustmentKey(slot.InstancePath,slot.Definition.Id);
            if(!_garagePhysicalEquipment.TryGetValue(key,out var picker)||picker.SelectedValue is not string id) continue;
            var existing=equipment.SingleOrDefault(e=>e.InstancePath==slot.InstancePath&&e.SlotId==slot.Definition.Id);
            equipment.RemoveAll(e=>e.InstancePath==slot.InstancePath&&e.SlotId==slot.Definition.Id);
            if(existing is not null&&existing.Component.AssetId==id) equipment.Add(existing);
            else equipment.Add(new(slot.InstancePath,slot.Definition.Id,new(id)));
        }
        var adjustments=new Dictionary<string,string>(before.Adjustments);
        foreach(var (key,field) in _garagePhysicalAdjustments)
        {
            var text=field.Input switch {TextBox box=>box.Text,ComboBox combo=>combo.SelectedValue as string??"",_=>"0"};
            // Keep invalid typed input verbatim. Continuous values are stored as normalized fractions.
            if(field.Input is TextBox&&double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out var shown)&&double.IsFinite(shown))
                text=GarageNumber(AdjustmentFromDisplay(field.Definition.Definition.Path,shown));
            else if(field.Input is TextBox) text="invalid:"+text;
            adjustments[key]=text;
        }
        _garagePhysicalDrafts[entry.Id]=new(before.Root,equipment.ToArray(),adjustments,_garageMassPolicy.SelectedItem as string??before.Policy,_garageBaseMass.Text,_garageCgX.Text,_garageCgY.Text,_garageCgZ.Text,_garagePayloads.Select(p=>new GaragePayloadRow {Id=p.Id,Kg=p.Kg,Xmm=p.Xmm,Ymm=p.Ymm,Zmm=p.Zmm}).ToArray(),CaptureMechanicalDraft(),before.RootParameters is not null?CaptureGarageRootParameters():null);
    }
    private static double AdjustmentToDisplay(AdjustmentPath path,double fraction)=>path switch
    {ThreadedAdjustmentPath t=>(t.MinimumLengthM+fraction*(t.MaximumLengthM-t.MinimumLengthM)-t.NominalLengthM)*1000,_=>fraction*100};
    private static double AdjustmentFromDisplay(AdjustmentPath path,double display)=>path switch
    {ThreadedAdjustmentPath t=>(display/1000+t.NominalLengthM-t.MinimumLengthM)/(t.MaximumLengthM-t.MinimumLengthM),_=>display/100};
    private void FillPhysicalGarage(GarageEntry entry)
    {
        _garagePhysicalUpdating=true;_garagePhysicalGraph=null;_garagePhysicalPanel.Children.Clear();_garagePhysicalEquipment.Clear();_garagePhysicalAdjustments.Clear();
        try
        {
            var draft=DraftFor(entry);_garagePhysicalDrafts[entry.Id]=draft;
            var roots=_designLibrary!.Assemblies().Where(a=>a.Vehicle is not null).Select(a=>a.Identity.AssetId==draft.Root.AssetId&&draft.Root.RevisionId is not null?_designLibrary.LoadAssembly(a.Identity.AssetId,draft.Root.RevisionId)??a:a).ToArray();_garageRoot.ItemsSource=roots;_garageRoot.SelectedValue=draft.Root.AssetId;
            void Label(string name)=>_garagePhysicalPanel.Children.Add(new TextBlock {Text=name,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,8,0,3)});
            Label("Physical vehicle assembly");_garagePhysicalPanel.Children.Add(_garageRoot);
            var openCar=new Button {Content="Open complete vehicle in Assembly Editor",Margin=new Thickness(0,4,0,4)};openCar.Click+=(_,_)=>AssemblyAction(OpenGarageVehicle);_garagePhysicalPanel.Children.Add(openCar);
            _garagePhysicalPanel.Children.Add(new TextBlock {Text="Geometry and installation dimensions are synthetic/estimated. Static camber, toe and ride height are calculated from the installed hardware.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,8)});
            FillGarageRootParameters(draft,_garageRoot.SelectedItem as AssemblyDefinition);
            ResolvedAssemblyGraph graph;string? draftConfigurationError=null;
            try {graph=new AssemblyGraphResolver(_designLibrary).Compile(draft.Root,draft.Equipment,ReadGarageRootParameterValues(draft));}
            catch(ArgumentException error) when(entry.Configuration is {ParameterValues:not null} saved&&saved.RootAssembly==draft.Root)
            {
                // Scalar draft errors must not erase other raw fields. Inspect hardware
                // against the saved importer configuration; Save still parses the draft.
                graph=new AssemblyGraphResolver(_designLibrary).Compile(draft.Root,draft.Equipment,entry.Configuration.ParameterValues!.Value);draftConfigurationError=error.Message;
                _garagePhysicalPanel.Children.Add(new TextBlock {Text="Draft configuration is invalid: "+error.Message+"\nHardware readouts below use the saved root configuration. No new result is accepted until the draft is corrected and saved.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,6),Foreground=System.Windows.Media.Brushes.Gold});
            }
            _garagePhysicalGraph=graph;
            FillGarageMechanical(graph,draft);
            foreach(var slot in graph.EquipmentSlots)
            {
                Label(slot.InstancePath+" · "+slot.Definition.Name);
                var choices=slot.Definition.CompatibleAssetIds.Select(id=>new GarageHardwareChoice(id,((slot.Kind==AssetKind.Component?_designLibrary.LoadComponent(id,id==slot.Selected.AssetId?slot.Selected.RevisionId:null)?.Identity.Name:_designLibrary.LoadAssembly(id,id==slot.Selected.AssetId?slot.Selected.RevisionId:null)?.Identity.Name)??id+" (missing)")+(id==slot.Selected.AssetId&&slot.Selected.RevisionId is {} pinned?" · pinned "+pinned[..Math.Min(12,pinned.Length)]:""))).ToArray();
                var picker=new ComboBox {ItemsSource=choices,DisplayMemberPath="Name",SelectedValuePath="Id",SelectedValue=slot.Selected.AssetId,ToolTip=slot.Definition.Description,Margin=new Thickness(0,0,0,4)};
                _garagePhysicalEquipment[GarageAdjustmentKey(slot.InstancePath,slot.Definition.Id)]=picker;_garagePhysicalPanel.Children.Add(picker);
                picker.SelectionChanged+=(_,_)=>
                {
                    if(_garagePhysicalUpdating||_garageSelected is null) return;
                    CachePhysicalGarageDraft();FillPhysicalGarage(_garageSelected);
                };
            }
            Label("Legacy mechanical adjusters · ownership migration pending");
            foreach(var adjustment in graph.SetupAdjustments)
            {
                var a=adjustment.Definition;var key=GarageAdjustmentKey(adjustment.InstancePath,a.Id);var saved=draft.Adjustments.GetValueOrDefault(key);
                Label(adjustment.InstancePath+" · "+a.Name);
                Control input;
                if(a.Path is DiscreteAdjustmentPath holes) input=new ComboBox {ItemsSource=holes.Choices,DisplayMemberPath="Name",SelectedValuePath="Id",SelectedValue=saved??a.Default.ChoiceId};
                else
                {
                    var fraction=saved is not null&&double.TryParse(saved,NumberStyles.Float,CultureInfo.InvariantCulture,out var f)?f:a.Default.Fraction??0;
                    input=new TextBox {Text=saved?.StartsWith("invalid:",StringComparison.Ordinal)==true?saved[8..]:GarageNumber(AdjustmentToDisplay(a.Path,fraction)),IsReadOnly=a.Path is FixedAdjustmentPath};
                }
                input.ToolTip=a.Description+"\n"+(a.Path is ThreadedAdjustmentPath t?$"Offset in mm; bounds {AdjustmentToDisplay(t,0):0.###} to {AdjustmentToDisplay(t,1):0.###}. Positive follows local thread axis.":a.Path is DiscreteAdjustmentPath?"Select a named hardware mounting hole.":"Position along the declared component path, 0–100%. Positive follows that path; alignment is a readout.");
                _garagePhysicalAdjustments[key]=(adjustment,input);_garagePhysicalPanel.Children.Add(input);
            }
            if(graph.SetupAdjustments.Length==0) _garagePhysicalPanel.Children.Add(new TextBlock {Text="Selected stock hardware has no declared setup adjusters.",TextWrapping=TextWrapping.Wrap});
            var unsupported=draft.Adjustments.Keys.Except(_garagePhysicalAdjustments.Keys).ToArray();
            if(unsupported.Length>0)
            {
                _garagePhysicalPanel.Children.Add(new TextBlock {Text="Saved adjustment conflict: "+string.Join(", ",unsupported),TextWrapping=TextWrapping.Wrap});
                var reset=new Button {Content="Remove settings absent from selected hardware"};reset.Click+=(_,_)=>{CachePhysicalGarageDraft();var current=_garagePhysicalDrafts[entry.Id];_garagePhysicalDrafts[entry.Id]=current with {Adjustments=current.Adjustments.Where(x=>!unsupported.Contains(x.Key)).ToDictionary()};FillPhysicalGarage(entry);};_garagePhysicalPanel.Children.Add(reset);
            }
            Label("Mass accounting");_garageMassPolicy.SelectedItem=draft.Policy;_garagePhysicalPanel.Children.Add(_garageMassPolicy);
            _garageMassPolicy.ToolTip="AggregateBase uses the total base mass once plus listed payloads. ComponentSum uses each component's declared mass plus payloads; an aggregate base is forbidden.";
            _garageMassPolicy.SelectionChanged-=GarageMassPolicyChanged;_garageMassPolicy.SelectionChanged+=GarageMassPolicyChanged;
            _garageBaseMass.Text=draft.BaseKg;_garageCgX.Text=draft.CgX;_garageCgY.Text=draft.CgY;_garageCgZ.Text=draft.CgZ;
            foreach(var field in new[]{("Base mass (kg)",_garageBaseMass),("Base CG X (mm, forward +)",_garageCgX),("Base CG Y (mm, left +)",_garageCgY),("Base CG Z (mm, up +)",_garageCgZ)})
            {Label(field.Item1);field.Item2.ToolTip="Base mass/CG in the chassis frame. Reference base includes the workbook driver already; additional payloads add mass once. CG is estimated. Geometry and ride height are not overridden.";_garagePhysicalPanel.Children.Add(field.Item2);}
            _garageBaseMass.IsEnabled=draft.Policy==nameof(MassAccountingPolicy.AggregateBase);
            Label("Added payloads · positions in chassis mm");_garagePayloads.Clear();foreach(var p in draft.Payloads) _garagePayloads.Add(p);
            _garagePayloadGrid=new DataGrid {ItemsSource=_garagePayloads,AutoGenerateColumns=true,CanUserAddRows=true,CanUserDeleteRows=true,MinHeight=90,MaxHeight=190,ToolTip="Each row adds positive mass at the specified chassis position. Remove a row to remove that payload. Do not add the reference driver a second time."};_garagePhysicalPanel.Children.Add(_garagePayloadGrid);
            var solve=new Button {Content="Check saved setup / static readouts",Margin=new Thickness(0,12,0,0)};solve.Click+=async (_,_)=>await CheckPhysicalGarage();_garagePhysicalPanel.Children.Add(solve);_garagePhysicalReadout.Text="Save changes, then check static equilibrium for calculated alignment, loads and ride heights.";_garagePhysicalPanel.Children.Add(_garagePhysicalReadout);
            if(draftConfigurationError is not null) _garageStatus.Text="Invalid root configuration retained; saved hardware preview shown. "+draftConfigurationError;
        }
        catch(Exception error)
        {
            _garageStatus.Text="Physical setup conflict: "+error.Message;App.LogException("Physical Garage fields",error);
            if(_garageRoot.Parent is null)
            {
                _garagePhysicalPanel.Children.Add(new TextBlock {Text="Choose a physical assembly to replace the unresolved reference setup. Existing saved revisions are retained.",TextWrapping=TextWrapping.Wrap});
                _garageRoot.ItemsSource=_designLibrary!.Assemblies().Where(a=>a.Vehicle is not null).ToArray();_garageRoot.SelectedIndex=-1;_garagePhysicalPanel.Children.Add(_garageRoot);
            }
        }
        finally {_garagePhysicalUpdating=false;}
    }
    private void GarageMassPolicyChanged(object sender,SelectionChangedEventArgs e)
    {
        if(_garagePhysicalUpdating) return;
        _garageBaseMass.IsEnabled=_garageMassPolicy.SelectedItem as string==nameof(MassAccountingPolicy.AggregateBase);
    }
    private GarageSetup ReadPhysicalGarage(GarageEntry entry)
    {
        CachePhysicalGarageDraft();var draft=DraftFor(entry);var selections=new List<AdjustmentSelection>();
        if(draft.RootParameters is null) throw new ArgumentException("Legacy recovery values are read-only. Return to the migrated saved car and configure its root parameters.");
        foreach(var value in draft.Adjustments)
        {
            var split=value.Key.LastIndexOf(':');var path=value.Key[..split];var id=value.Key[(split+1)..];
            var definition=_garagePhysicalGraph?.SetupAdjustments.SingleOrDefault(a=>a.InstancePath==path&&a.Definition.Id==id)?.Definition??throw new ArgumentException("Saved adjustment is absent from selected hardware: "+value.Key);
            selections.Add(definition.Path is DiscreteAdjustmentPath?new DiscreteAdjustmentSelection(path,id,value.Value):new ContinuousAdjustmentSelection(path,id,GarageReadNumber(value.Value,"Adjustment "+value.Key)));
        }
        var policy=Enum.Parse<MassAccountingPolicy>(draft.Policy);var fields=_workspaceDrafts[Workspace.Garage];
        var original=entry.Configuration??_physicalGarageCatalog!.Create(fields.Name.Text);
        var setup=original with {Identity=original.Identity with {Name=fields.Name.Text},RootAssembly=draft.Root,Equipment=[..draft.Equipment],Adjustments=[..selections],ParameterValues=draft.RootParameters is not null?ReadGarageRootParameterValues(draft):null,MechanicalValues=draft.RootParameters is null?ReadMechanicalValues(draft.Mechanics??throw new ArgumentException("Review the car's spring/bar configuration first.")):null,
            Mass=new(policy,policy==MassAccountingPolicy.AggregateBase?GarageReadNumber(draft.BaseKg,"Base mass"):null,new(GarageReadNumber(draft.CgX,"CG X")/1000,GarageReadNumber(draft.CgY,"CG Y")/1000,GarageReadNumber(draft.CgZ,"CG Z")/1000),
                [..draft.Payloads.Select(p=>new PayloadMass(p.Id,GarageReadNumber(p.Kg,"Payload mass"),new(GarageReadNumber(p.Xmm,"Payload X")/1000,GarageReadNumber(p.Ymm,"Payload Y")/1000,GarageReadNumber(p.Zmm,"Payload Z")/1000)))])};
        _=new GarageSetupResolver(_designLibrary!).Resolve(setup);return setup;
    }
    private async Task CheckPhysicalGarage()
    {
        try
        {
            var entry=_garageSelected??throw new InvalidOperationException("Select a saved setup.");var saved=_garage!.Load(entry.Id)!;
            var graph=new GarageSetupResolver(_designLibrary!).Resolve(saved.Configuration??throw new InvalidOperationException("Save a physical assembly first."));
            _garagePhysicalReadout.Text="Solving saved revision "+saved.Revision+"…";
            var result=await Task.Run(()=>new InstalledEquilibrium(graph).Solve(new(saved.Configuration.Mass)));
            if(_garageSelected?.Id!=saved.Id||_garage.Load(saved.Id)!.Revision!=saved.Revision) return;
            _garagePhysicalReadout.Text=$"Saved revision {saved.Revision} · {result.TotalMassKg:0.0} kg · CG height {result.CenterOfMassWorldM.Z*1000:0.0} mm\n"+string.Join("\n",result.Wheels.Select((w,i)=>$"{w.Corner}: camber {w.CamberRad*180/Math.PI:0.000}° · heading {w.HeadingRad*180/Math.PI:0.000}° · load {result.NormalLoadsN[i]:0.0} N"))+"\n"+string.Join(" · ",result.RideHeights.Select(h=>$"{h.Id} {h.HeightM*1000:0.0} mm"));
        }
        catch(Exception error) {_garagePhysicalReadout.Text="No accepted static result: "+error.Message;App.LogException("Garage static check",error);}
    }
    private GarageWorkspaceRecovery CapturePhysicalGarageWorkspace()
    {
        CachePhysicalGarageDraft();return new(_garageSelected?.Id,new(_garagePhysicalDrafts));
    }
    private void RestorePhysicalGarageWorkspace(JsonElement json)
    {
        var recovery=JsonSerializer.Deserialize<GarageWorkspaceRecovery>(json.GetRawText());if(recovery is null) return;
        foreach(var item in recovery.Drafts)
        {
            var raw=item.Value;
            try {if(_garage!.Load(item.Key) is {} entry) raw=UpgradeGarageParameterDraft(entry,raw);}
            catch(Exception error) when(error is ArgumentException or InvalidOperationException or IOException)
            {
                // Keep the exact old text (including invalid fields) as recovery evidence.
                _designLibrary!.SaveDraft("garage-legacy-"+item.Key+"-"+App.RunId,JsonSerializer.Serialize(raw));App.LogException("Legacy Garage draft needs explicit recovery review",error);
            }
            _garagePhysicalDrafts[item.Key]=raw;
        }
        _garageSelected=null;_garagePhysicalGraph=null;RefreshGarage(recovery.SelectedId);
    }
}
