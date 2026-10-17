using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using SuspensionSimulation.Design;
using SuspensionSimulation.Solvers.Design;

namespace SuspensionSimulation.App;

public partial class MainWindow
{
    internal async void RunMechanicsProof()
    {
        var folder=Path.Combine(App.RepositoryRoot,"artifacts","mechanical-authoring");Directory.CreateDirectory(folder);Exception? failure=null;
        try
        {
            await Task.Delay(500);var physics=_renderAttempt;NavigateWorkspace(Workspace.ComponentDesigner);
            foreach(var kind in new[]{ComponentKind.Spring,ComponentKind.Damper,ComponentKind.AntiRollBar})
            {
                _componentKind.SelectedItem=kind;if(kind==ComponentKind.Damper) {_integratedSeats.IsChecked=true;_perchAdjustment.IsChecked=true;_bodyAdjustment.IsChecked=true;_physicalFields["perchMin"].Text="-10";_physicalFields["perchMax"].Text="35";}
                NewComponent(false);PublishComponent();
                if(_designLibrary!.LoadComponent(_componentId)!.Behavior is null) throw new InvalidOperationException("Elastic template failed to publish.");
            }
            var damper=_designLibrary!.Components().Single(c=>c.Kind==ComponentKind.Damper);_componentLibraryList.SelectedValue=damper.Identity.AssetId;OpenSelectedComponent();
            var original=(AxialDamperBehavior)_componentDefinition!.Behavior!;if(original.Family!=DamperFamily.Shock||_physicalFields["perchMax"].Text!="35") throw new InvalidOperationException("Damper range/family did not reopen.");
            _physicalFields["compression"].Text="2500";PublishComponent();if(((AxialDamperBehavior)_designLibrary.LoadComponent(_componentId)!.Behavior!).CompressionNsPerM!=2500) throw new InvalidOperationException("Damper specification edits did not save.");
            var tabs=((Grid)_workspaceViews[Workspace.ComponentDesigner]).Children.OfType<TabControl>().Single();tabs.SelectedItem=tabs.Items.OfType<TabItem>().Single(t=>(string)t.Header=="Mechanics");await Task.Delay(150);CaptureLayout(Path.Combine(folder,"damper.png"));
            var invalid=CaptureComponentRaw();_physicalFields["compression"].Text="pending invalid value";var pending=CaptureComponentRaw();_designLibrary.SaveDraft("component-workspace",pending);_componentRecoveryAtOpen=null;
            try {RecoverComponent();}catch(ArgumentException) { }
            if(_physicalFields["compression"].Text!="pending invalid value") throw new InvalidOperationException("Recovery lost raw invalid physical inputs.");RestoreComponentRaw(invalid,true);
            var add=((StackPanel)tabs.Items.OfType<TabItem>().Single(t=>(string)t.Header=="Adjustments").Content).Children.OfType<Button>().Single();
            for(var kind=0;kind<7;kind++)
            {
                _componentKind.SelectedItem=ComponentKind.TopMount;_componentLength.Text="20";_componentWidth.Text="100";_componentHeight.Text="25";NewComponent(false);_adjustKind.SelectedIndex=kind;_adjustAxis.SelectedIndex=1;_adjustPointIds.Text="bearing";_adjustFrameIds.Text="bearing";_adjustStations.Text="bearing,chassis";
                add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));if(_componentMetadataSeed?.Adjustments?.Length!=1) throw new InvalidOperationException("Adjustment authoring failed for "+kind+": "+_componentStatus.Text);PublishComponent();
            }
            NavigateWorkspace(Workspace.AssemblyEditor);var models=TwinsInstalledCatalog.Seed(_designLibrary,App.RepositoryRoot);_assemblyExpectedDof.Text="5";LoadAssembly(models.Gr86);_assemblyViewport!.Preset("Isometric");
            for(var i=0;i<4;i++) _twinsTravel[i].Text=i%2==0?"10":"-10";_twinsSteering.Text="0";SolveTwinsMotionPreview();
            if(_twinsMotion is null||!_twinsMotionReadout.Text.Contains("N·m")) throw new InvalidOperationException("Installed mechanics did not reach the preview.");
            PublishAssembly();if(_assemblyGraph!.SpringSeats.Length!=4) throw new InvalidOperationException("Assembly editor dropped spring-seat bindings.");
            SolveTwinsMotionPreview();var motionTabs=((Grid)_workspaceViews[Workspace.AssemblyEditor]).Children.OfType<TabControl>().Single();motionTabs.SelectedIndex=motionTabs.Items.Count-1;await Task.Delay(150);CaptureLayout(Path.Combine(folder,"installed.png"));
            if(_renderAttempt!=physics) throw new InvalidOperationException("Mechanical authoring invoked the legacy equilibrium.");
        }
        catch(Exception error) {failure=error;App.LogException("Mechanical authoring proof",error);}
        File.WriteAllText(Path.Combine(folder,"proof.json"),JsonSerializer.Serialize(new {success=failure is null,error=failure?.ToString()},new JsonSerializerOptions {WriteIndented=true}));Application.Current.Shutdown(failure is null?0:2);
    }
}
