using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ATLink.Core;
using ATLink.Views;
internal static class PlayerAppearanceUiChecks
{
    public static void Run(string root)
    {
        var app=new Application();app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("pack://application:,,,/ATLink;component/Themes/Studio.xaml")});
        string data=Path.Combine(root,"files/FC27");var db=DatabaseDocument.Open(Path.Combine(data,"fifa_ng_db.db"),Path.Combine(data,"fifa_ng_db-meta.xml"));var catalog=new FootballCatalog(db);var player=catalog.Entities("players").First();var original=player.Row.ItemArray.ToArray();
        void Check(bool ok,string message){if(!ok)throw new Exception(message);}
        IEnumerable<T> Find<T>(DependencyObject d) where T:DependencyObject{for(int i=0;i<VisualTreeHelper.GetChildrenCount(d);i++){var child=VisualTreeHelper.GetChild(d,i);if(child is T t)yield return t;foreach(var n in Find<T>(child))yield return n;}}
        var editor=new PlayerEditor(catalog,player);var tabs=(TabControl)editor.FindName("EditorTabs");tabs.SelectedIndex=3;LinkedUiRenderChecks.Render(editor,"player-appearance");var appearance=Find<PlayerAppearanceEditor>(editor).Single();
        Check(!appearance.HasChanges,"Opening Appearance changed data");Check(appearance.EditableFields.Count==55,"Missing appearance fields: "+appearance.EditableFields.Count);
        var boxes=Find<TextBox>(appearance).Where(b=>b.Tag is string).ToDictionary(b=>(string)b.Tag,b=>b);
        string height=boxes["height"].Text;boxes["height"].Text="188";boxes["weight"].Text="invalid";Check(!editor.TryApply(),"Invalid appearance accepted");Check(player.Row.ItemArray.SequenceEqual(original),"Invalid appearance partially applied");
        appearance.Reset();Check(!appearance.HasChanges&&boxes["height"].Text==height,"Reset failed");boxes["height"].Text="188";boxes["weight"].Text="83";boxes["headassetid"].Text="1625";boxes["tattooleftarm"].Text="1";
        tabs.SelectedIndex=0;LinkedUiRenderChecks.Render(editor,"player-appearance-info-switch");Check(appearance.HasChanges,"Tab switch lost draft");tabs.SelectedIndex=3;Check(editor.TryApply(),"Appearance apply failed");Check(!appearance.HasChanges,"Applied appearance remained pending");
        Check(FootballCatalog.Value(player.Row,"height")=="188"&&FootballCatalog.Value(player.Row,"headassetid")=="1625","Appearance not applied");
        foreach(var field in catalog.Table("players").Fields.Where(f=>!new[]{"height","weight","headassetid","tattooleftarm"}.Contains(f.Name)))Check(Equals(player.Row[field.Name],original[player.Row.Table.Columns[field.Name]!.Ordinal]),"Unrelated field changed: "+field.Name);
        string output=Path.Combine(root,"Checks/output/player-appearance-test.db");File.WriteAllBytes(output,db.Serialize());var loaded=DatabaseDocument.Open(output,Path.Combine(data,"fifa_ng_db-meta.xml"));var row=new FootballCatalog(loaded).Rows("players").Single(r=>FootballCatalog.Value(r,"playerid")==player.Id);Check(FootballCatalog.Value(row,"height")=="188"&&FootballCatalog.Value(row,"tattooleftarm")=="1","Appearance roundtrip failed");
        Console.WriteLine("PASS Appearance: all fields, draft/reset, atomic validation, unchanged ratings, tab persistence, DB roundtrip and responsive renders");
    }
}
