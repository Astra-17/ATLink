using System.Data;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ATLink.Core;
using Microsoft.Win32;

namespace ATLink.Views;
public partial class ModulesWindow : UserControl, IWorkspacePage
{
    public event Action<bool>? Closed;
    public event Action? DatabaseRequested;
    public event Action? TablesRequested;
    public event Action? SettingsRequested;
    void TablesClick(object sender,RoutedEventArgs e){if(PlayerContent.Content is not PlayerEditor editor||editor.CanLeave())TablesRequested?.Invoke();}
    void SettingsClick(object sender,RoutedEventArgs e)=>SettingsRequested?.Invoke();
    private readonly DatabaseDocument document;
    private readonly FootballCatalog catalog;
    private string kind="players";
    private IReadOnlyList<EntityItem> items=[];
    public ModulesWindow(DatabaseDocument document,string initial="players")
    {
        InitializeComponent();this.document=document;catalog=new(document);Subtitle.Text=document.DisplayName;
        string national=Path.Combine(Path.GetDirectoryName(document.SourcePath)!,"FC26_NATIONAL_TEAM_IDS.csv");
        if(!File.Exists(national))national=Path.Combine(AppContext.BaseDirectory,"Data","FC26_NATIONAL_TEAM_IDS.csv");
        if(File.Exists(national)){try{catalog.LoadNationalTeams(national);}catch(Exception ex){Status.Text=ex.Message;}}
        Switch(initial);
    }
    private void Switch(string module)
    {
        if(PlayerContent.Content is PlayerEditor pending&&!pending.CanLeave())return;
        kind=module=="stadiums"&&document.Tables.All(t=>t.Name!="stadiums")?"ssfstadiums":module;
        foreach(Button button in ModuleNavigation.Children)
        {
            bool active=(string)button.Tag==module;
            button.Background=StudioPalette.Get(active?"AccentBrush":"InactiveNavigationBrush");
            button.BorderBrush=StudioPalette.Get(active?"AccentBrush":"LineBrush");
            button.FontWeight=active?FontWeights.SemiBold:FontWeights.Normal;
        }
        ModuleTitle.Text=char.ToUpper(kind[0])+kind[1..];
        items=kind=="transfers"?[]:catalog.Entities(kind=="ssfstadiums"?"ssfstadiums":kind);Search.Text="";Refresh();
        CreateButton.Visibility=kind=="transfers"?Visibility.Collapsed:Visibility.Visible;
        FormationButton.Visibility=kind=="teams"?Visibility.Visible:Visibility.Collapsed;
        RelatedButton.Visibility=kind is "teams" or "leagues"?Visibility.Visible:Visibility.Collapsed;
        bool hosted=kind is "players" or "transfers";
        ModuleHeader.Visibility=hosted?Visibility.Collapsed:Visibility.Visible;
        Results.Visibility=hosted?Visibility.Collapsed:Visibility.Visible;
        PlayerContent.Visibility=hosted?Visibility.Visible:Visibility.Collapsed;
        if(kind=="players")
        {
            var browser=new PlayerBrowser(catalog);
            browser.EditRequested+=item=>{Results.SelectedItem=item;EditClick(this,new RoutedEventArgs());};
            browser.CreateRequested+=item=>{Results.SelectedItem=item;CreateClick(this,new RoutedEventArgs());};
            PlayerContent.Content=browser;
            Status.Text=$"{catalog.Entities("players").Count:N0} players.";
        }
        else if(kind=="transfers")
        {
            PlayerContent.Content=new TransfersPage(catalog);
            Status.Text="Verify leagues or add a player manually.";
        }
        else {PlayerContent.Content=null;Status.Text=$"{items.Count:N0} {kind}. Select an entry to edit.";}
    }
    private void Refresh()=>Results.ItemsSource=items.Where(i=>i.Name.Contains(Search.Text,StringComparison.CurrentCultureIgnoreCase)||i.Id.Contains(Search.Text)).ToArray();
    private void ModuleClick(object sender,RoutedEventArgs e){try{Switch((string)((Button)sender).Tag);}catch(Exception ex){Error(ex);}}
    private void SearchChanged(object sender,TextChangedEventArgs e){if(Results is not null)Refresh();}
    private void HomeClick(object sender,RoutedEventArgs e){if(PlayerContent.Content is PlayerEditor editor&&!editor.CanLeave())return;if(DatabaseRequested is not null)DatabaseRequested();else Closed?.Invoke(false);}
    private void EditDoubleClick(object sender,MouseButtonEventArgs e)=>EditClick(sender,e);
    private UserControl EditorFor(EntityItem item,string title)
    {
        if(kind=="teams")return new TeamWorkspace(catalog,item);
        return new EntityEditor(catalog,catalog.Table(kind),item.Row,title);
    }
    private void EditClick(object sender,RoutedEventArgs e)
    {
        if(Results.SelectedItem is not EntityItem item)return;
        try
        {
            if(kind=="players")
            {
                var playerEditor=new PlayerEditor(catalog,item);
                playerEditor.BackRequested+=()=>Switch("players");
                PlayerContent.Content=playerEditor;
                return;
            }
            var editor=EditorFor(item,item.Name);
            if(editor is IWorkspacePage hosted)hosted.Closed+=ok=>{if(ok){string search=Search.Text;Switch(kind=="ssfstadiums"?"stadiums":kind);Search.Text=search;Status.Text="Changes applied in memory.";}};
            WorkspaceController.Open(editor);
        }
        catch(Exception ex){Error(ex);}
    }
    private void CreateClick(object sender,RoutedEventArgs e)
    {
        if(Results.SelectedItem is not EntityItem template){Status.Text="Select an existing entry to use as a template.";return;}
        DataRow? row=null;
        try
        {
            var table=catalog.Table(kind);row=TableEditing.Add(table,template.Row);
            var created=new EntityItem(FootballCatalog.Value(row,kind=="teams"?"teamid":kind=="players"?"playerid":kind is "leagues"?"leagueid":"stadiumid"),"New "+kind.TrimEnd('s'),"",row);
            var editor=kind=="teams"?new TeamWorkspace(catalog,created):(UserControl)new EntityEditor(catalog,table,row,created.Name,namesFrom:template.Row);
            if(editor is IWorkspacePage hosted)hosted.Closed+=ok=>{if(!ok&&row?.RowState==DataRowState.Added)row.Delete();Switch(kind);};
            WorkspaceController.Open(editor);
        }
        catch(Exception ex){if(row?.RowState==DataRowState.Added)row.Delete();Error(ex);}
    }
    private void RelatedClick(object sender,RoutedEventArgs e)
    {
        if(Results.SelectedItem is not EntityItem item)return;
        WorkspaceController.Open(kind=="teams"?RelatedRecordsPage.ForTeam(catalog,item.Id,item.Name):RelatedRecordsPage.ForLeague(catalog,item.Id,item.Name));
    }
    private void FormationClick(object sender,RoutedEventArgs e){if(Results.SelectedItem is not EntityItem team)return;try{WorkspaceController.Open(new FormationWindow(catalog,team.Id));}catch(Exception ex){Error(ex);}}
    private void SaveClick(object sender,RoutedEventArgs e)
    {
        if(PlayerContent.Content is PlayerEditor editor&&!editor.TryApply())return;
        var dialog=new SaveFileDialog{Filter="Database (*.db)|*.db",FileName=Path.GetFileNameWithoutExtension(document.SourcePath)+"-edited.db"};if(!ShellDialogs.Open(dialog,this))return;
        try{document.SaveAs(dialog.FileName);Status.Text="Saved: "+dialog.FileName;}catch(Exception ex){Error(ex);}
    }
    private void Error(Exception ex){Status.Text=ex.Message;ShellDialogs.Message(this,ex.Message,"ATLink");}
}
