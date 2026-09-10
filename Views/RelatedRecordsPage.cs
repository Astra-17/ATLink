using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ATLink.Core;

namespace ATLink.Views;
public sealed class RelatedRecordsPage : UserControl, IWorkspacePage
{
    public event Action<bool>? Closed;
    public static RelatedRecordsPage ForTeam(FootballCatalog catalog,string teamId,string name)=>new(catalog,name,new[]{"teamplayerlinks","formations","leagueteamlinks"},"teamid",teamId);
    public static RelatedRecordsPage ForLeague(FootballCatalog catalog,string leagueId,string name)=>new(catalog,name,new[]{"leagueteamlinks"},"leagueid",leagueId);
    public RelatedRecordsPage(FootballCatalog catalog,string title,IReadOnlyList<string> tables,string column,string id)
    {
        var dock=new DockPanel();
        StudioWindow.Style(this,dock);
        var back=new Button{Content="Back",HorizontalAlignment=HorizontalAlignment.Right};
        back.Click+=(_,_)=>Closed?.Invoke(false);
        var header=new Border{Background=StudioPalette.Get("PanelBrush"),Padding=new Thickness(16)};
        var bar=new DockPanel();DockPanel.SetDock(back,Dock.Right);bar.Children.Add(back);bar.Children.Add(new TextBlock{Text=title+" · Related records",FontSize=22,FontWeight=FontWeights.Bold,VerticalAlignment=VerticalAlignment.Center});
        header.Child=bar;DockPanel.SetDock(header,Dock.Top);dock.Children.Add(header);
        var tabs=new TabControl();
        foreach(string name in tables)
        {
            var table=catalog.Table(name);
            var view=new DataView(table.Data){RowFilter=$"[{column}] = '{id.Replace("'","''")}'"};
            var grid=new DataGrid{ItemsSource=TableOrdering.SortedRows(view,table).ToArray(),AutoGenerateColumns=true,IsReadOnly=true,CanUserAddRows=false};
            grid.MouseDoubleClick+=(_,_)=>{if(grid.SelectedItem is DataRowView selected)WorkspaceController.Open(new EntityEditor(catalog,table,selected.Row,name));};
            tabs.Items.Add(new TabItem{Header=name,Content=grid});
        }
        dock.Children.Add(tabs);
        Content=dock;
    }
}
