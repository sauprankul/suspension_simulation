using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using SuspensionSimulation.Components;
using SuspensionSimulation.Design;

namespace SuspensionSimulation.App;

internal sealed record BumpImportRecovery(string RawCsv,CurveDisplacementUnit DisplacementUnit,CurveForceUnit ForceUnit,CurveZero Zero,string ContactMm,string Conditions,string Source,DataProvenance Provenance);

public partial class MainWindow
{
    private readonly TextBox _bumpCsv=new(){AcceptsReturn=true,AcceptsTab=true,TextWrapping=TextWrapping.NoWrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,Height=140};
    private readonly ComboBox _bumpDisplacementUnit=new(){ItemsSource=Enum.GetValues<CurveDisplacementUnit>(),SelectedIndex=0};
    private readonly ComboBox _bumpForceUnit=new(){ItemsSource=Enum.GetValues<CurveForceUnit>(),SelectedIndex=0};
    private readonly ComboBox _bumpZero=new(){ItemsSource=Enum.GetValues<CurveZero>(),SelectedIndex=0};
    private readonly ComboBox _bumpProvenance=new(){ItemsSource=new[]{DataProvenance.Measured,DataProvenance.PublicSource,DataProvenance.Estimated,DataProvenance.Synthetic},SelectedIndex=2};
    private TabItem? _bumpCurveTab;
    private readonly TextBox _bumpContact=new(){Text="40"},_bumpConditions=new(){Text="Conditions not supplied"},_bumpSource=new(){Text="User supplied axial compression curve"};
    private readonly TextBlock _bumpCurveStatus=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)};
    private readonly Canvas _bumpPlot=new(){Height=140,Background=Brushes.Black,Margin=new Thickness(0,8,0,0)};
    private void AddBumpCurveImport(TabControl tabs)
    {
        var panel=new StackPanel {Margin=new Thickness(10)};
        panel.Children.Add(new TextBlock {Text="Imported axial bump-stop curve",FontWeight=FontWeights.Bold});
        panel.Children.Add(new TextBlock {Text="Import measured or estimated CSV as a reusable bumper component. Exact source data, units and conditions are saved with its revision. STEP/FEA publication remains gated on backend validation.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,8)});
        void Field(string label,Control control,string tip) {panel.Children.Add(new TextBlock {Text=label,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,5,0,2)});control.ToolTip=tip;panel.Children.Add(control);}
        Field("Compression units",_bumpDisplacementUnit,"CSV displacement means shortening from the declared curve zero. No automatic rescaling from guessed units.");
        Field("Force units",_bumpForceUnit,"Positive axial compression force. N or kN are converted exactly to SI.");
        Field("Curve zero",_bumpZero,"Contact: first sample is zero displacement/force. Installed: a nonzero initial reaction is allowed, and an unloaded branch is not inferred.");
        Field("Attachment separation at curve zero (mm)",_bumpContact,"Axial separation of the component's assembly attachment references at the curve zero. This sets engagement geometry; it is not an additional spring preload or an extrapolated travel stop.");
        Field("Source classification",_bumpProvenance,"Classification supplied with this dataset. Importing data does not establish independent part correlation.");
        Field("Test / installation conditions",_bumpConditions,"Record the actual fixture, temperature, rate and loading branch when known. Different conditions create a different evidence identity.");
        Field("Source note",_bumpSource,"Identify the measurement, manufacturer document or estimated source. Raw CSV is embedded in the saved part.");
        var load=new Button {Content="Load UTF-8 CSV…",Margin=new Thickness(0,8,0,4)};
        load.Click+=(_,_)=>ComponentAction(()=>
        {
            var dialog=new Microsoft.Win32.OpenFileDialog {Filter="Compression curves (*.csv)|*.csv",InitialDirectory=App.RepositoryRoot};
            if(dialog.ShowDialog(this)!=true) return;
            var bytes=File.ReadAllBytes(dialog.FileName);if(bytes.Length>2_000_000) throw new ArgumentException("CSV exceeds the 2-MB import limit.");
            _bumpCsv.Text=new UTF8Encoding(false,true).GetString(bytes);_bumpSource.Text=System.IO.Path.GetFileName(dialog.FileName);ScheduleComponentDraft();
        });panel.Children.Add(load);
        Field("CSV: compression,force (2–10000 samples)",_bumpCsv,"Use a decimal point, comma-separated columns, strictly increasing nonnegative compression and nondecreasing nonnegative force. Unit suffixes such as compression_mm,force_n must match the selections.");
        var create=new Button {Content="Create / update bumper from this curve",Margin=new Thickness(0,8,0,4)};create.Click+=(_,_)=>ComponentAction(CreateImportedBumpComponent);panel.Children.Add(create);
        panel.Children.Add(_bumpPlot);panel.Children.Add(_bumpCurveStatus);
        _bumpCurveTab=new TabItem {Header="Bump curve",Content=new ScrollViewer {Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}};tabs.Items.Add(_bumpCurveTab);
        foreach(var field in new[]{_bumpCsv,_bumpContact,_bumpConditions,_bumpSource}) field.TextChanged+=(_,_)=>ScheduleComponentDraft();
        foreach(var combo in new[]{_bumpDisplacementUnit,_bumpForceUnit,_bumpZero,_bumpProvenance}) combo.SelectionChanged+=(_,_)=>ScheduleComponentDraft();
        _bumpPlot.SizeChanged+=(_,_)=>{if(_componentDefinition?.Behavior is BumpCurveBehavior b) DrawBumpCurve(b);};
    }
    private ImportedBumpCurveEvidence ReadBumpEvidence()=>new(_bumpCsv.Text,(CurveDisplacementUnit)_bumpDisplacementUnit.SelectedItem,(CurveForceUnit)_bumpForceUnit.SelectedItem,
        (CurveZero)_bumpZero.SelectedItem,DesignerNumbers.Metres(_bumpContact.Text),_bumpConditions.Text,_bumpSource.Text,(DataProvenance)_bumpProvenance.SelectedItem);
    private void CreateImportedBumpComponent()
    {
        var evidence=ReadBumpEvidence();_=ImportedBumpCurve.Parse(evidence);var updating=_componentDefinition?.Kind==ComponentKind.BumpStop;
        var before=CaptureComponentRaw();
        if(!updating)
        {_componentId=Guid.NewGuid().ToString("N");_componentBaseRevision=null;_componentUndo.Clear();_componentRedo.Clear();_workspaceDrafts[Workspace.ComponentDesigner].Name.Text="Imported bump stop";}
        var identity=new RevisionIdentity(_componentId,"draft",_workspaceDrafts[Workspace.ComponentDesigner].Name.Text,new(evidence.Provenance,evidence.SourceNote,"Imported CSV SHA-256 "+evidence.RawSha256));
        var previous=_componentDefinition?.Behavior as BumpCurveBehavior;
        var component=updating?_componentDefinition! with {Identity=identity,Behavior=ImportedBumpCurve.Behavior(evidence,previous!.FirstPortId,previous.SecondPortId),BumpCurveEvidence=evidence}:ElasticComponentFactory.ImportedBumper(identity,evidence);
        ComponentAuthoring.ValidateForPublish(component);_componentMetadataSeed=component;_componentDefinition=component;_componentMass=component.Mass;_componentCustom=true;
        _componentKind.SelectedItem=ComponentKind.BumpStop;_workspaceDrafts[Workspace.ComponentDesigner].Notes.Text=evidence.SourceNote;_useMass.IsChecked=component.Mass is not null;
        FillComponentTables(component);if(updating) {_componentUndo.Push(before);_componentRedo.Clear();}_componentAcceptedRaw=null;ApplyComponentPreview(false);DrawBumpCurve((BumpCurveBehavior)component.Behavior!);
    }
    private void FillImportedBumpEvidence(ImportedBumpCurveEvidence? evidence)
    {
        if(evidence is null) return;
        _bumpCsv.Text=evidence.CsvUtf8;_bumpDisplacementUnit.SelectedItem=evidence.DisplacementUnit;_bumpForceUnit.SelectedItem=evidence.ForceUnit;_bumpZero.SelectedItem=evidence.Zero;_bumpProvenance.SelectedItem=evidence.Provenance;
        _bumpContact.Text=DesignerNumbers.Mm(evidence.ContactSeparationM);_bumpConditions.Text=evidence.Conditions;_bumpSource.Text=evidence.SourceNote;
    }
    private void DrawBumpCurve(BumpCurveBehavior curve)
    {
        _bumpPlot.Children.Clear();var width=Math.Max(100,_bumpPlot.ActualWidth)-16;var height=_bumpPlot.Height-28;var max=curve.Samples.Max(s=>s.ForceN);var stroke=curve.Samples[^1].CompressionM;
        var line=new Polyline {Stroke=Brushes.Gold,StrokeThickness=2};foreach(var point in curve.Samples) line.Points.Add(new(8+point.CompressionM/stroke*width,8+height*(1-(max>0?point.ForceN/max:0))));_bumpPlot.Children.Add(line);
        var label=new TextBlock {Text=$"0–{stroke*1000:0.###} mm · 0–{max:0.###} N",Foreground=Brushes.White,FontSize=11};Canvas.SetLeft(label,8);Canvas.SetBottom(label,1);_bumpPlot.Children.Add(label);
        _bumpCurveStatus.Text=$"{curve.Samples.Length} samples · {curve.Zero} zero · piecewise linear force/tangent\nValid compression 0–{stroke*1000:0.###} mm; no extrapolation or artificial rigid stop.\nEvidence {curve.EvidenceSha256[..12]}";
    }
    private BumpImportRecovery CaptureBumpImport()=>new(_bumpCsv.Text,(CurveDisplacementUnit)_bumpDisplacementUnit.SelectedItem,(CurveForceUnit)_bumpForceUnit.SelectedItem,(CurveZero)_bumpZero.SelectedItem,_bumpContact.Text,_bumpConditions.Text,_bumpSource.Text,(DataProvenance)_bumpProvenance.SelectedItem);
    private void RestoreBumpImport(BumpImportRecovery? raw)
    {
        if(raw is null) {_bumpCsv.Text="";FillImportedBumpEvidence(_componentMetadataSeed?.BumpCurveEvidence);return;}
        _bumpCsv.Text=raw.RawCsv;_bumpDisplacementUnit.SelectedItem=raw.DisplacementUnit;_bumpForceUnit.SelectedItem=raw.ForceUnit;_bumpZero.SelectedItem=raw.Zero;_bumpContact.Text=raw.ContactMm;_bumpConditions.Text=raw.Conditions;_bumpSource.Text=raw.Source;_bumpProvenance.SelectedItem=raw.Provenance;
    }
}
