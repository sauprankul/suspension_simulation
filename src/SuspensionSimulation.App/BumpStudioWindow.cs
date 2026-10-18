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

internal enum BumperFaceRole { Unassigned,LowerPlaten,UpperPlaten,Shaft,FoldSlave,FoldMaster }
internal sealed class BumperFaceRow
{
    public int FaceTag {get;set;}public string ExtentMm {get;set;}="";public BumperFaceRole Role {get;set;}
}
internal sealed record BumpStudioDraft(string? InspectionHash,string? RecipeHash,Dictionary<string,string> Fields,string MaterialId,BumperFaceRow[] Faces,bool Shaft,bool LowerBonded,bool UpperBonded,bool DimensionsConfirmed,string PrintRepresentation)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] public string? LastJobId {get;init;}
}

/// <summary>Actual CAD/fixture/material preparation. A verified numerical worker is deliberately a separate integration gate.</summary>
internal sealed partial class BumpStudioWindow : Window
{
    private readonly StepBumperPreparation _service;private readonly InspectionViewport _viewport=new(false);
    private readonly Dictionary<string,TextBox> _fields=new();private readonly ObservableCollection<BumperFaceRow> _faces=new();
    private readonly ComboBox _units=new(){ItemsSource=Enum.GetValues<StepLengthUnit>(),SelectedIndex=-1};
    private readonly ComboBox _material=new(){ItemsSource=BumpStudioContracts.MaterialCatalog(),DisplayMemberPath="Name",SelectedValuePath="Id",SelectedIndex=9};
    private readonly ComboBox _printRepresentation=new(){ItemsSource=Enum.GetValues<PrintedGeometryRepresentation>(),SelectedIndex=0};
    private readonly TextBlock _status=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(8)},_cadInfo=new(){TextWrapping=TextWrapping.Wrap},_materialInfo=new(){TextWrapping=TextWrapping.Wrap};
    private readonly CheckBox _shaft=new(){Content="Include rigid shaft",IsChecked=false},_lowerBonded=new(){Content="Bond lower selected faces",IsChecked=true},_upperBonded=new(){Content="Bond upper selected faces"},_confirmed=new(){Content="I checked the imported dimensions and units",Margin=new Thickness(0,8,0,8)};
    private readonly Button _cancel=new(){Content="Cancel import",IsEnabled=false};private readonly DataGrid _faceGrid;private CancellationTokenSource? _importCancellation;
    private readonly TabControl _tabs;
    private readonly System.Windows.Threading.DispatcherTimer _verificationTimer=new(){Interval=TimeSpan.FromSeconds(2)};
    private readonly TextBox _verificationStatus=new(){IsReadOnly=true,Text="Waiting for a readable benchmark status snapshot…",TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Height=420};
    private BumperCadInspection? _cad;private string? _recipeHash,_inspectionHash;private bool _restoring;
    internal BumpStudioWindow(string libraryRoot)
    {
        Title="Bump Stop Studio — STEP to force curve";Width=1450;Height=900;MinWidth=1100;MinHeight=700;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        ApplyTheme(this);
        _service=new(App.RepositoryRoot,libraryRoot);_backend=new(App.RepositoryRoot,libraryRoot);_outputLibrary=new(libraryRoot);
        var layout=new DockPanel {Margin=new Thickness(10)};var toolbar=new WrapPanel();DockPanel.SetDock(toolbar,Dock.Top);layout.Children.Add(toolbar);
        void Button(string text,Action action) {var button=new Button {Content=text,Margin=new Thickness(3),Padding=new Thickness(8,4,8,4)};button.Click+=(_,_)=>Act(action);toolbar.Children.Add(button);}
        Button("Import STEP…",ChooseStep);Button("Load simple example",LoadExample);Button("Save recipe",SaveRecipe);Button("Open recipe…",ChooseRecipe);Button("Apply orientation / fixtures",Preview);_cancel.Click+=(_,_)=>_importCancellation?.Cancel();toolbar.Children.Add(_cancel);
        _status.Text="Import a STEP solid, set its end faces and material, then run a coarse estimated compression job.";_status.Height=48;_status.TextTrimming=TextTrimming.CharacterEllipsis;DockPanel.SetDock(_status,Dock.Bottom);layout.Children.Add(_status);
        var grid=new Grid();grid.ColumnDefinitions.Add(new());grid.ColumnDefinitions.Add(new(){Width=new GridLength(445)});grid.Children.Add(_viewport);layout.Children.Add(grid);
        var tabs=_tabs=new TabControl {Margin=new Thickness(10,0,0,0)};Grid.SetColumn(tabs,1);grid.Children.Add(tabs);
        StackPanel Tab(string title) {var panel=new StackPanel {Margin=new Thickness(10)};tabs.Items.Add(new TabItem {Header=title,Content=new ScrollViewer {Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}});return panel;}
        TextBox Field(Panel panel,string key,string label,string value) {panel.Children.Add(new TextBlock {Text=label,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,7,0,2)});var box=new TextBox {Text=value};_fields.Add(key,box);panel.Children.Add(box);box.TextChanged+=(_,_)=>SaveDraft();return box;}
        var source=Tab("STEP");source.Children.Add(new TextBlock {Text="Original STEP and face selection",FontWeight=FontWeights.Bold});source.Children.Add(new TextBlock {Text="Original STEP bytes and CAD face identities are preserved. Inspection facets are for picking; each job creates its own quadratic volume mesh. Healing, sewing and solid reconstruction are disabled.",TextWrapping=TextWrapping.Wrap});
        source.Children.Add(new TextBlock {Text="Confirm the STEP source length unit before import",Margin=new Thickness(0,8,0,2)});source.Children.Add(_units);Field(source,"previewSize","Inspection facet size (mm)","4");source.Children.Add(_cadInfo);source.Children.Add(_confirmed);
        source.Children.Add(new TextBlock {Text="The initial importer accepts one solid with explicit millimetre/metre units. Unsupported units, open/nonmanifold boundaries and multiple solids reject. Self-intersection, duplicates/slivers and later mesh quality still require further checks; importing a volume alone does not certify a solvable model.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)});
        var orientation=Tab("Orientation");Field(orientation,"name","Recipe name","New bumper preparation");
        void Vector(Panel panel,string key,string caption,string[] values) {panel.Children.Add(new TextBlock {Text=caption,Margin=new Thickness(0,8,0,2)});var row=new System.Windows.Controls.Primitives.UniformGrid {Columns=3};panel.Children.Add(row);for(var n=0;n<3;n++) {var cell=new StackPanel {Margin=new Thickness(n==0?0:5,0,0,0)};row.Children.Add(cell);Field(cell,key+n,"XYZ"[n].ToString(),values[n]);}}
        Vector(orientation,"origin","Compression-frame origin in CAD (mm)",["0","0","0"]);Vector(orientation,"axis","Frame +Z, lower → upper (unit direction)",["0","0","1"]);Vector(orientation,"reference","Frame +X (unit reference, perpendicular to +Z)",["1","0","0"]);
        orientation.Children.Add(new TextBlock {Text="Driven fixture motion is along frame −Z. Orientation changes the recipe and fixture preview; the CAD source coordinates remain unchanged.",TextWrapping=TextWrapping.Wrap});Field(orientation,"freeLength","Free reference length (mm)","40");Field(orientation,"compression","Requested shortening (mm)","8");Field(orientation,"meshSize","Coarse quadratic volume-mesh target (mm)","8");
        var contacts=Tab("Faces / fixtures");contacts.Children.Add(new TextBlock {Text="Click a solid face or select a row; assign its actual contact role. The end-face action selects planar caps in the current compression frame.",TextWrapping=TextWrapping.Wrap});
        var caps=new Button {Content="Set planar end faces / fixtures",Margin=new Thickness(0,6,0,6)};caps.Click+=(_,_)=>Act(ConfigureEndFaces);contacts.Children.Add(caps);
        _faceGrid=new DataGrid {ItemsSource=_faces,AutoGenerateColumns=false,CanUserAddRows=false,CanUserDeleteRows=false,Height=220,SelectionMode=DataGridSelectionMode.Single};
        _faceGrid.Columns.Add(new DataGridTextColumn {Header="CAD face",Binding=new System.Windows.Data.Binding("FaceTag"),IsReadOnly=true});_faceGrid.Columns.Add(new DataGridTextColumn {Header="Z extent (mm)",Binding=new System.Windows.Data.Binding("ExtentMm"),IsReadOnly=true});_faceGrid.Columns.Add(new DataGridComboBoxColumn {Header="Contact role",ItemsSource=Enum.GetValues<BumperFaceRole>(),SelectedItemBinding=new System.Windows.Data.Binding("Role")});contacts.Children.Add(_faceGrid);
        _faceGrid.SelectionChanged+=(_,_)=>{if(_faceGrid.SelectedItem is BumperFaceRow face) _viewport.HighlightCadFaces([face.FaceTag]);};_viewport.CadFacePicked+=tag=>_faceGrid.SelectedItem=_faces.Single(f=>f.FaceTag==tag);
        Field(contacts,"plateOuter","Platen outer radius (mm)","30");Field(contacts,"plateInner","Platen central-hole radius (mm)","0");Field(contacts,"lowerGap","Lower fixture gap (mm; negative = interference)","0");Field(contacts,"upperGap","Upper fixture gap (mm; negative = interference)","0");Field(contacts,"friction","Fixture friction coefficient","0");contacts.Children.Add(_lowerBonded);contacts.Children.Add(_upperBonded);contacts.Children.Add(_shaft);Field(contacts,"shaftRadius","Shaft radius (mm)","3.95");Field(contacts,"shaftOffset","Shaft starting offset along frame +Z (mm)","-10");Field(contacts,"shaftLength","Shaft length (mm)","60");
        contacts.Children.Add(new TextBlock {Text="Coarse jobs currently require a bonded lower cap and zero initial gaps. Upper contact can slide or be bonded. An optional shaft needs explicit bore faces. FoldSlave/FoldMaster define one contact pair; general automatic self-contact and interference installation remain future work.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)});
        var materials=Tab("Material");materials.Children.Add(_material);materials.Children.Add(_materialInfo);Field(materials,"condition","Material / test conditions","Not supplied");Field(materials,"materialSource","Material source / notes","Needs calibration; no fitted compression data supplied");
        Field(materials,"law0","Coefficient 1: TPU C1 / foam cp (MPa)","0");Field(materials,"law1","Coefficient 2: TPU C2 / foam c1 (MPa)","0");Field(materials,"law2","Coefficient 3: TPU bulk K (MPa) / foam exponent m1","0");
        materials.Children.Add(new TextBlock {Text="Preset selection copies Estimated coefficients into this recipe. Edit any coefficient; shared catalog values remain unchanged. TPU uses an isotropic tensile-based estimate; foam uses approximate manufacturer guide-curve scales. No measured calibration or backend approval is implied.",TextWrapping=TextWrapping.Wrap});
        var loadCard=new Button {Content="Import documented material card JSON…",Margin=new Thickness(0,8,0,4)};loadCard.Click+=(_,_)=>Act(ImportMaterialCard);materials.Children.Add(loadCard);
        materials.Children.Add(new TextBlock {Text="Printed TPU / TPE recipe",FontWeight=FontWeights.Bold,Margin=new Thickness(0,12,0,0)});materials.Children.Add(_printRepresentation);
        Field(materials,"grade","Manufacturer and exact filament grade","Not supplied");Vector(materials,"layerNormal","Layer normal in CAD (unit direction)",["0","0","1"]);
        foreach(var entry in new[]{("layer","Layer height (mm)","0.2"),("nozzle","Nozzle diameter (mm)","0.4"),("lineWidth","Line width (mm)","0.45"),("walls","Wall count","3"),("wallThickness","Wall thickness (mm)","1.35"),("topLayers","Top layers","4"),("bottomLayers","Bottom layers","4"),("infill","Infill pattern / modeled structure","Explicit CAD internal geometry"),("infillPercent","Infill (%)","100"),("processing","Print processing / temperatures / orientation","Not supplied"),("conditioning","Print conditioning","Not supplied")}) Field(materials,entry.Item1,entry.Item2,entry.Item3);
        materials.Children.Add(new TextBlock {Text="Infill percentage is recorded metadata. It never scales material stiffness. Explicit internal CAD geometry and effective printed-structure cards are distinct representations; missing data remain Needs calibration.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)});
        BuildJobPanel(Tab("Run / curve"));
        var verification=Tab("Research");verification.Children.Add(new TextBlock {Text="Separate numerical benchmark status",FontWeight=FontWeights.Bold});verification.Children.Add(new TextBlock {Text="The advanced folding/convergence study is separate from the coarse product jobs. A partial benchmark is not an accepted force curve.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,8)});verification.Children.Add(_verificationStatus);
        var resultButton=new Button {Content="Inspect exported deformation / force result…",Margin=new Thickness(0,8,0,0)};resultButton.Click+=(_,_)=>Act(OpenDiagnosticViewer);verification.Children.Add(resultButton);
        _verificationTimer.Tick+=(_,_)=>ReadVerificationStatus();_verificationTimer.Start();ReadVerificationStatus();
        Content=layout;_material.SelectionChanged+=(_,_)=>{UpdateMaterial();SaveDraft();};foreach(var box in new[]{_shaft,_lowerBonded,_upperBonded,_confirmed}) {box.Checked+=(_,_)=>SaveDraft();box.Unchecked+=(_,_)=>SaveDraft();}_faceGrid.CellEditEnding+=(_,_)=>Dispatcher.BeginInvoke(SaveDraft);_printRepresentation.SelectionChanged+=(_,_)=>SaveDraft();UpdateMaterial();
        Loaded+=(_,_)=>_viewport.SetActive(true);Closed+=(_,_)=>{_verificationTimer.Stop();_importCancellation?.Cancel();CancelJob();SaveDraft();_diagnosticViewer?.Close();_viewport.Dispose();};
        RestoreDraft();
    }
    internal static void ApplyTheme(Window window)
    {
        window.Background=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(17,20,25));window.Foreground=System.Windows.Media.Brushes.White;
        var resources=(ResourceDictionary)System.Windows.Markup.XamlReader.Parse("""
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Style TargetType="TextBox"><Setter Property="Background" Value="#0E1217"/><Setter Property="Foreground" Value="#F5F8FA"/><Setter Property="BorderBrush" Value="#536171"/><Setter Property="Padding" Value="5,4"/><Setter Property="MinWidth" Value="0"/></Style>
              <Style TargetType="ComboBox"><Setter Property="Foreground" Value="Black"/><Setter Property="Background" Value="White"/><Setter Property="Padding" Value="3"/></Style>
              <Style TargetType="ComboBoxItem"><Setter Property="Foreground" Value="Black"/><Setter Property="Background" Value="White"/></Style>
              <Style TargetType="CheckBox"><Setter Property="Foreground" Value="#F5F8FA"/><Setter Property="Margin" Value="0,5,0,4"/></Style>
              <Style TargetType="Button"><Setter Property="Background" Value="#2A3440"/><Setter Property="Foreground" Value="#F5F8FA"/><Setter Property="BorderBrush" Value="#617184"/><Setter Property="Padding" Value="7,4"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button"><Border x:Name="surface" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1" Padding="{TemplateBinding Padding}"><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/></Border><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="surface" Property="Background" Value="#364A60"/></Trigger><Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.5"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
              <Style TargetType="TabControl"><Setter Property="Background" Value="#1B2027"/><Setter Property="BorderBrush" Value="#536171"/><Setter Property="Foreground" Value="#F5F8FA"/></Style>
              <Style TargetType="TabItem"><Setter Property="Foreground" Value="#F5F8FA"/><Setter Property="Background" Value="#2A3440"/><Setter Property="Padding" Value="7,5"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="TabItem"><Border x:Name="surface" Background="{TemplateBinding Background}" BorderBrush="#617184" BorderThickness="1" Padding="{TemplateBinding Padding}"><ContentPresenter ContentSource="Header"/></Border><ControlTemplate.Triggers><Trigger Property="IsSelected" Value="True"><Setter TargetName="surface" Property="Background" Value="#38536B"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
              <Style TargetType="DataGrid"><Setter Property="Background" Value="#11161C"/><Setter Property="Foreground" Value="#EDF2F6"/><Setter Property="RowBackground" Value="#171D24"/><Setter Property="AlternatingRowBackground" Value="#1D242D"/><Setter Property="GridLinesVisibility" Value="Horizontal"/><Setter Property="BorderBrush" Value="#3E4A57"/></Style>
              <Style TargetType="DataGridColumnHeader"><Setter Property="Background" Value="#26313D"/><Setter Property="Foreground" Value="#EAF0F5"/><Setter Property="Padding" Value="6,3"/></Style>
            </ResourceDictionary>
            """);window.Resources.MergedDictionaries.Add(resources);
    }
    private void Act(Action action) {try {action();}catch(Exception error) {_status.Text=error.Message;App.LogException("Bump Stop Studio preparation",error);}}
    private double Number(string key)=>DesignerNumbers.Number(_fields[key].Text);private double Metres(string key)=>Number(key)/1000;
    private DesignVector Vector(string key,bool mm=false)=>new(Number(key+0)/(mm?1000:1),Number(key+1)/(mm?1000:1),Number(key+2)/(mm?1000:1));
    private DesignFrame ReadFrame() {var frame=new DesignFrame(Vector("origin",true),Vector("axis"),Vector("reference"));DesignValidation.Frame(frame);return frame;}
    private ImmutableArray<RigidBumperFixture> ReadFixtures()
    {
        var frame=ReadFrame();var upper=frame with {OriginM=frame.OriginM+frame.Axis*(Metres("freeLength")+Metres("upperGap"))};var lower=frame with {OriginM=frame.OriginM-frame.Axis*Metres("lowerGap")};
        var list=new List<RigidBumperFixture> {new("lower",RigidBumperFixtureKind.AnnularPlane,lower,Metres("plateInner"),Metres("plateOuter"),0,false),new("upper",RigidBumperFixtureKind.AnnularPlane,upper,Metres("plateInner"),Metres("plateOuter"),0,true)};
        if(_shaft.IsChecked==true) list.Add(new("shaft",RigidBumperFixtureKind.Cylinder,frame with {OriginM=frame.OriginM+frame.Axis*Metres("shaftOffset")},0,Metres("shaftRadius"),Metres("shaftLength"),false));return [..list];
    }
    private BumperMaterialCard ReadMaterial()
    {
        var material=_material.SelectedItem as BumperMaterialCard??throw new ArgumentException("Select a material card.");BumperPrintRecipe? print=null;
        int Integer(string key)=>int.TryParse(_fields[key].Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out var value)?value:throw new ArgumentException(key+" must be an integer.");
        if(material.Family==BumperMaterialFamily.PrintedTpu) print=new((PrintedGeometryRepresentation)_printRepresentation.SelectedItem,_fields["grade"].Text,Vector("layerNormal"),Metres("layer"),Metres("nozzle"),Metres("lineWidth"),Integer("walls"),Metres("wallThickness"),Integer("topLayers"),Integer("bottomLayers"),_fields["infill"].Text,Number("infillPercent"),_fields["processing"].Text,_fields["conditioning"].Text);
        if(EstimatedBumperMaterials.Keys(material).Length==3) material=EstimatedBumperMaterials.Override(material,[Number("law0")*1e6,Number("law1")*1e6,Number("law2")*(material.Family==BumperMaterialFamily.PrintedTpu?1e6:1)]);
        var result=material with {Conditions=_fields["condition"].Text,SourceNote=_fields["materialSource"].Text,PrintRecipe=print};BumpStudioContracts.ValidateMaterial(result);return result;
    }
    private BumperJobRecipe ReadRecipe()
    {
        if(_cad is null) throw new ArgumentException("Import a STEP solid first.");if(_confirmed.IsChecked!=true) throw new ArgumentException("Check and confirm the imported dimensions/units before saving a recipe.");_faceGrid.CommitEdit();_faceGrid.CommitEdit();
        ImmutableArray<int> Faces(BumperFaceRole role)=>[.._faces.Where(f=>f.Role==role).Select(f=>f.FaceTag)];var friction=Number("friction");
        var contacts=new List<BumperContactPair> {new("lower",Faces(BumperFaceRole.LowerPlaten),[],"lower",friction,_lowerBonded.IsChecked==true),new("upper",Faces(BumperFaceRole.UpperPlaten),[],"upper",friction,_upperBonded.IsChecked==true)};
        if(_shaft.IsChecked==true) contacts.Add(new("shaft",Faces(BumperFaceRole.Shaft),[],"shaft",friction,false));else if(Faces(BumperFaceRole.Shaft).Length>0) throw new ArgumentException("Shaft faces are selected but the shaft fixture is disabled.");
        if(Faces(BumperFaceRole.FoldSlave).Length>0||Faces(BumperFaceRole.FoldMaster).Length>0) contacts.Add(new("fold",Faces(BumperFaceRole.FoldSlave),Faces(BumperFaceRole.FoldMaster),null,friction,false));
        return new(_fields["name"].Text,_inspectionHash!,ReadFrame(),Metres("freeLength"),Metres("compression"),[..contacts],ReadFixtures(),ReadMaterial(),new(Metres("meshSize"),2,false),BudgetSeconds:checked((int)Number("budget"))) {Quality=BumperSolveQuality.CoarseEstimate};
    }
    private void Preview() {if(_cad is null) return;_viewport.SetCadInspection(_cad,ReadFrame(),ReadFixtures());_viewport.Preset("Fit / reset");SaveDraft();}
    private async void ChooseStep()
    {
        try {var dialog=new Microsoft.Win32.OpenFileDialog {Filter="STEP solids (*.step;*.stp)|*.step;*.stp",InitialDirectory=App.RepositoryRoot};if(dialog.ShowDialog(this)!=true) return;await ImportStep(dialog.FileName);}
        catch(Exception error) {_status.Text=error.Message;App.LogException("STEP preparation import",error);}
    }
    internal async Task ImportStep(string path)
    {
        if(_units.SelectedItem is not StepLengthUnit unit) throw new ArgumentException("Select/confirm the STEP source unit first.");if(_importCancellation is not null) throw new InvalidOperationException("A STEP import is already running.");
        _importCancellation=new();_cancel.IsEnabled=true;_status.Text="Importing the solid and its actual CAD-face inspection mesh…";
        try {_cad=await _service.ImportAsync(path,unit,Metres("previewSize"),_importCancellation.Token);_recipeHash=null;_confirmed.IsChecked=false;FillCad();Preview();_status.Text="STEP imported. Assign actual faces and inspect the units/fixture geometry before saving. No FEA solution was run.";App.Log("STEP inspection imported: "+BumpStudioContracts.Hash(_cad),"CAD");}
        finally {_importCancellation.Dispose();_importCancellation=null;_cancel.IsEnabled=false;}
    }
    private void FillCad()
    {
        var cad=_cad!;_faces.Clear();foreach(var f in cad.Faces) _faces.Add(new(){FaceTag=f.Tag,ExtentMm=$"{f.Bounds.MinimumM.Z*1000:0.###}…{f.Bounds.MaximumM.Z*1000:0.###}"});
        _inspectionHash=BumpStudioContracts.Hash(cad);
        _units.SelectedItem=cad.SourceUnit;
        var size=cad.Bounds.MaximumM-cad.Bounds.MinimumM;_cadInfo.Text=$"\n{cad.SourceFileName}\n{size.X*1000:0.###} × {size.Y*1000:0.###} × {size.Z*1000:0.###} mm\n{cad.Faces.Length} CAD faces · {cad.Triangles.Length} inspection facets\nSource SHA {cad.SourceSha256}\nHealing: disabled\n"+string.Join("\n",cad.Warnings);
        _fields["freeLength"].Text=DesignerNumbers.Mm(size.Z);_fields["compression"].Text=DesignerNumbers.Mm(size.Z*.2);_fields["origin2"].Text=DesignerNumbers.Mm(cad.Bounds.MinimumM.Z);
    }
    private void ReadVerificationStatus()
    {
        try
        {
            var folder=Path.Combine(App.RepositoryRoot,"artifacts/febio-verification");var path=Path.Combine(folder,"current-job.json");var text="No local benchmark job status is available.";
            if(File.Exists(path))
            {
                using var json=LiveStatusSnapshot.Read(path);var root=json.RootElement;
                string Value(string name)=>root.TryGetProperty(name,out var property)?property.ToString():"unavailable";
                var fraction=root.TryGetProperty("lastConvergedNormalizedTime",out var f)&&f.ValueKind==JsonValueKind.Number&&f.TryGetDouble(out var q)&&double.IsFinite(q)?(q*100).ToString("0.###",CultureInfo.InvariantCulture)+"%":"unavailable";
                double Numeric(string name)=>root.TryGetProperty(name,out var property)&&property.ValueKind==JsonValueKind.Number&&property.TryGetDouble(out var value)?value:double.NaN;
                string Duration(double seconds)=>double.IsFinite(seconds)?seconds>=3600?$"{seconds/3600:0.00} h":$"{seconds/60:0.0} min":"unavailable";
                var memory=Numeric("peakWorkingSetBytes");text=$"State: {Value("state")}\nElapsed / budget: {Duration(Numeric("seconds"))} / {Duration(Numeric("timeoutSeconds"))}\nAccepted fraction of requested stroke: {fraction}\nCurve accepted: {Value("curveAccepted")}\nPeak RAM: {(double.IsFinite(memory)?(memory/1048576).ToString("0.0",CultureInfo.InvariantCulture)+" MiB":"unavailable")}\nWorker PID: {Value("pid")}\nUpdated UTC: {Value("updatedAtUtc")}\n\nJob folder (input and logs):\n{Value("folder")}\n\nRaw diagnostic status:\n{path}";
            }
            var study=Path.Combine(folder,"feasibility-study.json");if(File.Exists(study)) {using var json=LiveStatusSnapshot.Read(study);var studyRoot=json.RootElement;string Value(string name)=>studyRoot.TryGetProperty(name,out var value)?value.ToString():"unavailable";text+=$"\n\nStudy stage: {Value("state")}\nBackend gate passed: {Value("backendGatePassed")}\nProduction curve published: {Value("productionCurvePublished")}\nStudy details:\n{study}";}_verificationStatus.Text=text;
        }
        // The writer can replace either journal between polls. Keep the last complete display
        // through a transient write/replace rather than moving controls or presenting half a poll.
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or JsonException) { }
    }
    private void UpdateMaterial() {if(_material.SelectedItem is BumperMaterialCard card) {SetMaterialCoefficients(card);_materialInfo.Text=$"{card.Readiness}\n{card.SourceNote}\n"+(card.Law is null?"No constitutive coefficients are supplied. A preparation recipe can be saved; no worker solve can use this card yet.":"Law "+card.Law.ModelKey+" · imported evidence is retained; worker support must be independently verified.");if(_fields.ContainsKey("condition")) {_fields["condition"].Text=card.Conditions;_fields["materialSource"].Text=card.SourceNote;}}}
    private void ImportMaterialCard()
    {
        var dialog=new Microsoft.Win32.OpenFileDialog {Filter="Material card (*.json)|*.json"};if(dialog.ShowDialog(this)!=true) return;var card=DesignJson.Deserialize<BumperMaterialCard>(File.ReadAllText(dialog.FileName));BumpStudioContracts.ValidateMaterial(card);
        var missing=card.EvidenceSha256.Where(hash=>!File.Exists(_service.MaterialEvidencePath(hash))).ToHashSet();
        if(missing.Count>0)
        {var evidence=new Microsoft.Win32.OpenFileDialog {Title="Select the original material data/evidence matching the card's SHA-256 hashes",Multiselect=true};if(evidence.ShowDialog(this)!=true) throw new ArgumentException("Material card was not imported: its original evidence artifacts are required.");foreach(var path in evidence.FileNames) missing.Remove(_service.PreserveMaterialEvidence(path));if(missing.Count>0) throw new ArgumentException("Selected evidence does not match all hashes in this card. The current material selection was retained.");}
        _material.ItemsSource=BumpStudioContracts.MaterialCatalog().Where(c=>c.Id!=card.Id).Append(card).ToArray();_material.SelectedValue=card.Id;
    }
    internal void SaveRecipe()
    {
        var recipe=ReadRecipe();PreservePresetEvidence(recipe.Material);_recipeHash=_service.SaveRecipe(recipe);_status.Text="Frozen coarse recipe saved. "+recipe.Material.Readiness+" material · run it from Run / curve.";SaveDraft();App.Log("Bumper preparation recipe saved: "+_recipeHash,"CAD");
    }
    private void ChooseRecipe() {var dialog=new Microsoft.Win32.OpenFileDialog {Filter="Saved preparation recipe (*.json)|*.json",InitialDirectory=Path.Combine(_service.Root,"recipes")};if(dialog.ShowDialog(this)!=true) return;OpenRecipe(Path.GetFileNameWithoutExtension(dialog.FileName));}
    internal void OpenRecipe(string hash)
    {
        var recipe=_service.LoadRecipe(hash);_cad=_service.LoadInspection(recipe.InspectionSha256);_recipeHash=hash;FillCad();_fields["budget"].Text=recipe.BudgetSeconds.ToString(CultureInfo.InvariantCulture);
        _fields["name"].Text=recipe.Name;SetVector("origin",recipe.CompressionFrameInCad.OriginM,true);SetVector("axis",recipe.CompressionFrameInCad.Axis);SetVector("reference",recipe.CompressionFrameInCad.ReferenceDirection);_fields["freeLength"].Text=DesignerNumbers.Mm(recipe.FreeLengthM);_fields["compression"].Text=DesignerNumbers.Mm(recipe.RequestedCompressionM);_fields["meshSize"].Text=DesignerNumbers.Mm(recipe.Mesh.TargetElementSizeM);
        _material.ItemsSource=BumpStudioContracts.MaterialCatalog().Where(c=>c.Id!=recipe.Material.Id).Append(recipe.Material).ToArray();_material.SelectedValue=recipe.Material.Id;
        var upper=recipe.Fixtures.Single(f=>f.Driven);var lower=recipe.Fixtures.Single(f=>f.Id=="lower");_fields["plateInner"].Text=DesignerNumbers.Mm(upper.InnerRadiusM);_fields["plateOuter"].Text=DesignerNumbers.Mm(upper.OuterRadiusM);
        _fields["lowerGap"].Text=DesignerNumbers.Mm((recipe.CompressionFrameInCad.OriginM-lower.FrameInCad.OriginM).Dot(recipe.CompressionFrameInCad.Axis));_fields["upperGap"].Text=DesignerNumbers.Mm((upper.FrameInCad.OriginM-recipe.CompressionFrameInCad.OriginM).Dot(recipe.CompressionFrameInCad.Axis)-recipe.FreeLengthM);
        _shaft.IsChecked=recipe.Fixtures.Any(f=>f.Id=="shaft");if(recipe.Fixtures.SingleOrDefault(f=>f.Id=="shaft") is {} shaft) {_fields["shaftRadius"].Text=DesignerNumbers.Mm(shaft.OuterRadiusM);_fields["shaftLength"].Text=DesignerNumbers.Mm(shaft.LengthM);_fields["shaftOffset"].Text=DesignerNumbers.Mm((shaft.FrameInCad.OriginM-recipe.CompressionFrameInCad.OriginM).Dot(recipe.CompressionFrameInCad.Axis));}
        foreach(var contact in recipe.Contacts)
        {
            var role=contact.RigidFixtureId switch {"lower"=>BumperFaceRole.LowerPlaten,"upper"=>BumperFaceRole.UpperPlaten,"shaft"=>BumperFaceRole.Shaft,_=>BumperFaceRole.FoldSlave};foreach(var tag in contact.SlaveFaceTags) _faces.Single(f=>f.FaceTag==tag).Role=role;foreach(var tag in contact.MasterFaceTags) _faces.Single(f=>f.FaceTag==tag).Role=BumperFaceRole.FoldMaster;
        }
        _fields["friction"].Text=recipe.Contacts[0].FrictionCoefficient.ToString("R",CultureInfo.InvariantCulture);_lowerBonded.IsChecked=recipe.Contacts.Single(c=>c.RigidFixtureId=="lower").Bonded;_upperBonded.IsChecked=recipe.Contacts.Single(c=>c.RigidFixtureId=="upper").Bonded;_confirmed.IsChecked=true;ApplyPrint(recipe.Material.PrintRecipe);_faceGrid.Items.Refresh();Preview();
        _status.Text="Opened frozen preparation recipe "+hash+". No force result is implied.";
    }
    private void SetVector(string key,DesignVector v,bool mm=false) {var values=new[]{v.X,v.Y,v.Z};for(var i=0;i<3;i++) _fields[key+i].Text=(values[i]*(mm?1000:1)).ToString("R",CultureInfo.InvariantCulture);}
    private void ApplyPrint(BumperPrintRecipe? p) {if(p is null) return;_printRepresentation.SelectedItem=p.Representation;_fields["grade"].Text=p.ManufacturerGrade;SetVector("layerNormal",p.LayerNormalInCad);foreach(var field in new[]{("layer",p.LayerHeightM*1000),("nozzle",p.NozzleDiameterM*1000),("lineWidth",p.LineWidthM*1000),("walls",(double)p.WallCount),("wallThickness",p.WallThicknessM*1000),("topLayers",(double)p.TopLayers),("bottomLayers",(double)p.BottomLayers),("infillPercent",p.InfillPercent)}) _fields[field.Item1].Text=field.Item2.ToString("R",CultureInfo.InvariantCulture);_fields["infill"].Text=p.InfillPattern;_fields["processing"].Text=p.Processing;_fields["conditioning"].Text=p.Conditioning;}
    internal object CaptureState()=>new {preparationOnly=false,inspectionHash=_inspectionHash,recipeHash=_recipeHash,status=_status.Text,draft=CaptureDraft(),job=CaptureJobState(),diagnostic=_diagnosticViewer?.CaptureState()};
    private BumpStudioDraft CaptureDraft()=>new(_inspectionHash,_recipeHash,_fields.ToDictionary(f=>f.Key,f=>f.Value.Text),_material.SelectedValue as string??"",_faces.ToArray(),_shaft.IsChecked==true,_lowerBonded.IsChecked==true,_upperBonded.IsChecked==true,_confirmed.IsChecked==true,_printRepresentation.SelectedItem?.ToString()??"") {LastJobId=_jobFolder is null?null:Path.GetFileName(_jobFolder)};
    private string DraftPath=>Path.Combine(_service.Root,"preparation-draft.json");
    private void SaveDraft() {if(_restoring||!IsLoaded) return;try {Directory.CreateDirectory(_service.Root);var path=DraftPath+".tmp";File.WriteAllText(path,DesignJson.Serialize(CaptureDraft()));File.Move(path,DraftPath,true);}catch(Exception error) {App.LogException("Bumper preparation draft",error);}}
    private void RestoreDraft()
    {
        if(!File.Exists(DraftPath)) return;_restoring=true;
        try {var draft=DesignJson.Deserialize<BumpStudioDraft>(File.ReadAllText(DraftPath));if(draft.RecipeHash is {} recipe) OpenRecipe(recipe);else if(draft.InspectionHash is {} inspection) {_cad=_service.LoadInspection(inspection);FillCad();}
            _material.SelectedValue=draft.MaterialId;foreach(var field in draft.Fields) if(_fields.TryGetValue(field.Key,out var box)) box.Text=field.Value;foreach(var face in draft.Faces) if(_faces.SingleOrDefault(f=>f.FaceTag==face.FaceTag) is {} row) row.Role=face.Role;
            _shaft.IsChecked=draft.Shaft;_lowerBonded.IsChecked=draft.LowerBonded;_upperBonded.IsChecked=draft.UpperBonded;_confirmed.IsChecked=draft.DimensionsConfirmed;if(Enum.TryParse<PrintedGeometryRepresentation>(draft.PrintRepresentation,out var representation)) _printRepresentation.SelectedItem=representation;_faceGrid.Items.Refresh();Act(Preview);RestoreJob(draft.LastJobId);
        }catch(Exception error) {_status.Text="Draft recovery conflict: "+error.Message;App.LogException("Bumper preparation recovery",error);}finally {_restoring=false;}
    }
    internal void ConfigureProofFaces() {foreach(var face in _faces) {var f=_cad!.Faces.Single(x=>x.Tag==face.FaceTag);if(Math.Abs(f.Bounds.MinimumM.Z)<1e-6&&Math.Abs(f.Bounds.MaximumM.Z)<1e-6) face.Role=BumperFaceRole.LowerPlaten;else if(Math.Abs(f.Bounds.MinimumM.Z-.04)<1e-6&&Math.Abs(f.Bounds.MaximumM.Z-.04)<1e-6) face.Role=BumperFaceRole.UpperPlaten;}_faceGrid.Items.Refresh();_confirmed.IsChecked=true;}
    internal void SetProofUnits()=>_units.SelectedItem=StepLengthUnit.Millimetres;
    internal string? RecipeHash=>_recipeHash;
    internal void CaptureProof(string path)
    {
        var layout=new System.Windows.Media.Imaging.RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,System.Windows.Media.PixelFormats.Pbgra32);layout.Render(this);var visual=new System.Windows.Media.DrawingVisual();
        using(var drawing=visual.RenderOpen()) {drawing.DrawImage(layout,new Rect(0,0,ActualWidth,ActualHeight));var origin=_viewport.Viewport.TranslatePoint(new Point(0,0),this);drawing.DrawImage(HelixToolkit.Wpf.SharpDX.ViewportExtensions.RenderBitmap(_viewport.Viewport),new Rect(origin,new Size(_viewport.Viewport.ActualWidth,_viewport.Viewport.ActualHeight)));}
        var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(visual);
        var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using var stream=File.Create(path);encoder.Save(stream);
    }
    internal void SelectProofTab(int index)=>_tabs.SelectedIndex=index;
    internal void VerifyProofPick() {var tag=_viewport.PickCadFaceForProof();if((_faceGrid.SelectedItem as BumperFaceRow)?.FaceTag!=tag) throw new InvalidOperationException("CAD hit selection did not select its actual face row.");}
    internal bool ProofRawRecovery() {var before=File.ReadAllText(DraftPath);_fields["axis0"].Text="invalid raw direction";SaveDraft();RestoreDraft();var valid=_fields["axis0"].Text=="invalid raw direction";File.WriteAllText(DraftPath,before);RestoreDraft();return valid;}
}
