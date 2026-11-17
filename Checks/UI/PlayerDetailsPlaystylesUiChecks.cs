using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using ATLink.Core;
using ATLink.Views;
internal static class PlayerDetailsPlaystylesUiChecks
{
    public static void Run(string root){
        var app=new Application();app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("pack://application:,,,/ATLink;component/Themes/Studio.xaml")});
        var db=DatabaseDocument.Open(Path.Combine(root,"files/FC27/fifa_ng_db.db"),Path.Combine(root,"files/FC27/fifa_ng_db-meta.xml"));var catalog=new FootballCatalog(db);
        var player=catalog.Entities("players").First();
        player.Row["trait1"]="3";player.Row["icontrait1"]="2";player.Row["trait2"]="65568";player.Row["icontrait2"]="32";
        var original=player.Row.ItemArray.ToArray();
        PlayerBrowser.PlayerCard Card()=>new(player,catalog.NationNames());
        var detail=new PlayerDetails{DataContext=Card(),Width=500};
        var panel=(WrapPanel)detail.FindName("PlaystylesPanel");var empty=(TextBlock)detail.FindName("NoPlaystyles");
        if(panel.Children.Count!=3||empty.Visibility!=Visibility.Collapsed)throw new Exception("Wrong active style count");
        var images=panel.Children.Cast<Image>().ToArray();
        if(!((BitmapImage)images[0].Source).UriSource.ToString().EndsWith("-white.png")||images.Skip(1).Any(i=>!((BitmapImage)i.Source).UriSource.ToString().EndsWith("-gold.png")))throw new Exception("Wrong style colors or plus precedence");
        LinkedUiRenderChecks.Render(detail,"player-details-playstyles");
        UiText.Current.SetLanguage("fr");detail.DataContext=Card();LinkedUiRenderChecks.Render(detail,"player-details-playstyles-fr");
        if(!((string)((Image)panel.Children[0]).ToolTip).Contains("Tir en finesse"))throw new Exception("Missing French playstyle name");
        if(!player.Row.ItemArray.SequenceEqual(original))throw new Exception("Details wrote player data");
        foreach(var field in new[]{"trait1","trait2","icontrait1","icontrait2"})player.Row[field]="0";
        detail.DataContext=Card();if(panel.Children.Count!=0||empty.Visibility!=Visibility.Visible)throw new Exception("Empty player retained styles");
        detail.DataContext=null;if(panel.Children.Count!=0)throw new Exception("Stale selection icons");
        foreach(string language in new[]{"fr","en"}){
            UiText.Current.SetLanguage(language);var browser=new PlayerBrowser(catalog);
            LinkedUiRenderChecks.Render(browser,"player-browser-age-position-"+language);
            IEnumerable<T> Find<T>(DependencyObject d) where T:DependencyObject{
                for(int i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(d);i++){var child=System.Windows.Media.VisualTreeHelper.GetChild(d,i);if(child is T value)yield return value;foreach(var nested in Find<T>(child))yield return nested;}
            }
            var grid=Find<DataGrid>(browser).Single();
            foreach(string property in new[]{"Age","Position"}){
                var column=grid.Columns.Single(c=>c.SortMemberPath==property);
                if(property=="Age"&&(column is not DataGridTextColumn textColumn||textColumn.Binding is not System.Windows.Data.Binding binding||binding.Path.Path!=property))throw new Exception("Localized player-column binding");
                if(property=="Position"&&column is not DataGridTemplateColumn)throw new Exception("Missing position badge template");
            }
            var first=(PlayerBrowser.PlayerCard)grid.Items[0];
            if(first.Age is null||string.IsNullOrWhiteSpace(first.Position))throw new Exception("Player age or primary position missing");
        }
        Console.WriteLine("PASS PlayerDetails: active styles, plus precedence, goalkeeper bits, unknown bits ignored, empty/changed selection, translated names and read-only rendering");
    }
}
