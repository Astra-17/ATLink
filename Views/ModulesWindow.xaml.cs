using System.Data;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ATLink.Core;

namespace ATLink.Views;
public partial class ModulesWindow : UserControl, IWorkspacePage
{
    public event Action<bool>? Closed;
    public event Action? DatabaseRequested;
    public event Action? TablesRequested;
    public event Action? SettingsRequested;
    void TablesClick(object sender,RoutedEventArgs e)=>TablesRequested?.Invoke();
    void SettingsClick(object sender,RoutedEventArgs e)=>SettingsRequested?.Invoke();
    private readonly DatabaseDocument document;
    private readonly FootballCatalog catalog;
    private string kind="players";
    private IReadOnlyList<EntityItem> items=[];
    public ModulesWindow(DatabaseDocument document,string initial="players")
    {
        InitializeComponent();
        CareerNotice.Visibility=document.IsCareer?Visibility.Visible:Visibility.Collapsed;CareerNotice.Text=UiText.Get("Career database loaded. Export creates a separate .db file, not a playable career save.");this.document=document;catalog=new(document);
        Subtitle.Text=document.DisplayName;Subtitle.IsReadOnly=!document.IsSquad;
        if(!document.IsSquad)Subtitle.ToolTip=document.DisplayName;
        string national=Path.Combine(Path.GetDirectoryName(document.SourcePath)!,"FC26_NATIONAL_TEAM_IDS.csv");
        if(!File.Exists(national))national=Path.Combine(RuntimeData.Root,"FC26_NATIONAL_TEAM_IDS.csv");
        if(File.Exists(national)){try{catalog.LoadNationalTeams(national);}catch(Exception ex){Status.Text=ex.Message;}}
        Switch(initial);
        Loaded+=(_,_)=>{document.DisplayLocalizationChanged+=RefreshLocalizedNames;RefreshLocalizedNames();};
        Unloaded+=(_,_)=>document.DisplayLocalizationChanged-=RefreshLocalizedNames;
    }
    void RefreshLocalizedNames()
    {
        string? selectedId=(Results.SelectedItem as EntityItem)?.Id;
        if(kind!="transfers")items=catalog.Entities(kind);
        if(kind is not ("players" or "transfers")){Refresh();Results.SelectedItem=items.FirstOrDefault(i=>i.Id==selectedId);}
        if(PlayerContent.Content is PlayerBrowser browser)browser.RefreshLocalizedNames();
    }
    private void Switch(string module)
    {
        if(PlayerContent.Content is PlayerEditor pending&&!pending.CanLeave())return;
        PlayerActions.Visibility=Visibility.Collapsed;
        foreach(var button in new[]{ApplyPlayerButton,UndoPlayerButton,RedoPlayerButton}){System.Windows.Data.BindingOperations.ClearBinding(button,IsEnabledProperty);button.IsEnabled=false;}PlayerEditStatus.Visibility=Visibility.Collapsed;System.Windows.Data.BindingOperations.ClearBinding(PlayerEditStatus,TextBlock.TextProperty);
        NavigationRail.Visibility=Visibility.Visible;NavigationColumn.Width=new GridLength(230);
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
        EditButton.Visibility=kind=="teams"?Visibility.Collapsed:Visibility.Visible;
        FormationButton.Visibility=Visibility.Collapsed;
        RelatedButton.Visibility=kind=="leagues"?Visibility.Visible:Visibility.Collapsed;
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
            Status.Text=ATLink.Core.UiText.Format("{0:N0} players.", catalog.Entities("players").Count);
        }
        else if(kind=="transfers")
        {
            PlayerContent.Content=new TransfersPage(catalog);
            Status.Text=ATLink.Core.UiText.Get("Verify leagues or add a player manually.");
        }
        else {PlayerContent.Content=null;Status.Text=ATLink.Core.UiText.Format("{0:N0} {1}. Select an entry to edit.", items.Count, kind);}
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
                ShowPlayerEditor(item);
                return;
            }
            var editor=EditorFor(item,item.Name);
            if(editor is IWorkspacePage hosted)hosted.Closed+=ok=>{if(ok){string search=Search.Text;Switch(kind=="ssfstadiums"?"stadiums":kind);Search.Text=search;Status.Text=ATLink.Core.UiText.Get("Changes applied in memory.");}};
            WorkspaceController.Open(editor);
        }
        catch(Exception ex){Error(ex);}
    }
    void ShowPlayerEditor(EntityItem item)
    {
        var playerEditor=new PlayerEditor(catalog,item);
        playerEditor.BackRequested+=()=>Switch("players");
        PlayerContent.Content=playerEditor;
        PlayerActions.Visibility=Visibility.Visible;
        ApplyPlayerButton.SetBinding(IsEnabledProperty,new System.Windows.Data.Binding(nameof(PlayerEditor.HasPendingChanges)){Source=playerEditor});
        UndoPlayerButton.SetBinding(IsEnabledProperty,new System.Windows.Data.Binding(nameof(PlayerEditor.CanUndo)){Source=playerEditor});
        RedoPlayerButton.SetBinding(IsEnabledProperty,new System.Windows.Data.Binding(nameof(PlayerEditor.CanRedo)){Source=playerEditor});PlayerEditStatus.Visibility=Visibility.Visible;
        PlayerEditStatus.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding("Text"){Source=playerEditor.FindName("Error")});
        NavigationRail.Visibility=Visibility.Collapsed;NavigationColumn.Width=new GridLength(0);
    }
    internal bool CreatePlayerFromSelection(EntityItem template,Func<string,bool> confirm)
    {
        string name=catalog.PlayerName(template.Row);
        if(!confirm(UiText.Format("Create a new player based on {0}?",name)))return false;
        var created=PlayerEditing.Clone(catalog,template.Row);
        try{ShowPlayerEditor(created);Status.Text=UiText.Format("{0:N0} players.",catalog.Rows("players").Count());return true;}
        catch{
            foreach(var row in catalog.Rows("editedplayernames").Where(r=>FootballCatalog.Value(r,"playerid")==created.Id).ToArray())
                if(row.RowState==DataRowState.Added)row.Table.Rows.Remove(row);
            if(created.Row.RowState==DataRowState.Added)created.Row.Table.Rows.Remove(created.Row);
            throw;
        }
    }
    private void CreateClick(object sender,RoutedEventArgs e)
    {
        if(Results.SelectedItem is not EntityItem template){Status.Text=ATLink.Core.UiText.Get("Select an existing entry to use as a template.");return;}
        DataRow? row=null;
        try
        {
            if(kind=="players"){
                CreatePlayerFromSelection(template,message=>ShellDialogs.Message(this,message,UiText.Get("Create player"),MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes);
                return;
            }
            if(kind=="teams"){
                var creation=TeamCreation.Begin(catalog,template.Row);
                try{
                    var teamEditor=new TeamWorkspace(catalog,creation.Team);teamEditor.Applied+=creation.Commit;teamEditor.Released+=creation.Rollback;
                    teamEditor.Closed+=ok=>{if(!creation.IsCommitted)creation.Rollback();Switch(kind);};WorkspaceController.Open(teamEditor);return;
                }catch{creation.Rollback();throw;}
            }
            var table=catalog.Table(kind);row=TableEditing.Add(table,template.Row);
            var created=new EntityItem(FootballCatalog.Value(row,kind=="teams"?"teamid":kind=="players"?"playerid":kind is "leagues"?"leagueid":"stadiumid"),ATLink.Core.UiText.Concat(ATLink.Core.UiText.Get("New "), kind.TrimEnd('s')),"",row);
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
    private void UndoPlayerClick(object sender,RoutedEventArgs e){if(PlayerContent.Content is PlayerEditor editor)editor.UndoDraft();}
    private void RedoPlayerClick(object sender,RoutedEventArgs e){if(PlayerContent.Content is PlayerEditor editor)editor.RedoDraft();}
    private void ApplyPlayerClick(object sender,RoutedEventArgs e)
    {
        if(PlayerContent.Content is PlayerEditor editor)editor.TryApply();
    }
    private void SaveClick(object sender,RoutedEventArgs e)
    {
        if(PlayerContent.Content is PlayerEditor editor&&!editor.TryApply())return;
        CommitSquadName();
        var dialog=ShellDialogs.ForDatabase(document);if(!ShellDialogs.Open(dialog,this))return;
        try{document.SaveAs(dialog.FileName);Status.Text=ATLink.Core.UiText.Concat(ATLink.Core.UiText.Get("Saved: "), dialog.FileName);}catch(Exception ex){Error(ex);}
    }
    void SubtitleKey(object sender,KeyEventArgs e)
    {
        if(e.Key==Key.Enter){CommitSquadName();e.Handled=true;Keyboard.ClearFocus();}
        else if(e.Key==Key.Escape){Subtitle.Text=document.DisplayName;Keyboard.ClearFocus();e.Handled=true;}
    }
    void SubtitleCommit(object sender,RoutedEventArgs e)=>CommitSquadName();
    void CommitSquadName()
    {
        if(!document.IsSquad){Subtitle.Text=document.DisplayName;return;}
        try{document.SetSquadName(Subtitle.Text);Subtitle.Text=document.DisplayName;}
        catch(Exception ex){Subtitle.Text=document.DisplayName;Error(ex);}
    }
    private void Error(Exception ex){Status.Text=ex.Message;ShellDialogs.Message(this,ex.Message,"ATLink");}
}
