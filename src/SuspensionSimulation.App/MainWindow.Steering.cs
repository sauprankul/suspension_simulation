using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using SuspensionSimulation.Kinematics;
using SuspensionSimulation.Solvers.HeadToHead;
using SuspensionSimulation.Solvers.Kinematics;

namespace SuspensionSimulation.App;

public partial class MainWindow
{
    private SteeringGeometry _steeringGeometry=new();
    private bool _restoringControls;
    private void SteeringSliderChanged(object sender,RoutedPropertyChangedEventArgs<double> e)
    {
        if(!IsLoaded || _restoringControls) return;
        SteeringAngle.Text=e.NewValue.ToString("0.0",CultureInfo.InvariantCulture);ScheduleRender();
    }
    private void SteeringTextChanged(object sender,RoutedEventArgs e)
    {
        if(!IsLoaded || _restoringControls) return;
        if(double.TryParse(SteeringAngle.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var angle) && double.IsFinite(angle) && Math.Abs(angle)<=SteeringControl.Maximum)
            SteeringControl.Value=angle;
        Render();
    }
    private void CenterSteeringClick(object sender,RoutedEventArgs e)
    {SteeringAngle.Text="0";SteeringControl.Value=0;Render();}
    private double ReadSteering()
    {
        if(!double.TryParse(SteeringAngle.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var angle)||!double.IsFinite(angle)) throw new ArgumentException("Steering angle must be a finite number.");
        _steeringGeometry.RackDisplacementM(angle);return angle;
    }
    private void SteeringGeometryClick(object sender,RoutedEventArgs e)
    {
        var dialog=new SteeringGeometryWindow(_steeringGeometry){Owner=this};
        if(dialog.ShowDialog()!=true || dialog.Result is null || _packageDirectory is null) return;
        try
        {
            var candidate=dialog.Result;
            var solver=new HeadToHeadComparisonSolver(_packageDirectory,candidate);
            _=new InspectionSceneBuilder(_packageDirectory,candidate);
            var path=Path.Combine(_packageDirectory,"steering.json");
            File.WriteAllText(path+".tmp",JsonSerializer.Serialize(candidate,new JsonSerializerOptions {WriteIndented=true}));File.Move(path+".tmp",path,true);
            _steeringGeometry=candidate;_headToHeadSolver=solver;_sceneBuilder=new InspectionSceneBuilder(_packageDirectory,candidate);
            SteeringControl.Minimum=-candidate.SteeringWheelLockDegrees;SteeringControl.Maximum=candidate.SteeringWheelLockDegrees;
            _inspectionViewport?.SetReference(_sceneBuilder.BuildReference(_vehicle!.Chassis.CgHeightM));
            Render();
        }
        catch(Exception error) {App.LogException("Steering geometry rejected",error);MessageBox.Show(this,error.Message,"Geometry not applied",MessageBoxButton.OK,MessageBoxImage.Warning);}
    }

    internal void RestoreLiveState(string path)
    {
        using var document=JsonDocument.Parse(File.ReadAllText(path));
        var ui=document.RootElement.GetProperty("state").GetProperty("ui");
        var inputs=ui.GetProperty("inputs");var controls=inputs.GetProperty("controls");
        _restoringControls=true;
        try
        {
            void Text(TextBox box,JsonElement item,string name)=>box.Text=item.GetProperty(name).GetString()??"";
            Text(SweepCap,controls,"axisCapG");Text(TotalGCap,controls,"combinedCapG");Text(FrontUnsprungMass,controls,"frontUnsprungMassKgPerCorner");Text(RearUnsprungMass,controls,"rearUnsprungMassKgPerCorner");
            foreach(var first in new[]{true,false})
            {
                var item=controls.GetProperty(first?"setupA":"setupB");
                Text(first?AFrontSpring:BFrontSpring,item,"frontSpringKgfPerMm");Text(first?ARearSpring:BRearSpring,item,"rearSpringKgfPerMm");
                Text(first?AFrontOd:BFrontOd,item,"frontBarOdMm");Text(first?AFrontId:BFrontId,item,"frontBarIdMm");Text(first?ARearOd:BRearOd,item,"rearBarOdMm");Text(first?ARearId:BRearId,item,"rearBarIdMm");
            }
            if(controls.TryGetProperty("steeringWheelDegrees",out var steering)) SteeringAngle.Text=steering.GetString()??"0";
            _ax=inputs.GetProperty("axG").GetDouble();_ay=inputs.GetProperty("ayG").GetDouble();
        }
        finally {_restoringControls=false;}
        Render();
        if(double.TryParse(SteeringAngle.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var restoredAngle))
        {_restoringControls=true;SteeringControl.Value=restoredAngle;_restoringControls=false;}
        var view=ui.GetProperty("view");InspectionSetup.SelectedIndex=view.GetProperty("selectedSetup").GetString()=="B"?1:0;
        if(view.GetProperty("mode").GetString()=="3D")
        {
            SetInspectionMode(true);
            if(view.TryGetProperty("camera",out var camera) && camera.ValueKind==JsonValueKind.Object) _inspectionViewport?.RestoreCamera(camera);
            if(view.TryGetProperty("selectedComponent",out var selected) && selected.ValueKind==JsonValueKind.String) _inspectionViewport?.Select(selected.GetString()!);
        }
        if(ui.TryGetProperty("window",out var window) && window.TryGetProperty("state",out var state) && state.GetString()=="Maximized") WindowState=WindowState.Maximized;
    }
}

internal sealed class SteeringGeometryWindow : Window
{
    public SteeringGeometry? Result {get;private set;}
    public SteeringGeometryWindow(SteeringGeometry current)
    {
        Title="Shared rack geometry and static alignment";Width=490;Height=650;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new StackPanel {Margin=new Thickness(16)};Content=new ScrollViewer {Content=root,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        root.Children.Add(new TextBlock {Text="Metres become millimetres below. +X forward, +Y left, +Z up from the static CG ground projection. Both inner joints translate together along Y.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8)});
        var fields=new Dictionary<string,TextBox>();
        var values=new (string Name,string Label,double Value)[]{
            ("x","Rack center X (mm)",current.RackCenterXM*1000),("y","Rack center Y (mm)",current.RackCenterYM*1000),("z","Rack center Z (mm)",current.RackCenterZM*1000),
            ("spacing","Inner joint half-spacing (mm)",current.InnerHalfSpacingM*1000),
            ("ox","Left outer pickup ΔX from wheel center (mm)",current.OuterOffsetXM*1000),("oy","Left outer pickup ΔY (mirrored on right, mm)",current.OuterOffsetYM*1000),("oz","Outer pickup ΔZ (mm)",current.OuterOffsetZM*1000),
            ("travel","Rack travel from center to lock (mm)",current.RackTravelAtLockM*1000),("lock","Steering wheel angle at lock (degrees)",current.SteeringWheelLockDegrees),
            ("fc","Front static camber (degrees)",current.FrontStaticCamberDegrees),("rc","Rear static camber (degrees)",current.RearStaticCamberDegrees),
            ("ft","Front static TOTAL toe (degrees, + toe-in)",current.FrontStaticTotalToeDegrees),("rt","Rear static TOTAL toe (degrees, + toe-in)",current.RearStaticTotalToeDegrees)};
        foreach(var (name,label,value) in values)
        {
            var row=new DockPanel {Margin=new Thickness(0,3,0,3)};var box=new TextBox {Text=value.ToString("0.#####",CultureInfo.InvariantCulture),Width=90,Padding=new Thickness(3)};
            fields[name]=box;DockPanel.SetDock(box,Dock.Right);row.Children.Add(box);row.Children.Add(new TextBlock {Text=label,VerticalAlignment=VerticalAlignment.Center});root.Children.Add(row);
        }
        root.Children.Add(new TextBlock {Text="Positive steering wheel angle turns left (rack moves −Y). Positive camber means top outward. Static alignment sets the installed wheel-plane reference; it does not relocate hard points. Rack travel is an estimate until measured.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8)});
        var error=new TextBlock {Foreground=System.Windows.Media.Brushes.Firebrick,TextWrapping=TextWrapping.Wrap};root.Children.Add(error);
        var apply=new Button {Content="Apply shared geometry and alignment",Padding=new Thickness(8)};root.Children.Add(apply);
        apply.Click+=(_,_)=>
        {
            try
            {
                double V(string key)=>double.Parse(fields[key].Text,CultureInfo.InvariantCulture);
                var value=current with {RackCenterXM=V("x")/1000,RackCenterYM=V("y")/1000,RackCenterZM=V("z")/1000,InnerHalfSpacingM=V("spacing")/1000,
                    OuterOffsetXM=V("ox")/1000,OuterOffsetYM=V("oy")/1000,OuterOffsetZM=V("oz")/1000,RackTravelAtLockM=V("travel")/1000,SteeringWheelLockDegrees=V("lock"),
                    FrontStaticCamberDegrees=V("fc"),RearStaticCamberDegrees=V("rc"),FrontStaticTotalToeDegrees=V("ft"),RearStaticTotalToeDegrees=V("rt")};
                value.Validate();Result=value;DialogResult=true;
            }
            catch(Exception ex) {error.Text=ex.Message;}
        };
    }
}
