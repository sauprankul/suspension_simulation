using System.IO;
using System.Windows;
using System.Windows.Controls;
using SuspensionSimulation.Design;

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

    private FrameworkElement BuildGarage(FrameworkElement details)
    {
        _garage=new GarageLibrary(Path.Combine(App.RepositoryRoot,"library","garage"));
        var grid=new Grid();grid.ColumnDefinitions.Add(new(){Width=new GridLength(300)});grid.ColumnDefinitions.Add(new());
        var left=new StackPanel {Margin=new Thickness(24)};left.Children.Add(new TextBlock {Text="Find a car"});left.Children.Add(_garageSearch);
        left.Children.Add(_garageArchived);left.Children.Add(_garageList);
        foreach(var action in new[]{"New","Duplicate","Save changes","Archive","Restore"})
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
    {if(_garageSelected is {} entry) {var fields=_workspaceDrafts[Workspace.Garage];_garageDrafts[entry.Id]=(fields.Name.Text,fields.Notes.Text);}}
    private void SelectGarage(GarageEntry? entry)
    {
        CacheGarageDraft();_garageSelected=entry;
        if(entry is null) return;
        var fields=_workspaceDrafts[Workspace.Garage];
        var draft=_garageDrafts.GetValueOrDefault(entry.Id,(entry.Name,entry.Notes));fields.Name.Text=draft.Name;fields.Notes.Text=draft.Notes;
        _garageStatus.Text=$"Saved revision {entry.Revision}. "+(entry.Archived?"Archived — recover with Restore.":"Needs assembly — cannot run yet.");
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
            if(action=="New") saved=_garage!.Create("New car");
            else
            {
                var current=_garageSelected??throw new InvalidOperationException("Select an entry first.");
                var draft=current with {Name=fields.Name.Text,Notes=fields.Notes.Text};
                saved=action switch {"Duplicate"=>_garage!.Duplicate(draft),"Archive"=>_garage!.Save(draft with {Archived=true}),
                    "Restore"=>_garage!.Save(draft with {Archived=false}),_=>_garage!.Save(draft)};
                _garageDrafts.Remove(current.Id);
                // Keep the newly saved values when refreshing the selected row.
                if(action!="Duplicate") _garageSelected=saved;
            }
            _garageSearch.Text="";_garageArchived.IsChecked=saved.Archived;RefreshGarage(saved.Id);
        }
        catch(Exception error) {_garageStatus.Text=error.Message;App.LogException("Garage action",error);}
    }
}
