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
            AddWorkspace(destination,new ScrollViewer {Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
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
        _workspace=workspace;RootGrid.Visibility=IsSimulationWorkspace?Visibility.Visible:Visibility.Collapsed;
        foreach(var (destination,view) in _workspaceViews) view.Visibility=destination==workspace?Visibility.Visible:Visibility.Collapsed;
        if(IsSimulationWorkspace) SetInspectionMode(workspace==Workspace.Visualizer3D);
        else _inspectionViewport?.SetActive(false);
        Title=IsSimulationWorkspace?"Suspension Simulation — Stock GR86 vs BRZ":"Suspension Simulation — "+WorkspaceName(workspace);
    }

    private object CaptureShellState()=>new
    {
        workspace=_workspace.ToString(),
        drafts=_workspaceDrafts.ToDictionary(x=>x.Key.ToString(),x=>new {name=x.Value.Name.Text,notes=x.Value.Notes.Text})
    };
    private void RestoreShellState(JsonElement ui)
    {
        if(!ui.TryGetProperty("shell",out var shell)) {NavigateWorkspace(_inspectionEnabled?Workspace.Visualizer3D:Workspace.HeadToHead);return;}
        if(shell.TryGetProperty("drafts",out var drafts))
            foreach(var (workspace,fields) in _workspaceDrafts)
                if(drafts.TryGetProperty(workspace.ToString(),out var draft))
                {fields.Name.Text=draft.GetProperty("name").GetString()??"";fields.Notes.Text=draft.GetProperty("notes").GetString()??"";}
        if(Enum.TryParse<Workspace>(shell.GetProperty("workspace").GetString(),out var destination) && Enum.IsDefined(destination)) NavigateWorkspace(destination);
        else throw new ArgumentException("Unknown saved workspace.");
    }
}
