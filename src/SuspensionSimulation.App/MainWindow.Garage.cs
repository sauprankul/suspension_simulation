using System.IO;
using System.Windows;
using System.Windows.Controls;
using SuspensionSimulation.Design;
using SuspensionSimulation.Solvers.HeadToHead;

namespace SuspensionSimulation.App;

public partial class MainWindow
{
    private GarageLibrary? _garage;
    private GarageEntry? _garageSelected;
    private readonly ListBox _garageList=new(){DisplayMemberPath="Name",MinHeight=250};
    private readonly TextBox _garageSearch=new();
    private readonly CheckBox _garageArchived=new(){Content="Archived entries",Foreground=System.Windows.Media.Brushes.LightSteelBlue};
    private readonly TextBlock _garageStatus=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,0)};
    private readonly Dictionary<string,(string Name,string Notes)> _garageDrafts=new();
    private bool _garageRefresh;
    private readonly Dictionary<string,ComboBox> _garageEquipment=new();
    private readonly Dictionary<string,ReferenceGarageEquipment?> _garageEquipmentDrafts=new();

    private FrameworkElement BuildGarage(FrameworkElement details)
    {
        var test=Environment.GetCommandLineArgs().Any(x=>x.Contains("proof",StringComparison.Ordinal));
        _garage=new GarageLibrary(Path.Combine(App.RepositoryRoot,test?"artifacts/garage-analysis-proof/"+App.RunId:"library/garage"));
        _garageCatalog=new ReferenceGarageCatalog(Path.Combine(App.RepositoryRoot,"data/reference/stock-86-brz/reference-sheet.json"));
        _garageCatalog.Seed(_garage);
        var editor=(StackPanel)((ScrollViewer)details).Content;
        ((TextBlock)editor.Children[2]).Text="Saved cars · complete-assembly configurations";
        ((TextBlock)editor.Children[3]).Text="Only saved Garage revisions are used by the visualizers.";
        var equipment=new StackPanel {Margin=new Thickness(0,0,0,15),Visibility=Visibility.Collapsed};
        foreach(var role in new[]{"front-spring","rear-spring","front-bar","rear-bar"})
        {
            equipment.Children.Add(new TextBlock {Text=role.Replace('-',' ')});
            var picker=new ComboBox {ItemsSource=_garageCatalog.Options.Where(x=>x.Role==role).ToArray(),DisplayMemberPath="Name",SelectedValuePath="Id",Margin=new Thickness(0,3,0,8),MinWidth=400};
            _garageEquipment.Add(role,picker);equipment.Children.Add(picker);
        }
        editor.Children.Insert(6,equipment);
        InitializePhysicalGarage(editor);
        var grid=new Grid();grid.ColumnDefinitions.Add(new(){Width=new GridLength(300)});grid.ColumnDefinitions.Add(new());
        var left=new StackPanel {Margin=new Thickness(24)};left.Children.Add(new TextBlock {Text="Find a car"});left.Children.Add(_garageSearch);
        left.Children.Add(_garageArchived);left.Children.Add(_garageList);
        foreach(var action in new[]{"New","Duplicate","Save changes","Archive","Restore","Open 2D","Open 3D"})
        {
            var button=new Button {Content=action,Margin=new Thickness(0,4,0,0)};
            button.Click+=(_,_)=>GarageAction(action);left.Children.Add(button);
        }
        left.Children.Add(_garageStatus);grid.Children.Add(left);Grid.SetColumn(details,1);grid.Children.Add(details);
        _garageSearch.TextChanged+=(_,_)=>RefreshGarage();_garageArchived.Checked+=(_,_)=>RefreshGarage();_garageArchived.Unchecked+=(_,_)=>RefreshGarage();
        _garageList.SelectionChanged+=(_,_)=>{if(!_garageRefresh) SelectGarage(_garageList.SelectedItem as GarageEntry);};
        try {RefreshGarage();} catch(Exception error) {_garageStatus.Text="Garage could not open: "+error.Message;App.LogException("Garage load",error);}
        return grid;
    }
    private void CacheGarageDraft()
    {if(_garageSelected is {} entry) {var fields=_workspaceDrafts[Workspace.Garage];_garageDrafts[entry.Id]=(fields.Name.Text,fields.Notes.Text);_garageEquipmentDrafts[entry.Id]=ReadGarageEquipment();CachePhysicalGarageDraft();}}
    private ReferenceGarageEquipment? ReadGarageEquipment()
    {
        string Id(string role)=>_garageEquipment[role].SelectedValue as string??"";
        if(_garageEquipment.Values.All(x=>x.SelectedValue is null)) return null;
        return new(ReferenceGarageCatalog.ModelId,Id("front-spring"),Id("rear-spring"),Id("front-bar"),Id("rear-bar"));
    }
    private void SelectGarage(GarageEntry? entry)
    {
        CacheGarageDraft();_garageSelected=entry;
        if(entry is null) return;
        var fields=_workspaceDrafts[Workspace.Garage];
        var draft=_garageDrafts.GetValueOrDefault(entry.Id,(entry.Name,entry.Notes));fields.Name.Text=draft.Name;fields.Notes.Text=draft.Notes;
        var selected=_garageEquipmentDrafts.GetValueOrDefault(entry.Id,entry.ReferenceEquipment);
        _garageEquipment["front-spring"].SelectedValue=selected?.FrontSpringId;_garageEquipment["rear-spring"].SelectedValue=selected?.RearSpringId;
        _garageEquipment["front-bar"].SelectedValue=selected?.FrontBarId;_garageEquipment["rear-bar"].SelectedValue=selected?.RearBarId;
        _garageStatus.Text=$"Saved revision {entry.Revision}. "+(entry.Archived?"Archived — recover with Restore.":entry.Configuration is null?"Needs physical assembly.":"Physical setup · estimated geometry; check equilibrium before analysis.");
        FillPhysicalGarage(entry);
    }
    private void RefreshGarage(string? selectId=null)
    {
        CacheGarageDraft();var id=selectId??_garageSelected?.Id;
        var entries=_garage!.List().Where(x=>x.Archived==(_garageArchived.IsChecked==true) && x.Name.Contains(_garageSearch.Text,StringComparison.OrdinalIgnoreCase)).ToArray();
        _garageRefresh=true;
        try {_garageList.ItemsSource=entries;_garageList.SelectedItem=entries.FirstOrDefault(x=>x.Id==id);}
        finally {_garageRefresh=false;}
        SelectGarage(_garageList.SelectedItem as GarageEntry);
    }
    private void GarageAction(string action)
    {
        try
        {
            var fields=_workspaceDrafts[Workspace.Garage];GarageEntry saved;
            if(action is "Open 2D" or "Open 3D")
            {
                var entry=_garageSelected??throw new InvalidOperationException("Select a saved Garage entry first.");
                if(entry.Archived) throw new InvalidOperationException("Restore this archived entry before analysis.");
                _=new SuspensionSimulation.Solvers.Design.GarageSetupResolver(_designLibrary!).Resolve(entry.Configuration??throw new InvalidOperationException("Save a physical assembly first."));
                RefreshAnalysisChoices();_analysisA.SelectedValue=entry.Id;NavigateWorkspace(action=="Open 3D"?Workspace.Visualizer3D:Workspace.HeadToHead);return;
            }
            if(action=="New") saved=_garage!.Save(new(Guid.NewGuid().ToString("N"),0,"New car","",false,_physicalGarageCatalog!.Create("New car"),_garageCatalog!.Stock(false)));
            else
            {
                var current=_garageSelected??throw new InvalidOperationException("Select an entry first.");
                var draft=current with {Name=fields.Name.Text,Notes=fields.Notes.Text,ReferenceEquipment=ReadGarageEquipment(),Configuration=ReadPhysicalGarage(current)};
                if(draft.ReferenceEquipment is not null) _=_garageCatalog!.Resolve(draft with {Archived=false});
                saved=action switch {"Duplicate"=>_garage!.Duplicate(draft),"Archive"=>_garage!.Save(draft with {Archived=true}),
                    "Restore"=>_garage!.Save(draft with {Archived=false}),_=>_garage!.Save(draft)};
                _garageDrafts.Remove(current.Id);
                _garageEquipmentDrafts.Remove(current.Id);
                _garagePhysicalDrafts.Remove(current.Id);
                // Keep the newly saved values when refreshing the selected row.
                if(action!="Duplicate") _garageSelected=saved;
            }
            _garageSearch.Text="";_garageArchived.IsChecked=saved.Archived;RefreshGarage(saved.Id);
            RefreshAnalysisChoices();
        }
        catch(Exception error) {_garageStatus.Text=error.Message;App.LogException("Garage action",error);}
    }
}
