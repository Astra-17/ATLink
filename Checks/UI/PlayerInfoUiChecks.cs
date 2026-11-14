using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ATLink.Core;
using ATLink.Views;
internal static class PlayerInfoUiChecks
{
    public static void Run(string root)
    {
        var app=new Application();app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("pack://application:,,,/ATLink;component/Themes/Studio.xaml")});
        string data=Path.Combine(root,"files/FC27");var db=DatabaseDocument.Open(Path.Combine(data,"fifa_ng_db.db"),Path.Combine(data,"fifa_ng_db-meta.xml"));var catalog=new FootballCatalog(db);var player=catalog.Entities("players").First();var original=player.Row.ItemArray.ToArray();
        void Check(bool v,string m){if(!v)throw new Exception(m);}
        IEnumerable<T> Find<T>(DependencyObject d) where T:DependencyObject{for(int i=0;i<VisualTreeHelper.GetChildrenCount(d);i++){var c=VisualTreeHelper.GetChild(d,i);if(c is T t)yield return t;foreach(var n in Find<T>(c))yield return n;}}
        var editor=new PlayerEditor(catalog,player);LinkedUiRenderChecks.Render(editor,"player-info");var info=Find<PlayerInfoEditor>(editor).Single();Check(!info.HasChanges,"Opening Info changed values");
        var boxes=Find<TextBox>(info).Where(b=>b.Tag is string).ToDictionary(b=>(string)b.Tag,b=>b);
        boxes["wage"].Text="12345";Check(info.HasChanges&&FootballCatalog.Value(player.Row,"wage")!="12345","Wage did not stay in draft");boxes["birthdate"].Text="2025-02-30";Check(!editor.TryApply(),"Invalid date accepted");Check(player.Row.ItemArray.SequenceEqual(original),"Invalid apply partially changed player");
        boxes["birthdate"].Text="2003-12-06";boxes["role1"].Text="1";Check(editor.TryApply(),"Valid Info apply failed");Check(FootballCatalog.Value(player.Row,"wage")=="12345"&&FifaDate.ToIso(FootballCatalog.Value(player.Row,"birthdate"))=="2003-12-06","Info changes not saved");Check(!info.HasChanges,"Applied draft still pending");
        string path=Path.Combine(root,"Checks/output/player-info-test.db");File.WriteAllBytes(path,db.Serialize());var reopened=DatabaseDocument.Open(path,Path.Combine(data,"fifa_ng_db-meta.xml"));Check(new FootballCatalog(reopened).Rows("players").Any(r=>FootballCatalog.Value(r,"playerid")==player.Id&&FootballCatalog.Value(r,"wage")=="12345"),"Info lost after reopen");
        Console.WriteLine("PASS player Info: draft isolation, invalid-date atomicity, apply, DB roundtrip and 1400/960 renders");
    }
}
