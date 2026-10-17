using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SuspensionSimulation.Components;
using SuspensionSimulation.Design;
using SuspensionSimulation.Kinematics;
using SuspensionSimulation.Solvers.Design;

namespace SuspensionSimulation.App;

internal sealed record AssemblyRecovery(string Id,string? BaseRevision,string Name,string Notes,string Grounds,string ExpectedDof,
    AssemblyInstanceRow[] Instances,AssemblyJointRow[] Joints,AssemblyPortRow[] Ports);
internal sealed record AssemblyCatalogChoice(string Name,AssetReference Reference,bool IsAssembly);

public partial class MainWindow
{
    private InspectionViewport? _assemblyViewport;
    private AssemblyDefinition? _assemblyDefinition;
    private ResolvedAssemblyGraph? _assemblyGraph;
    private string _assemblyId=Guid.NewGuid().ToString("N");private string? _assemblyBaseRevision;
    private bool _assemblyReady,_assemblyUpdating,_assemblyDirty;
    private string? _assemblyRecoveryAtOpen,_assemblyAcceptedRaw;
    private string? _assemblyPublishedRaw,_assemblyConflict;
    private readonly Stack<string> _assemblyUndo=new(),_assemblyRedo=new();
    private readonly ListBox _assemblyLibraryList=new(){DisplayMemberPath="Identity.Name",SelectedValuePath="Identity.AssetId",MinHeight=90,MaxHeight=160};
    private readonly ComboBox _assemblyInsertChoice=new(){DisplayMemberPath="Name",Margin=new Thickness(0,5,0,4)};
    private readonly TextBox _assemblyGrounds=new(),_assemblyExpectedDof=new(){Text="1"};
    private readonly TextBlock _assemblyStatus=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)},_assemblyDiagnostics=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(10)};
    private readonly TextBlock _assemblyTree=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(10)};
    private readonly ObservableCollection<AssemblyInstanceRow> _assemblyInstances=new();
    private readonly ObservableCollection<AssemblyJointRow> _assemblyJoints=new();private readonly ObservableCollection<AssemblyPortRow> _assemblyPorts=new();
    private readonly List<DataGrid> _assemblyGrids=new();private DataGrid? _assemblyInstanceGrid;
    private readonly DispatcherTimer _assemblyAutosave=new(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(350)};
    private readonly ComboBox _assemblyMateA=new(),_assemblyMateB=new();

    private void BuildAssemblyEditor()
    {
        var proof=Environment.GetCommandLineArgs().Any(x=>x.Contains("proof",StringComparison.Ordinal));
        _designLibrary??=new DesignAssetLibrary(Path.Combine(App.RepositoryRoot,proof?"artifacts/component-designer/"+App.RunId:"library/design"));
        var old=_workspaceViews[Workspace.AssemblyEditor];_shell.Children.Remove(old);var metadata=_workspaceDrafts[Workspace.AssemblyEditor];
        ((Panel)metadata.Name.Parent).Children.Remove(metadata.Name);((Panel)metadata.Notes.Parent).Children.Remove(metadata.Notes);
        metadata.Name.MinWidth=0;metadata.Notes.MinWidth=0;metadata.Notes.MinHeight=65;metadata.Name.Text="New assembly";metadata.Notes.Text="Synthetic assembly; unvalidated physical mechanism.";
        var layout=new Grid {Margin=new Thickness(12)};layout.ColumnDefinitions.Add(new(){Width=new GridLength(255)});layout.ColumnDefinitions.Add(new());layout.ColumnDefinitions.Add(new(){Width=new GridLength(510)});
        var left=new StackPanel {Margin=new Thickness(0,0,8,0)};
        void Button(string text,Action action) {var b=new Button {Content=text,Padding=new Thickness(5,3,5,3),Margin=new Thickness(0,3,0,3)};b.Click+=(_,_)=>AssemblyAction(action);left.Children.Add(b);}
        Button("Main menu",()=>NavigateWorkspace(Workspace.MainMenu));left.Children.Add(new TextBlock {Text="Assembly Editor",FontSize=22,FontWeight=FontWeights.Bold,Margin=new Thickness(0,8,0,8)});
        left.Children.Add(_assemblyLibraryList);Button("Open selected assembly",OpenSelectedAssembly);Button("Refresh latest dependencies",RefreshAssemblyDependencies);
        Button("New empty assembly",NewAssembly);Button("Load synthetic corner example",()=>{var a=SyntheticAssemblyCatalog.SeedCorner(_designLibrary);RefreshAssemblyCatalog();LoadAssembly(a);});
        left.Children.Add(new TextBlock {Text="Name"});left.Children.Add(metadata.Name);left.Children.Add(new TextBlock {Text="Insert from library"});left.Children.Add(_assemblyInsertChoice);Button("Insert selected part / assembly",InsertAssemblyItem);Button("Refresh library",RefreshAssemblyCatalog);
        Button("Group selected instances",GroupAssemblySelection);Button("Mirror selected across Y = 0",MirrorAssemblySelection);
        left.Children.Add(new TextBlock {Text="Grounded body/subtree paths (comma separated)",TextWrapping=TextWrapping.Wrap});left.Children.Add(_assemblyGrounds);
        left.Children.Add(new TextBlock {Text="Expected operating freedoms (blank = inspect)"});left.Children.Add(_assemblyExpectedDof);
        Button("Apply / check mobility",()=>ApplyAssemblyPreview(true));Button("Undo",()=>UndoAssembly(false));Button("Redo",()=>UndoAssembly(true));Button("Publish revision",PublishAssembly);Button("Export frozen assembly",ExportAssembly);Button("Import independent assembly",ImportAssembly);Button("Recover last draft",RecoverAssembly);
        left.Children.Add(new TextBlock {Text="Provenance notes (synthetic)",Margin=new Thickness(0,8,0,0)});left.Children.Add(metadata.Notes);left.Children.Add(_assemblyStatus);
        layout.Children.Add(new ScrollViewer {Content=left,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        _assemblyViewport=new InspectionViewport(false);Grid.SetColumn(_assemblyViewport,1);layout.Children.Add(_assemblyViewport);
        _assemblyViewport.Failed+=error=>{_assemblyStatus.Text="Preview unavailable: "+error.Message;_assemblyViewport.SetActive(false);};
        var tabs=new TabControl {Margin=new Thickness(8,0,0,0)};Grid.SetColumn(tabs,2);layout.Children.Add(tabs);
        _assemblyInstanceGrid=AddGrid("Instances",_assemblyInstances);_assemblyInstanceGrid.SelectionMode=DataGridSelectionMode.Extended;
        AddGrid("Joints",_assemblyJoints);AddGrid("Exports",_assemblyPorts);
        var checks=new StackPanel();checks.Children.Add(_assemblyDiagnostics);checks.Children.Add(_assemblyTree);tabs.Items.Add(new TabItem {Header="Checks / tree",Content=new ScrollViewer {Content=checks,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}});
        var mate=new StackPanel {Margin=new Thickness(10)};mate.Children.Add(new TextBlock {Text="Align a direct child instance to a stationary port. This changes its declared placement; it does not add a joint. Add the actual joint on the Joints tab.",TextWrapping=TextWrapping.Wrap});
        mate.Children.Add(new TextBlock {Text="Stationary port"});mate.Children.Add(_assemblyMateA);mate.Children.Add(new TextBlock {Text="Moving port"});mate.Children.Add(_assemblyMateB);
        var align=new Button {Content="Align port frames",Margin=new Thickness(0,8,0,8)};align.Click+=(_,_)=>AssemblyAction(MateAssemblyPorts);mate.Children.Add(align);
        mate.Children.Add(new TextBlock {Text="Instance coordinates and mirror origins are mm. Axis and reference vectors are orthogonal unit vectors (+Z and +X). Empty revision follows latest; an exact hash pins a version. Nested joints appear in the checks/tree view.",TextWrapping=TextWrapping.Wrap});
        tabs.Items.Add(new TabItem {Header="Mate",Content=mate});
        _workspaceViews[Workspace.AssemblyEditor]=layout;layout.Visibility=Visibility.Collapsed;_shell.Children.Add(layout);
        _assemblyAutosave.Tick+=(_,_)=>{_assemblyAutosave.Stop();try {_designLibrary.SaveDraft("assembly-workspace",CaptureAssemblyRaw());}catch(Exception error) {_assemblyStatus.Text="Draft recovery could not save: "+error.Message;}};
        foreach(var box in new[]{metadata.Name,metadata.Notes,_assemblyGrounds,_assemblyExpectedDof}) box.TextChanged+=(_,_)=>ScheduleAssemblyDraft();
        _assemblyInstances.CollectionChanged+=(_,_)=>ScheduleAssemblyDraft();_assemblyJoints.CollectionChanged+=(_,_)=>ScheduleAssemblyDraft();_assemblyPorts.CollectionChanged+=(_,_)=>ScheduleAssemblyDraft();
        AssemblyAction(RefreshAssemblyCatalog);NewAssembly();_assemblyRecoveryAtOpen=_designLibrary.ReadDraft("assembly-workspace");_assemblyReady=true;
        if(_assemblyRecoveryAtOpen is not null) _assemblyStatus.Text="A recovery draft is available. Choose Recover last draft to open it.";else ScheduleAssemblyDraft();
        DataGrid AddGrid<T>(string title,ObservableCollection<T> rows)
        {
            var grid=new DataGrid {ItemsSource=rows,AutoGenerateColumns=true,CanUserAddRows=true,CanUserDeleteRows=true,HeadersVisibility=DataGridHeadersVisibility.Column,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto};
            grid.CellEditEnding+=(_,_)=>Dispatcher.BeginInvoke(ScheduleAssemblyDraft);_assemblyGrids.Add(grid);tabs.Items.Add(new TabItem {Header=title,Content=grid});return grid;
        }
    }
    private void AssemblyAction(Action action) {try {action();}catch(Exception error) {_assemblyStatus.Text=error.Message;App.LogException("Assembly Editor",error);}}
    private void ScheduleAssemblyDraft() {if(_assemblyReady&&!_assemblyUpdating) {_assemblyDirty=true;_assemblyAutosave.Stop();_assemblyAutosave.Start();}}
    private AssemblyDefinition ReadAssembly()
    {
        foreach(var grid in _assemblyGrids) {grid.CommitEdit();grid.CommitEdit();}
        var metadata=_workspaceDrafts[Workspace.AssemblyEditor];
        return new(new(_assemblyId,"draft",metadata.Name.Text,new(DataProvenance.Synthetic,metadata.Notes.Text,null)),[.._assemblyInstances.Select(x=>x.Read())],[.._assemblyJoints.Select(x=>x.Read())],[.._assemblyPorts.Select(x=>x.Read())])
            {GroundedInstances=[.._assemblyGrounds.Text.Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries)]};
    }
    private int? AssemblyExpectedDof()=>string.IsNullOrWhiteSpace(_assemblyExpectedDof.Text)?null:int.TryParse(_assemblyExpectedDof.Text,out var n)&&n>=0?n:throw new ArgumentException("Expected freedoms must be a nonnegative integer or blank.");
    private void ApplyAssemblyPreview(bool undo)
    {
        var definition=ReadAssembly();var raw=CaptureAssemblyRaw();var resolver=new AssemblyGraphResolver(_designLibrary!);
        var graph=raw==_assemblyPublishedRaw&&_assemblyBaseRevision is not null?resolver.Compile(new(_assemblyId,_assemblyBaseRevision)):resolver.CompileDraft(definition);var report=MechanismMobility.Inspect(graph,AssemblyExpectedDof());
        if(undo&&_assemblyAcceptedRaw is {} before&&before!=raw) {_assemblyUndo.Push(before);_assemblyRedo.Clear();}
        _assemblyDefinition=definition;_assemblyGraph=graph;_assemblyAcceptedRaw=raw;ShowAssemblyGraph(graph,report);ScheduleAssemblyDraft();
    }
    private void ShowAssemblyGraph(ResolvedAssemblyGraph graph,MechanismMobilityReport report)
    {
        _assemblyViewport!.SetScene(AssemblyScene(graph));
        _assemblyDiagnostics.Text=$"Operating freedoms: {report.DegreesOfFreedom}\nJacobian rank: {report.Rank} / {report.FreeCoordinates}\nRedundant rows: {report.RedundantRows}\nJoint closure error: {report.MaximumScaledClosureError:0.###E+0} (length scale {report.CharacteristicLengthM*1000:0.###} mm)\n\n"+string.Join("\n",report.Diagnostics);
        _assemblyTree.Text="Resolved bodies\n"+string.Join("\n",graph.Bodies.Select(x=>$"{x.Path} · {x.Component.Identity.Name} · {x.Component.Identity.RevisionId[..12]}"))+"\n\nJoints\n"+string.Join("\n",graph.Joints.Select(x=>$"{x.Path} · {x.Kind}"));
        var selectedA=_assemblyMateA.SelectedItem as string;var selectedB=_assemblyMateB.SelectedItem as string;
        var choices=graph.Interfaces.Keys.Order(StringComparer.Ordinal).ToArray();_assemblyMateA.ItemsSource=choices;_assemblyMateB.ItemsSource=choices;
        _assemblyMateA.SelectedItem=selectedA;_assemblyMateB.SelectedItem=selectedB;
        _assemblyStatus.Text=$"{graph.Bodies.Length} bodies · {report.DegreesOfFreedom} operating freedoms · "+(report.IsClosed?"joint frames close":"joint frames need mating")+". No vehicle equilibrium is run.";
    }
    private void NewAssembly()
    {
        _assemblyId=Guid.NewGuid().ToString("N");_assemblyBaseRevision=null;_assemblyGraph=null;_assemblyUndo.Clear();_assemblyRedo.Clear();_assemblyAcceptedRaw=null;_assemblyPublishedRaw=null;_assemblyConflict=null;
        _workspaceDrafts[Workspace.AssemblyEditor].Name.Text="New assembly";_assemblyInstances.Clear();_assemblyJoints.Clear();_assemblyPorts.Clear();_assemblyGrounds.Text="";ApplyAssemblyPreview(false);
    }
    private void RefreshAssemblyCatalog()
    {
        var selected=_assemblyInsertChoice.SelectedItem as AssemblyCatalogChoice;_assemblyLibraryList.ItemsSource=_designLibrary!.Assemblies().OrderBy(x=>x.Identity.Name).ToArray();_assemblyLibraryList.SelectedValue=_assemblyId;
        _assemblyInsertChoice.ItemsSource=_designLibrary.Components().Select(x=>new AssemblyCatalogChoice("Part · "+x.Identity.Name,new(x.Identity.AssetId),false))
            .Concat(_designLibrary.Assemblies().Where(x=>x.Identity.AssetId!=_assemblyId).Select(x=>new AssemblyCatalogChoice("Assembly · "+x.Identity.Name,new(x.Identity.AssetId),true))).OrderBy(x=>x.Name).ToArray();
        if(selected is not null) _assemblyInsertChoice.SelectedItem=selected;else _assemblyInsertChoice.SelectedIndex=0;
    }
    private void OpenSelectedAssembly()=>LoadAssembly(_assemblyLibraryList.SelectedItem as AssemblyDefinition??throw new ArgumentException("Select a published assembly."));
    private void LoadAssembly(AssemblyDefinition a)
    {
        ResolvedAssemblyGraph? previous=null;
        var cached=_designLibrary!.ReadDraft(AssemblyCacheKey(a.Identity.AssetId));
        if(cached is not null) {try {previous=AssemblyGraphResolver.OpenBundle(DesignJson.Deserialize<PortableDesignBundle>(cached));}catch(Exception error) {App.LogException("Stored assembly fallback",error);}}
        var resolved=new AssemblyGraphResolver(_designLibrary).Resolve(new(a.Identity.AssetId,a.Identity.RevisionId),previous);
        if(resolved.Graph is null) throw new InvalidOperationException(resolved.Conflict);
        _assemblyId=a.Identity.AssetId;_assemblyBaseRevision=a.Identity.RevisionId;_assemblyUndo.Clear();_assemblyRedo.Clear();_assemblyAcceptedRaw=null;FillAssembly(a);
        _assemblyDefinition=a;_assemblyGraph=resolved.Graph;_assemblyConflict=resolved.Conflict;_assemblyPublishedRaw=_assemblyAcceptedRaw=CaptureAssemblyRaw();
        ShowAssemblyGraph(_assemblyGraph,MechanismMobility.Inspect(_assemblyGraph,AssemblyExpectedDof()));RefreshAssemblyCatalog();
        if(resolved.IsStale) _assemblyStatus.Text="Dependency conflict; stored graph retained. "+resolved.Conflict;else SaveAssemblyGraph();ScheduleAssemblyDraft();
    }
    private void FillAssembly(AssemblyDefinition a)
    {
        _assemblyUpdating=true;
        try
        {
            var metadata=_workspaceDrafts[Workspace.AssemblyEditor];metadata.Name.Text=a.Identity.Name;metadata.Notes.Text=a.Identity.Provenance.Note;
            _assemblyInstances.Clear();foreach(var x in a.Instances) _assemblyInstances.Add(AssemblyInstanceRow.From(x));_assemblyJoints.Clear();foreach(var x in a.Joints) _assemblyJoints.Add(AssemblyJointRow.From(x));
            _assemblyPorts.Clear();foreach(var x in a.Ports) _assemblyPorts.Add(AssemblyPortRow.From(x));_assemblyGrounds.Text=string.Join(",",a.GroundedInstances);
        }
        finally {_assemblyUpdating=false;}
    }
    private void InsertAssemblyItem()
    {
        var choice=_assemblyInsertChoice.SelectedItem as AssemblyCatalogChoice??throw new ArgumentException("Select a library item.");
        var id="item-"+Guid.NewGuid().ToString("N")[..6];PhysicalInstance item=choice.IsAssembly?new AssemblyInstance(id,choice.Reference,AssemblyAuthoring.IdentityPlacement):new ComponentInstance(id,choice.Reference,AssemblyAuthoring.IdentityPlacement);
        _assemblyInstances.Add(AssemblyInstanceRow.From(item));ApplyAssemblyPreview(true);
    }
    private void GroupAssemblySelection()
    {
        var selected=_assemblyInstanceGrid!.SelectedItems.OfType<AssemblyInstanceRow>().Select(x=>x.Id).ToArray();var parent=ReadAssembly();var id="group-"+Guid.NewGuid().ToString("N")[..8];
        var result=AssemblyAuthoring.Group(parent,selected,parent.Identity with {AssetId=Guid.NewGuid().ToString("N"),Name="Group "+string.Join(" + ",selected)},id);
        _designLibrary!.Publish(result.Child,null);FillAssembly(result.Parent);ApplyAssemblyPreview(true);RefreshAssemblyCatalog();
    }
    private void MirrorAssemblySelection()
    {
        var selected=_assemblyInstanceGrid!.SelectedItem as AssemblyInstanceRow??throw new ArgumentException("Select one instance to mirror.");var a=ReadAssembly();var id=selected.Id+"-mirror-"+Guid.NewGuid().ToString("N")[..4];
        a=AssemblyAuthoring.MirrorInstance(a,selected.Id,id,new(new(0,0,0),new(0,1,0)));
        a=a with {GroundedInstances=a.GroundedInstances.AddRange(a.GroundedInstances.Where(p=>p==selected.Id||p.StartsWith(selected.Id+"/",StringComparison.Ordinal)).Select(p=>id+p[selected.Id.Length..]))};
        FillAssembly(a);ApplyAssemblyPreview(true);
    }
    private void PublishAssembly()
    {
        ApplyAssemblyPreview(true);var saved=_designLibrary!.Publish(_assemblyDefinition!,_assemblyBaseRevision);_assemblyBaseRevision=saved.Identity.RevisionId;
        FillAssembly(saved);
        _assemblyDefinition=saved;_assemblyGraph=new AssemblyGraphResolver(_designLibrary).Compile(new(_assemblyId,saved.Identity.RevisionId));_assemblyAcceptedRaw=CaptureAssemblyRaw();RefreshAssemblyCatalog();
        _assemblyPublishedRaw=_assemblyAcceptedRaw;_assemblyConflict=null;SaveAssemblyGraph();
        _assemblyStatus.Text="Published "+saved.Identity.RevisionId[..12]+". Mobility diagnostics remain visible; publication is not vehicle validation.";ScheduleAssemblyDraft();
    }
    private void RefreshAssemblyDependencies()
    {
        if(_assemblyBaseRevision is null) {ApplyAssemblyPreview(true);return;}
        if(_assemblyPublishedRaw!=CaptureAssemblyRaw()) throw new ArgumentException("Publish your draft before refreshing dependencies.");
        var result=new AssemblyGraphResolver(_designLibrary!).Resolve(new(_assemblyId,_assemblyBaseRevision),_assemblyGraph,g=>
        {
            var report=MechanismMobility.Inspect(g,AssemblyExpectedDof());if(!report.IsClosed) return "Updated joint frames do not close. Last complete graph retained.";
            return AssemblyExpectedDof() is {} expected&&report.DegreesOfFreedom!=expected?"Updated mechanism has an unexpected mobility. Last complete graph retained.":null;
        });
        _assemblyConflict=result.Conflict;
        if(result.IsStale) {_assemblyStatus.Text="Dependency conflict; previous graph retained. "+result.Conflict;return;}
        _assemblyGraph=result.Graph!;ShowAssemblyGraph(_assemblyGraph,MechanismMobility.Inspect(_assemblyGraph,AssemblyExpectedDof()));SaveAssemblyGraph();
    }
    private void MateAssemblyPorts()
    {
        ApplyAssemblyPreview(true);var aKey=_assemblyMateA.SelectedItem as string??throw new ArgumentException("Choose a stationary port.");var bKey=_assemblyMateB.SelectedItem as string??throw new ArgumentException("Choose a moving port.");
        var instanceId=bKey[..bKey.IndexOf(':')];if(instanceId.Contains('/')) throw new ArgumentException("Choose the exported port of a direct child assembly to move the entire group.");
        if(aKey.Split(':')[0]==instanceId||aKey.StartsWith(instanceId+"/",StringComparison.Ordinal)) throw new ArgumentException("Choose ports on different direct child instances.");
        var target=_assemblyGraph!.Interfaces[aKey].Frame;var moving=_assemblyGraph.Interfaces[bKey].Frame;
        var a=ReadAssembly();var instance=a.Instances.Single(x=>x.Id==instanceId);var raw=DesignTransform.From(instance.Placement);
        var bx=moving.ReferenceDirection;var by=moving.Axis.Cross(bx);var bz=moving.Axis;var ax=target.ReferenceDirection;var ay=target.Axis.Cross(ax);var az=target.Axis;
        DesignVector R(DesignVector v)=>ax*v.Dot(bx)+ay*v.Dot(by)+az*v.Dot(bz);
        var origin=target.OriginM+R(raw.Origin-moving.OriginM);var x=R(raw.X);var z=R(raw.Z);
        if(instance.Placement.Reflection is {} m)
        {DesignVector Reflect(DesignVector v)=>v-m.UnitNormal*(2*v.Dot(m.UnitNormal));origin=m.OriginM+Reflect(origin-m.OriginM);x=Reflect(x);z=Reflect(z);}
        var placement=instance.Placement with {Frame=new(origin,z,x)};a=a with {Instances=a.Instances.Replace(instance,instance with {Placement=placement})};FillAssembly(a);ApplyAssemblyPreview(true);
    }
    private void ExportAssembly()
    {
        if(_assemblyBaseRevision is null||_assemblyPublishedRaw!=CaptureAssemblyRaw()||_assemblyConflict is not null) throw new ArgumentException("Publish this draft and resolve dependency conflicts before exporting.");
        var bundle=AssemblyGraphResolver.Export(_assemblyGraph!);
        var dialog=new Microsoft.Win32.SaveFileDialog {Filter="Suspension assembly (*.json)|*.json",FileName=_assemblyId+".json",InitialDirectory=App.RepositoryRoot};
        if(dialog.ShowDialog(this)==true) {File.WriteAllText(dialog.FileName,DesignJson.Serialize(bundle));_assemblyStatus.Text="Exported exact revisions and transitive dependency hashes.";}
    }
    private string CaptureAssemblyRaw()=>JsonSerializer.Serialize(new AssemblyRecovery(_assemblyId,_assemblyBaseRevision,_workspaceDrafts[Workspace.AssemblyEditor].Name.Text,_workspaceDrafts[Workspace.AssemblyEditor].Notes.Text,
        _assemblyGrounds.Text,_assemblyExpectedDof.Text,_assemblyInstances.ToArray(),_assemblyJoints.ToArray(),_assemblyPorts.ToArray()));
    private void ImportAssembly()
    {
        var dialog=new Microsoft.Win32.OpenFileDialog {Filter="Suspension assembly (*.json)|*.json",InitialDirectory=App.RepositoryRoot};
        if(dialog.ShowDialog(this)!=true) return;
        var bundle=DesignJson.Deserialize<PortableDesignBundle>(File.ReadAllText(dialog.FileName));var imported=_designLibrary!.ImportClone(bundle);RefreshAssemblyCatalog();LoadAssembly(imported);
        _assemblyStatus.Text="Imported an independent assembly with exact child revisions. Original source bundle and hashes are archived.";
    }
    private void RestoreAssemblyRaw(string json,bool identity)
    {
        var raw=JsonSerializer.Deserialize<AssemblyRecovery>(json)??throw new ArgumentException("Invalid assembly recovery draft.");_assemblyUpdating=true;
        try
        {
            if(identity) {_assemblyId=raw.Id;_assemblyBaseRevision=raw.BaseRevision;}_workspaceDrafts[Workspace.AssemblyEditor].Name.Text=raw.Name;_workspaceDrafts[Workspace.AssemblyEditor].Notes.Text=raw.Notes;
            _assemblyGrounds.Text=raw.Grounds;_assemblyExpectedDof.Text=raw.ExpectedDof;_assemblyInstances.Clear();foreach(var x in raw.Instances) _assemblyInstances.Add(x);_assemblyJoints.Clear();foreach(var x in raw.Joints) _assemblyJoints.Add(x);_assemblyPorts.Clear();foreach(var x in raw.Ports) _assemblyPorts.Add(x);
        }
        finally {_assemblyUpdating=false;}
        ApplyAssemblyPreview(false);
    }
    private static string AssemblyCacheKey(string id)=>"assembly-resolved-"+Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(id)))[..24];
    private void SaveAssemblyGraph()
    {
        if(_assemblyGraph?.Root.Identity.RevisionId.Length==64) _designLibrary!.SaveDraft(AssemblyCacheKey(_assemblyId),DesignJson.Serialize(AssemblyGraphResolver.Export(_assemblyGraph)));
    }
    private void RecoverAssembly() {var raw=_assemblyRecoveryAtOpen??_designLibrary!.ReadDraft("assembly-workspace")??throw new ArgumentException("No recovery draft is available.");_assemblyRecoveryAtOpen=null;RestoreAssemblyRaw(raw,true);}
    private void UndoAssembly(bool redo) {var source=redo?_assemblyRedo:_assemblyUndo;var target=redo?_assemblyUndo:_assemblyRedo;if(source.Count==0) return;target.Push(CaptureAssemblyRaw());RestoreAssemblyRaw(source.Pop(),false);}
    private void CloseAssemblyEditor()
    {
        _assemblyAutosave.Stop();if(_assemblyDirty&&_designLibrary is not null) {try {_designLibrary.SaveDraft("assembly-workspace",CaptureAssemblyRaw());}catch(Exception error) {App.LogException("Assembly draft on close",error);}}
        _assemblyViewport?.Dispose();
    }
    private static InspectionScene AssemblyScene(ResolvedAssemblyGraph graph)
    {
        static Vector3 V(DesignVector p)=>new((float)p.X,(float)p.Y,(float)p.Z);
        var points=new List<InspectionPoint>();var lines=new List<InspectionSegment>();
        foreach(var body in graph.Bodies)
        {
            foreach(var point in body.Component.Points) points.Add(new(body.Path+"."+point.Id,body.Path+" · "+point.Name,body.Path,V(body.Transform.Point(point.PositionM)),SceneSource.Authored));
            foreach(var edge in body.Component.VisualEdges) lines.Add(new(body.Path+"."+edge.Id,body.Path+" · "+edge.Id,body.Path,"front",V(body.Transform.Point(body.Component.Points.Single(x=>x.Id==edge.StartPointId).PositionM)),V(body.Transform.Point(body.Component.Points.Single(x=>x.Id==edge.EndPointId).PositionM)),SceneSource.Authored));
            foreach(var port in body.Component.Ports)
            {
                var f=body.Transform.Frame(body.Component.Frames.Single(x=>x.Id==port.FrameId).Frame);var x=f.ReferenceDirection;var y=f.Axis.Cross(x);
                foreach(var (name,axis,layer) in new[]{("X",x,"rear"),("Y",y,"bars"),("Z",f.Axis,"chassis")}) lines.Add(new(body.Path+".port."+port.Id+name,body.Path+" · "+port.Id+" "+name,body.Path,layer,V(f.OriginM),V(f.OriginM+axis*.025),SceneSource.Authored));
            }
        }
        foreach(var j in graph.Joints.Where(x=>x.Kind==JointKind.RigidDistance)) lines.Add(new("joint"+j.Path,j.Path,"Assembly","bars",V(j.First.Frame.OriginM),V(j.Second.Frame.OriginM),SceneSource.Authored));
        return new(graph.Root.Identity.Name,"assembly-authoring","authored-assembly-draft",[..points],[..lines],[],["Synthetic mechanism; local mobility is separate from finite travel and equilibrium."]);
    }
}
