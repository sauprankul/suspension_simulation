using System.Windows;
using System.Windows.Controls;
using SuspensionSimulation.Design;

namespace SuspensionSimulation.App;

public partial class MainWindow
{
    private readonly ComboBox _assemblyBumperChoice=new(){DisplayMemberPath="Identity.Name",SelectedValuePath="Identity.AssetId"};
    private readonly ComboBox _assemblyBumperFirst=new(),_assemblyBumperSecond=new();
    private readonly TextBox _assemblyBumperId=new(){Text="FL-bumper"};
    private readonly CheckBox _assemblyBumperGarage=new(){Content="Expose this bumper as Garage equipment",IsChecked=true,Margin=new Thickness(0,8,0,4)};
    private readonly TextBlock _assemblyBumperReadout=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)};
    private TabItem? _assemblyBumperTab;
    private void AddBumperInstallation(TabControl tabs)
    {
        var panel=new StackPanel {Margin=new Thickness(10)};
        panel.Children.Add(new TextBlock {Text="Install a published bumper curve",FontWeight=FontWeights.Bold});
        panel.Children.Add(new TextBlock {Text="Create or import the curve in Component Designer, then choose its two actual mounting references here. The curve's zero separation and fixture conditions must describe this installation. Publishing this assembly makes it available in Garage.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,8)});
        void Field(string caption,Control input) {panel.Children.Add(new TextBlock {Text=caption,Margin=new Thickness(0,6,0,2)});panel.Children.Add(input);}
        Field("Published bumper",_assemblyBumperChoice);Field("Instance ID",_assemblyBumperId);
        Field("First mounting reference",_assemblyBumperFirst);Field("Second mounting reference",_assemblyBumperSecond);
        panel.Children.Add(new TextBlock {Text="Only direct-child ports and exported child-assembly ports supporting spherical axial attachments are listed. Add/export mounting frames in the component/assembly editor if the physical references are missing.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,0)});
        panel.Children.Add(_assemblyBumperGarage);
        var refresh=new Button {Content="Refresh curves and mounting references",Margin=new Thickness(0,8,0,4)};refresh.Click+=(_,_)=>AssemblyAction(()=>{ApplyAssemblyPreview(false);RefreshAssemblyCatalog();});panel.Children.Add(refresh);
        var install=new Button {Content="Install bumper on these references",Margin=new Thickness(0,4,0,4)};install.Click+=(_,_)=>AssemblyAction(InstallAssemblyBumper);panel.Children.Add(install);
        panel.Children.Add(_assemblyBumperReadout);_assemblyBumperTab=new TabItem {Header="Install bumper",Content=new ScrollViewer {Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}};tabs.Items.Add(_assemblyBumperTab);
        foreach(var picker in new[]{_assemblyBumperChoice,_assemblyBumperFirst,_assemblyBumperSecond}) picker.SelectionChanged+=(_,_)=>UpdateBumperInstallationReadout();
    }
    private void RefreshBumperInstallation()
    {
        var selected=_assemblyBumperChoice.SelectedValue as string;
        _assemblyBumperChoice.ItemsSource=_designLibrary!.Components().Where(c=>c.Kind==ComponentKind.BumpStop&&c.Behavior is BumpCurveBehavior).OrderBy(c=>c.Identity.Name).ToArray();
        _assemblyBumperChoice.SelectedValue=selected;
        var direct=_assemblyGraph?.Root.Instances.Select(i=>i.Id).ToHashSet(StringComparer.Ordinal)??[];
        var choices=_assemblyGraph?.Interfaces.Where(p=>direct.Contains(p.Key[..p.Key.IndexOf(':')])&&p.Value.SupportedJoints.Contains(JointKind.Spherical)).Select(p=>p.Key).Order(StringComparer.Ordinal).ToArray()??[];
        foreach(var picker in new[]{_assemblyBumperFirst,_assemblyBumperSecond}) {var old=picker.SelectedItem as string;picker.ItemsSource=choices;picker.SelectedItem=old;}
        UpdateBumperInstallationReadout();
    }
    private void UpdateBumperInstallationReadout()
    {
        if(_assemblyBumperChoice.SelectedItem is not ComponentDefinition {Behavior:BumpCurveBehavior curve} bumper) {_assemblyBumperReadout.Text="Choose a published axial bumper curve. Import a CSV in Component Designer if none is listed.";return;}
        var text=$"{bumper.Identity.Provenance.Kind} · curve zero {curve.Zero}\nReference separation {curve.ContactLengthM*1000:0.###} mm · curve shortening range 0–{curve.Samples[^1].CompressionM*1000:0.###} mm\n"+(bumper.BumpCurveEvidence?.Conditions??bumper.Identity.Provenance.Note);
        if(_assemblyGraph is {} graph&&_assemblyBumperFirst.SelectedItem is string a&&_assemblyBumperSecond.SelectedItem is string b&&graph.Interfaces.TryGetValue(a,out var first)&&graph.Interfaces.TryGetValue(b,out var second))
        {var length=(second.Frame.OriginM-first.Frame.OriginM).Length;var shortening=curve.ContactLengthM-length;text+=$"\nAssembly reference separation {length*1000:0.###} mm · "+(shortening>=0?$"shortening {shortening*1000:0.###} mm":$"contact gap {-shortening*1000:0.###} mm")+". Loaded engagement is solved in Garage / the visualizers.";}
        _assemblyBumperReadout.Text=text;
    }
    private void InstallAssemblyBumper()
    {
        var bumper=_assemblyBumperChoice.SelectedItem as ComponentDefinition??throw new ArgumentException("Choose a published bumper curve.");
        InstancePort Mount(ComboBox picker) {var key=picker.SelectedItem as string??throw new ArgumentException("Choose both mounting references.");var split=key.IndexOf(':');return new(key[..split],key[(split+1)..]);}
        var assembly=ReadAssembly();var before=CaptureAssemblyRaw();
        var added=BumperInstallation.Add(assembly,_designLibrary!,new(bumper.Identity.AssetId),_assemblyBumperId.Text.Trim(),Mount(_assemblyBumperFirst),Mount(_assemblyBumperSecond),_assemblyBumperGarage.IsChecked==true);
        FillAssembly(added);_assemblyAcceptedRaw=before;ApplyAssemblyPreview(true);
        _assemblyStatus.Text="Bumper installed on its two declared mounting references. Publish this assembly, then select it in Garage. Curve validity is checked by the installed-equilibrium solve.";
    }
    private void DuplicateAssembly()
    {
        var original=ReadAssembly();_assemblyId=Guid.NewGuid().ToString("N");_assemblyBaseRevision=null;_assemblyUndo.Clear();_assemblyRedo.Clear();_assemblyAcceptedRaw=null;_assemblyPublishedRaw=null;_assemblyConflict=null;
        var copy=original with {Identity=original.Identity with {AssetId=_assemblyId,RevisionId="draft",Name=original.Identity.Name+" — custom"}};
        FillAssembly(copy);ApplyAssemblyPreview(false);RefreshAssemblyCatalog();
        _assemblyStatus.Text="Independent assembly draft created. Shared child components still follow their declared references; publishing this assembly leaves the original assembly unchanged.";
    }
}
