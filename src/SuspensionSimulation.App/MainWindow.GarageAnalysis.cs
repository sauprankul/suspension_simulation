using System.IO;
using System.Windows;
using System.Windows.Controls;
using SuspensionSimulation.Design;
using SuspensionSimulation.Solvers.HeadToHead;

namespace SuspensionSimulation.App;

public partial class MainWindow
{
    private readonly bool _legacyProofMode=Environment.GetCommandLineArgs().Any(x=>new[]{"--inspection-proof","--steering-proof","--hard-stop-proof","--stage3-proof","--stage3-nonpointer","--inspection-soak"}.Contains(x));
    private ReferenceGarageCatalog? _garageCatalog;
    private readonly ComboBox _analysisA=new(){DisplayMemberPath="Name",SelectedValuePath="Id",MinWidth=230,Margin=new Thickness(5,4,12,0)};
    private readonly ComboBox _analysisB=new(){DisplayMemberPath="Name",SelectedValuePath="Id",MinWidth=230,Margin=new Thickness(5,4,12,0)};
    private readonly TextBlock _analysisALabel=new(){Text="Garage A",VerticalAlignment=VerticalAlignment.Center};
    private readonly TextBlock _analysisBLabel=new(){Text="Garage B",VerticalAlignment=VerticalAlignment.Center};
    private bool _refreshingAnalysis;
    private bool _analysisInitialized;

    private void InitializeGarageAnalysis()
    {
        if(_legacyProofMode) return;
        var row=new StackPanel {Orientation=Orientation.Horizontal};row.Children.Add(_analysisALabel);row.Children.Add(_analysisA);row.Children.Add(_analysisBLabel);row.Children.Add(_analysisB);
        var edit=new Button {Content="Edit in Garage",Margin=new Thickness(0,4,0,0),Padding=new Thickness(8,2,8,2)};
        edit.Click+=(_,_)=>{NavigateWorkspace(Workspace.Garage);RefreshGarage(_analysisA.SelectedValue as string);};row.Children.Add(edit);
        ((StackPanel)((FrameworkElement)ViewModeButton.Parent).Parent).Children.Add(row);
        ((FrameworkElement)AFrontSpring.Parent).Visibility=((FrameworkElement)BFrontSpring.Parent).Visibility=Visibility.Collapsed;
        FrontUnsprungMass.Visibility=RearUnsprungMass.Visibility=Visibility.Collapsed;
        var labels=((Grid)((FrameworkElement)FrontUnsprungMass.Parent).Parent).Children.OfType<StackPanel>().First().Children.OfType<TextBlock>().Skip(2);
        foreach(var label in labels) label.Visibility=Visibility.Collapsed;
        RackGeometryButton.Visibility=Visibility.Collapsed;AReferenceLabel.Visibility=BReferenceLabel.Visibility=Visibility.Collapsed;
        _analysisInitialized=true;RefreshAnalysisChoices(initial:true);
        _analysisA.SelectionChanged+=(_,_)=>{if(!_refreshingAnalysis) Render();};
        _analysisB.SelectionChanged+=(_,_)=>{if(!_refreshingAnalysis) Render();};
    }
    private void RefreshAnalysisChoices(bool initial=false)
    {
        if(!_analysisInitialized) return;
        var a=initial?ReferenceGarageCatalog.Gr86Id:_analysisA.SelectedValue as string;
        var b=initial?ReferenceGarageCatalog.BrzId:_analysisB.SelectedValue as string;
        var entries=_garage!.List().Where(x=>!x.Archived && x.ReferenceEquipment is not null).ToArray();
        _refreshingAnalysis=true;
        try {_analysisA.ItemsSource=entries;_analysisB.ItemsSource=entries;_analysisA.SelectedValue=a;_analysisB.SelectedValue=b;}
        finally {_refreshingAnalysis=false;}
    }
    private GarageEntry SelectedAnalysisEntry(bool first)
    {
        var id=(first?_analysisA:_analysisB).SelectedValue as string??throw new InvalidOperationException("Select saved Garage entries for analysis.");
        return _garage!.Load(id)??throw new InvalidOperationException("The selected Garage entry no longer exists.");
    }
    private void EnsureGarageAnalysisCurrent()
    {
        if(!_analysisInitialized) return;
        RefreshAnalysisChoices();
        var a=_analysisA.SelectedItem as GarageEntry;var b=_analysisB.SelectedItem as GarageEntry;
        if(a is null || _acceptedSnapshot?.GarageA is not {} oldA || oldA.EntryId!=a.Id || oldA.Revision!=a.Revision ||
            (!_inspectionEnabled && (b is null || _acceptedSnapshot.GarageB is not {} oldB || oldB.EntryId!=b.Id || oldB.Revision!=b.Revision))) Render();
    }
    private void UpdateGarageAnalysisPresentation()
    {
        if(!_analysisInitialized) return;
        var single=_inspectionEnabled;
        _analysisALabel.Text=single?"Garage":"Garage A";_analysisB.Visibility=_analysisBLabel.Visibility=single?Visibility.Collapsed:Visibility.Visible;
        InspectionSetup.Visibility=Visibility.Collapsed;InspectionSetup.SelectedIndex=0;
        AReferenceLabel.Visibility=BReferenceLabel.Visibility=Visibility.Collapsed;
        if(_comparison is {} comparison)
        {
            ((TextBlock)((DockPanel)AReferenceLabel.Parent).Children[0]).Text=comparison.A.Input.Name;
            ((TextBlock)((DockPanel)BReferenceLabel.Parent).Children[0]).Text=comparison.B.Input.Name;
            ((TextBlock)((StackPanel)((FrameworkElement)ViewModeButton.Parent).Parent).Children[0]).Text=single?comparison.A.Input.Name:comparison.A.Input.Name+" vs "+comparison.B.Input.Name;
            ComparisonGrid.Columns[1].Header=comparison.A.Input.Name;ComparisonGrid.Columns[2].Header=comparison.B.Input.Name;ComparisonGrid.Columns[3].Header="B − A";
            StatusText.ToolTip=StatusText.Text;
            StatusText.Text=$"Saved Garage revisions · A {_acceptedSnapshot?.GarageA?.Revision} / B {_acceptedSnapshot?.GarageB?.Revision} · synthetic reference geometry; provisional equilibrium. Installed-force calibration and contact/lift equilibrium remain unverified.";
            if(single && _rejectedRevision is null) ViewSummary.Text=$"RSD {Percent(comparison.A.State.RollStiffnessDistributionFront)} · Garage revision {_acceptedSnapshot?.GarageA?.Revision} · provisional equilibrium";
        }
    }
}
