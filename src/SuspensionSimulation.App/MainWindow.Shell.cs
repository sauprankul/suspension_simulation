using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Markup;

namespace SuspensionSimulation.App;

internal enum Workspace { MainMenu, ComponentDesigner, AssemblyEditor, Garage, HeadToHead, Visualizer3D }

public partial class MainWindow
{
    private readonly Grid _shell = new();
    private readonly Dictionary<Workspace,FrameworkElement> _workspaceViews = new();
    private readonly Dictionary<Workspace,(TextBox Name,TextBox Notes)> _workspaceDrafts = new();
    private Workspace _workspace = Workspace.HeadToHead;
    private bool IsSimulationWorkspace => _workspace is Workspace.HeadToHead or Workspace.Visualizer3D;

    private void BuildShell()
    {
        Content=null;_shell.Children.Add(RootGrid);Content=_shell;
        var menu=new StackPanel {Width=700,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center};
        menu.Children.Add(new TextBlock {Text="Suspension Simulation",FontSize=32,FontWeight=FontWeights.Bold,Margin=new Thickness(0,0,0,20)});
        var menuButtonTemplate=(ControlTemplate)XamlReader.Parse("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Button">
              <Border x:Name="surface" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1" Padding="{TemplateBinding Padding}">
                <ContentPresenter HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}" VerticalAlignment="Center"/>
              </Border>
              <ControlTemplate.Triggers>
                <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="surface" Property="Background" Value="#364A60"/></Trigger>
                <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="surface" Property="BorderBrush" Value="#9ED4FF"/></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate>
            """);
        foreach(var destination in Enum.GetValues<Workspace>().Where(x=>x!=Workspace.MainMenu))
        {
            var button=new Button {Content=WorkspaceName(destination),Template=menuButtonTemplate,HorizontalContentAlignment=HorizontalAlignment.Left,FontSize=18,Margin=new Thickness(0,4,0,4),Padding=new Thickness(18,12,18,12)};
            button.Click+=(_,_)=>NavigateWorkspace(destination);menu.Children.Add(button);
        }
        AddWorkspace(Workspace.MainMenu,menu);
        foreach(var destination in new[]{Workspace.ComponentDesigner,Workspace.AssemblyEditor,Workspace.Garage})
        {
            var panel=new StackPanel {Margin=new Thickness(36),MaxWidth=900,HorizontalAlignment=HorizontalAlignment.Left};
            var back=new Button {Content="Main menu",HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,0,0,20)};
            back.Click+=(_,_)=>NavigateWorkspace(Workspace.MainMenu);panel.Children.Add(back);
            panel.Children.Add(new TextBlock {Text=WorkspaceName(destination),FontSize=28,FontWeight=FontWeights.Bold});
            panel.Children.Add(new TextBlock {Text=destination switch
                {Workspace.ComponentDesigner=>"Geometry authoring tools are not available yet.",Workspace.AssemblyEditor=>"Assembly connection tools are not available yet.",_=>"Runnable car setups are not available yet."},
                Foreground=Brushes.LightSteelBlue,FontSize=15,Margin=new Thickness(0,12,0,4)});
            panel.Children.Add(new TextBlock {Text="Draft details are kept while you switch screens.",Margin=new Thickness(0,0,0,20)});
            var name=new TextBox {MinWidth=620,Margin=new Thickness(0,6,0,18)};
            var notes=new TextBox {MinWidth=620,MinHeight=200,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new Thickness(0,6,0,0)};
            panel.Children.Add(new TextBlock {Text="Draft name"});panel.Children.Add(name);
            panel.Children.Add(new TextBlock {Text="Notes"});panel.Children.Add(notes);
            _workspaceDrafts[destination]=(name,notes);
            var details=new ScrollViewer {Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
            AddWorkspace(destination,destination==Workspace.Garage?BuildGarage(details):details);
        }
    }

    private void AddWorkspace(Workspace workspace,FrameworkElement view)
    {view.Visibility=Visibility.Collapsed;_workspaceViews.Add(workspace,view);_shell.Children.Add(view);}
    private static string WorkspaceName(Workspace workspace)=>workspace switch
    {
        Workspace.ComponentDesigner=>"Component Designer",Workspace.AssemblyEditor=>"Assembly Editor",Workspace.Garage=>"Garage",
        Workspace.HeadToHead=>"Head-to-Head Motion Visualizer",Workspace.Visualizer3D=>"3D Visualizer",_=>"Main menu"
    };

    internal void MainMenuClick(object sender,RoutedEventArgs e)=>NavigateWorkspace(Workspace.MainMenu);
    private void NavigateWorkspace(Workspace workspace)
    {
        if(!Enum.IsDefined(workspace)) throw new ArgumentOutOfRangeException(nameof(workspace));
        if(workspace==Workspace.ComponentDesigner && _componentViewport is null) BuildComponentDesigner();
        if(workspace==Workspace.AssemblyEditor && _assemblyViewport is null) BuildAssemblyEditor();
        if(workspace==Workspace.ComponentDesigner) ComponentAction(RefreshComponentLibrary);
        if(workspace==Workspace.AssemblyEditor)
        {
            AssemblyAction(RefreshAssemblyCatalog);
            if(_assemblyBaseRevision is not null&&_assemblyPublishedRaw==CaptureAssemblyRaw()) AssemblyAction(RefreshAssemblyDependencies);
        }
        _workspace=workspace;RootGrid.Visibility=IsSimulationWorkspace?Visibility.Visible:Visibility.Collapsed;
        foreach(var (destination,view) in _workspaceViews) view.Visibility=destination==workspace?Visibility.Visible:Visibility.Collapsed;
        if(IsSimulationWorkspace) {SetInspectionMode(workspace==Workspace.Visualizer3D);EnsureGarageAnalysisCurrent();}
        else _inspectionViewport?.SetActive(false);
        _componentViewport?.SetActive(workspace==Workspace.ComponentDesigner && WindowState!=WindowState.Minimized);
        _assemblyViewport?.SetActive(workspace==Workspace.AssemblyEditor && WindowState!=WindowState.Minimized);
        Title=IsSimulationWorkspace?"Suspension Simulation — Stock GR86 vs BRZ":"Suspension Simulation — "+WorkspaceName(workspace);
    }

    private object CaptureShellState()=>new
    {
        workspace=_workspace.ToString(),
        installedPreview=_installedPreview is null?null:new {garage=_installedPreviewGarage,result=_installedPreview},
        analysis=new {a=_analysisA.SelectedValue,b=_analysisB.SelectedValue},
        component=_componentReady?new {assetId=_componentId,baseRevision=_componentBaseRevision,rawDraft=CaptureComponentRaw(),camera=_componentViewport?.CaptureCamera()}:null,
        assembly=_assemblyReady?new {assetId=_assemblyId,baseRevision=_assemblyBaseRevision,rawDraft=CaptureAssemblyRaw(),camera=_assemblyViewport?.CaptureCamera(),manifest=_assemblyGraph?.Manifest,conflict=_assemblyConflict,motion=_twinsMotion,motionInputs=_twinsTravel.Select(x=>x.Text).Append(_twinsSteering.Text).ToArray()}:null,
        drafts=_workspaceDrafts.ToDictionary(x=>x.Key.ToString(),x=>new {name=x.Value.Name.Text,notes=x.Value.Notes.Text})
    };
    private void RestoreShellState(JsonElement ui)
    {
        if(!ui.TryGetProperty("shell",out var shell)) {NavigateWorkspace(_inspectionEnabled?Workspace.Visualizer3D:Workspace.HeadToHead);return;}
        if(_analysisInitialized && shell.TryGetProperty("analysis",out var analysis))
        {
            _refreshingAnalysis=true;
            try {_analysisA.SelectedValue=analysis.GetProperty("a").GetString();_analysisB.SelectedValue=analysis.GetProperty("b").GetString();}
            finally {_refreshingAnalysis=false;}
        }
        if(shell.TryGetProperty("drafts",out var drafts))
            foreach(var (workspace,fields) in _workspaceDrafts)
                if(drafts.TryGetProperty(workspace.ToString(),out var draft))
                {fields.Name.Text=draft.GetProperty("name").GetString()??"";fields.Notes.Text=draft.GetProperty("notes").GetString()??"";}
        if(Enum.TryParse<Workspace>(shell.GetProperty("workspace").GetString(),out var destination) && Enum.IsDefined(destination)) NavigateWorkspace(destination);
        else throw new ArgumentException("Unknown saved workspace.");
        if(shell.TryGetProperty("component",out var component) && component.ValueKind==JsonValueKind.Object && component.TryGetProperty("rawDraft",out var raw) && raw.ValueKind==JsonValueKind.String)
        {
            if(_componentViewport is null) BuildComponentDesigner();
            ComponentAction(()=>RestoreComponentRaw(raw.GetString()!,true));
            if(component.TryGetProperty("camera",out var camera) && camera.ValueKind==JsonValueKind.Object) _componentViewport?.RestoreCamera(camera);
            _componentViewport?.SetActive(_workspace==Workspace.ComponentDesigner&&WindowState!=WindowState.Minimized);
        }
        if(shell.TryGetProperty("assembly",out var assembly) && assembly.ValueKind==JsonValueKind.Object && assembly.TryGetProperty("rawDraft",out var assemblyRaw) && assemblyRaw.ValueKind==JsonValueKind.String)
        {
            if(_assemblyViewport is null) BuildAssemblyEditor();
            AssemblyAction(()=>RestoreAssemblyRaw(assemblyRaw.GetString()!,true));
            if(assembly.TryGetProperty("motionInputs",out var motionInputs)&&motionInputs.ValueKind==JsonValueKind.Array)
            {var values=motionInputs.EnumerateArray().Select(x=>x.GetString()??"0").ToArray();if(values.Length==5) {for(var i=0;i<4;i++) _twinsTravel[i].Text=values[i];_twinsSteering.Text=values[4];}}
            if(assembly.TryGetProperty("motion",out var motion)&&motion.ValueKind==JsonValueKind.Object)
                AssemblyAction(()=>{var saved=JsonSerializer.Deserialize<SuspensionSimulation.Solvers.Design.TwinsMotionState>(motion.GetRawText(),new JsonSerializerOptions {PropertyNameCaseInsensitive=true})!;ShowTwinsMotion(saved.Travel,saved.SteeringWheelDegrees);});
            if(assembly.TryGetProperty("camera",out var camera) && camera.ValueKind==JsonValueKind.Object) _assemblyViewport?.RestoreCamera(camera);
            _assemblyViewport?.SetActive(_workspace==Workspace.AssemblyEditor&&WindowState!=WindowState.Minimized);
        }
    }
}
