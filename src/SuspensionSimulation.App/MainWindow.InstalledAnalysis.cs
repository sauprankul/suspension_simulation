using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Path=System.IO.Path;
using SuspensionSimulation.Design;
using SuspensionSimulation.Kinematics;
using SuspensionSimulation.Solvers.Design;
using SuspensionSimulation.Solvers.Kinematics;

namespace SuspensionSimulation.App;

public partial class MainWindow
{
    private InstalledAnalysisEngine? _installedEngine;
    private InstalledAnalysisSnapshot? _acceptedInstalled;
    private CancellationTokenSource? _installedCancellation;
    private string? _installedRequestKey;
    private bool _installedPending;
    private long _installedGeneration;
    private Task _installedTask=Task.CompletedTask;
    private InspectionScene? _installedScene,_installedNeutralScene;
    private readonly Button _cancelInstalled=new(){Content="Cancel calculation",IsEnabled=false,Margin=new Thickness(0,4,0,0),Padding=new Thickness(8,2,8,2)};
    private FileSystemWatcher? _designWatcher,_garageWatcher;

    private void InitializeInstalledAnalysis(StackPanel row)
    {
        _installedEngine=new(_designLibrary!);row.Children.Add(_cancelInstalled);
        ValidityBadge.Text="CALCULATING PHYSICAL SETUP";
        _cancelInstalled.Click+=(_,_)=>
        {
            _installedCancellation?.Cancel();_installedRequestKey=null;
            RejectInstalled(_renderAttempt,"Calculation canceled; previous accepted state retained.");
        };
        void Changed(object sender,FileSystemEventArgs e)=>Dispatcher.BeginInvoke(()=>
        {
            if(e.FullPath.Contains(".tmp",StringComparison.Ordinal)) return;
            if(ReferenceEquals(sender,_designWatcher)&&_acceptedInstalled is {} current&&_rejectedRevision is null&&!_installedPending)
            {
                var asset=Path.GetFileName(Path.GetDirectoryName(e.FullPath));
                if(!current.A.Source.Manifest.Dependencies.Concat(current.B.Source.Manifest.Dependencies).Any(d=>d.AssetId==asset)) return;
            }
            _installedGeneration++;if(IsSimulationWorkspace) {_installedRequestKey=null;ScheduleRender();}
        });
        var assets=_designLibrary!.Root;
        _designWatcher=new(assets,"head") {IncludeSubdirectories=true,NotifyFilter=NotifyFilters.FileName|NotifyFilters.LastWrite,EnableRaisingEvents=true};
        _designWatcher.Changed+=Changed;_designWatcher.Created+=Changed;_designWatcher.Renamed+=Changed;_designWatcher.Deleted+=Changed;
        _garageWatcher=new(_garage!.DirectoryPath,"*.json") {IncludeSubdirectories=true,NotifyFilter=NotifyFilters.FileName|NotifyFilters.LastWrite,EnableRaisingEvents=true};
        _garageWatcher.Changed+=Changed;_garageWatcher.Created+=Changed;_garageWatcher.Renamed+=Changed;_garageWatcher.Deleted+=Changed;
        RestoreInstalledCheckpoint();
    }
    private void QueueInstalledAnalysis(long attempt,GarageEntry a,GarageEntry b,double steering)
    {
        var key=DesignJson.Serialize(new {a,b,ax=_ax,ay=_ay,steering,generation=_installedGeneration});
        if(key==_installedRequestKey) {if(_acceptedInstalled is not null) DrawInstalledAnalysis();return;}
        _installedRequestKey=key;_installedCancellation?.Cancel();var cancellation=new CancellationTokenSource();_installedCancellation=cancellation;
        _installedPending=true;_cancelInstalled.IsEnabled=true;
        StatusText.Text="Calculating saved physical setup revisions… Previous accepted result stays visible.";
        ViewSummary.Text=_acceptedInstalled is null?"Calculating installed equilibrium…":$"Updating · showing accepted revision {_acceptedInstalled.Revision}";
        _installedTask=SolveInstalledAnalysis(attempt,a,b,_ax,_ay,steering,key,cancellation);
    }
    private async Task SolveInstalledAnalysis(long attempt,GarageEntry a,GarageEntry b,double ax,double ay,double steering,string key,CancellationTokenSource cancellation)
    {
        try
        {
            var snapshot=await _installedEngine!.SolveAsync(attempt,a,b,ax,ay,steering,cancellation.Token,_acceptedInstalled);
            if(cancellation.IsCancellationRequested||key!=_installedRequestKey) return;
            var latestA=_garage!.Load(a.Id)??throw new InvalidOperationException("Garage A was removed.");
            var latestB=_garage.Load(b.Id)??throw new InvalidOperationException("Garage B was removed.");
            if(!_installedEngine.SourcesMatch(snapshot.A,latestA)||!_installedEngine.SourcesMatch(snapshot.B,latestB))
            {_installedRequestKey=null;Render();return;}
            var scene=InstalledScene(snapshot.A);var neutralScene=InstalledScene(snapshot.A,true);
            // Whole A/B result is swapped once, after both jobs and the latest-source check pass.
            _acceptedInstalled=snapshot;_acceptedSnapshot=null;_comparison=null;_rejectedRevision=null;_rejectedError=null;
            _installedScene=scene;_installedNeutralScene=neutralScene;
            _installedPending=false;_cancelInstalled.IsEnabled=false;
            DrawInstalledAnalysis();UpdateInspectionScene();UpdateGarageAnalysisPresentation();
            SaveInstalledCheckpoint(snapshot);
            App.Log(JsonSerializer.Serialize(new {eventName="installed-analysis-accepted",snapshot.Revision,snapshot.SolveMilliseconds,snapshot.RequestSha256,
                a=new {snapshot.A.Entry.Id,snapshot.A.Entry.Revision,snapshot.A.SetupSha256,state=snapshot.A.State},b=new {snapshot.B.Entry.Id,snapshot.B.Entry.Revision,snapshot.B.SetupSha256,state=snapshot.B.State}}),"RESULT");
        }
        catch(OperationCanceledException) { }
        catch(Exception error)
        {
            if(!cancellation.IsCancellationRequested&&key==_installedRequestKey) {RejectInstalled(attempt,error.Message);App.LogException("Installed analysis rejected",error);}
        }
        finally
        {
            if(ReferenceEquals(cancellation,_installedCancellation)) {_installedPending=false;_cancelInstalled.IsEnabled=false;_installedCancellation=null;}
            cancellation.Dispose();
        }
    }
    private void RejectInstalled(long attempt,string error)
    {
        _rejectedRevision=attempt;_rejectedError=error;_installedPending=false;_cancelInstalled.IsEnabled=false;
        ValidityBadge.Text=_acceptedInstalled is null?"NO ACCEPTED PHYSICAL RESULT":"RETAINED RESULT · SETUP CONFLICT";
        StatusText.Text="Physical setup/solve conflict: "+error;
        ViewSummary.Text=$"STALE · accepted revision {_acceptedInstalled?.Revision.ToString()??"none"} · {error}";
        _inspectionViewport?.MarkStale(attempt,error);
    }
    private void CloseInstalledAnalysis()
    {_installedCancellation?.Cancel();_designWatcher?.Dispose();_garageWatcher?.Dispose();}
    private string InstalledCheckpointPath=>Path.Combine(_designLibrary!.Root,"analysis-cache","last-accepted.json");
    private void SaveInstalledCheckpoint(InstalledAnalysisSnapshot snapshot)
    {
        var path=InstalledCheckpointPath;var temp=path+".tmp";
        try {Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(temp,DesignJson.Serialize(snapshot));File.Move(temp,path,true);}
        catch(Exception error) {App.LogException("Installed result checkpoint",error);}
        finally {if(File.Exists(temp)) File.Delete(temp);}
    }
    private void RestoreInstalledCheckpoint()
    {
        try
        {
            if(!File.Exists(InstalledCheckpointPath)) return;
            var saved=DesignJson.Deserialize<InstalledAnalysisSnapshot>(File.ReadAllText(InstalledCheckpointPath));
            if(saved.A.Entry.Id!=_analysisA.SelectedValue as string||saved.B.Entry.Id!=_analysisB.SelectedValue as string) return;
            foreach(var side in new[]{saved.A,saved.B})
            {
                var source=AssemblyGraphResolver.OpenBundle(side.Source);_=GarageSetupResolver.Freeze(source,side.Entry.Configuration!);
                if(side.State.Manifest is null||!side.State.Manifest.Dependencies.SequenceEqual(source.Manifest.Dependencies)||side.State.ForceResidualN.Length>.05||side.State.MomentResidualNm.Length>.05||side.State.RigidReactions.Any(r=>r.GapM< -1e-8)) throw new InvalidDataException("Stored result did not preserve an accepted physical state.");
            }
            _acceptedInstalled=saved;_installedScene=InstalledScene(saved.A);_installedNeutralScene=InstalledScene(saved.A,true);
            _renderAttempt=Math.Max(_renderAttempt,saved.Revision);DrawInstalledAnalysis();
            ViewSummary.Text="Retained accepted result · checking current saved revisions…";
        }
        catch(Exception error) {App.LogException("Installed result recovery",error);}
    }
    private void DrawInstalledAnalysis()
    {
        if(_acceptedInstalled is not {} accepted) return;
        var a=accepted.A;var b=accepted.B;
        ARsd.Text=InstalledPercent(a.RollMetrics.FrontDistribution);BRsd.Text=InstalledPercent(b.RollMetrics.FrontDistribution);
        ARcb.Text=InstalledSigned(a.RollMetrics.RollCoupleBiasPercentagePoints," pp");BRcb.Text=InstalledSigned(b.RollMetrics.RollCoupleBiasPercentagePoints," pp");
        ARsd.ToolTip=BRsd.ToolTip="Front share of installed elastic roll tangent with grounded wheel heights held, including preload curvature. Active rigid limits have infinite loading stiffness. Balance is undefined when both axles are rigid or a tire loses contact.";
        AStateSummary.Text=InstalledSummary(a);BStateSummary.Text=InstalledSummary(b);AStateSummary.Height=BStateSummary.Height=100;AStateSummary.FontSize=BStateSummary.FontSize=11;
        DrawInstalledCar(ACarCanvas,a,Brushes.DeepSkyBlue);DrawInstalledCar(BCarCanvas,b,Brushes.Orange);
        DeltaHeadline.Text="B − A RCB  "+InstalledSigned(a.RollMetrics.RollCoupleBiasPercentagePoints is {} av&&b.RollMetrics.RollCoupleBiasPercentagePoints is {} bv?bv-av:null," pp");
        var rows=new List<ComparisonRow>();
        void Add(string label,double x,double y,string unit)=>rows.Add(Row(label,x,y,unit));
        Add("Total mass",a.State.TotalMassKg,b.State.TotalMassKg,"kg");Add("CG height",a.State.CenterOfMassWorldM.Z*1000,b.State.CenterOfMassWorldM.Z*1000,"mm");
        Add("Body heave",a.State.Chassis.HeaveM*1000,b.State.Chassis.HeaveM*1000,"mm");Add("Body roll",Degrees(a.State.Chassis.RollRad),Degrees(b.State.Chassis.RollRad),"deg");Add("Body pitch",Degrees(a.State.Chassis.PitchRad),Degrees(b.State.Chassis.PitchRad),"deg");
        if(a.State.Motion.ChassisFlex is not null||b.State.Motion.ChassisFlex is not null)
        {
            Add("Chassis twist",Degrees(a.State.Motion.ChassisFlex?.TwistRad??0),Degrees(b.State.Motion.ChassisFlex?.TwistRad??0),"deg");Add("Chassis longitudinal / vertical bend",Degrees(a.State.Motion.ChassisFlex?.VerticalBendRad??0),Degrees(b.State.Motion.ChassisFlex?.VerticalBendRad??0),"deg");Add("Chassis lateral bend",Degrees(a.State.Motion.ChassisFlex?.LateralBendRad??0),Degrees(b.State.Motion.ChassisFlex?.LateralBendRad??0),"deg");Add("Chassis strain energy",a.State.ChassisStrainEnergyJ,b.State.ChassisStrainEnergyJ,"J");
        }
        foreach(var axle in new[]{"front","rear"})
        {
            var x=a.RollMetrics.Axles.Single(t=>t.Axle==axle);var y=b.RollMetrics.Axles.Single(t=>t.Axle==axle);
            Add(axle+" installed spring roll tangent",x.SpringNmPerRad*Math.PI/180,y.SpringNmPerRad*Math.PI/180,"N·m/deg");Add(axle+" torsion-only bar roll tangent",x.BarNmPerRad*Math.PI/180,y.BarNmPerRad*Math.PI/180,"N·m/deg");
            rows.Add(new(axle+" rigid-limit loading stiffness",x.RigidLoading?"∞":"inactive",y.RigidLoading?"∞":"inactive","—","N·m/rad"));
            var index=axle=="front"?0:2;
            Add(axle+" total toe (+in)",Degrees(a.State.Wheels[index+1].HeadingRad-a.State.Wheels[index].HeadingRad),Degrees(b.State.Wheels[index+1].HeadingRad-b.State.Wheels[index].HeadingRad),"deg");
            Add(axle+" thrust",Degrees(InstalledThrust(a.State,index)),Degrees(InstalledThrust(b.State,index)),"deg");
        }
        for(var i=0;i<4;i++)
        {
            var name=a.State.Wheels[i].Corner;
            Add(name+" normal load",a.State.NormalLoadsN[i],b.State.NormalLoadsN[i],"N");Add(name+" jounce",a.State.Motion.Travel.Values[i]*1000,b.State.Motion.Travel.Values[i]*1000,"mm");
            Add(name+" camber",Degrees(a.State.Wheels[i].CamberRad),Degrees(b.State.Wheels[i].CamberRad),"deg");Add(name+" heading",Degrees(a.State.Wheels[i].HeadingRad),Degrees(b.State.Wheels[i].HeadingRad),"deg");
            Add(name+" spring force",a.State.Mechanics.Elements.SingleOrDefault(e=>e.Path==name+"-spring")?.ForceOrTorque??0,b.State.Mechanics.Elements.SingleOrDefault(e=>e.Path==name+"-spring")?.ForceOrTorque??0,"N");
            Add(name+" rigid reaction",a.State.RigidReactions.Where(r=>r.Id.StartsWith(name+"-",StringComparison.Ordinal)).Sum(r=>r.ForceN),b.State.RigidReactions.Where(r=>r.Id.StartsWith(name+"-",StringComparison.Ordinal)).Sum(r=>r.ForceN),"N");
        }
        var bumperA=AssemblyGraphResolver.OpenBundle(a.Source).ElasticElements.Where(e=>e.Component.Behavior is BumpCurveBehavior).ToDictionary(e=>e.Path,e=>(BumpCurveBehavior)e.Component.Behavior!);
        var bumperB=AssemblyGraphResolver.OpenBundle(b.Source).ElasticElements.Where(e=>e.Component.Behavior is BumpCurveBehavior).ToDictionary(e=>e.Path,e=>(BumpCurveBehavior)e.Component.Behavior!);
        void Optional(string label,double? x,double? y,string unit)=>rows.Add(new(label,x?.ToString("0.###")??"—",y?.ToString("0.###")??"—",x is {} xv&&y is {} yv?(yv-xv).ToString("+0.###;-0.###;0"):"—",unit));
        foreach(var path in bumperA.Keys.Union(bumperB.Keys).Order(StringComparer.Ordinal))
        {
            var x=a.State.Mechanics.Elements.SingleOrDefault(e=>e.Path==path&&e.Kind=="bump-stop");var y=b.State.Mechanics.Elements.SingleOrDefault(e=>e.Path==path&&e.Kind=="bump-stop");
            double? Shortening(Dictionary<string,BumpCurveBehavior> curves,InstalledElementState? state)=>state is not null&&curves.TryGetValue(path,out var curve)?Math.Max(0,curve.ContactLengthM-state.LengthM):null;
            var sx=Shortening(bumperA,x);var sy=Shortening(bumperB,y);
            Optional(path+" force",x?.ForceOrTorque,y?.ForceOrTorque,"N");Optional(path+" shortening",sx*1000,sy*1000,"mm");Optional(path+" axial tangent",x?.Tangent/1000,y?.Tangent/1000,"N/mm");
            Optional(path+" remaining curve range",sx is {} cx?(bumperA[path].Samples[^1].CompressionM-cx)*1000:null,sy is {} cy?(bumperB[path].Samples[^1].CompressionM-cy)*1000:null,"mm");
        }
        foreach(var datum in a.State.RideHeights) if(b.State.RideHeights.SingleOrDefault(h=>h.Id==datum.Id) is {} other) Add(datum.Name,datum.HeightM*1000,other.HeightM*1000,"mm");
        ComparisonGrid.ItemsSource=rows;ValidityBadge.Text="INSTALLED EQUILIBRIUM · ESTIMATED GEOMETRY";
        UpdateGarageAnalysisPresentation();
    }
    private static string InstalledPercent(double? value)=>value is {} x?Percent(x):"—";
    private static string InstalledSigned(double? value,string suffix="")=>value is {} x?Signed(x,suffix):"—";
    private static double InstalledThrust(InstalledEquilibriumResult result,int first)=>Math.Atan2(Math.Sin(result.Wheels[first].HeadingRad)+Math.Sin(result.Wheels[first+1].HeadingRad),Math.Cos(result.Wheels[first].HeadingRad)+Math.Cos(result.Wheels[first+1].HeadingRad));
    private static string InstalledSummary(InstalledAnalysisSide side)
    {
        var state=side.State;var limits=state.RigidReactions.Count(r=>r.ForceN>1e-5);var loose=state.Mechanics.Elements.Count(e=>e.Unseated);
        return $"Roll {Degrees(state.Chassis.RollRad):0.000}° · pitch {Degrees(state.Chassis.PitchRad):0.000}°\nMass {state.TotalMassKg:0.0} kg · CG {state.CenterOfMassWorldM.Z*1000:0.0} mm\nForce residual {state.ForceResidualN.Length:0.00E+0} N\nMoment residual {state.MomentResidualNm.Length:0.00E+0} N·m\nRigid {limits} · lifted {state.NormalLoadsN.Count(n=>n<1e-5)} · loose springs {loose}";
    }
    private static void DrawInstalledCar(Canvas canvas,InstalledAnalysisSide side,Brush accent)
    {
        var result=side.State;
        canvas.Children.Clear();var w=Math.Max(200,canvas.ActualWidth);var h=Math.Max(200,canvas.ActualHeight);
        var chassis=InstalledScene(side).Segments.Where(s=>s.Corner=="chassis").ToArray();
        if(chassis.Length==0) Add(canvas,new Rectangle {Width=w*.32,Height=h*.62,RadiusX=25,RadiusY=25,Fill=Brushes.DarkSlateGray,Stroke=accent,StrokeThickness=2},w*.34,h*.12);
        else
        {
            var front=result.Wheels.Take(2).Average(p=>p.CenterM.X);var rear=result.Wheels.Skip(2).Average(p=>p.CenterM.X);var span=Math.Max(.1,front-rear);var middle=(front+rear)/2;var track=Math.Max(.1,result.Wheels.Max(p=>p.CenterM.Y)-result.Wheels.Min(p=>p.CenterM.Y));
            foreach(var segment in chassis) canvas.Children.Add(new Line {X1=w*.5-segment.StartM.Y*w*.63/track,Y1=h*.5-(segment.StartM.X-middle)*h*.48/span,X2=w*.5-segment.EndM.Y*w*.63/track,Y2=h*.5-(segment.EndM.X-middle)*h*.48/span,Stroke=accent,StrokeThickness=segment.Id.Contains("rack-crossmember",StringComparison.Ordinal)?3:2,ToolTip=segment.Label});
        }
        for(var i=0;i<4;i++)
        {
            var left=i%2==0;var front=i<2;var x=w*(left?.13:.76);var y=h*(front?.15:.63);var wheel=result.Wheels[i];
            Add(canvas,new Rectangle {Width=35,Height=48,Fill=result.NormalLoadsN[i]<1e-5?Brushes.OrangeRed:accent,Opacity=.86,RenderTransformOrigin=new(.5,.5),RenderTransform=new RotateTransform(-Degrees(wheel.HeadingRad))},x,y);
            Add(canvas,new TextBlock {Text=$"{wheel.Corner} {result.NormalLoadsN[i]:0} N\n{Degrees(wheel.CamberRad):+0.00;-0.00;0.00}° camber",Foreground=Brushes.White,FontSize=10},x-7,y+51);
        }
        Add(canvas,new TextBlock {Text=$"{result.NormalLoadsN.Sum():0} N",Foreground=Brushes.White,FontWeight=FontWeights.Bold},w*.43,h*.42);
    }
    private static InspectionScene InstalledScene(InstalledAnalysisSide side,bool neutral=false)
    {
        var source=AssemblyGraphResolver.OpenBundle(side.Source);var graph=GarageSetupResolver.Freeze(source,side.Entry.Configuration!);var result=neutral?side.Static:side.State;
        var scene=InstalledWorldScene(AssemblyScene(graph,InstalledMechanism(graph,result),result.Motion.Pose),result.Chassis);
        string Layer(string path)=>path.StartsWith("front-bar",StringComparison.Ordinal)||path.StartsWith("rear-bar",StringComparison.Ordinal)?"bars":path.Contains("chassis",StringComparison.Ordinal)||path.EndsWith("/guide",StringComparison.Ordinal)?"chassis":path.StartsWith("front",StringComparison.Ordinal)||path.StartsWith('F')?"front":"rear";
        string Caption(string path)
        {
            if(path.StartsWith("front-bar/",StringComparison.Ordinal)||path.StartsWith("rear-bar/",StringComparison.Ordinal))
            {
                var prefix=path.StartsWith("front",StringComparison.Ordinal)?"F ":"R ";var part=path.Split('/').Last();
                return prefix+(part switch {"coupler"=>"ARB torsion","housing"=>"ARB bearings","left-arm"=>"ARB arm L","right-arm"=>"ARB arm R",_=>part.EndsWith("left",StringComparison.Ordinal)?"endlink L":"endlink R"});
            }
            if(path=="front/left-tie") return "FL tie rod";if(path=="front/right-tie") return "FR tie rod";if(path=="front/rack") return "Steering rack";if(path=="front/guide") return "Rack housing";
            var corner=path.StartsWith("front/left/",StringComparison.Ordinal)?"FL":path.StartsWith("front/right/",StringComparison.Ordinal)?"FR":path.StartsWith("rear-left/",StringComparison.Ordinal)?"RL":path.StartsWith("rear-right/",StringComparison.Ordinal)?"RR":"";
            var last=path.Split('/').Last() switch {"rear-traction"=>"traction","rear-toe"=>"toe link",var value=>value.Replace('-',' ')};
            return (corner+" "+last).Trim();
        }
        var contact=graph.Root.Vehicle!.Contacts.ToDictionary(c=>c.InstancePath,c=>c.Corner);
        return scene with {PackageFingerprint=side.SetupSha256,StateId=side.Entry.Name+" · Garage revision "+side.Entry.Revision,Validity="converged-installed-equilibrium",
            Points=[..scene.Points.Where(p=>!contact.ContainsKey(p.Corner))],
            Segments=[..scene.Segments.Where(s=>!s.Id.Contains(".port.",StringComparison.Ordinal)&&!contact.ContainsKey(s.Corner)).Select(s=>s with {Layer=Layer(s.Corner),Label=Caption(s.Corner),Source=SceneSource.Solved,ActiveLimit=result.RigidReactions.Any(r=>r.ForceN>1e-5&&r.Id.StartsWith(s.Corner+":",StringComparison.Ordinal))})],
            Wheels=[..scene.Wheels.Select(w=>w with {Corner=contact[w.Corner],Source=SceneSource.Solved})],Diagnostics=["Saved Garage installed equilibrium; synthetic hard points and estimated installation dimensions. Rigid tire envelope; no tire compliance or friction-capacity model."]};
    }
    private void ExportInstalledInspection()
    {
        if(_acceptedInstalled is not {} snapshot||_inspectionViewport is null) return;
        try
        {
            var folder=Path.Combine(App.RepositoryRoot,"artifacts","inspection-snapshots",DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss-fff"));Directory.CreateDirectory(folder);
            _inspectionViewport.SaveImage(Path.Combine(folder,"screenshot.png"));File.WriteAllText(Path.Combine(folder,"physical-result.json"),DesignJson.Serialize(snapshot));
            File.WriteAllText(Path.Combine(folder,"snapshot.json"),JsonSerializer.Serialize(new {schema="suspension-installed-inspection/v1",acceptedRevision=snapshot.Revision,physicalResult="physical-result.json",screenshot="screenshot.png",camera=_inspectionViewport.CaptureCamera(),selectedComponent=_inspectionViewport.SelectedId,
                rejectedRevision=_rejectedRevision,rejectedError=_rejectedError,calculationPending=_installedPending},new JsonSerializerOptions {WriteIndented=true}));
            ViewSummary.Text="Snapshot exported to "+folder;App.Log("Installed snapshot exported: "+folder,"EXPORT");
        }
        catch(Exception error) {ViewSummary.Text="Snapshot export failed: "+error.Message;App.LogException("Installed snapshot export",error);}
    }
}
