using System.Numerics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.SharpDX;
using HelixToolkit.Wpf.SharpDX;
using SuspensionSimulation.Kinematics;
using SuspensionSimulation.Solvers.HeadToHead;

namespace SuspensionSimulation.App;

internal sealed partial class InspectionViewport
{
    private readonly Dictionary<string,TextBlock[]> _readouts=new();
    private readonly Dictionary<string,TextBlock> _axleReadouts=new();
    private FrameworkElement BuildReadoutDashboard()
    {
        var panel=new StackPanel {Height=168};
        var grid=new Grid {Height=108};
        grid.ColumnDefinitions.Add(new(){Width=new GridLength(94)});
        for(var i=0;i<4;i++) grid.ColumnDefinitions.Add(new());
        for(var i=0;i<6;i++) grid.RowDefinitions.Add(new(){Height=new GridLength(18)});
        void Put(string text,int row,int column,Brush color)
        {var block=new TextBlock {Text=text,Foreground=color,FontSize=11};Grid.SetRow(block,row);Grid.SetColumn(block,column);grid.Children.Add(block);}
        foreach(var (name,row) in new[]{("Load (N)",1),("Jounce (mm)",2),("Camber (°)",3),("Heading (°)",4),("Δ bump toe (°)",5)}) Put(name,row,0,Brushes.LightSteelBlue);
        var corners=new[]{"FL","FR","RL","RR"};
        for(var column=1;column<=4;column++)
        {
            var corner=corners[column-1];Put(corner,0,column,corner.StartsWith("F")?Brushes.DeepSkyBlue:Brushes.Orange);
            var values=new TextBlock[5];
            for(var row=1;row<=5;row++)
            {var block=new TextBlock {Text="—",Foreground=Brushes.White,FontSize=13,FontWeight=FontWeights.SemiBold};Grid.SetColumn(block,column);Grid.SetRow(block,row);grid.Children.Add(block);values[row-1]=block;}
            _readouts[corner]=values;
        }
        panel.Children.Add(grid);
        var axles=new UniformGrid {Columns=4,Rows=2,Height=60};
        foreach(var name in new[]{"Front total toe","Rear total toe","Front thrust","Rear thrust","Front mean camber","Rear mean camber","Rack travel","Steering ratio"})
        {
            var tile=new StackPanel();tile.Children.Add(new TextBlock {Text=name,FontSize=10,Foreground=Brushes.LightSteelBlue});
            var number=new TextBlock {Text="—",FontSize=14,Foreground=Brushes.White,FontWeight=FontWeights.Bold};tile.Children.Add(number);_axleReadouts[name]=number;axles.Children.Add(tile);
        }
        panel.Children.Add(axles);return panel;
    }
    private void UpdateReadouts(InspectionSideSnapshot side)
    {
        foreach(var (corner,blocks) in _readouts)
        {
            var a=side.Steering?.Wheels.Single(x=>x.Corner==corner);
            blocks[0].Text=side.NormalLoadsN[corner].ToString("0");blocks[1].Text=(side.Corners[corner].WheelJounceM*1000).ToString("+0.00;-0.00;0.00");
            blocks[2].Text=a?.CamberDegrees.ToString("+0.00;-0.00;0.00")??"—";blocks[3].Text=a?.HeadingDegrees.ToString("+0.00;-0.00;0.00")??"—";
            blocks[4].Text=a?.BumpToeDegrees.ToString("+0.00;-0.00;0.00")??"—";
        }
        if(side.Steering is not {} s) return;
        void Degrees(string name,double value)=>_axleReadouts[name].Text=value.ToString("+0.00;-0.00;0.00")+"°";
        Degrees("Front total toe",s.Front.TotalToeDegrees);Degrees("Rear total toe",s.Rear.TotalToeDegrees);Degrees("Front thrust",s.Front.ThrustDegrees);Degrees("Rear thrust",s.Rear.ThrustDegrees);
        Degrees("Front mean camber",s.Front.MeanCamberDegrees);Degrees("Rear mean camber",s.Rear.MeanCamberDegrees);
        _axleReadouts["Rack travel"].Text=(s.RackDisplacementM*1000).ToString("+0.00;-0.00;0.00")+" mm";
        _axleReadouts["Steering ratio"].Text=Math.Abs(s.Front.ThrustDegrees)<.001?"—":Math.Abs(s.SteeringWheelDegrees/s.Front.ThrustDegrees).ToString("0.0")+":1";
        _axleReadouts["Front total toe"].ToolTip="Positive = convergence (toe-in); includes the solved left/right Ackermann difference while steering.";
        _axleReadouts["Steering ratio"].ToolTip="Steering-wheel angle divided by mean front wheel heading. Rack travel per degree is set in rack geometry.";
    }
    private void ShowDetails()
    {
        var dialog=new Window {Title="Selected component and technical diagnostics",Owner=Window.GetWindow(this),Width=680,Height=480,WindowStartupLocation=WindowStartupLocation.CenterOwner,
            Content=new TextBox {Text=_detail.Text+"\n\n"+_technical.Text,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new Thickness(12)}};
        dialog.ShowDialog();
    }
    private static string LabelName(InspectionSegment line)=>line.Id.Split('.').Last() switch
    {
        "lower-arm-front"=>"Lower arm","upper-arm-front"=>"Upper arm","steering"=>line.Corner.StartsWith("F")?"Tie rod":"Toe link",
        "spring"=>"Spring","bar-support"=>"Bar support*","bar-arm"=>"ARB arm","endlink"=>"Endlink","strut"=>"Strut","upright"=>"Upright","hub"=>"Hub",
        "lower-axis"=>"Lower pivots","upper-axis"=>"Upper pivots","trailing-reference"=>"Trailing link*",_=>line.Label
    };
    private static string LabelCaption(InspectionSegment line)=>line.Id switch
    {
        "front-bar.axis"=>"Front ARB","rear-bar.axis"=>"Rear ARB","rack.bar"=>"Rack","rack.housing"=>"Rack housing",
        _=>$"{line.Corner} {LabelName(line)}"
    };
    private void SetLabel(string id,string layer,string caption,Vector3 position)
    {
        if(_labels.TryGetValue(id,out var existing))
        {
            _labels[id]=(existing.Model,layer,caption,position);
        }
        else
        {
            var geometry=new BillboardSingleText3D {TextInfo=new TextInfo(caption,position),FontSize=12,FontColor=new(0,0,0,1),BackgroundColor=new(1,1,1,1),Padding=new(3,1,3,1)};
            var model=new BillboardTextModel3D {Geometry=geometry,FixedSize=false,IsHitTestVisible=false};Viewport.Items.Add(model);_labels[id]=(model,layer,caption,position);
        }
        _labelLayoutSignature=null;
    }
    internal void RestoreCamera(JsonElement camera)
    {
        _initialFit=true;_sceneFitted=true;
        var p=camera.GetProperty("position");var l=camera.GetProperty("lookDirection");var u=camera.GetProperty("upDirection");
        Viewport.Camera=new HelixToolkit.Wpf.SharpDX.OrthographicCamera {Position=new(p[0].GetDouble(),p[1].GetDouble(),p[2].GetDouble()),LookDirection=new(l[0].GetDouble(),l[1].GetDouble(),l[2].GetDouble()),UpDirection=new(u[0].GetDouble(),u[1].GetDouble(),u[2].GetDouble()),Width=camera.GetProperty("width").GetDouble()};
        if(camera.TryGetProperty("visibleLayers",out var layers))
        {
            var restored=layers.EnumerateArray().Select(x=>x.GetString()!).ToHashSet();
            foreach(var (layer,check) in _layerChecks) check.IsChecked=restored.Contains(layer);
            _visible.Clear();foreach(var layer in restored) _visible.Add(layer);ApplyVisibility();
        }
    }
}
