using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using SuspensionSimulation.Design;

namespace SuspensionSimulation.App;

/// <summary>Read-only inspection of real exported converged solver frames. It has no publication or solve path.</summary>
internal sealed class BumperDiagnosticWindow : Window
{
    private readonly InspectionViewport _viewport=new(false);
    private readonly TextBlock _badges=new(){Text="Open an exported diagnostic result.",TextWrapping=TextWrapping.Wrap,Height=78,FontSize=15,FontWeight=FontWeights.Bold},_readout=new(){TextWrapping=TextWrapping.Wrap,Height=112},_identity=new(){TextWrapping=TextWrapping.Wrap},_status=new(){TextWrapping=TextWrapping.Wrap};
    private readonly Slider _frame=new(){Minimum=0,Maximum=0,IsSnapToTickEnabled=true,TickFrequency=1,IsEnabled=false};
    private readonly Button _open=new(){Content="Open diagnostic JSON…"},_play=new(){Content="Play converged frames",IsEnabled=false},_pause=new(){Content="Pause",IsEnabled=false};
    private readonly Canvas _plot=new(){Height=210,Background=new SolidColorBrush(Color.FromRgb(11,16,22)),Margin=new Thickness(0,8,0,8)};
    private readonly CheckBox _fixtures=new(){Content="Show rigid fixtures",IsChecked=true};
    private readonly DispatcherTimer _playback=new(){Interval=TimeSpan.FromMilliseconds(250)};
    private LoadedFeaDiagnostic? _loaded;private bool _settingFrame,_closed;private int _openGeneration;
    internal BumperDiagnosticWindow()
    {
        Title="Bump Stop Studio — solver diagnostic inspection";Width=1460;Height=900;MinWidth=1100;MinHeight=700;WindowStartupLocation=WindowStartupLocation.CenterOwner;BumpStudioWindow.ApplyTheme(this);
        var dock=new DockPanel {Margin=new Thickness(10)};var toolbar=new StackPanel {Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,8)};toolbar.Children.Add(_open);toolbar.Children.Add(new TextBlock {Text="Read-only · actual solver deformation ×1 · no curve publication",Margin=new Thickness(12,4,0,0)});DockPanel.SetDock(toolbar,Dock.Top);dock.Children.Add(toolbar);
        var footer=new ScrollViewer {Content=_status,Height=70,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new Thickness(0,8,0,0)};DockPanel.SetDock(footer,Dock.Bottom);dock.Children.Add(footer);
        var grid=new Grid();grid.ColumnDefinitions.Add(new());grid.ColumnDefinitions.Add(new(){Width=new GridLength(420)});grid.Children.Add(_viewport);var panel=new StackPanel {Margin=new Thickness(12,0,0,0)};var details=new ScrollViewer {Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};Grid.SetColumn(details,1);grid.Children.Add(details);dock.Children.Add(grid);
        panel.Children.Add(_badges);panel.Children.Add(new TextBlock {Text="Discrete converged frames",FontWeight=FontWeights.Bold});panel.Children.Add(_frame);var controls=new StackPanel {Orientation=Orientation.Horizontal,Margin=new Thickness(0,5,0,8)};controls.Children.Add(_play);controls.Children.Add(_pause);panel.Children.Add(controls);panel.Children.Add(_fixtures);panel.Children.Add(new TextBlock {Text="Blue: deformable body · Gray: rigid fixtures",FontSize=11,Margin=new Thickness(0,0,0,6)});panel.Children.Add(_readout);panel.Children.Add(_plot);
        panel.Children.Add(new TextBlock {Text="Force is signed positive in compression. Curve tangents use the piecewise-linear interval ending at a sample; work is trapezoidal force integration from the first retained sample. Neither quantity is extrapolated. Negative tangents/reversals remain visible.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8)});
        panel.Children.Add(new TextBlock {Text="Exported source identities",FontWeight=FontWeights.Bold});panel.Children.Add(_identity);
        _status.Text="This viewer displays solver boundary surfaces and converged increments from the supplied file. It does not certify the backend, calibrate a material, or publish a suspension bumper curve.";Content=dock;
        _open.Click+=async(_,_)=>{var dialog=new Microsoft.Win32.OpenFileDialog {Filter="FEA diagnostic (*.json)|*.json",InitialDirectory=System.IO.Path.Combine(App.RepositoryRoot,"artifacts")};if(dialog.ShowDialog(this)==true) await OpenResult(dialog.FileName);};
        _frame.ValueChanged+=(_,_)=>{if(!_settingFrame&&_loaded is not null) ShowFrame((int)Math.Round(_frame.Value));};_play.Click+=(_,_)=>Play();_pause.Click+=(_,_)=>Pause();_fixtures.Checked+=(_,_)=>_viewport.ShowDiagnosticFixtures(true);_fixtures.Unchecked+=(_,_)=>_viewport.ShowDiagnosticFixtures(false);
        _playback.Tick+=(_,_)=>{if(_loaded is null||_frame.Value>=_loaded.Result.Frames.Length-1) {Pause();return;}_frame.Value+=1;};_plot.SizeChanged+=(_,_)=>DrawCurve();
        Loaded+=(_,_)=>_viewport.SetActive(true);Closed+=(_,_)=>{_closed=true;_openGeneration++;_playback.Stop();_viewport.Dispose();};
    }
    internal async Task<bool> OpenResult(string path)
    {
        Pause();var generation=++_openGeneration;_open.IsEnabled=false;
        try
        {
            var loaded=await Task.Run(()=>FeaDiagnosticResults.Load(path));if(_closed||generation!=_openGeneration) return false;
            _loaded=loaded;_viewport.SetDiagnosticResult(loaded.Result);_viewport.ShowDiagnosticFixtures(_fixtures.IsChecked==true);_settingFrame=true;_frame.Maximum=loaded.Result.Frames.Length-1;_frame.Value=0;_frame.IsEnabled=loaded.Result.Frames.Length>1;_settingFrame=false;
            _play.IsEnabled=loaded.Result.Frames.Length>1;_badges.Foreground=loaded.Result.Complete?Brushes.LightSkyBlue:Brushes.Gold;
            _badges.Text=$"{(loaded.Result.Complete?"COMPLETE RUN":"INCOMPLETE RUN")} · {(loaded.Result.Synthetic?"SYNTHETIC":"SOURCE AS EXPORTED")}\nExported case accepted: {(loaded.Result.CurveAccepted?"yes":"no")}\nBackend/material calibration not certified here";
            _identity.Text=$"Model SHA-256\n{loaded.Result.SourceModelSha256}\nInput SHA-256\n{loaded.Result.InputFileSha256}\nPlot SHA-256\n{loaded.Result.PlotFileSha256}\nDiagnostic file SHA-256\n{loaded.FileSha256}";
            _status.Text=$"{loaded.Result.Label}\n{loaded.Result.Conditions}\n{string.Join(" · ",loaded.Result.Warnings)}\nFile: {loaded.FilePath}";ShowFrame(0);DrawCurve();App.Log("Opened read-only FEA diagnostic "+loaded.FileSha256+": "+loaded.FilePath,"DIAGNOSTIC");return true;
        }
        catch(Exception error) {if(!_closed) {_status.Text="Diagnostic rejected; any prior result was retained. "+error.Message;App.LogException("FEA diagnostic import",error);}return false;}
        finally {if(!_closed&&generation==_openGeneration) _open.IsEnabled=true;}
    }
    private void ShowFrame(int index)
    {
        if(_loaded is null) return;var result=_loaded.Result;var frame=result.Frames[index];_viewport.SetDiagnosticFrame(index);var values=FeaDiagnosticResults.Evaluate(result,frame.CompressionM);
        _readout.Text=$"Frame {index+1} / {result.Frames.Length} · {frame.NormalizedTime*100:0.###}% of requested stroke\nShortening {frame.CompressionM*1000:0.###} / {result.RequestedCompressionM*1000:0.###} mm\nAxial force {frame.ForceN:+0.###;-0.###;0} N\nCurve tangent {(values.TangentNPerM is {} tangent?(tangent/1000).ToString("+0.###;-0.###;0",CultureInfo.InvariantCulture)+" N/mm":"undefined (one sample)")}\nWork since first sample {values.WorkFromFirstSampleJ:+0.######;-0.######;0} J\nWork reference: {result.Curve[0].CompressionM*1000:0.###} mm";DrawCurve();
    }
    private void Play() {if(_loaded?.Result.Frames.Length is not >1) return;if(_frame.Value>=_frame.Maximum) _frame.Value=0;_playback.Start();_play.IsEnabled=false;_pause.IsEnabled=true;}
    private void Pause() {_playback.Stop();_play.IsEnabled=_loaded?.Result.Frames.Length>1;_pause.IsEnabled=false;}
    private void DrawCurve()
    {
        _plot.Children.Clear();if(_loaded is null) return;var result=_loaded.Result;var width=Math.Max(80,_plot.ActualWidth-52);var height=_plot.Height-48;
        var xmin=result.Curve[0].CompressionM;var xmax=result.Curve[^1].CompressionM;if(xmax<=xmin) xmax=xmin+Math.Max(1e-6,result.RequestedCompressionM);
        var ymin=Math.Min(0,result.Curve.Min(p=>p.ForceN));var ymax=Math.Max(0,result.Curve.Max(p=>p.ForceN));if(ymax<=ymin) ymax=ymin+1;
        double X(double c)=>36+(c-xmin)/(xmax-xmin)*width;double Y(double f)=>12+height*(1-(f-ymin)/(ymax-ymin));
        var line=new System.Windows.Shapes.Polyline {Stroke=Brushes.LightSkyBlue,StrokeThickness=1.5};foreach(var p in result.Curve) line.Points.Add(new(X(p.CompressionM),Y(p.ForceN)));_plot.Children.Add(line);
        var selected=result.Frames[(int)Math.Round(_frame.Value)];_plot.Children.Add(new Line {X1=X(selected.CompressionM),X2=X(selected.CompressionM),Y1=12,Y2=12+height,Stroke=Brushes.Goldenrod,StrokeThickness=1});var marker=new Ellipse {Width=7,Height=7,Fill=Brushes.Gold};Canvas.SetLeft(marker,X(selected.CompressionM)-3.5);Canvas.SetTop(marker,Y(selected.ForceN)-3.5);_plot.Children.Add(marker);
        void Label(string text,double x,double y) {var label=new TextBlock {Text=text,Foreground=Brushes.White,FontSize=10};Canvas.SetLeft(label,x);Canvas.SetTop(label,y);_plot.Children.Add(label);}
        Label($"Force (N)  {ymin:0.###} … {ymax:0.###}",8,0);Label($"Shortening (mm)  {xmin*1000:0.###} … {xmax*1000:0.###}",36,_plot.Height-20);
    }
    internal object CaptureState()=>new {readOnly=true,diagnosticFile=_loaded?.FilePath,fileSha256=_loaded?.FileSha256,label=_loaded?.Result.Label,complete=_loaded?.Result.Complete,curveAccepted=_loaded?.Result.CurveAccepted,synthetic=_loaded?.Result.Synthetic,frame=(int)Math.Round(_frame.Value),playing=_playback.IsEnabled};
    internal async Task VerifyPlaybackForProof()
    {
        if(_loaded is null||_loaded.Result.Frames.Length<2) throw new InvalidOperationException("The proof needs at least two converged diagnostic frames.");await Task.Delay(200);var camera=JsonSerializer.Serialize(_viewport.CaptureCamera());var objects=_viewport.SceneObjectCount;var first=_viewport.DiagnosticDisplayForProof();_frame.Value=_frame.Maximum;var last=_viewport.DiagnosticDisplayForProof();
        if(first.Frame==last.Frame||objects!=_viewport.SceneObjectCount||camera!=JsonSerializer.Serialize(_viewport.CaptureCamera())) throw new InvalidOperationException("Diagnostic playback changed camera/resources or failed to select its actual frame.");
        var body=_loaded.Result.Parts.First(p=>p.Role==FeaDiagnosticPartRole.Body);var triangle=_loaded.Result.Triangles.First(t=>t.PartId==body.Id);var n=_loaded.Result.Nodes.Select((node,index)=>(node,index)).Single(item=>item.node.Id==triangle.A).index;var target=_loaded.Result.Nodes[n].PositionM+_loaded.Result.Frames[^1].DisplacementsM[n];
        if((new DesignVector(last.FirstBodyVertex.X,last.FirstBodyVertex.Y,last.FirstBodyVertex.Z)-target).Length>1e-6) throw new InvalidOperationException("Displayed diagnostic geometry does not equal actual node position plus displacement at 1×.");
        var snapshot=_loaded;var status=_status.Text;await OpenResult(System.IO.Path.Combine(App.RepositoryRoot,"artifacts/does-not-exist-diagnostic.json"));
        if(!ReferenceEquals(snapshot,_loaded)) throw new InvalidOperationException("Invalid diagnostic import replaced the accepted viewer data.");_status.Text=status;_frame.Value=0;Play();await Task.Delay(600);Pause();if(_frame.Value==0) throw new InvalidOperationException("Diagnostic Play did not advance converged frames.");
    }
    internal bool DiagnosticFlagsForProof(bool complete,bool accepted)=>_loaded is {} loaded&&loaded.Result.Complete==complete&&loaded.Result.CurveAccepted==accepted&&loaded.Result.Synthetic&&_badges.Text.Contains(complete?"COMPLETE RUN":"INCOMPLETE RUN",StringComparison.Ordinal)&&_badges.Text.Contains("SYNTHETIC",StringComparison.Ordinal);
    internal void SelectLastFrameForProof()=>_frame.Value=_frame.Maximum;
    internal void CaptureProof(string path)
    {
        var layout=new System.Windows.Media.Imaging.RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);layout.Render(this);var visual=new DrawingVisual();using(var drawing=visual.RenderOpen()) {drawing.DrawImage(layout,new Rect(0,0,ActualWidth,ActualHeight));var origin=_viewport.Viewport.TranslatePoint(new Point(0,0),this);drawing.DrawImage(HelixToolkit.Wpf.SharpDX.ViewportExtensions.RenderBitmap(_viewport.Viewport),new Rect(origin,new Size(_viewport.Viewport.ActualWidth,_viewport.Viewport.ActualHeight)));}var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using var output=File.Create(path);encoder.Save(output);
    }
}
