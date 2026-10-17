using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using HelixToolkit.Wpf.SharpDX;
using SuspensionSimulation.Kinematics;

namespace SuspensionSimulation.App;

internal sealed partial class InspectionViewport
{
    private readonly Canvas _navigation=new(){Width=112,Height=112,Background=Brushes.Transparent,VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(0,0,0,8),Cursor=Cursors.Hand};
    private Point? _navigationDrag;
    private bool _rollDrag;
    private string? _navigationState;
    private Point _pickStart;
    private readonly Dictionary<string,CheckBox> _layerChecks=new();
    private int _cubeDowns,_cubeMoves,_cubeUps;
    public bool MeasurementPicking {get;set;}
    public event Action<Vector3>? PointPicked;
    internal object CubeInputState=>new {_cubeDowns,_cubeMoves,_cubeUps,drag=_navigationDrag?.ToString(),captured=_navigation.IsMouseCaptured};

    private FrameworkElement BuildNavigationHost()
    {
        var grid=new Grid {Background=new SolidColorBrush(Color.FromRgb(13,17,22))};
        grid.ColumnDefinitions.Add(new());grid.ColumnDefinitions.Add(new(){Width=new GridLength(112)});
        grid.Children.Add(Viewport);Grid.SetColumn(_navigation,1);grid.Children.Add(_navigation);
        _navigation.ToolTip="Drag cube to orbit; drag ring to roll around the viewing axis.";
        _navigation.MouseLeftButtonDown+=(_,e)=>
        {
            _cubeDowns++;
            var p=e.GetPosition(_navigation);_navigationDrag=p;
            _rollDrag=(p-new Point(56,56)).Length>=43;_navigation.CaptureMouse();e.Handled=true;
        };
        _navigation.MouseMove+=(_,e)=>
        {
            if(_navigationDrag is not {} previous || e.LeftButton!=MouseButtonState.Pressed) return;
            _cubeMoves++;
            var p=e.GetPosition(_navigation);
            if(_rollDrag)
            {
                var angle=Math.Atan2(p.Y-56,p.X-56)-Math.Atan2(previous.Y-56,previous.X-56);
                if(angle>Math.PI) angle-=2*Math.PI;if(angle< -Math.PI) angle+=2*Math.PI;
                RollCamera((float)-angle);
            }
            else OrbitCamera((float)(p.X-previous.X),(float)(p.Y-previous.Y));
            _navigationDrag=p;e.Handled=true;
        };
        _navigation.MouseLeftButtonUp+=(_,e)=>{_cubeUps++;_navigationDrag=null;_navigation.ReleaseMouseCapture();e.Handled=true;};
        _navigation.LostMouseCapture+=(_,_)=>_navigationDrag=null;
        Viewport.PreviewMouseLeftButtonDown+=(_,e)=>_pickStart=e.GetPosition(Viewport);
        Viewport.PreviewMouseLeftButtonUp+=(_,e)=>
        {
            var point=e.GetPosition(Viewport);
            if((point-_pickStart).Length>=4) return;
            // Pan handles the press before Helix raises MouseDown3D. Pick an unmoved
            // release directly, so a click selects while a drag remains a pan.
            var hit=Viewport.FindHits(point).FirstOrDefault();
            if(MeasurementPicking && hit is not null)
            {PointPicked?.Invoke(new((float)hit.PointHit.X,(float)hit.PointHit.Y,(float)hit.PointHit.Z));e.Handled=true;return;}
            if(hit?.ModelHit is Element3D {Tag:string id})
                Select(id.Replace(".marker","",StringComparison.Ordinal));
        };
        Viewport.PreviewMouseWheel+=(_,e)=>{Viewport.AddZoomForce(-e.Delta*.001);e.Handled=true;};
        return grid;
    }
    private CameraFrame ReadCamera()
    {
        var c=Viewport.Camera!;
        return new(new((float)c.Position.X,(float)c.Position.Y,(float)c.Position.Z),
            new((float)(c.Position.X+c.LookDirection.X),(float)(c.Position.Y+c.LookDirection.Y),(float)(c.Position.Z+c.LookDirection.Z)),
            new((float)c.UpDirection.X,(float)c.UpDirection.Y,(float)c.UpDirection.Z));
    }
    private void WriteCamera(CameraFrame frame)
    {
        var c=Viewport.Camera!;c.Position=new(frame.Position.X,frame.Position.Y,frame.Position.Z);
        var look=frame.Target-frame.Position;c.LookDirection=new(look.X,look.Y,look.Z);c.UpDirection=new(frame.Up.X,frame.Up.Y,frame.Up.Z);
        Viewport.InvalidateRender();UpdateNavigationCube();
    }
    internal void OrbitCamera(float dx,float dy)=>WriteCamera(ReadCamera().Orbit(dx,dy));
    internal void RollCamera(float radians)=>WriteCamera(ReadCamera().Roll(radians));
    internal void ShowLabels(bool visible)=>_layerChecks["labels"].IsChecked=visible;
    internal void ShowLayer(string layer,bool visible)=>_layerChecks[layer].IsChecked=visible;
    internal object CaptureStrokeDisplay()=>new {dots=_dots.Select(x=>new {id=x.Key,visible=x.Value.Model.IsRendering}).ToArray(),strokes=_scene?.SpringStrokes};
    internal object CaptureDashboardLayout()=>new {height=((Grid)Content).RowDefinitions[2].ActualHeight,
        viewportWidth=Viewport.ActualWidth,viewportHeight=Viewport.ActualHeight,labelCount=_labels.Count,
        whiteLabels=_labels.Values.All(x=>x.Model.Geometry is HelixToolkit.SharpDX.BillboardSingleText3D b && b.BackgroundColor==new HelixToolkit.Maths.Color4(1,1,1,1)),
        leftDragPans=Viewport.InputBindings.OfType<MouseBinding>().Any(x=>x.Command==HelixToolkit.Wpf.SharpDX.ViewportCommands.Pan && x.Gesture is MouseGesture {MouseAction:MouseAction.LeftClick}),
        cubeWidth=_navigation.ActualWidth,cubeHeight=_navigation.ActualHeight};
    private void UpdateNavigationCube()
    {
        if(Viewport.Camera is not {} camera) return;
        var signature=$"{camera.LookDirection}|{camera.UpDirection}";
        if(signature==_navigationState) return;_navigationState=signature;_navigation.Children.Clear();
        var ring=new Ellipse {Width=104,Height=104,Stroke=Brushes.LightSteelBlue,StrokeThickness=7,Fill=Brushes.Transparent};Canvas.SetLeft(ring,4);Canvas.SetTop(ring,4);_navigation.Children.Add(ring);
        var f=ReadCamera();var eye=Vector3.Normalize(f.Position-f.Target);var right=Vector3.Normalize(Vector3.Cross(-eye,f.Up));var up=Vector3.Normalize(Vector3.Cross(right,-eye));
        Point Project(Vector3 v)=>new(56+Vector3.Dot(v,right)*25,56-Vector3.Dot(v,up)*25);
        var faces=new[]{(Vector3.UnitX,Vector3.UnitY,Vector3.UnitZ,"Front"),(-Vector3.UnitX,-Vector3.UnitY,Vector3.UnitZ,"Rear"),
            (Vector3.UnitY,-Vector3.UnitX,Vector3.UnitZ,"Left"),(-Vector3.UnitY,Vector3.UnitX,Vector3.UnitZ,"Right"),
            (Vector3.UnitZ,Vector3.UnitX,Vector3.UnitY,"Top"),(-Vector3.UnitZ,Vector3.UnitX,-Vector3.UnitY,"Bottom")};
        foreach(var (normal,a,b,name) in faces.Where(x=>Vector3.Dot(x.Item1,eye)>0).OrderBy(x=>Vector3.Dot(x.Item1,eye)))
        {
            var polygon=new Polygon {Points=new PointCollection(new[]{Project((normal-a-b)*.65f),Project((normal+a-b)*.65f),Project((normal+a+b)*.65f),Project((normal-a+b)*.65f)}),
                Fill=new SolidColorBrush(Color.FromRgb((byte)(110+70*Vector3.Dot(normal,eye)),(byte)(130+70*Vector3.Dot(normal,eye)),(byte)(145+70*Vector3.Dot(normal,eye)))),Stroke=Brushes.White,StrokeThickness=1};
            _navigation.Children.Add(polygon);
            var p=Project(normal*.65f);var text=new TextBlock {Text=name=="Bottom"?"Btm":name[..1],FontSize=10,Foreground=Brushes.Black,Background=Brushes.White,Padding=new Thickness(1),IsHitTestVisible=false};
            text.Measure(new Size(100,100));Canvas.SetLeft(text,p.X-text.DesiredSize.Width/2);Canvas.SetTop(text,p.Y-text.DesiredSize.Height/2);_navigation.Children.Add(text);
        }
    }
}
