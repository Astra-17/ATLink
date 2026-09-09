using System.Data;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ATLink.Core;
using Microsoft.Win32;
namespace ATLink.Views;
public sealed class LocalizationWindow:UserControl,IWorkspacePage
{
    public event Action<bool>? Closed;
    public LocalizationWindow()
    {
        var dock=new DockPanel{Margin=new Thickness(16)};Content=dock;StudioWindow.Style(this,dock);
        var bar=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,10)};DockPanel.SetDock(bar,Dock.Top);dock.Children.Add(bar);
        var back=new Button{Content="Back",Margin=new Thickness(0,0,8,0),Padding=new Thickness(12,6,12,6)};bar.Children.Add(back);back.Click+=(_,_)=>Closed?.Invoke(false);
        var status=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)};DockPanel.SetDock(status,Dock.Bottom);dock.Children.Add(status);
        var table=new DataTable();table.Columns.Add("hashid");table.Columns.Add("stringid");table.Columns.Add("sourcetext");
        var grid=new DataGrid{ItemsSource=IdSortedView.Bind(table.DefaultView),AutoGenerateColumns=true,CanUserAddRows=true,CanUserDeleteRows=true};dock.Children.Add(grid);
        void Load(IReadOnlyList<string[]> rows){table.Clear();foreach(var row in rows.ById(r=>r[0]))table.Rows.Add(row[0],row[1],row[2]);status.Text=$"{table.Rows.Count} chaînes. La langue intégrée se choisit dans System ; ses tables sont disponibles dans Table Editor.";}
        var open=new Button{Content="Importer CSV/TSV FIFA Editor Tool",Margin=new Thickness(0,0,8,0),Padding=new Thickness(12,6,12,6)};bar.Children.Add(open);
        open.Click+=(_,_)=>{var dialog=new OpenFileDialog{Filter="Chaînes (*.csv;*.tsv;*.txt)|*.csv;*.tsv;*.txt"};if(!ShellDialogs.Open(dialog,this))return;try{Load(LocalizationFormat.ParseStrings(File.ReadAllText(dialog.FileName)));}catch(Exception ex){status.Text=ex.Message;}};
        var hash=new Button{Content="Recalculer les hash",Margin=new Thickness(0,0,8,0)};bar.Children.Add(hash);
        hash.Click+=(_,_)=>{foreach(DataRow row in table.Rows){if(row.RowState==DataRowState.Deleted)continue;string key=row["stringid"]?.ToString()??"";if(key.Length>0)row["hashid"]=unchecked((int)LanguageHash.Compute(key)).ToString();}status.Text="Hash recalculés (CRC FIFA, identifiants signés).";};
        var save=new Button{Content="Exporter CSV"};bar.Children.Add(save);
        save.Click+=(_,_)=>
        {
            if(!grid.CommitEdit(DataGridEditingUnit.Cell,true)||!grid.CommitEdit(DataGridEditingUnit.Row,true))return;
            var dialog=new SaveFileDialog{Filter="CSV (*.csv)|*.csv",FileName="localization.csv"};if(!ShellDialogs.Open(dialog,this))return;
            var rows=table.Rows.Cast<DataRow>().Where(r=>r.RowState!=DataRowState.Deleted).Select(r=>new[]{r["hashid"]?.ToString()??"",r["stringid"]?.ToString()??"",r["sourcetext"]?.ToString()??""});
            File.WriteAllText(dialog.FileName,LocalizationFormat.Export(rows));status.Text="Exporté : "+dialog.FileName;
        };
        status.Text="CSV/TSV de LanguageStrings. La langue de la DB est chargée automatiquement et se choisit dans System.";
    }
}
