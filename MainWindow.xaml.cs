using System.Data;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using ATLink.Core;
using ATLink.ViewModels;
using Microsoft.Win32;

namespace ATLink;
public partial class MainWindow : Window
{
    private readonly MainViewModel model=new();
    private readonly Views.WorkspaceController workspace;
    private readonly StudioData studioData=new(Path.Combine(AppContext.BaseDirectory,"Data"));
    public MainWindow()
    {
        InitializeComponent();DataContext=model;Closing+=OnClosing;model.PropertyChanged+=ModelChanged;
        workspace=new Views.WorkspaceController(model,this);model.ResetWorkspace=workspace.Clear;
        Loaded+=FirstLaunch;
    }
    private void FirstLaunch(object sender,RoutedEventArgs e)
    {
        Loaded-=FirstLaunch;
        try
        {
            if(studioData.NeedsLanguageChoice)
            {
                // An installer choice is an explicit default; portable launches use the welcome page.
                string installed=Path.Combine(studioData.Root,"default-language.txt");
                if(File.Exists(installed)&&studioData.Languages.Any(l=>l.Code==File.ReadAllText(installed).Trim()))studioData.SaveLanguage(File.ReadAllText(installed).Trim());
                else OpenSettings(true);
            }
        }
        catch(Exception ex){Error(ex);}
    }
    private void EditingClick(object sender,RoutedEventArgs e)
    {
        if(model.Busy)return;
        workspace.Clear();model.Screen="Editing";
    }
    private void HomeClick(object sender,RoutedEventArgs e)
    {
        if(model.Busy)return;
        workspace.Clear();model.Screen="Editing";
    }
    private void SettingsClick(object sender,RoutedEventArgs e)
    {
        if(model.Busy)return;
        try{OpenSettings(false);}catch(Exception ex){Error(ex);}
    }
    private void OpenSettings(bool initial)
    {
        OpenPage(new Views.SettingsPage(studioData,initial,async code=>
        {
            if(model.Localization?.HasChanges==true&&MessageBox.Show(this,"Abandonner les modifications de la langue actuelle ? Les modifications de la DB seront conservées.","Changer de langue",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)throw new OperationCanceledException("La langue actuelle a été conservée.");
            model.Busy=true;
            try
            {
                var localization=await Task.Run(()=>studioData.OpenLocalization(code));
                studioData.SaveLanguage(code);
                if(model.Document is not null)model.SetLocalization(localization);
                model.Status="Database language: "+studioData.Languages.Single(l=>l.Code==code).Name;
            }
            finally{model.Busy=false;}
        }));
    }
    private async void BaseDbClick(object sender,RoutedEventArgs e)
    {
        if(model.Busy||!MayDiscard())return;
        try
        {
            if(studioData.NeedsLanguageChoice){OpenSettings(true);return;}
            model.Busy=true;model.Status="Opening the included database…";
            string code=studioData.SuggestedLanguage().Code;
            var result=await Task.Run(()=>studioData.OpenBase(code));
            model.Load(result.Main,result.Loc);OpenPlayers();
        }
        catch(Exception ex){Error(ex);}
        finally{model.Busy=false;}
    }
    private void PlayersHomeClick(object sender,RoutedEventArgs e)=>OpenPlayers();
    private void OpenPlayers()
    {
        if(model.Document is null)return;
        workspace.Clear();model.Screen="Editing";
        var page=new Views.ModulesWindow(model.Document,"players");
        page.DatabaseRequested+=()=>{workspace.Clear();model.Screen="Editing";};
        page.TablesRequested+=()=>{workspace.Clear();model.ShowTablesCommand.Execute(null);};
        page.SettingsRequested+=()=>OpenSettings(false);
        OpenPage(page);
    }
    private void OpenPage(UserControl page)=>workspace.Push(page);
    private void HashClick(object sender,RoutedEventArgs e)=>OpenPage(new Views.LanguageHashWindow());
    private bool MayDiscard()=>model.Document?.HasChanges!=true && model.Localization?.HasChanges!=true || MessageBox.Show(this,"Abandonner les modifications en mémoire ?","Modifications non enregistrées",MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes;
    private void SaveClick(object sender,RoutedEventArgs e)
    {
        if(model.Busy || model.Document is null)return;
        if(!TableGrid.CommitEdit(DataGridEditingUnit.Cell,true)||!TableGrid.CommitEdit(DataGridEditingUnit.Row,true))return;
        if(model.Localization?.HasChanges==true){try{SaveFullProject();}catch(Exception ex){Error(ex);}return;}
        var save=new SaveFileDialog{Title="Enregistrer dans un nouveau fichier",Filter="Base de données (*.db)|*.db",FileName=Path.GetFileNameWithoutExtension(model.Document.SourcePath)+"-edited.db"};
        if(save.ShowDialog(this)!=true)return;
        try{model.Document.SaveAs(save.FileName);model.Status=$"Copie enregistrée : {save.FileName}. L'original reste inchangé.";}catch(Exception ex){Error(ex);}
    }
    private void RevertClick(object sender,RoutedEventArgs e){if(model.Busy || !MayDiscard())return;TableGrid.CancelEdit();TableGrid.CancelEdit(DataGridEditingUnit.Row);model.Revert();}
    private void ExportClick(object sender,RoutedEventArgs e)
    {
        if(model.Busy || model.SelectedTable is null)return;
        var save=new SaveFileDialog{Filter="CSV (*.csv)|*.csv",FileName=model.SelectedTable.Name+".csv"};if(save.ShowDialog(this)!=true)return;
        try
        {
            static string Quote(object x)=>"\""+(x.ToString()??"").Replace("\"","\"\"")+"\"";
            using var writer=new StreamWriter(save.FileName,false,new UTF8Encoding(true));
            var data=model.SelectedTable.Data;writer.WriteLine(string.Join(",",data.Columns.Cast<DataColumn>().Select(c=>Quote(c.ColumnName))));
            var id=TableOrdering.IdColumns(model.SelectedTable);
            IEnumerable<DataRow> rows=data.Rows.Cast<DataRow>().Where(r=>r.RowState!=DataRowState.Deleted);
            if(id.Count>0)rows=rows.OrderBy(r=>TableOrdering.NumericId(r[id[0]]?.ToString())).ThenBy(r=>id.Count>1?TableOrdering.NumericId(r[id[1]]?.ToString()):0);
            foreach(DataRow row in rows)writer.WriteLine(string.Join(",",row.ItemArray.Select(x=>Quote(x??""))));
            model.Status="Table exportée en CSV.";
        }catch(Exception ex){Error(ex);}
    }
    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedTable))
        {
            TableGrid.Columns.Clear();
            if (model.SelectedTable is not null)
            {
                var number = new DataGridCheckBoxColumn { Header = "#", Width = 44, MinWidth = 44, CanUserSort = false, Binding = new Binding("IsSelected") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(DataGridRow), 1) } };
                TableGrid.Columns.Add(number);
                TableGrid.FrozenColumnCount = 1;
                foreach (var field in model.SelectedTable.Fields)
                {
                    bool readOnly = false;
                    TableGrid.Columns.Add(new DataGridTextColumn
                    {
                        Header = field.Name, SortMemberPath = field.Name, IsReadOnly = readOnly,
                        Binding = new Binding($"[{field.Name}]") { Mode = readOnly ? BindingMode.OneWay : BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus, ValidatesOnExceptions = true },
                        Width = Math.Clamp(field.Name.Length * 7 + 22, 132, 280)
                    });
                }
            }
        }
        if (e.PropertyName is nameof(MainViewModel.SelectedTable) or nameof(MainViewModel.ActiveSortColumn) or nameof(MainViewModel.ActiveSortAscending))
        {
            foreach (var column in TableGrid.Columns)
                column.SortDirection = column.SortMemberPath == model.ActiveSortColumn ? (model.ActiveSortAscending ? ListSortDirection.Ascending : ListSortDirection.Descending) : null;
        }
    }
    private void CloseClick(object sender, RoutedEventArgs e) { if (!model.Busy && MayDiscard()) model.Close(); }
    private void CopyClick(object sender, RoutedEventArgs e)
    {
        if(model.SelectedTable is null)return;
        var rows=TableGrid.SelectedItems.Cast<DataRowView>().ToArray();if(rows.Length==0)return;
        static string Quote(string value)=>"\""+value.Replace("\"","\"\"")+"\"";
        var lines=new List<string>{string.Join('\t',model.SelectedTable.Fields.Select(f=>Quote(f.Name)))};
        lines.AddRange(rows.Select(r=>string.Join('\t',model.SelectedTable.Fields.Select(f=>Quote(r[f.Name].ToString()??"")))));
        Clipboard.SetText(string.Join(Environment.NewLine,lines));model.Status=$"Copied {rows.Length} rows with headers.";
    }
    private void CountClick(object sender, RoutedEventArgs e) => model.Status = $"{model.Rows?.Count ?? 0:N0} matching rows · {TableGrid.SelectedItems.Count:N0} selected";
    private void GoToColumnClick(object sender, RoutedEventArgs e)
    {
        var column = TableGrid.Columns.FirstOrDefault(c => c.SortMemberPath == model.SearchColumn);
        if (column is not null && TableGrid.Items.Count > 0) TableGrid.ScrollIntoView(TableGrid.Items[0], column);
    }
    private void FindClick(object sender, RoutedEventArgs e)
    {
        if (TableGrid.Items.Count > 0) { TableGrid.SelectedIndex = 0; GoToColumnClick(sender, e); TableGrid.Focus(); }
        else model.Status = "No matching rows.";
    }
    private void GridSorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        if (!TableGrid.CommitEdit(DataGridEditingUnit.Cell, true) || !TableGrid.CommitEdit(DataGridEditingUnit.Row, true)) return;
        bool ascending = e.Column.SortDirection != ListSortDirection.Ascending;
        foreach (var column in TableGrid.Columns) column.SortDirection = null;
        model.Sort(e.Column.SortMemberPath, ascending);
        e.Column.SortDirection = ascending ? ListSortDirection.Ascending : ListSortDirection.Descending;
    }
    private void ModulesClick(object sender,RoutedEventArgs e)
    {
        if(model.Document is null)return;
        try
        {
            var modules=new ATLink.Views.ModulesWindow(model.Document,((sender as Button)?.Tag as string)??"players");
            modules.Closed+=_=>{model.RefreshPage();model.Screen="Editing";};
            modules.DatabaseRequested+=()=>{workspace.Clear();model.Screen="Editing";};
            modules.TablesRequested+=()=>{workspace.Clear();model.ShowTablesCommand.Execute(null);};
            modules.SettingsRequested+=()=>OpenSettings(false);
            OpenPage(modules);
        }catch(Exception ex){Error(ex);}
    }
    private void NewClick(object sender,RoutedEventArgs e)
    {
        if(model.SelectedTable is null)return;
        try{TableEditing.Add(model.SelectedTable,(TableGrid.SelectedItem as DataRowView)?.Row);model.RefreshPage();model.Status="New row added.";}catch(Exception ex){Error(ex);}
    }
    private void DeleteClick(object sender,RoutedEventArgs e)
    {
        if(model.SelectedTable is null||model.Document is null)return;
        var rows=TableGrid.SelectedItems.Cast<DataRowView>().Select(r=>r.Row).ToArray();if(rows.Length==0)return;
        try{new FootballCatalog(model.Document).ValidateDelete(model.SelectedTable,rows);if(MessageBox.Show(this,$"Delete {rows.Length} rows?","Delete",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;foreach(var row in rows)row.Delete();model.RefreshPage();}catch(Exception ex){Error(ex);}
    }
    private void ImportClick(object sender,RoutedEventArgs e)
    {
        if(model.SelectedTable is null)return;
        var dialog=new OpenFileDialog{Filter="Tables (*.csv;*.tsv;*.txt)|*.csv;*.tsv;*.txt"};if(dialog.ShowDialog(this)!=true)return;
        try
        {
            string text=File.ReadAllText(dialog.FileName);char delimiter=text.Split('\n')[0].Contains('\t')?'\t':',';
            var preview=TableEditing.PreviewImport(model.SelectedTable,text,delimiter);
            if(MessageBox.Show(this,$"Replace {model.SelectedTable.RowCount} rows in {model.SelectedTable.Name} with {preview.Count} validated rows?","Import preview",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
            TableEditing.Replace(model.SelectedTable,preview);model.RefreshPage();model.Status=$"Imported {preview.Count} rows. Pending save.";
        }catch(Exception ex){Error(ex);}
    }
    private void ExportAllClick(object sender,RoutedEventArgs e)
    {
        if(model.Document is null)return;
        var dialog=new OpenFolderDialog{Title="Export all tables"};if(dialog.ShowDialog(this)!=true)return;
        try
        {
            var folder=Path.Combine(dialog.FolderName,"ATLink-export-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..6]);Directory.CreateDirectory(folder);
            foreach(var table in model.Document.Tables)File.WriteAllText(Path.Combine(folder,table.Name+".csv"),TableEditing.Export(table),new UTF8Encoding(true));
            model.Status="Exported all tables: "+folder;
        }catch(Exception ex){Error(ex);}
    }
    private void PasteClick(object sender,RoutedEventArgs e)
    {
        if(model.SelectedTable is null)return;
        try
        {
            string text=Clipboard.GetText();var rows=TableEditing.PreviewImport(model.SelectedTable,text,'\t');
            if(MessageBox.Show(this,$"Append {rows.Count} clipboard rows? Unique keys must be supplied.","Paste preview",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
            var added=new List<DataRow>();try{foreach(var row in rows)added.Add(model.SelectedTable.Data.Rows.Add(row.Cast<object>().ToArray()));DatabaseDocument.ValidateKeys(model.SelectedTable);}catch{foreach(var row in added)row.Delete();throw;}
            model.RefreshPage();
        }catch(Exception ex){Error(ex);}
    }
    private void ReplaceClick(object sender,RoutedEventArgs e)
    {
        if(model.SelectedTable is null||model.SearchColumn is null)return;
        var rows=TableGrid.SelectedItems.Cast<DataRowView>().Select(v=>v.Row).ToArray();
        if(rows.Length==0){model.Status="Select rows to edit in bulk.";return;}
        var input=new TextBox{Text=rows[0][model.SearchColumn].ToString()??"",Margin=new Thickness(0,0,0,12)};
        var page=new Views.ActionPage($"Replace {model.SearchColumn} · {rows.Length} rows",input,"Apply to selected rows",p=>{try{TableEditing.SetValues(model.SelectedTable,rows,model.SearchColumn,input.Text);p.Finish(true);}catch(Exception ex){Views.ShellDialogs.Message(p,ex.Message,"Invalid value");}});
        page.Closed+=ok=>{if(ok){model.RefreshPage();model.Status=$"Updated {rows.Length} rows.";}};
        OpenPage(page);
    }
    private void CompdataClick(object sender,RoutedEventArgs e)
    {
        var dialog=new OpenFolderDialog{Title="Open Compdata folder"};if(dialog.ShowDialog(this)!=true)return;
        try{OpenPage(new ATLink.Views.CompdataWindow(CompdataProject.Open(dialog.FolderName)));}catch(Exception ex){Error(ex);}
    }
    private void BigClick(object sender,RoutedEventArgs e)
    {
        var file=new OpenFileDialog{Filter="BIG archive (*.big)|*.big"};if(file.ShowDialog(this)!=true)return;
        var folder=new OpenFolderDialog{Title="Parent folder for extraction"};if(folder.ShowDialog(this)!=true)return;
        try{string target=Path.Combine(folder.FolderName,"BIG-"+Guid.NewGuid().ToString("N")[..8]);var entries=BigArchive.Extract(file.FileName,target);model.Status=$"{entries.Count} files extracted to {target}; {entries.Count(e=>e.Compressed)} compressed payloads remain compressed.";MessageBox.Show(this,model.Status,"BIG extraction");}catch(Exception ex){Error(ex);}
    }
    private void LocalizationClick(object sender,RoutedEventArgs e)=>OpenPage(new Views.LocalizationWindow());
    private async void SquadClick(object sender,RoutedEventArgs e)
    {
        if(model.Busy||!MayDiscard())return;
        try{if(studioData.NeedsLanguageChoice){OpenSettings(true);return;}}catch(Exception ex){Error(ex);return;}
        var file=new OpenFileDialog{Title="Ouvrir un Squad FBCHUNKS",Filter="Squad|*|Tous|*.*"};if(file.ShowDialog(this)!=true)return;
        try
        {
            if(studioData.NeedsLanguageChoice){OpenSettings(true);return;}
            model.Busy=true;model.Status="Lecture du conteneur Squad…";
            string code=studioData.SuggestedLanguage().Code;
            var result=await Task.Run(()=>studioData.OpenSquad(file.FileName,code));
            model.Load(result.Main,result.Loc);OpenPlayers();
        }
        catch(Exception ex){Error(ex);}finally{model.Busy=false;}
    }
    private void SaveFullProject()
    {
        if(model.Document is null||model.Localization is null)return;
        var dialog=new OpenFolderDialog{Title="Parent folder for a new DB + LOC copy"};if(dialog.ShowDialog(this)!=true)return;
        byte[] main=model.Document.Serialize(),loc=model.Localization.Serialize();
        string target=Path.Combine(dialog.FolderName,"ATLink-project-"+Guid.NewGuid().ToString("N")[..8]);Directory.CreateDirectory(target);
        File.WriteAllBytes(Path.Combine(target,Path.GetFileName(model.Document.SourcePath)),main);
        string locName=Path.GetFileName(model.Localization.SourcePath);if(locName==Path.GetFileName(model.Document.SourcePath))locName="localization-"+locName;
        File.WriteAllBytes(Path.Combine(target,locName),loc);model.Status="DB and LOC saved: "+target;
    }    private void ImportAllClick(object sender,RoutedEventArgs e)
    {
        if(model.Document is null)return;
        var dialog=new OpenFolderDialog{Title="Import matching CSV tables"};if(dialog.ShowDialog(this)!=true)return;
        try
        {
            var previews=new List<(DatabaseTable Table,IReadOnlyList<string[]> Rows)>();
            foreach(var table in model.Document.Tables){string path=Path.Combine(dialog.FolderName,table.Name+".csv");if(File.Exists(path))previews.Add((table,TableEditing.PreviewImport(table,File.ReadAllText(path))));}
            if(previews.Count==0){model.Status="No matching CSV files.";return;}
            if(MessageBox.Show(this,$"Replace {previews.Count} tables with {previews.Sum(p=>p.Rows.Count):N0} validated rows? Changes stay in memory until save.","Bulk import preview",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
            foreach(var preview in previews)TableEditing.Replace(preview.Table,preview.Rows);model.RefreshPage();model.Status=$"Imported {previews.Count} tables.";
        }catch(Exception ex){Error(ex);}
    }    private void OnClosing(object? sender,System.ComponentModel.CancelEventArgs e){if(model.Busy || !MayDiscard())e.Cancel=true;}
    private void Error(Exception ex){model.Status=ex.Message;MessageBox.Show(this,ex.Message,"ATLink",MessageBoxButton.OK,MessageBoxImage.Warning);}
}






