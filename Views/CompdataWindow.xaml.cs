using System.IO;
using System.Windows;
using System.Windows.Controls;
using ATLink.Core;
using Microsoft.Win32;
namespace ATLink.Views;
public partial class CompdataWindow:UserControl,IWorkspacePage
{
    public event Action<bool>? Closed;
    private readonly CompdataProject project;private CompdataFile? selected;private bool updating;private CompdataTable? table;private int mode;
    public CompdataWindow(CompdataProject project){InitializeComponent();this.project=project;Files.ItemsSource=project.Files;Files.SelectedIndex=0;}
    private void BackClick(object sender,RoutedEventArgs e)
    {
        try{CommitTable();}catch(Exception ex){ShellDialogs.Message(this,ex.Message,"Compdata");return;}
        if(project.Files.Any(f=>f.Changed)&&ShellDialogs.Message(this,"Close with unsaved in-memory changes?","Compdata",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
        Closed?.Invoke(false);
    }
    private void OpenThenRefresh(UserControl page,string status)
    {
        if(page is IWorkspacePage hosted)hosted.Closed+=ok=>
        {
            if(!ok)return;
            table=selected is null?null:new CompdataTable(selected);Records.ItemsSource=IdSortedView.Bind(table?.Data.DefaultView);
            updating=true;Files.ItemsSource=project.Files;Files.SelectedItem=selected;updating=false;
            updating=true;Editor.Text=selected?.Text??"";updating=false;
            Status.Text=status;
        };
        WorkspaceController.Open(page);
    }
    private void ManageTournamentClick(object sender,RoutedEventArgs e)
    {
        try{CommitTable();OpenThenRefresh(new CompetitionManagerWindow(project),"Competition settings updated in memory.");}
        catch(Exception ex){updating=false;ShellDialogs.Message(this,ex.Message,"Compdata");}
    }
    private void NewTournamentClick(object sender,RoutedEventArgs e)
    {
        try{CommitTable();OpenThenRefresh(new TournamentWizard(project),"Tournament created in memory. Use Save As to write a new folder.");}
        catch(Exception ex){ShellDialogs.Message(this,ex.Message,"Compdata");}
    }
    private void SelectFile(object sender,SelectionChangedEventArgs e)
    {
        if(updating)return;
        try{CommitTable();selected=Files.SelectedItem as CompdataFile;table=selected is null?null:new CompdataTable(selected);Records.ItemsSource=IdSortedView.Bind(table?.Data.DefaultView);updating=true;Editor.Text=selected?.Text??"";updating=false;Status.Text=selected?.RelativePath??"";}
        catch(Exception ex){updating=true;Files.SelectedItem=selected;updating=false;ShellDialogs.Message(this,ex.Message,"Compdata");}
    }
    private void EditText(object sender,TextChangedEventArgs e){if(!updating&&selected is not null)selected.Text=Editor.Text;}
    private void CommitTable(){if(mode==0&&selected is not null&&table is not null){Records.CommitEdit(DataGridEditingUnit.Cell,true);Records.CommitEdit(DataGridEditingUnit.Row,true);selected.Text=table.Serialize();}}
    private void ChangeMode(object sender,SelectionChangedEventArgs e)
    {
        if(e.Source!=EditorTabs||updating||selected is null)return;
        try{CommitTable();mode=EditorTabs.SelectedIndex;updating=true;if(mode==1)Editor.Text=selected.Text;else{table=new CompdataTable(selected);Records.ItemsSource=IdSortedView.Bind(table.Data.DefaultView);}updating=false;}catch(Exception ex){updating=true;EditorTabs.SelectedIndex=mode;updating=false;ShellDialogs.Message(this,ex.Message,"Compdata");}
    }
    private void ValidateClick(object sender,RoutedEventArgs e){try{CommitTable();var errors=project.Validate();Status.Text=errors.Count==0?"Object hierarchy validated. Other competition rules require game validation.":string.Join(" | ",errors.Take(8));}catch(Exception ex){ShellDialogs.Message(this,ex.Message,"Compdata");}}
    private void CompetitionClick(object sender,RoutedEventArgs e)
    {
        try{CommitTable();}catch(Exception ex){ShellDialogs.Message(this,ex.Message,"Compdata");return;}
        var panel=new StackPanel{Margin=new Thickness(20)};
        panel.Children.Add(new TextBlock{Text="Competition template",FontSize=18});var choices=new ComboBox{ItemsSource=CompetitionTools.Choices(project),DisplayMemberPath="Name",Margin=new Thickness(0,8,0,12)};panel.Children.Add(choices);
        panel.Children.Add(new TextBlock{Text="New unique code (e.g. C123)"});var code=new TextBox{Margin=new Thickness(0,5,0,12)};panel.Children.Add(code);
        panel.Children.Add(new TextBlock{Text="Competition name"});var name=new TextBox{Margin=new Thickness(0,5,0,12)};panel.Children.Add(name);
        var detail=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,10)};var previewButton=new Button{Content="Preview",Margin=new Thickness(0,0,0,8)};CompetitionClone? preview=null;
        void Invalidate(){preview=null;}code.TextChanged+=(_,_)=>Invalidate();name.TextChanged+=(_,_)=>Invalidate();choices.SelectionChanged+=(_,_)=>Invalidate();
        ActionPage? page=null;
        previewButton.Click+=(_,_)=>{try{if(choices.SelectedItem is not CompetitionChoice choice)return;preview=CompetitionTools.PreviewClone(project,choice.Id,code.Text,name.Text);detail.Text=$"{preview.Objects} new objects. Files: {string.Join(", ",preview.Changes.Select(c=>c.Path))}. Existing rules, team sources and schedule offsets are copied; review them for the new competition.";}catch(Exception ex){detail.Text=ex.Message;}};
        panel.Children.Add(previewButton);panel.Children.Add(detail);
        page=new ActionPage("Create competition from template",panel,"Apply",p=>{try{if(preview is null)return;CompetitionTools.Apply(project,preview);p.Finish(true);}catch(Exception ex){detail.Text=ex.Message;}});
        page.Closed+=ok=>{if(!ok)return;updating=true;Files.ItemsSource=project.Files;Files.SelectedItem=selected;updating=false;table=selected is null?null:new CompdataTable(selected);Records.ItemsSource=IdSortedView.Bind(table?.Data.DefaultView);updating=true;Editor.Text=selected?.Text??"";updating=false;Status.Text="Competition created in memory. Review its rules, teams and calendar before saving.";};
        WorkspaceController.Open(page);
    }
    private void SaveClick(object sender,RoutedEventArgs e){var dialog=new OpenFolderDialog{Title="Parent folder for a new Compdata copy"};if(!ShellDialogs.Open(dialog,this))return;try{CommitTable();string folder=Path.Combine(dialog.FolderName,"compdata-"+Guid.NewGuid().ToString("N")[..8]);project.SaveAs(folder);Status.Text="Saved: "+folder;}catch(Exception ex){ShellDialogs.Message(this,ex.Message,"Compdata");}}
}
