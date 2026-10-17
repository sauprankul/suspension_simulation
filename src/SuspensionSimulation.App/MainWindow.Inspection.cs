using System.IO;
using System.Windows;
using System.Windows.Controls;
using SuspensionSimulation.Solvers.Kinematics;
using SuspensionSimulation.Solvers.HeadToHead;

namespace SuspensionSimulation.App;

public partial class MainWindow
{
    private InspectionViewport? _inspectionViewport;
    private InspectionSceneBuilder? _sceneBuilder;
    private bool _inspectionEnabled;
    private AcceptedInspectionSnapshot? _acceptedSnapshot;
    private long? _rejectedRevision;
    private string? _rejectedError;

    private void ViewModeClick(object sender,RoutedEventArgs e)=>SetInspectionMode(!_inspectionEnabled);
    internal void SetInspectionMode(bool enabled)
    {
        if(_vehicle is null || _packageDirectory is null) return;
        try
        {
            if(enabled && _inspectionViewport is null)
            {
                _sceneBuilder ??=new InspectionSceneBuilder(_packageDirectory);
                _inspectionViewport=new InspectionViewport();
                _inspectionViewport.Failed+=InspectionFailed;
                _inspectionViewport.ExportRequested+=ExportInspection;
                InspectionViewportHost.Content=_inspectionViewport;
                _inspectionViewport.SetReference(_sceneBuilder.BuildReference(_vehicle.Chassis.CgHeightM));
                if(_acceptedSnapshot is null) _inspectionViewport.SetScene(_sceneBuilder.BuildReference(_vehicle.Chassis.CgHeightM));
            }
            _inspectionEnabled=enabled;
            ComparisonGrid.Visibility=StatusText.Visibility=enabled?Visibility.Collapsed:Visibility.Visible;
            RootGrid.RowDefinitions[2].Height=enabled?GridLength.Auto:new GridLength(210);
            AReferenceLabel.Visibility=BReferenceLabel.Visibility=enabled?Visibility.Collapsed:Visibility.Visible;
            ASetupPanel.Padding=BSetupPanel.Padding=new Thickness(enabled?8:13);
            ARsd.FontSize=ARcb.FontSize=BRsd.FontSize=BRcb.FontSize=enabled?20:26;
            InspectionHost.Visibility=enabled?Visibility.Visible:Visibility.Collapsed;
            ACarCanvas.Visibility=BCarCanvas.Visibility=enabled?Visibility.Collapsed:Visibility.Visible;
            MoveSelectedSetup();
            _inspectionViewport?.SetActive(enabled && WindowState!=WindowState.Minimized);
            ViewModeButton.Content=enabled?"Dashboard":"Open 3D inspection";
            UpdateInspectionScene();
            if(!enabled && _rejectedRevision is null) ViewSummary.Text="Shared synthetic geometry";
        }
        catch(Exception ex) {InspectionFailed(ex);}
    }
    private void InspectionSetupChanged(object sender,SelectionChangedEventArgs e)
    {if(_inspectionEnabled) {MoveSelectedSetup();UpdateInspectionScene();}}
    private void UpdateInspectionScene()
    {
        if(!_inspectionEnabled || _acceptedSnapshot is not {} snapshot || _inspectionViewport is null) return;
        var side=InspectionSetup.SelectedIndex==0?snapshot.A:snapshot.B;
        _inspectionViewport.SetState(side,snapshot.SolveMilliseconds);
        if(_rejectedRevision is {} rejected)
        {
            _inspectionViewport.MarkStale(rejected,_rejectedError??"Rejected input");
            ViewSummary.Text=$"STALE · accepted revision {snapshot.Revision} · rejected revision {rejected}";
            return;
        }
        ViewSummary.Text=$"A RSD {Percent(snapshot.A.FrontRsd)} · B RSD {Percent(snapshot.B.FrontRsd)} · revision {snapshot.Revision} · final pose · provisional equilibrium";
    }
    private void ComparisonToggleClick(object sender,RoutedEventArgs e)
    {
        var visible=ComparisonGrid.Visibility!=Visibility.Visible;
        ComparisonGrid.Visibility=StatusText.Visibility=visible?Visibility.Visible:Visibility.Collapsed;
        RootGrid.RowDefinitions[2].Height=visible?new GridLength(210):GridLength.Auto;
    }

    private void MoveSelectedSetup()
    {
        if(InspectionSetupHost.Content is Border previous)
        {InspectionSetupHost.Content=null;RootGrid.Children.Add(previous);}
        ASetupPanel.Visibility=BSetupPanel.Visibility=Visibility.Visible;
        if(!_inspectionEnabled) return;
        var selected=InspectionSetup.SelectedIndex==0?ASetupPanel:BSetupPanel;
        var hidden=InspectionSetup.SelectedIndex==0?BSetupPanel:ASetupPanel;
        RootGrid.Children.Remove(selected);hidden.Visibility=Visibility.Collapsed;
        InspectionSetupHost.Content=selected;
    }
    private void InspectionFailed(Exception error)
    {
        if(!Dispatcher.CheckAccess()) {Dispatcher.BeginInvoke(()=>InspectionFailed(error));return;}
        App.LogException("3D viewport unavailable",error);
        _inspectionViewport?.Dispose();_inspectionViewport=null;InspectionViewportHost.Content=null;
        SetInspectionMode(false);
        ViewModeButton.Content="Retry 3D inspection";
        StatusText.Text=$"3D could not initialize. Update the graphics driver and retry. {error.Message} · Log: {App.LogPath}";
    }
    internal void CaptureInspection(string path)=>_inspectionViewport?.SaveImage(path);
    private void ExportInspection()
    {
        if(_acceptedSnapshot is null || _inspectionViewport is null) return;
        try
        {
            var directory=Path.Combine(App.RepositoryRoot,"artifacts","inspection-snapshots",DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss-fff"));
            Directory.CreateDirectory(directory);
            var options=new System.Text.Json.JsonSerializerOptions {WriteIndented=true,IncludeFields=true,NumberHandling=System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals};
            options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
            _inspectionViewport.SaveImage(Path.Combine(directory,"screenshot.png"));
            File.WriteAllText(Path.Combine(directory,"snapshot.json"),System.Text.Json.JsonSerializer.Serialize(new
            {
                schema="suspension-inspection-snapshot/v1",snapshot=_acceptedSnapshot,
                selectedSetup=InspectionSetup.SelectedIndex==0?"A":"B",selectedComponent=_inspectionViewport.SelectedId,
                camera=_inspectionViewport.CaptureCamera(),rejectedRevision=_rejectedRevision,rejectedError=_rejectedError,
                screenshot="screenshot.png",renderer="HelixToolkit.Wpf.SharpDX 3.1.2",adapter=_inspectionViewport.Adapter,
                software=_inspectionViewport.Software
            },options));
            App.Log($"Inspection snapshot exported: {directory}","EXPORT");
            ViewSummary.Text=$"Snapshot exported to {directory}";
        }
        catch(Exception error) {App.LogException("Snapshot export failed",error);ViewSummary.Text="Snapshot export failed: "+error.Message;}
    }

    internal async void RunInspectionProof()
    {
        var directory=Path.Combine(App.RepositoryRoot,"artifacts","visualizer");Directory.CreateDirectory(directory);
        Exception? failure=null;
        var timings=new List<double>();
        var maximumGap=0d;
        var cameraFps=0d;var averageFrameMilliseconds=0d;var deviceRecovery=false;var exportVerified=false;
        var cameraBenchmarkSeconds=0d;long cameraRenderedFrames=0;
        try
        {
            await Task.Delay(400);SetInspectionMode(true);await Task.Delay(1000);
            if(_inspectionViewport is null) throw new InvalidOperationException("Viewport initialization failed.");
            foreach(var size in new[]{(1280d,720d),(1600d,900d)})
            {
                Width=size.Item1;Height=size.Item2;await Task.Delay(400);
                CaptureInspection(Path.Combine(directory,$"reference-{size.Item1}x{size.Item2}.png"));
                CaptureLayout(Path.Combine(directory,$"layout-{size.Item1}x{size.Item2}.png"));
            }
            var revision=_renderAttempt;var comparison=_comparison;
            foreach(var preset in new[]{"Front","Rear","Side","Top","Isometric"}) _inspectionViewport.Preset(preset);
            if(revision!=_renderAttempt || !ReferenceEquals(comparison,_comparison)) throw new InvalidOperationException("Camera changed calculation state.");
            InspectionSetup.SelectedIndex=1;InspectionSetup.SelectedIndex=0;
            SetInspectionMode(false);SetInspectionMode(true);await Task.Delay(400);
            Set(BFrontSpring,2.9);Set(BRearSpring,4.0);
            foreach(var g in new[]{(0d,.5d),(0d,-.5d),(.5d,0d),(-.5d,0d),(.3d,-.6d),(.07733333333333318,-1.1304347826086956)})
            {
                _ax=g.Item1;_ay=g.Item2;
                var elapsed=System.Diagnostics.Stopwatch.StartNew();Render();elapsed.Stop();timings.Add(elapsed.Elapsed.TotalMilliseconds);
                if(_rejectedRevision is not null) throw new InvalidOperationException(_rejectedError);
                maximumGap=Math.Max(maximumGap,_acceptedSnapshot!.B.Scene.Wheels.Max(x=>Math.Abs(x.GroundGapM)));
                await Task.Delay(80);
            }
            InspectionSetup.SelectedIndex=1;
            if(_acceptedSnapshot!.B.Diagnostics.Limits.Single(x=>x.Corner=="FR").Direction!="rebound") throw new InvalidOperationException("FR top-out missing.");
            CaptureInspection(Path.Combine(directory,"live-top-out.png"));
            _inspectionViewport.Select("FR.spring");await Task.Delay(100);
            CaptureLayout(Path.Combine(directory,"top-out-selected-layout.png"));
            Width=1280;Height=720;await Task.Delay(250);_inspectionViewport.Preset("Fit / reset");await Task.Delay(100);
            CaptureLayout(Path.Combine(directory,"top-out-selected-1280x720.png"));
            Width=1600;Height=900;await Task.Delay(250);_inspectionViewport.Preset("Isometric");
            ExportInspection();
            var export=Directory.EnumerateFiles(Path.Combine(App.RepositoryRoot,"artifacts","inspection-snapshots"),"snapshot.json",SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).First();
            using(var document=System.Text.Json.JsonDocument.Parse(File.ReadAllText(export)))
                exportVerified=document.RootElement.GetProperty("snapshot").GetProperty("B").GetProperty("Scene").GetProperty("Wheels")[0].GetProperty("CenterM").TryGetProperty("X",out _);
            if(!exportVerified) throw new InvalidOperationException("Snapshot export lost coordinates.");
            var lastValid=_acceptedSnapshot;var lastResult=_comparison;var oldId=AFrontId.Text;
            AFrontId.Text="999";Render();
            if(!ReferenceEquals(lastValid,_acceptedSnapshot)||!ReferenceEquals(lastResult,_comparison)||_rejectedRevision is null) throw new InvalidOperationException("Rejected input replaced accepted snapshot.");
            SetInspectionMode(false);SetInspectionMode(true);
            if(_rejectedRevision is null) throw new InvalidOperationException("Mode toggle cleared stale state.");
            AFrontId.Text=oldId;Render();
            if(_rejectedRevision is not null) throw new InvalidOperationException("Valid state did not recover.");
            var framesBefore=_inspectionViewport.FrameCount;var cameraWatch=System.Diagnostics.Stopwatch.StartNew();
            var cameraRevision=_renderAttempt;
            for(var i=0;i<180;i++)
            {
                _inspectionViewport.Viewport.AddRotateForce(.5,0);
                _inspectionViewport.Viewport.InvalidateRender();
                await Task.Delay(15);
            }
            cameraWatch.Stop();cameraBenchmarkSeconds=cameraWatch.Elapsed.TotalSeconds;cameraRenderedFrames=_inspectionViewport.FrameCount-framesBefore;
            cameraFps=cameraRenderedFrames/cameraBenchmarkSeconds;
            if(cameraRevision!=_renderAttempt) throw new InvalidOperationException("Camera movement triggered a solve.");
            var cameraBefore=_inspectionViewport.CaptureCamera();
            _inspectionViewport.Viewport.AddPanForce(8,4);_inspectionViewport.Viewport.AddZoomForce(.1);await Task.Delay(150);
            if(System.Text.Json.JsonSerializer.Serialize(cameraBefore)==System.Text.Json.JsonSerializer.Serialize(_inspectionViewport.CaptureCamera())) throw new InvalidOperationException("Pan/zoom did not move the camera.");
            averageFrameMilliseconds=_inspectionViewport.Viewport.RenderHost?.RenderStatistics?.LatencyStatistics.AverageValue??0;
            InspectionFailed(new InvalidOperationException("Injected device failure for recovery acceptance"));
            if(_inspectionEnabled || StatusText.Visibility!=Visibility.Visible) throw new InvalidOperationException("Device failure did not return to actionable dashboard.");
            SetInspectionMode(true);await Task.Delay(500);deviceRecovery=_inspectionEnabled && _inspectionViewport is not null;
            if(!deviceRecovery) throw new InvalidOperationException("Viewport did not reopen after device failure.");
        }
        catch(Exception ex) {failure=ex;}
        File.WriteAllText(Path.Combine(directory,"inspection-proof.json"),System.Text.Json.JsonSerializer.Serialize(new
        {success=failure is null,error=failure?.ToString(),solveAndPublishMs=timings,p95Milliseconds=timings.Count==0?0:timings.Order().ElementAt((int)Math.Ceiling(timings.Count*.95)-1),maximumGapM=maximumGap,
            cameraFps,cameraBenchmarkSeconds,cameraRenderedFrames,averageFrameMilliseconds,deviceRecovery,exportVerified,dpi=System.Windows.Media.VisualTreeHelper.GetDpi(this).PixelsPerDip,
            visualAcceptance="app-owned layout/state proof; separate Stage 3 native pointer and injected DPI results are in artifacts/stage3/acceptance.json"},new System.Text.Json.JsonSerializerOptions {WriteIndented=true}));
        Close();Application.Current.Shutdown(failure is null?0:2);
    }
    private void CaptureLayout(string path)
    {
        var layout=new System.Windows.Media.Imaging.RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,System.Windows.Media.PixelFormats.Pbgra32);
        layout.Render(this);
        var visual=new System.Windows.Media.DrawingVisual();
        using(var drawing=visual.RenderOpen())
        {
            drawing.DrawImage(layout,new Rect(0,0,ActualWidth,ActualHeight));
            if(_inspectionEnabled && _inspectionViewport is {} inspector)
            {
                var origin=inspector.Viewport.TranslatePoint(new Point(0,0),this);
                drawing.DrawImage(HelixToolkit.Wpf.SharpDX.ViewportExtensions.RenderBitmap(inspector.Viewport),new Rect(origin,new Size(inspector.Viewport.ActualWidth,inspector.Viewport.ActualHeight)));
            }
        }
        var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(visual);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var output=File.Create(path);encoder.Save(output);
    }
}
