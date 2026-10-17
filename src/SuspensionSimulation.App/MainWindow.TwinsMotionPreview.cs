using System.IO;
using System.Windows;
using System.Windows.Controls;
using SuspensionSimulation.Design;
using SuspensionSimulation.Solvers.Design;

namespace SuspensionSimulation.App;

public partial class MainWindow
{
    private readonly TextBox[] _twinsTravel=Enumerable.Range(0,4).Select(_=>new TextBox {Text="0"}).ToArray();
    private readonly TextBox _twinsSteering=new(){Text="0"};
    private readonly TextBlock _twinsMotionReadout=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,0)};
    private TwinsMotionState? _twinsMotion;
    private void AddTwinsMotionTools(TabControl tabs,StackPanel left)
    {
        var example=new Button {Content="Load explicit twins example",Margin=new Thickness(0,3,0,3),Padding=new Thickness(5,3,5,3)};
        example.Click+=(_,_)=>AssemblyAction(()=>
        {
            var seed=TwinsTopologyCatalog.Seed(_designLibrary!,Path.Combine(App.RepositoryRoot,"data/synthetic/gr86/design-topology.json"));_assemblyExpectedDof.Text="5";LoadAssembly(seed.Vehicle);_assemblyViewport!.Preset("Isometric");
        });left.Children.Insert(Math.Min(7,left.Children.Count),example);
        foreach(var variant in new[]{"GR86","BRZ"})
        {
            var installed=new Button {Content="Load installed "+variant+" example",Margin=new Thickness(0,3,0,3),Padding=new Thickness(5,3,5,3)};
            installed.Click+=(_,_)=>AssemblyAction(()=>{var models=TwinsInstalledCatalog.Seed(_designLibrary!,App.RepositoryRoot);_assemblyExpectedDof.Text="5";LoadAssembly(variant=="GR86"?models.Gr86:models.Brz);_assemblyViewport!.Preset("Isometric");});left.Children.Insert(Math.Min(8,left.Children.Count),installed);
        }
        var panel=new StackPanel {Margin=new Thickness(10)};panel.Children.Add(new TextBlock {Text="Explicit twins mechanism preview",FontWeight=FontWeights.Bold});
        panel.Children.Add(new TextBlock {Text="Load the explicit twins example first. Travel is imposed for kinematic inspection. Spring forces and loaded ride height are not solved here.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8)});
        foreach(var (name,index) in new[]{("Front left jounce (mm)",0),("Front right jounce (mm)",1),("Rear left jounce (mm)",2),("Rear right jounce (mm)",3)})
        {panel.Children.Add(new TextBlock {Text=name});panel.Children.Add(_twinsTravel[index]);}
        panel.Children.Add(new TextBlock {Text="Steering wheel angle (degrees, ±450)"});panel.Children.Add(_twinsSteering);
        var solve=new Button {Content="Solve motion preview",Margin=new Thickness(0,10,0,8)};solve.Click+=(_,_)=>AssemblyAction(SolveTwinsMotionPreview);panel.Children.Add(solve);
        panel.Children.Add(new TextBlock {Text="Camber is negative with the tire top inward. Heading difference is left heading minus right heading. Thrust is the heading of the average forward tire directions. All are measured from the solved wheel frames.",TextWrapping=TextWrapping.Wrap});panel.Children.Add(_twinsMotionReadout);
        tabs.Items.Add(new TabItem {Header="Motion",Content=new ScrollViewer {Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}});
    }
    private void SolveTwinsMotionPreview()
    {
        var values=_twinsTravel.Select(x=>DesignerNumbers.Metres(x.Text)).ToArray();var travel=new FourCornerTravel(values[0],values[1],values[2],values[3]);
        ShowTwinsMotion(travel,DesignerNumbers.Number(_twinsSteering.Text));
    }
    private void ShowTwinsMotion(FourCornerTravel travel,double steering)
    {
        var source=_assemblyGraph??throw new InvalidOperationException("Load an explicit mechanism first.");var graph=GraphAdjustments.Freeze(source,[]);var kinematics=new TwinsKinematics(graph);var solved=kinematics.Solve(travel,steering);
        var mechanical=new InstalledMechanics(kinematics.System).Evaluate(solved.Pose.Coordinates);
        _assemblyViewport!.SetScene(AssemblyScene(graph,kinematics.System,solved.Pose));_twinsMotion=solved;
        static double D(double rad)=>rad*180/Math.PI;
        _twinsMotionReadout.Text=string.Join("\n",solved.Wheels.Select(w=>$"{w.Corner} · camber {D(w.CamberRad):+0.000;-0.000;0.000}° · heading {D(w.HeadingRad):+0.000;-0.000;0.000}°"))+
            $"\n\nFront heading difference {D(solved.Front.HeadingDifferenceRad):0.000}° · thrust {D(solved.Front.ThrustAngleRad):0.000}°\nRear heading difference {D(solved.Rear.HeadingDifferenceRad):0.000}° · thrust {D(solved.Rear.ThrustAngleRad):0.000}°\nScaled closure residual {solved.Pose.MaximumScaledResidual:0.###E+0}";
        if(mechanical.Elements.Length>0) _twinsMotionReadout.Text+="\n\n"+string.Join("\n",mechanical.Elements.Select(e=>$"{e.Path}: {e.ForceOrTorque:0.0} {(e.Kind=="torsion-bar"?"N·m":"N")}"))+"\n\n"+string.Join("\n",mechanical.Limits.Select(l=>$"{l.Description}: {l.GapM*1000:0.0} mm"));
        _assemblyStatus.Text="Solved motion preview · synthetic geometry · tire dimensions estimated · no loaded equilibrium.";
    }
}
