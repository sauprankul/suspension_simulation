using System.Collections.ObjectModel;
using System.Collections.Immutable;
using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using SuspensionSimulation.Components;
using SuspensionSimulation.Design;
using SuspensionSimulation.Kinematics;

namespace SuspensionSimulation.App;

internal sealed record DesignerRecovery(string AssetId,string? BaseRevision,ComponentKind Kind,bool Custom,string LengthMm,string WidthMm,string HeightMm,
    string Name,string Notes,DesignerPointRow[] Points,DesignerFrameRow[] Frames,DesignerPortRow[] Ports,DesignerEdgeRow[] Edges,ComponentMass? Mass=null,ComponentDefinition? Metadata=null,ComponentPhysicsRecovery? Physics=null);
internal sealed record DesignerPick(string Name,DesignVector Point);
internal sealed record DesignerFrameChoice(string Name,DesignFrame? Frame);

public partial class MainWindow
{
    private DesignAssetLibrary? _designLibrary;
    private InspectionViewport? _componentViewport;
    private ComponentDefinition? _componentDefinition;
    private ComponentMass? _componentMass;
    private ComponentDefinition? _componentMetadataSeed;
    private readonly ComboBox _componentHistory=new(){MinWidth=100};
    private readonly TextBlock _componentDiff=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(8)};
    private string _componentId=Guid.NewGuid().ToString("N");
    private string? _componentBaseRevision;
    private bool _componentCustom,_componentUpdating;
    private bool _componentReady,_componentDirty;
    private string? _componentRecoveryAtOpen;
    private string? _componentAcceptedRaw;
    private readonly Stack<string> _componentUndo=new(),_componentRedo=new();
    private readonly ListBox _componentLibraryList=new(){DisplayMemberPath="Identity.Name",SelectedValuePath="Identity.AssetId",MinHeight=100,MaxHeight=180};
    private readonly ComboBox _componentKind=new(){ItemsSource=Enum.GetValues<ComponentKind>(),SelectedIndex=0,Margin=new Thickness(0,5,0,10)};
    private readonly TextBox _componentLength=new(){Text="300"},_componentWidth=new(){Text="200"},_componentHeight=new(){Text="100"};
    private readonly TextBlock _componentStatus=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)};
    private readonly ObservableCollection<DesignerPointRow> _componentPoints=new();
    private readonly ObservableCollection<DesignerFrameRow> _componentFrames=new();
    private readonly ObservableCollection<DesignerPortRow> _componentPorts=new();
    private readonly ObservableCollection<DesignerEdgeRow> _componentEdges=new();
    private readonly List<DataGrid> _componentGrids=new();
    private readonly ComboBox _measureA=new(){DisplayMemberPath="Name"},_measureB=new(){DisplayMemberPath="Name"},_measureFrame=new(){DisplayMemberPath="Name"};
    private readonly TextBox[] _measureCoordinates=Enumerable.Range(0,6).Select(_=>new TextBox {Text="0",MinWidth=50}).ToArray();
    private readonly TextBlock _measurement=new(){Text="Choose two points from the current preview.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)};
    private readonly DispatcherTimer _componentAutosave=new(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(300)};
    private int _measureNext;

    private void BuildComponentDesigner()
    {
        var proof=Environment.GetCommandLineArgs().Any(x=>x.Contains("proof",StringComparison.Ordinal));
        _designLibrary??=new DesignAssetLibrary(Path.Combine(App.RepositoryRoot,proof?"artifacts/component-designer/"+App.RunId:"library/design"));
        var old=_workspaceViews[Workspace.ComponentDesigner];_shell.Children.Remove(old);
        var metadata=_workspaceDrafts[Workspace.ComponentDesigner];
        ((Panel)metadata.Name.Parent).Children.Remove(metadata.Name);((Panel)metadata.Notes.Parent).Children.Remove(metadata.Notes);
        metadata.Name.MinWidth=0;metadata.Notes.MinWidth=0;metadata.Notes.MinHeight=70;
        metadata.Name.Text="New rigid link";metadata.Notes.Text="User-authored synthetic geometry; no measured vehicle fit asserted.";
        var grid=new Grid {Margin=new Thickness(12)};
        grid.ColumnDefinitions.Add(new(){Width=new GridLength(260)});grid.ColumnDefinitions.Add(new());grid.ColumnDefinitions.Add(new(){Width=new GridLength(390)});
        var left=new StackPanel {Margin=new Thickness(0,0,10,0)};
        void Button(string text,Action action)
        {var button=new Button {Content=text,Margin=new Thickness(0,3,0,3),Padding=new Thickness(6,3,6,3)};button.Click+=(_,_)=>ComponentAction(action);left.Children.Add(button);}
        Button("Main menu",()=>NavigateWorkspace(Workspace.MainMenu));left.Children.Add(new TextBlock {Text="Component Designer",FontSize=21,FontWeight=FontWeights.Bold,Margin=new Thickness(0,8,0,8)});
        left.Children.Add(_componentLibraryList);Button("Open selected revision",OpenSelectedComponent);
        left.Children.Add(new TextBlock {Text="Template"});left.Children.Add(_componentKind);
        Button("New from template",()=>NewComponent(false));Button("Clone as independent part",()=>NewComponent(true));
        left.Children.Add(new TextBlock {Text="Name"});left.Children.Add(metadata.Name);
        foreach(var (label,field) in new[]{("Length / reach / slot (mm)",_componentLength),("Width (mm)",_componentWidth),("Height (mm)",_componentHeight)})
        {left.Children.Add(new TextBlock {Text=label});left.Children.Add(field);}
        Button("Apply dimensions / preview",()=>ApplyComponentPreview(true));Button("Edit custom coordinates",MakeComponentCustom);
        Button("Undo",()=>UndoComponent(false));Button("Redo",()=>UndoComponent(true));Button("Publish revision",PublishComponent);Button("Recover last draft",RecoverComponent);
        left.Children.Add(new TextBlock {Text="Provenance notes (synthetic)",Margin=new Thickness(0,10,0,0)});left.Children.Add(metadata.Notes);left.Children.Add(_componentStatus);
        var scroll=new ScrollViewer {Content=left,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};grid.Children.Add(scroll);
        _componentViewport=new InspectionViewport(false);Grid.SetColumn(_componentViewport,1);grid.Children.Add(_componentViewport);
        _componentViewport.Failed+=error=>{_componentStatus.Text="Preview unavailable: "+error.Message;_componentViewport.SetActive(false);};
        _componentViewport.PointPicked+=p=>
        {
            var index=_measureNext++%2*3;_measureCoordinates[index].Text=DesignerNumbers.Mm(p.X);_measureCoordinates[index+1].Text=DesignerNumbers.Mm(p.Y);_measureCoordinates[index+2].Text=DesignerNumbers.Mm(p.Z);MeasureCoordinates();
        };
        var tabs=new TabControl {Margin=new Thickness(8,0,0,0)};Grid.SetColumn(tabs,2);grid.Children.Add(tabs);
        AddGrid("Points (mm)",_componentPoints);AddGrid("Frames",_componentFrames);AddGrid("Ports",_componentPorts);AddGrid("Edges",_componentEdges);
        AddComponentMechanics(tabs);
        var measurements=new StackPanel {Margin=new Thickness(8)};
        measurements.Children.Add(new TextBlock {Text="Named points and port origins",FontWeight=FontWeights.Bold});measurements.Children.Add(_measureA);measurements.Children.Add(_measureB);
        measurements.Children.Add(new TextBlock {Text="Measurement frame",Margin=new Thickness(0,8,0,3)});measurements.Children.Add(_measureFrame);
        var named=new Button {Content="Measure selected points",Margin=new Thickness(0,5,0,8)};
        named.Click+=(_,_)=>ComponentAction(()=>{var a=(_measureA.SelectedItem as DesignerPick)??throw new ArgumentException("Choose point A.");var b=(_measureB.SelectedItem as DesignerPick)??throw new ArgumentException("Choose point B.");ShowMeasurement(a.Point,b.Point);});measurements.Children.Add(named);
        measurements.Children.Add(new TextBlock {Text="Arbitrary point A / B coordinates (mm)",TextWrapping=TextWrapping.Wrap});
        foreach(var i in new[]{0,1})
        {var row=new UniformGrid {Columns=3};foreach(var box in _measureCoordinates.Skip(i*3).Take(3)) row.Children.Add(box);measurements.Children.Add(row);}
        var arbitrary=new Button {Content="Measure coordinates",Margin=new Thickness(0,5,0,8)};arbitrary.Click+=(_,_)=>ComponentAction(MeasureCoordinates);measurements.Children.Add(arbitrary);
        var picks=new CheckBox {Content="Pick measurement points in viewport",Foreground=Brushes.White,Margin=new Thickness(0,5,0,5)};
        picks.Checked+=(_,_)=>{_measureNext=0;_componentViewport.MeasurementPicking=true;};picks.Unchecked+=(_,_)=>_componentViewport.MeasurementPicking=false;measurements.Children.Add(picks);
        measurements.Children.Add(new TextBlock {Text="Viewport picks are approximate. Named points retain exact authored coordinates. Frame +Z is its axis; +X is its reference direction. X orange · Y purple · Z grey.",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.LightSteelBlue});
        measurements.Children.Add(_measurement);tabs.Items.Add(new TabItem {Header="Measure",Content=new ScrollViewer {Content=measurements,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}});
        var history=new StackPanel {Margin=new Thickness(8)};history.Children.Add(new TextBlock {Text="Published revisions",FontWeight=FontWeights.Bold});history.Children.Add(_componentHistory);
        var compare=new Button {Content="Compare preview to revision",Margin=new Thickness(0,8,0,8)};
        compare.Click+=(_,_)=>ComponentAction(()=>
        {
            var revision=_componentHistory.SelectedItem as string??throw new ArgumentException("Select a published revision.");
            var saved=_designLibrary.LoadComponent(_componentId,revision)!;var current=ReadComponent();
            var differences=new List<string>();
            void Change(string name,object a,object b) {if(DesignJson.Serialize(a)!=DesignJson.Serialize(b)) differences.Add(name+" changed");}
            Change("Name",saved.Identity.Name,current.Identity.Name);Change("Provenance",saved.Identity.Provenance,current.Identity.Provenance);Change("Dimensions",saved.Dimensions,current.Dimensions);
            Change("Points",saved.Points,current.Points);Change("Frames",saved.Frames,current.Frames);Change("Ports",saved.Ports,current.Ports);Change("Edges",saved.VisualEdges,current.VisualEdges);
            _componentDiff.Text=differences.Count==0?"Preview matches this revision.":string.Join("\n",differences);
        });history.Children.Add(compare);history.Children.Add(_componentDiff);tabs.Items.Add(new TabItem {Header="History",Content=history});
        _workspaceViews[Workspace.ComponentDesigner]=grid;grid.Visibility=Visibility.Collapsed;_shell.Children.Add(grid);
        _componentAutosave.Tick+=(_,_)=>{_componentAutosave.Stop();try {_designLibrary.SaveDraft("component-workspace",CaptureComponentRaw());}catch(Exception error) {_componentStatus.Text="Draft recovery could not save: "+error.Message;}};
        foreach(var box in new[]{metadata.Name,metadata.Notes,_componentLength,_componentWidth,_componentHeight}) box.TextChanged+=(_,_)=>ScheduleComponentDraft();
        _componentKind.SelectionChanged+=(_,_)=>ScheduleComponentDraft();
        _componentPoints.CollectionChanged+=(_,_)=>ScheduleComponentDraft();_componentFrames.CollectionChanged+=(_,_)=>ScheduleComponentDraft();_componentPorts.CollectionChanged+=(_,_)=>ScheduleComponentDraft();_componentEdges.CollectionChanged+=(_,_)=>ScheduleComponentDraft();
        ComponentAction(RefreshComponentLibrary);NewComponent(false);
        _componentRecoveryAtOpen=_designLibrary.ReadDraft("component-workspace");_componentReady=true;
        if(_componentRecoveryAtOpen is not null) _componentStatus.Text="A recovery draft is available. Choose Recover last draft to open it.";
        else ScheduleComponentDraft();

        void AddGrid<T>(string title,ObservableCollection<T> items)
        {
            var data=new DataGrid {ItemsSource=items,AutoGenerateColumns=true,CanUserAddRows=true,CanUserDeleteRows=true,IsReadOnly=true,HeadersVisibility=DataGridHeadersVisibility.Column};
            data.CellEditEnding+=(_,_)=>Dispatcher.BeginInvoke(ScheduleComponentDraft);
            data.ToolTip=title=="Frames"?"Origins in mm; axis and reference are orthogonal unit vectors. Edit custom coordinates to unlock.":title=="Ports"?"Joints: Fixed, Revolute, Spherical, Prismatic, Cylindrical, Universal, RigidDistance (comma separated).":"Edit custom coordinates to unlock. IDs remain stable across revisions.";
            _componentGrids.Add(data);tabs.Items.Add(new TabItem {Header=title,Content=data});
        }
    }

    private void ComponentAction(Action action)
    {try {action();}catch(Exception error) {_componentStatus.Text=error.Message;App.LogException("Component Designer",error);}}
    private void ScheduleComponentDraft() {if(!_componentUpdating&&_componentReady) {_componentDirty=true;_componentAutosave.Stop();_componentAutosave.Start();}}
    private RevisionIdentity ComponentIdentity()=>new(_componentId,"draft",_workspaceDrafts[Workspace.ComponentDesigner].Name.Text,
        new(DataProvenance.Synthetic,_workspaceDrafts[Workspace.ComponentDesigner].Notes.Text,_componentMetadataSeed?.Identity.Provenance.Source));
    private ComponentDefinition ReadComponent()
    {
        foreach(var grid in _componentGrids) {grid.CommitEdit();grid.CommitEdit();}
        var kind=(ComponentKind)_componentKind.SelectedItem;
        _componentMass=ReadComponentMass();
        if(!_componentCustom && kind is ComponentKind.Spring or ComponentKind.Damper or ComponentKind.AntiRollBar) return CreateElasticComponent(kind) with {Mass=_componentMass};
        if(!_componentCustom) return ComponentAuthoring.Create(ComponentIdentity(),kind,new(DesignerNumbers.Metres(_componentLength.Text),kind==ComponentKind.RigidLink?0:DesignerNumbers.Metres(_componentWidth.Text),kind==ComponentKind.RigidLink?0:DesignerNumbers.Metres(_componentHeight.Text))) with {Mass=_componentMass,AxialGauge=_componentMetadataSeed?.AxialGauge,Rack=_componentMetadataSeed?.Rack,Adjustments=_componentMetadataSeed?.Adjustments};
        var geometry=new ComponentDefinition(ComponentIdentity(),kind,[.._componentPoints.Select(x=>x.Read())],[.._componentFrames.Select(x=>x.Read())],[.._componentPorts.Select(x=>x.Read())],
            [.._componentEdges.Select(x=>(DesignDimension)new MeasuredDistance(x.Id,x.StartPointId,x.EndPointId))],_componentMass)
            {VisualEdges=[.._componentEdges.Select(x=>new VisualEdge(x.Id,x.StartPointId,x.EndPointId))]};
        var result=_componentMetadataSeed is {} metadata?metadata with {Identity=geometry.Identity,Kind=geometry.Kind,Points=geometry.Points,Frames=geometry.Frames,Ports=geometry.Ports,Dimensions=geometry.Dimensions,Template=null,Mass=_componentMass,VisualEdges=geometry.VisualEdges}:geometry;
        return ApplyComponentMechanicalEdits(result);
    }
    private void ApplyComponentPreview(bool undo)
    {
        var component=ReadComponent();ComponentAuthoring.ValidateForPublish(component);
        var raw=CaptureComponentRaw();if(undo && _componentAcceptedRaw is {} before && before!=raw) {_componentUndo.Push(before);_componentRedo.Clear();}
        _componentDefinition=component;_componentAcceptedRaw=raw;
        if(!_componentCustom) FillComponentTables(component);
        _componentViewport!.SetScene(ComponentScene(component));
        var picks=component.Points.Select(x=>new DesignerPick(x.Name,x.PositionM)).Concat(component.Frames.Select(x=>new DesignerPick("Port/frame · "+x.Name,x.Frame.OriginM))).ToArray();
        _measureA.ItemsSource=picks;_measureB.ItemsSource=picks;_measureA.SelectedIndex=0;_measureB.SelectedIndex=Math.Min(1,picks.Length-1);
        _measureFrame.ItemsSource=new[]{new DesignerFrameChoice("Component/world",null)}.Concat(component.Frames.Select(x=>new DesignerFrameChoice(x.Name,x.Frame))).ToArray();_measureFrame.SelectedIndex=0;
        _componentStatus.Text=(_componentCustom?"Custom coordinates":"Template dimensions")+" · valid preview; publish to save a revision.";ScheduleComponentDraft();
    }
    private void FillComponentTables(ComponentDefinition component)
    {
        _componentUpdating=true;
        try
        {
            _componentPoints.Clear();foreach(var item in component.Points) _componentPoints.Add(DesignerPointRow.From(item));
            _componentFrames.Clear();foreach(var item in component.Frames) _componentFrames.Add(DesignerFrameRow.From(item));
            _componentPorts.Clear();foreach(var item in component.Ports) _componentPorts.Add(DesignerPortRow.From(item));
            _componentEdges.Clear();foreach(var item in component.VisualEdges) _componentEdges.Add(new(){Id=item.Id,StartPointId=item.StartPointId,EndPointId=item.EndPointId});
            foreach(var grid in _componentGrids) grid.IsReadOnly=!_componentCustom;
            _componentLength.IsEnabled=!_componentCustom;_componentWidth.IsEnabled=_componentHeight.IsEnabled=!_componentCustom&&component.Kind!=ComponentKind.RigidLink;
            if(!ComponentAuthoring.RigidTemplates.Contains(component.Kind)) _componentLength.IsEnabled=_componentWidth.IsEnabled=_componentHeight.IsEnabled=false;
            FillComponentMechanics(component);
        }
        finally {_componentUpdating=false;}
    }
    private void NewComponent(bool clone)
    {
        _componentId=Guid.NewGuid().ToString("N");_componentBaseRevision=null;_componentUndo.Clear();_componentRedo.Clear();_componentAcceptedRaw=null;
        if(clone && _componentDefinition is {} previous)
        {
            _componentMass=previous.Mass;
            _componentMetadataSeed=previous;
            _componentKind.SelectedItem=previous.Kind;_componentCustom=previous.Template is null;_workspaceDrafts[Workspace.ComponentDesigner].Name.Text=previous.Identity.Name+" copy";
            if(previous.Template is {} spec) {_componentLength.Text=DesignerNumbers.Mm(spec.LengthM);_componentWidth.Text=DesignerNumbers.Mm(spec.WidthM);_componentHeight.Text=DesignerNumbers.Mm(spec.HeightM);}
            FillComponentTables(previous);
        }
        else {_componentMetadataSeed=null;_componentMass=null;_useMass.IsChecked=false;_componentCustom=false;_workspaceDrafts[Workspace.ComponentDesigner].Name.Text="New "+_componentKind.SelectedItem;foreach(var grid in _componentGrids) grid.IsReadOnly=true;}
        _componentHistory.ItemsSource=null;
        ApplyComponentPreview(false);
    }
    private void MakeComponentCustom()
    {ApplyComponentPreview(true);_componentCustom=true;_componentDefinition=ComponentAuthoring.MakeCustom(_componentDefinition!);FillComponentTables(_componentDefinition);ApplyComponentPreview(true);}
    private void RefreshComponentLibrary() {_componentLibraryList.ItemsSource=_designLibrary!.Components().OrderBy(x=>x.Identity.Name).ToArray();_componentLibraryList.SelectedValue=_componentId;}
    private void OpenSelectedComponent()
    {
        var component=_componentLibraryList.SelectedItem as ComponentDefinition??throw new ArgumentException("Select a published component.");
        _componentMetadataSeed=component;_componentMass=component.Mass;_componentHistory.ItemsSource=_designLibrary!.ComponentRevisions(component.Identity.AssetId);_componentHistory.SelectedIndex=0;
        _componentId=component.Identity.AssetId;_componentBaseRevision=component.Identity.RevisionId;_componentCustom=component.Template is null;
        var metadata=_workspaceDrafts[Workspace.ComponentDesigner];metadata.Name.Text=component.Identity.Name;metadata.Notes.Text=component.Identity.Provenance.Note;_componentKind.SelectedItem=component.Kind;
        if(component.Template is {} spec) {_componentLength.Text=DesignerNumbers.Mm(spec.LengthM);_componentWidth.Text=DesignerNumbers.Mm(spec.WidthM);_componentHeight.Text=DesignerNumbers.Mm(spec.HeightM);}
        FillComponentTables(component);_componentUndo.Clear();_componentRedo.Clear();_componentAcceptedRaw=null;ApplyComponentPreview(false);
        _componentStatus.Text="Opened revision "+component.Identity.RevisionId[..12];
    }
    private void PublishComponent()
    {
        ApplyComponentPreview(true);var saved=_designLibrary!.Publish(_componentDefinition!,_componentBaseRevision);_componentBaseRevision=saved.Identity.RevisionId;
        _componentDefinition=saved;_componentAcceptedRaw=CaptureComponentRaw();RefreshComponentLibrary();_componentStatus.Text="Published revision "+saved.Identity.RevisionId[..12];ScheduleComponentDraft();
        _componentHistory.ItemsSource=_designLibrary.ComponentRevisions(_componentId);_componentHistory.SelectedIndex=0;
    }
    private string CaptureComponentRaw()=>JsonSerializer.Serialize(new DesignerRecovery(_componentId,_componentBaseRevision,(ComponentKind)_componentKind.SelectedItem,_componentCustom,
        _componentLength.Text,_componentWidth.Text,_componentHeight.Text,_workspaceDrafts[Workspace.ComponentDesigner].Name.Text,_workspaceDrafts[Workspace.ComponentDesigner].Notes.Text,
        _componentPoints.ToArray(),_componentFrames.ToArray(),_componentPorts.ToArray(),_componentEdges.ToArray(),_componentMass,_componentMetadataSeed,CaptureComponentPhysics()));
    private void RestoreComponentRaw(string json,bool identity)
    {
        var raw=JsonSerializer.Deserialize<DesignerRecovery>(json)??throw new ArgumentException("Invalid recovery draft.");_componentUpdating=true;
        try
        {
            if(identity) {_componentId=raw.AssetId;_componentBaseRevision=raw.BaseRevision;}
            _componentMass=raw.Mass;
            _componentMetadataSeed=raw.Metadata;
            RestoreComponentPhysics(raw.Physics);
            _componentKind.SelectedItem=raw.Kind;_componentCustom=raw.Custom;_componentLength.Text=raw.LengthMm;_componentWidth.Text=raw.WidthMm;_componentHeight.Text=raw.HeightMm;
            _workspaceDrafts[Workspace.ComponentDesigner].Name.Text=raw.Name;_workspaceDrafts[Workspace.ComponentDesigner].Notes.Text=raw.Notes;
            _componentPoints.Clear();foreach(var row in raw.Points) _componentPoints.Add(row);_componentFrames.Clear();foreach(var row in raw.Frames) _componentFrames.Add(row);
            _componentPorts.Clear();foreach(var row in raw.Ports) _componentPorts.Add(row);_componentEdges.Clear();foreach(var row in raw.Edges) _componentEdges.Add(row);
            foreach(var grid in _componentGrids) grid.IsReadOnly=!_componentCustom;
        }
        finally {_componentUpdating=false;}
        ApplyComponentPreview(false);
    }
    private void RecoverComponent()
    {var raw=_componentRecoveryAtOpen??_designLibrary!.ReadDraft("component-workspace")??throw new ArgumentException("No recovery draft is available.");_componentRecoveryAtOpen=null;RestoreComponentRaw(raw,true);}
    private void CloseComponentDesigner()
    {
        _componentAutosave.Stop();
        if(_componentDirty&&_designLibrary is not null) {try {_designLibrary.SaveDraft("component-workspace",CaptureComponentRaw());}catch(Exception error) {App.LogException("Component draft on close",error);}}
        _componentViewport?.Dispose();
    }
    private void UndoComponent(bool redo)
    {
        var source=redo?_componentRedo:_componentUndo;var target=redo?_componentUndo:_componentRedo;
        if(source.Count==0) return;target.Push(CaptureComponentRaw());RestoreComponentRaw(source.Pop(),false);
    }
    private void MeasureCoordinates()=>ShowMeasurement(new(DesignerNumbers.Metres(_measureCoordinates[0].Text),DesignerNumbers.Metres(_measureCoordinates[1].Text),DesignerNumbers.Metres(_measureCoordinates[2].Text)),
        new(DesignerNumbers.Metres(_measureCoordinates[3].Text),DesignerNumbers.Metres(_measureCoordinates[4].Text),DesignerNumbers.Metres(_measureCoordinates[5].Text)));
    private void ShowMeasurement(DesignVector a,DesignVector b)
    {var measured=ComponentAuthoring.Measure(a,b,(_measureFrame.SelectedItem as DesignerFrameChoice)?.Frame);_measurement.Text=$"Distance {measured.DistanceM*1000:0.######} mm\nΔ X {measured.DeltaM.X*1000:0.######} · Y {measured.DeltaM.Y*1000:0.######} · Z {measured.DeltaM.Z*1000:0.######} mm";}
    private static InspectionScene ComponentScene(ComponentDefinition component)
    {
        static Vector3 V(DesignVector p)=>new((float)p.X,(float)p.Y,(float)p.Z);
        var points=component.Points.Select(x=>new InspectionPoint("design."+x.Id,x.Name,"Part",V(x.PositionM),SceneSource.Authored)).ToList();var lines=new List<InspectionSegment>();
        foreach(var edge in component.VisualEdges) lines.Add(new("edge."+edge.Id,edge.Id,"Part","front",V(component.Points.Single(x=>x.Id==edge.StartPointId).PositionM),V(component.Points.Single(x=>x.Id==edge.EndPointId).PositionM),SceneSource.Authored));
        foreach(var frame in component.Frames)
        {
            var z=V(frame.Frame.Axis);var x=V(frame.Frame.ReferenceDirection);var y=Vector3.Cross(z,x);var origin=V(frame.Frame.OriginM);
            foreach(var (name,axis,layer) in new[]{("X",x,"rear"),("Y",y,"bars"),("Z",z,"chassis")}) lines.Add(new("frame."+frame.Id+"."+name,frame.Name+" "+name,"Part",layer,origin,origin+axis*.035f,SceneSource.Authored));
        }
        return new(component.Identity.Name,"authoring","authored-component-draft",[..points],[..lines],[],["User-authored geometry; no vehicle equilibrium is run."]);
    }
}
