using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using HelixToolkit.SharpDX;
using HelixToolkit.Wpf.SharpDX;
using SuspensionSimulation.Kinematics;
using SuspensionSimulation.Solvers.HeadToHead;
using HCamera=HelixToolkit.Wpf.SharpDX.OrthographicCamera;

namespace SuspensionSimulation.App;

internal sealed partial class InspectionViewport : UserControl, IDisposable
{
    private readonly DefaultEffectsManager _effects;
    internal Viewport3DX Viewport {get;}
    private readonly Dictionary<string,(LineGeometryModel3D Model,string Layer)> _models=new();
    private readonly Dictionary<string,(PointGeometryModel3D Model,string Layer)> _dots=new();
    private readonly Dictionary<string,(BillboardTextModel3D Model,string Layer,string Caption,Vector3 Anchor)> _labels=new();
    private readonly HashSet<string> _visible=new(["chassis","front","rear","bars","wheels","ground"]);
    private readonly TextBlock _status=new(){TextWrapping=TextWrapping.Wrap,Foreground=Brushes.LightSteelBlue,FontSize=11};
    private readonly TextBlock _detail=new(){TextWrapping=TextWrapping.Wrap,Foreground=Brushes.White,MinHeight=32};
    private readonly TextBlock _technical=new(){TextWrapping=TextWrapping.Wrap,Foreground=Brushes.LightSteelBlue,FontSize=11};
    private readonly UniformGrid _travel=new(){Columns=4,Height=32,Margin=new Thickness(0,2,0,2)};
    private readonly ComboBox _components=new(){DisplayMemberPath="Label",SelectedValuePath="Id",MaxDropDownHeight=220};
    private InspectionScene? _scene;
    private bool _disposed;
    private InspectionSideSnapshot? _side;
    private long _frameCount;
    private bool _initialFit;
    private readonly RenderTimerResolution _timerResolution=new();
    internal bool TimerResolutionActive=>_timerResolution.Active;
    private readonly DispatcherTimer _renderPump=new(DispatcherPriority.Background) {Interval=TimeSpan.FromMilliseconds(8)};
    public event Action<Exception>? Failed;
    public event Action? ExportRequested;
    public string Adapter {get;}
    public bool Software {get;}
    public string? SelectedId => _components.SelectedValue as string;
    public long FrameCount=>Interlocked.Read(ref _frameCount);
    public int SceneObjectCount=>Viewport.Items.Count;
    public string ResourceSummary=>_effects.GetResourceCountSummary();
    public long? GpuMemoryUsageBytes
    {
        get
        {
            try
            {
                using var dxgi=_effects.Device!.QueryInterface<SharpDX.DXGI.Device>();using var adapter=dxgi.Adapter;
                using var adapter3=adapter.QueryInterface<SharpDX.DXGI.Adapter3>();
                return adapter3.QueryVideoMemoryInfo(0,SharpDX.DXGI.MemorySegmentGroup.Local).CurrentUsage;
            }
            catch(SharpDX.SharpDXException) {return null;}
        }
    }

    public InspectionViewport()
    {
        _effects=new DefaultEffectsManager(new EffectsManagerConfiguration {EnableSoftwareRendering=false});
        try
        {
        using(var dxgi=(_effects.Device??throw new InvalidOperationException("Graphics device unavailable.")).QueryInterface<SharpDX.DXGI.Device>())
        using(var adapter=dxgi.Adapter)
        using(var adapter1=adapter.QueryInterface<SharpDX.DXGI.Adapter1>())
        {
            Adapter=adapter.Description.Description.TrimEnd('\0');
            Software=(adapter1.Description1.Flags & SharpDX.DXGI.AdapterFlags.Software)!=0;
        }
        if(Software) throw new InvalidOperationException("A hardware Direct3D adapter is required for 3D inspection; the available adapter is software rendering.");
        Viewport=new Viewport3DX {EffectsManager=_effects,ModelUpDirection=new Vector3D(0,0,1),
            BackgroundColor=Color.FromRgb(13,17,22),ShowCoordinateSystem=true,ShowViewCube=false,IsInertiaEnabled=false,UseDefaultGestures=false,
            EnableSwapChainRendering=true};
        Viewport.RenderExceptionOccurred+=(_,e)=>
        {
            e.Handled=true;var error=e.Exception;
            Dispatcher.BeginInvoke(()=>{if(!_disposed) Failed?.Invoke(error);});
        };
        Viewport.OnRendered+=(_,_)=>Interlocked.Increment(ref _frameCount);
        // The host's composition callback can be throttled on this machine despite ~1 ms
        // GPU frames. Drive the existing Helix host on the UI dispatcher; idle hosts skip work.
        _renderPump.Tick+=(_,_)=>
        {
            if(_disposed || !IsVisible || Window.GetWindow(this)?.WindowState==WindowState.Minimized) return;
            try {UpdateNavigationCube();UpdateLabelLayout();if(Viewport.RenderHost is {IsRendering:true} host) host.UpdateAndRender();}
            catch(Exception error) {Failed?.Invoke(error);}
        };
        Loaded+=(_,_)=>SetActive(IsVisible);
        Viewport.Loaded+=(_,_)=>{if(!_initialFit) {Fit();_initialFit=true;}};
        // Explicit bindings keep the displayed help accurate across renderer defaults.
        Viewport.InputBindings.Add(new MouseBinding(ViewportCommands.Pan,new MouseGesture(MouseAction.LeftClick)));
        Viewport.InputBindings.Add(new MouseBinding(ViewportCommands.Pan,new MouseGesture(MouseAction.MiddleClick)));
        var grid=new Grid();grid.RowDefinitions.Add(new(){Height=GridLength.Auto});grid.RowDefinitions.Add(new());
        grid.RowDefinitions.Add(new(){Height=new GridLength(264)});grid.RowDefinitions.Add(new(){Height=new GridLength(0)});
        var tools=new WrapPanel {Margin=new Thickness(0,0,0,5)};
        foreach(var name in new[]{"Isometric","Front","Rear","Side","Top","Fit / reset"})
        {var button=new Button {Content=name,Padding=new Thickness(6,3,6,3),Margin=new Thickness(0,0,3,3)};button.Click+=(_,_)=>Preset(name);tools.Children.Add(button);}
        foreach(var layer in new[]{"chassis","front","rear","bars","wheels","labels","ground","reference"})
        {
            var check=new CheckBox {Content=layer,IsChecked=_visible.Contains(layer),Margin=new Thickness(3),Foreground=Brushes.White};
            _layerChecks[layer]=check;
            check.Checked+=(_,_)=>{_visible.Add(layer);ApplyVisibility();};
            check.Unchecked+=(_,_)=>{_visible.Remove(layer);ApplyVisibility();};tools.Children.Add(check);
        }
        var ghost=new CheckBox {Content="neutral ghost",Foreground=Brushes.White,Margin=new Thickness(3)};
        _layerChecks["ghost"]=ghost;
        ghost.Checked+=(_,_)=>{_visible.Add("ghost");ApplyVisibility();};ghost.Unchecked+=(_,_)=>{_visible.Remove("ghost");ApplyVisibility();};tools.Children.Add(ghost);
        var export=new Button {Content="Export snapshot",Padding=new Thickness(6,3,6,3),Margin=new Thickness(3)};
        export.Click+=(_,_)=>ExportRequested?.Invoke();tools.Children.Add(export);
        grid.Children.Add(tools);var navigationHost=BuildNavigationHost();Grid.SetRow(navigationHost,1);grid.Children.Add(navigationHost);
        var info=new StackPanel {Margin=new Thickness(0,4,0,0),ClipToBounds=true};
        info.Children.Add(_travel);
        info.Children.Add(BuildReadoutDashboard());
        info.Children.Add(new TextBlock {Text="Drag model: pan · Drag cube: orbit · Drag ring: roll · Wheel: zoom · Click: inspect",Foreground=Brushes.LightGray,FontSize=10,Height=15});
        var selectionRow=new DockPanel {Height=24};
        var details=new Button {Content="Details",Padding=new Thickness(5,1,5,1)};details.Click+=(_,_)=>ShowDetails();DockPanel.SetDock(details,Dock.Right);selectionRow.Children.Add(details);
        _components.SelectionChanged+=(_,_)=>ShowSelection();selectionRow.Children.Add(_components);info.Children.Add(selectionRow);
        _status.TextWrapping=TextWrapping.NoWrap;_status.TextTrimming=TextTrimming.CharacterEllipsis;_status.Height=18;
        info.Children.Add(_status);
        Grid.SetRow(info,2);grid.Children.Add(info);Content=grid;
        Preset("Isometric");
        AddGround();
        }
        catch
        {
            _renderPump.Stop();_timerResolution.Dispose();Viewport?.Dispose();_effects.Dispose();throw;
        }
    }

    internal void Preset(string name)
    {
        var position=name switch {"Front"=>new Point3D(6,0,.5),"Rear"=>new Point3D(-6,0,.5),"Side"=>new Point3D(0,-6,.5),"Top"=>new Point3D(0,0,7),_=>new Point3D(4,-5,3.5)};
        Viewport.Camera=new HCamera {Position=position,LookDirection=new Vector3D(-position.X,-position.Y,.35-position.Z),
            UpDirection=name=="Top"?new Vector3D(1,0,0):new Vector3D(0,0,1),Width=5};
        Fit();
    }

    private void Fit()
    {
        if(_scene is null || Viewport.Camera is not HCamera camera || Viewport.ActualHeight<=0) return;
        var positions=_scene.Points.Select(x=>x.PositionM).ToList();
        foreach(var w in _scene.Wheels)
            foreach(var x in new[]{-1,1}) foreach(var y in new[]{-1,1}) foreach(var z in new[]{-1,1})
                positions.Add(w.CenterM+new Vector3((float)(x*w.RadiusM),(float)(y*w.WidthM/2),(float)(z*w.RadiusM)));
        var center=positions.Aggregate(Vector3.Zero,(sum,p)=>sum+p)/positions.Count;
        var forward=new Vector3((float)camera.LookDirection.X,(float)camera.LookDirection.Y,(float)camera.LookDirection.Z);forward=Vector3.Normalize(forward);
        var up=new Vector3((float)camera.UpDirection.X,(float)camera.UpDirection.Y,(float)camera.UpDirection.Z);
        var right=Vector3.Normalize(Vector3.Cross(forward,up));up=Vector3.Normalize(Vector3.Cross(right,forward));
        var horizontal=positions.Select(p=>Vector3.Dot(p-center,right)).ToArray();var vertical=positions.Select(p=>Vector3.Dot(p-center,up)).ToArray();
        camera.Width=1.15*Math.Max(2*horizontal.Max(Math.Abs),2*vertical.Max(Math.Abs)*Viewport.ActualWidth/Viewport.ActualHeight);
        camera.Position=new Point3D(center.X-forward.X*6,center.Y-forward.Y*6,center.Z-forward.Z*6);
        camera.LookDirection=new Vector3D(forward.X*6,forward.Y*6,forward.Z*6);
    }

    public void SetScene(InspectionScene scene)
    {
        _scene=scene;
        _status.Foreground=Brushes.LightSteelBlue;
        _status.ToolTip=null;
        foreach(var segment in scene.Segments)
        {
            SetLine(segment.Id,segment.Layer,segment.StartM,segment.EndM,
                segment.ActiveLimit?Colors.Tomato:segment.Id.EndsWith(".spring")?Colors.LimeGreen:ColorFor(segment.Layer),segment.Source==SceneSource.UnconstrainedReference);
            if(!segment.Id.EndsWith("-arm-rear") && (segment.Source==SceneSource.Solved || segment.Id is "rack.housing" or "front-bar.axis" or "rear-bar.axis" || segment.Layer=="reference"))
                SetLabel(segment.Id,segment.Layer,LabelCaption(segment),(segment.StartM+segment.EndM)/2);
        }
        foreach(var point in scene.Points)
        {
            if(point.Id.EndsWith(".springUpper")||point.Id.EndsWith(".springLower")||point.Id.EndsWith(".stroke-marker"))
            {
                var marker=point.Id.EndsWith(".stroke-marker");
                var active=scene.SpringStrokes.Any(x=>x.Corner==point.Corner && (x.BumpRemainingM==0||x.ReboundRemainingM==0));
                SetDot(point.Id,point.Corner.StartsWith("F")?"front":"rear",point.PositionM,marker?(active?Colors.Tomato:Colors.Gold):Colors.White,marker?10:7);
                continue;
            }
            var d=new Vector3(.012f,0,0);
            SetLine(point.Id+".marker",point.Id.EndsWith("ideal-contact")?"ground":point.Source==SceneSource.FixedChassis?"chassis":point.Corner.StartsWith("F")?"front":"rear",point.PositionM-d,point.PositionM+d,Colors.Gainsboro);
        }
        foreach(var wheel in scene.Wheels)
        {
            var builder=new LineBuilder();
            Vector3 At(double angle,double width) => wheel.CenterM+Vector3.Transform(new((float)(wheel.RadiusM*Math.Cos(angle)),(float)width,(float)(wheel.RadiusM*Math.Sin(angle))),wheel.Orientation);
            for(var i=0;i<48;i++)
            {
                var a=i*2*Math.PI/48;var b=(i+1)*2*Math.PI/48;
                foreach(var edge in new[]{-wheel.WidthM/2,wheel.WidthM/2}) builder.AddLine(At(a,edge),At(b,edge));
                if(i%6==0) builder.AddLine(At(a,-wheel.WidthM/2),At(a,wheel.WidthM/2));
            }
            SetGeometry(wheel.Corner+".tire","wheels",builder.ToLineGeometry3D(),Colors.SlateGray);
            SetLine(wheel.Corner+".wheel-axis","wheels",wheel.CenterM,wheel.CenterM+Vector3.Transform(new Vector3(0,.2f,0),wheel.Orientation),Colors.White);
            SetLabel(wheel.Corner+".tire","wheels",wheel.Corner+" Tire",wheel.CenterM+new Vector3(0,0,.4f));
        }
        var selected=SelectedId;
        _components.ItemsSource=scene.Segments.Select(x=>new ComponentChoice(x.Id,$"{x.Corner} · {x.Label}")).Concat(scene.Points.Select(x=>new ComponentChoice(x.Id,$"{x.Corner} · {x.Label}"))).Concat(scene.Wheels.Select(x=>new ComponentChoice(x.Corner+".tire",x.Corner+" · Tire (visual estimate)"))).ToArray();
        _components.SelectedValue=selected;ApplyVisibility();ShowSelection();
        _status.Text=$"Live · {scene.StateId} · synthetic geometry / provisional equilibrium";
        _technical.Text=$"{Adapter} · {(Software?"SOFTWARE":"hardware GPU")} · {scene.StateId}\n{string.Join(" ",scene.Diagnostics)}";
    }

    public void SetState(InspectionSideSnapshot side,double solveMilliseconds)
    {
        _side=side;SetScene(side.Scene);_travel.Children.Clear();
        UpdateReadouts(side);
        foreach(var limit in side.Diagnostics.Limits.OrderBy(x=>x.Corner))
        {
            var near=Math.Min(limit.ReboundRemainingM,limit.BumpRemainingM)<.01;
            var color=limit.Active?Brushes.Tomato:near?Brushes.Goldenrod:Brushes.LightSteelBlue;
            var panel=new StackPanel();
            var select=new Button {Content=$"{limit.Corner} · {(limit.Direction=="rebound"?"TOP-OUT":limit.Direction=="bump"?"BOTTOM-OUT":near?"NEAR LIMIT":"IN TRAVEL")}",
                Foreground=color,Padding=new Thickness(1,0,1,0),Height=18,FontSize=10};
            select.Click+=(_,_)=>Select(limit.Corner+".spring");panel.Children.Add(select);
            var stroke=side.Scene.SpringStrokes.Single(x=>x.Corner==limit.Corner);
            panel.Children.Add(new TextBlock {Text=$"B {stroke.BumpRemainingM*1000:0.0} / R {stroke.ReboundRemainingM*1000:0.0} mm",Foreground=color,FontSize=10,
                ToolTip="Component-axis stroke remaining. Gold/red dot to upper mount = remaining compression stroke."});
            _travel.Children.Add(panel);
        }
        _technical.Text+=$"\nSolve + pose {solveMilliseconds:0.0} ms · render {Viewport.RenderHost?.RenderStatistics?.LatencyStatistics.AverageValue:0.00} ms · objects {SceneObjectCount} · frames {FrameCount}";
        var gap=side.Scene.Wheels.Max(x=>Math.Abs(x.GroundGapM));
        _technical.Text+=$"\nEstimated tire/ground envelope gap: {gap*1000:0.0} mm. Body equilibrium and stiffness use the centered rack; steering is constrained kinematic inspection at that body state. Static alignment calibrates the wheel-plane reference.";
        ShowSelection();
    }

    public void SetReference(InspectionScene reference)
    {
        foreach(var line in reference.Segments.Where(x=>x.Layer!="reference")) SetLine("ghost."+line.Id,"ghost",line.StartM,line.EndM,Colors.DarkSlateBlue);
        foreach(var wheel in reference.Wheels)
        {
            var builder=new LineBuilder();
            for(var i=0;i<48;i++)
            {
                Vector3 At(int index)=>wheel.CenterM+Vector3.Transform(new Vector3((float)(wheel.RadiusM*Math.Cos(index*2*Math.PI/48)),0,(float)(wheel.RadiusM*Math.Sin(index*2*Math.PI/48))),wheel.Orientation);
                builder.AddLine(At(i),At(i+1));
            }
            SetGeometry("ghost."+wheel.Corner+".tire","ghost",builder.ToLineGeometry3D(),Colors.DarkSlateBlue);
        }
        ApplyVisibility();
    }

    public void Select(string id)=>_components.SelectedValue=id;
    public object CaptureCamera()=>new {projection="orthographic",position=new[]{Viewport.Camera!.Position.X,Viewport.Camera.Position.Y,Viewport.Camera.Position.Z},
        lookDirection=new[]{Viewport.Camera.LookDirection.X,Viewport.Camera.LookDirection.Y,Viewport.Camera.LookDirection.Z},
        upDirection=new[]{Viewport.Camera.UpDirection.X,Viewport.Camera.UpDirection.Y,Viewport.Camera.UpDirection.Z},width=(Viewport.Camera as HCamera)?.Width,
        visibleLayers=_visible.Order().ToArray()};

    private void SetLine(string id,string layer,Vector3 a,Vector3 b,Color color,bool dashed=false)
    {
        var builder=new LineBuilder();
        if(dashed) {for(var i=0;i<12;i+=2) builder.AddLine(Vector3.Lerp(a,b,i/12f),Vector3.Lerp(a,b,(i+1)/12f));}
        else builder.AddLine(a,b);
        SetGeometry(id,layer,builder.ToLineGeometry3D(),color);
    }
    private void SetGeometry(string id,string layer,LineGeometry3D geometry,Color color)
    {
        geometry.IsDynamic=layer is not ("ground" or "ghost");
        if(_models.TryGetValue(id,out var entry))
        {
            if(entry.Model.Geometry is {} existing && existing.Positions is {} positions && geometry.Positions is {} incoming && positions.Count==incoming.Count)
            {
                for(var i=0;i<incoming.Count;i++) positions[i]=incoming[i];
                existing.UpdateBounds();existing.UpdateVertices();
            }
            else entry.Model.Geometry=geometry;
            entry.Model.Color=color;
        }
        else
        {
            var model=new LineGeometryModel3D {Geometry=geometry,Color=color,Thickness=layer=="ground"?1:2.5,HitTestThickness=5,Tag=id,IsHitTestVisible=layer!="ghost"};
            _models[id]=(model,layer);Viewport.Items.Add(model);
        }
    }
    private void AddGround()
    {
        for(var i=-6;i<=6;i++)
        {
            SetLine("ground.x"+i,"ground",new(-3,i*.5f,0),new(3,i*.5f,0),Colors.DarkSlateGray);
            SetLine("ground.y"+i,"ground",new(i*.5f,-3,0),new(i*.5f,3,0),Colors.DarkSlateGray);
        }
    }
    private void ApplyVisibility()
    {
        foreach(var (id,entry) in _models)
            entry.Model.IsRendering=_visible.Contains(entry.Layer) && (!id.StartsWith("label-leader.") || _labels.TryGetValue(id[13..],out var label) && _visible.Contains(label.Layer));
        foreach(var entry in _labels.Values) entry.Model.IsRendering=_visible.Contains("labels") && _visible.Contains(entry.Layer);
        foreach(var entry in _dots.Values) entry.Model.IsRendering=_visible.Contains(entry.Layer);
        _labelLayoutSignature=null;
        Viewport.InvalidateRender();
    }
    private void ShowSelection()
    {
        var segment=_scene?.Segments.FirstOrDefault(x=>x.Id==SelectedId);
        var point=_scene?.Points.FirstOrDefault(x=>x.Id==SelectedId);
        var wheel=_scene?.Wheels.FirstOrDefault(x=>x.Corner+".tire"==SelectedId);
        _detail.Text=segment is not null?$"{segment.Corner} {segment.Label} · {Source(segment.Source)} · length {Vector3.Distance(segment.StartM,segment.EndM)*1000:0.00} mm\n{Coordinates(segment.StartM)} → {Coordinates(segment.EndM)}":
            point is not null?$"{point.Corner} {point.Label} · {point.Source} · {Coordinates(point.PositionM)}":
            wheel is not null?$"{wheel.Corner} tire · estimated rigid visual · ground gap {wheel.GroundGapM*1000:+0.0;-0.0;0.0} mm\n{Coordinates(wheel.CenterM)}": "Select a component in the view or list.";
        var corner=segment?.Corner??point?.Corner??wheel?.Corner;
        foreach(var (id,entry) in _models) entry.Model.Thickness=id==SelectedId || id==SelectedId+".marker"?4.5:entry.Layer=="labels"?.7:entry.Layer=="ground"?1:2.5;
        if(corner is not null && _side?.Corners.TryGetValue(corner,out var cornerPose)==true)
            _detail.Text+=$" · jounce {cornerPose.WheelJounceM*1000:0.00} mm · normal load {_side.NormalLoadsN[corner]:0.0} N";
        var stroke=_scene?.SpringStrokes.FirstOrDefault(x=>x.Corner==corner);
        if(stroke is not null && (segment?.Id.EndsWith(".spring")==true || point is not null))
            _detail.Text+=$"\nComponent length {stroke.CurrentLengthM*1000:0.00} mm; rigid range {stroke.MinimumLengthM*1000:0.00}–{stroke.MaximumLengthM*1000:0.00} mm. Remaining bump {stroke.BumpRemainingM*1000:0.00} mm / rebound {stroke.ReboundRemainingM*1000:0.00} mm.\nWhite dots: upper/lower mounts. Gold/red dot: remaining compression stroke measured from the upper mount; coincides with it at bottom-out.";
        if(segment?.Layer=="bars" && _side is not null)
        {
            var bar=_side.Bars[segment.Corner is "FL" or "FR" or "front-bar"?0:1];
            _detail.Text+=$" · final bar twist {bar.TwistRad*180/Math.PI:0.000}° · torque {bar.TorqueNm:0.0} N·m";
        }
        var limit=_side?.Diagnostics.Limits.FirstOrDefault(x=>x.Corner==corner && x.Active);
        if(limit is null && corner is null) limit=_side?.Diagnostics.Limits.FirstOrDefault(x=>x.Active);
        if(limit is not null)
        {
            _detail.Text+=$"\n{limit.Message} {limit.LimitId}: exact travel constraint, reaction {limit.ReactionForceN:0.0} N, ∞ loading stiffness.\n"+
                "Further load changes the reaction and the remaining free body motion. Unloading releases the stop. RSD is undefined if both axles have active rigid stops.";
        }
    }
    private static string Source(SceneSource source)=>source switch {SceneSource.FixedChassis=>"fixed chassis",SceneSource.VisualEstimate=>"visual estimate",SceneSource.UnconstrainedReference=>"not constrained by current solver",_=>"solved geometry"};
    private static string Coordinates(Vector3 p)=>$"({p.X:0.000}, {p.Y:0.000}, {p.Z:0.000}) m";
    private static Color ColorFor(string layer)=>layer switch {"front"=>Colors.DeepSkyBlue,"rear"=>Colors.Orange,"bars"=>Colors.MediumPurple,"reference"=>Colors.DarkGray,_=>Colors.SlateGray};
    public void SetActive(bool active)
    {
        if(Viewport.RenderHost is {} host) host.IsRendering=active;
        _timerResolution.SetActive(active && !_disposed);
        if(active && !_disposed) _renderPump.Start();else _renderPump.Stop();
    }
    public void MarkStale(long rejectedRevision,string error)
    {
        _status.Text=$"STALE · displaying {_scene?.StateId??"no accepted pose"} · rejected revision {rejectedRevision}: {error}";
        _status.Foreground=Brushes.Orange;
        _status.ToolTip=_status.Text;
    }
    public void SaveImage(string path)=>Viewport.SaveScreen(path);
    private void SetDot(string id,string layer,Vector3 position,Color color,double size)
    {
        if(_dots.TryGetValue(id,out var existing))
        {
            var geometry=(PointGeometry3D)existing.Model.Geometry!;geometry.Positions![0]=position;geometry.UpdateBounds();geometry.UpdateVertices();existing.Model.Color=color;
        }
        else
        {
            var model=new PointGeometryModel3D {Geometry=new PointGeometry3D {Positions=new([position]),IsDynamic=true},Color=color,Size=new(size,size),Figure=PointFigure.Ellipse,Tag=id,DepthBias=-50};
            _dots[id]=(model,layer);Viewport.Items.Add(model);
        }
    }
    public void Dispose() {if(_disposed)return;_disposed=true;_renderPump.Stop();_timerResolution.Dispose();Viewport.Dispose();_effects.Dispose();}
    private sealed record ComponentChoice(string Id,string Label);
}
