using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ATLink.Core;
using ATLink.Views;
internal static class PlayerCloneUiChecks
{
    public static void Run(string root){
        var app=new Application();app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("pack://application:,,,/ATLink;component/Themes/Studio.xaml")});
        var metadata=Path.Combine(root,"files/FC27/fifa_ng_db-meta.xml");
        var db=DatabaseDocument.Open(Path.Combine(root,"files/FC27/fifa_ng_db.db"),metadata);var catalog=new FootballCatalog(db);
        var source=catalog.Entities("players").First();
        PlayerEditing.Apply(catalog,source.Row,new Dictionary<string,string>(),new Dictionary<string,string>{{"firstname","Source"},{"surname","Test"},{"commonname","Source Test"},{"playerjerseyname","SOURCE"}});
        var original=source.Row.ItemArray.ToArray();int count=catalog.Rows("players").Count(),nameCount=catalog.Rows("editedplayernames").Count(),links=catalog.Rows("teamplayerlinks").Count();
        var page=new ModulesWindow(db);
        var method=typeof(ModulesWindow).GetMethod("CreatePlayerFromSelection",BindingFlags.Instance|BindingFlags.NonPublic)!;
        bool Create(Func<string,bool> confirm)=>(bool)method.Invoke(page,new object[]{source,confirm})!;
        string prompt="";
        if(Create(message=>{prompt=message;return false;})||!prompt.Contains("Source Test")||catalog.Rows("players").Count()!=count||catalog.Rows("editedplayernames").Count()!=nameCount)throw new Exception("Cancelled confirmation created player or omitted source name");
        if(!Create(_=>true))throw new Exception("Confirmed clone not created");
        var editor=((ContentControl)page.FindName("PlayerContent")).Content as PlayerEditor??throw new Exception("Clone opened generic editor");
        var added=catalog.Rows("players").Single(r=>r.RowState==System.Data.DataRowState.Added);
        string id=FootballCatalog.Value(added,"playerid");
        if(id==source.Id||catalog.Rows("players").Count()!=count+1||catalog.Rows("editedplayernames").Count()!=nameCount+1)throw new Exception("Clone identity or name copy failed");
        foreach(System.Data.DataColumn column in source.Row.Table.Columns)
            if(column.ColumnName!="playerid"&&!Equals(added[column],original[column.Ordinal]))throw new Exception("Clone lost field "+column.ColumnName);
        if(catalog.PlayerName(added)!="Source Test"||catalog.Rows("teamplayerlinks").Count()!=links)throw new Exception("Clone name or unrelated team links changed");
        LinkedUiRenderChecks.Render(page,"player-clone-editor");
        IEnumerable<T> Find<T>(DependencyObject d) where T:DependencyObject{
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(d);i++){var c=VisualTreeHelper.GetChild(d,i);if(c is T t)yield return t;foreach(var item in Find<T>(c))yield return item;}
        }
        var info=(PlayerInfoEditor)((ContentControl)editor.FindName("InfoHost")).Content;
        Find<TextBox>(info).Single(t=>Equals(t.Tag,"commonname")).Text="Clone Test";
        if(!editor.TryApply()||catalog.PlayerName(added)!="Clone Test"||catalog.PlayerName(source.Row)!="Source Test"||!source.Row.ItemArray.SequenceEqual(original))throw new Exception("Editing clone affected source");
        string path=Path.Combine(root,"Checks/output/player-clone-test.db");File.WriteAllBytes(path,db.Serialize());
        var reopened=new FootballCatalog(DatabaseDocument.Open(path,metadata));
        if(reopened.PlayerName(reopened.Rows("players").Single(r=>FootballCatalog.Value(r,"playerid")==id))!="Clone Test")throw new Exception("Clone lost after reopening");
        Console.WriteLine("PASS player cloning: named confirmation, cancel without writes, unique ID, all fields and edited names copied, direct PlayerEditor, independent edits and DB roundtrip");
    }
}
