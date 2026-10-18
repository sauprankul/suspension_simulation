using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using SuspensionSimulation.Design;
using SuspensionSimulation.Solvers.Design;

namespace SuspensionSimulation.App;

internal sealed partial class BumpStudioWindow
{
    private readonly LocalFebioBumperBackend _backend;
    private readonly DesignAssetLibrary _outputLibrary;
    private readonly Button _runJob=new(){Content="Run coarse FEA"},_cancelJob=new(){Content="Cancel job",IsEnabled=false},
        _createComponent=new(){Content="Create bumper component",IsEnabled=false},_viewJob=new(){Content="View deformation",IsEnabled=false};
    private readonly TextBlock _jobStatus=new(){Text="No compression job run.",TextWrapping=TextWrapping.Wrap,Height=68,Margin=new Thickness(0,8,0,4)},
        _curveValues=new(){Text="No complete curve available.",Height=44,TextWrapping=TextWrapping.Wrap};
    private readonly TextBox _jobPath=new(){IsReadOnly=true,TextWrapping=TextWrapping.Wrap,Height=64};
    private readonly ProgressBar _jobProgress=new(){Minimum=0,Maximum=1,Height=12,Margin=new Thickness(0,8,0,8)};
    private readonly Canvas _jobPlot=new(){Height=180,Background=Brushes.Black,ClipToBounds=true,Margin=new Thickness(0,8,0,8)};
    private CancellationTokenSource? _jobCancellation;
    private CompletedBumperFeaJob? _completedJob;
    private string? _jobFolder,_publishedComponentId;
    internal event Action<ComponentDefinition>? ComponentCreated;

    private void BuildJobPanel(StackPanel panel)
    {
        panel.Children.Add(new TextBlock {Text="Coarse estimated force curve",FontWeight=FontWeights.Bold});
        panel.Children.Add(new TextBlock {Text="Start with a simple solid, large elements and a short stroke. A single-mesh job uses real nonlinear FEA and contact; its result is an engineering estimate without mesh convergence or physical calibration.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,6)});
        void Field(string key,string label,string initial)
        {panel.Children.Add(new TextBlock {Text=label,Margin=new Thickness(0,5,0,2)});var box=new TextBox {Text=initial};_fields.Add(key,box);panel.Children.Add(box);box.TextChanged+=(_,_)=>SaveDraft();}
        Field("steps","Initial compression steps (2–100; solver may subdivide)","8");Field("budget","Time budget in seconds (up to 28,800 / eight hours)","180");
        var actions=new WrapPanel();panel.Children.Add(actions);foreach(var button in new[]{_runJob,_cancelJob,_viewJob,_createComponent}) {button.Margin=new Thickness(0,8,6,0);button.Padding=new Thickness(8,4,8,4);actions.Children.Add(button);}
        _runJob.Click+=async(_,_)=>await RunJobFromUi();_cancelJob.Click+=(_,_)=>CancelJob();_viewJob.Click+=async(_,_)=>await ViewCompletedJob();_createComponent.Click+=(_,_)=>Act(()=>PublishCompletedComponent());
        var open=new Button {Content="Open saved completed job…",Margin=new Thickness(0,8,0,0)};open.Click+=(_,_)=>Act(ChooseCompletedJob);panel.Children.Add(open);
        panel.Children.Add(_jobProgress);panel.Children.Add(_jobStatus);panel.Children.Add(_jobPlot);panel.Children.Add(_curveValues);
        panel.Children.Add(new TextBlock {Text="Job folder · input, status and native logs",Margin=new Thickness(0,8,0,2)});panel.Children.Add(_jobPath);
        panel.Children.Add(new TextBlock {Text="The completed job freezes the STEP, material and recipe. Component creation always uses those frozen values. To change material or geometry, run another job. Install the published component from Assembly Editor → Install bumper.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)});
        _jobPlot.SizeChanged+=(_,_)=>DrawJobCurve();
    }
    private async void LoadExample()
    {
        try {_units.SelectedItem=StepLengthUnit.Millimetres;await ImportStep(System.IO.Path.Combine(App.RepositoryRoot,"data/examples/bumper-cylinder-mm.step"));ConfigureEndFaces();_confirmed.IsChecked=true;_tabs.SelectedIndex=4;_status.Text="Simple 40 × 20 mm cylinder loaded. Review the material, then run its coarse estimate.";}
        catch(Exception error) {ReportJobError(error);}
    }
    private void ConfigureEndFaces()
    {
        if(_cad is null) throw new ArgumentException("Import a STEP solid first.");
        var frame=ReadFrame();double Z(DesignVector p)=>(p-frame.OriginM).Dot(frame.Axis);
        var nodes=_cad.Nodes.ToDictionary(n=>n.Tag,n=>n.PositionM);var z=_cad.Nodes.Select(n=>Z(n.PositionM)).ToArray();var bottom=z.Min();var top=z.Max();
        var roles=new Dictionary<int,BumperFaceRole>();
        foreach(var face in _cad.Faces)
        {
            var positions=_cad.Triangles.Where(t=>t.FaceTag==face.Tag).SelectMany(t=>new[]{t.A,t.B,t.C}).Distinct().Select(n=>Z(nodes[n])).ToArray();
            if(positions.All(v=>Math.Abs(v-bottom)<2e-7)) roles[face.Tag]=BumperFaceRole.LowerPlaten;
            else if(positions.All(v=>Math.Abs(v-top)<2e-7)) roles[face.Tag]=BumperFaceRole.UpperPlaten;
        }
        if(!roles.Values.Contains(BumperFaceRole.LowerPlaten)||!roles.Values.Contains(BumperFaceRole.UpperPlaten)) throw new ArgumentException("No planar end caps found in this compression direction. Adjust orientation or assign actual faces manually.");
        foreach(var face in _faces) if(roles.TryGetValue(face.FaceTag,out var role)) face.Role=role;else if(face.Role is BumperFaceRole.LowerPlaten or BumperFaceRole.UpperPlaten) face.Role=BumperFaceRole.Unassigned;
        SetVector("origin",frame.OriginM+frame.Axis*bottom,true);_fields["freeLength"].Text=DesignerNumbers.Mm(top-bottom);_fields["compression"].Text=DesignerNumbers.Mm((top-bottom)*.2);
        _fields["lowerGap"].Text="0";_fields["upperGap"].Text="0";_lowerBonded.IsChecked=true;_faceGrid.Items.Refresh();Preview();
        _status.Text="Planar end faces selected. Confirm the dimensions and inspect the fixtures before running.";
    }
    private async Task RunJobFromUi()
    {try {await RunCompressionJob();}catch(OperationCanceledException) {_jobStatus.Text="Canceled. Input and partial diagnostics retained; no component was created.";}catch(Exception error) {ReportJobError(error);}}
    private void ReportJobError(Exception error) {_status.Text=error.Message;_jobStatus.Text=error.Message;App.LogException("Bump Stop Studio job",error);}
    internal async Task<CompletedBumperFeaJob> RunCompressionJob()
    {
        if(_jobCancellation is not null) throw new InvalidOperationException("A compression job is already running.");
        if(!int.TryParse(_fields["steps"].Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out var steps)||steps is <2 or >100) throw new ArgumentException("Use 2–100 whole compression steps.");
        SaveRecipe();var hash=_recipeHash!;_jobCancellation=new();_completedJob=null;_jobFolder=null;_publishedComponentId=null;_createComponent.IsEnabled=false;_viewJob.IsEnabled=false;_runJob.IsEnabled=false;_cancelJob.IsEnabled=true;DrawJobCurve();
        try
        {
            _completedJob=await _backend.RunAsync(hash,steps,new Progress<BumperFeaProgress>(ShowJobProgress),_jobCancellation.Token);
            ShowCompletedJob(_completedJob);return _completedJob;
        }
        finally {_jobCancellation.Dispose();_jobCancellation=null;_runJob.IsEnabled=true;_cancelJob.IsEnabled=false;SaveDraft();}
    }
    private void ShowJobProgress(BumperFeaProgress value)
    {
        _jobFolder=value.Folder;_jobPath.Text=value.Folder;_jobProgress.Value=Math.Clamp(value.Fraction,0,1);
        _jobStatus.Text=$"{value.Stage} · {value.Fraction:P0} of requested stroke · {value.ElapsedSeconds:0} s\n{value.Message}";SaveDraft();
    }
    private void CancelJob()
    {if(_jobCancellation is null) return;if(_jobFolder is not null) LocalFebioBumperBackend.RequestCancel(_jobFolder);_jobCancellation.Cancel();_cancelJob.IsEnabled=false;}
    private void ShowCompletedJob(CompletedBumperFeaJob complete)
    {
        _completedJob=complete;_jobFolder=complete.Folder;_jobPath.Text=complete.Folder;_jobProgress.Value=1;_viewJob.IsEnabled=true;_createComponent.IsEnabled=true;
        _jobStatus.Text=$"Complete · Coarse estimate · {complete.Result.Tetrahedra} elements · {complete.Result.Seconds:0.0} s\nFrozen recipe: {complete.Recipe.Name}";
        _status.Text="Estimated curve ready. Inspect deformation or create its reusable bumper component.";DrawJobCurve();SaveDraft();
    }
    internal ComponentDefinition PublishCompletedComponent()
    {
        var complete=_completedJob??throw new InvalidOperationException("Run or open a completed job first.");
        // Re-read hashes at publication, so externally changed output cannot be published from a stale display.
        complete=_backend.LoadCompleted(complete.Folder);var id="fea-bumper-"+System.IO.Path.GetFileName(complete.Folder);var existing=_outputLibrary.LoadComponent(id);
        if(existing is not null&&existing.BumperFeaSource?.ResultSha256!=complete.ResultSha256) throw new InvalidOperationException("This job's component has a different source revision. Create a new job rather than replacing it.");
        var saved=existing??_outputLibrary.Publish(complete.Component(id),null);_publishedComponentId=saved.Identity.AssetId;ComponentCreated?.Invoke(saved);
        _status.Text="Bumper component available in Component Library and Assembly Editor → Install bumper.";return saved;
    }
    private async Task ViewCompletedJob()
    {
        try {var complete=_completedJob??throw new InvalidOperationException("Open a completed job first.");OpenDiagnosticViewer();if(!await _diagnosticViewer!.OpenResult(complete.DiagnosticPath)) throw new InvalidDataException("The diagnostic viewer could not open this job.");}
        catch(Exception error) {ReportJobError(error);}
    }
    private void ChooseCompletedJob()
    {
        if(_jobCancellation is not null) throw new InvalidOperationException("Finish or cancel the current job first.");
        Directory.CreateDirectory(_backend.JobsRoot);var dialog=new Microsoft.Win32.OpenFileDialog {Title="Open a completed job's result.json",Filter="FEA result (result.json)|result.json",InitialDirectory=_backend.JobsRoot};
        if(dialog.ShowDialog(this)==true) ShowCompletedJob(_backend.LoadCompleted(System.IO.Path.GetDirectoryName(dialog.FileName)!));
    }
    private void RestoreJob(string? id)
    {
        if(id is null||id.Length!=32||!id.All(Uri.IsHexDigit)) return;
        _jobFolder=System.IO.Path.Combine(_backend.JobsRoot,id);_jobPath.Text=_jobFolder;
        if(LocalFebioBumperBackend.ReadProgress(_jobFolder) is not {} status) return;
        ShowJobProgress(status);if(status.Stage=="completed") ShowCompletedJob(_backend.LoadCompleted(_jobFolder));
    }
    private void DrawJobCurve()
    {
        _jobPlot.Children.Clear();if(_completedJob is null) {_curveValues.Text="No complete curve available.";return;}
        var curve=_completedJob.Result.Curve;var width=Math.Max(40,_jobPlot.ActualWidth);var height=_jobPlot.Height;var maxX=curve[^1].CompressionM;var maxY=curve.Max(p=>p.ForceN);
        var line=new System.Windows.Shapes.Polyline {Stroke=Brushes.DeepSkyBlue,StrokeThickness=2};
        foreach(var sample in curve) line.Points.Add(new Point(12+(width-24)*sample.CompressionM/maxX,height-12-(height-24)*sample.ForceN/maxY));_jobPlot.Children.Add(line);
        _curveValues.Text=$"Compression → {maxX*1000:0.###} mm · force ↑ {maxY:0.###} N\n{curve.Length} actual samples · end force {curve[^1].ForceN:0.###} N · no smoothing";
    }
    private object CaptureJobState()=>new {backend=_backend.Id,folder=_jobFolder,running=_jobCancellation is not null,progress=_jobFolder is null?null:LocalFebioBumperBackend.ReadProgress(_jobFolder),
        result=_completedJob?.Result,publishedComponentId=_publishedComponentId};
    internal Task ViewJobForProof()=>ViewCompletedJob();
    internal async Task CaptureJobDiagnosticProof(string path) {_diagnosticViewer!.SelectLastFrameForProof();await Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.Render);await Task.Delay(200);_diagnosticViewer.CaptureProof(path);}
    internal bool HasCompletedJobForProof(string sha)=>_completedJob?.ResultSha256==sha;
    internal void SetupProductProof()
    {ConfigureEndFaces();_confirmed.IsChecked=true;_fields["compression"].Text="4";_fields["meshSize"].Text="8";_fields["budget"].Text="120";_fields["steps"].Text="8";_material.SelectedValue="printed-tpu-95";_tabs.SelectedIndex=4;}
}
