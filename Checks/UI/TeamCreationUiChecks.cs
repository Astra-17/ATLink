using System.Data;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ATLink.Core;
using ATLink.Views;
using ATLink.ViewModels;
internal static class TeamCreationUiChecks
{
    public static void Run(string root)
    {
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("pack://application:,,,/ATLink;component/Themes/Studio.xaml")});
        const BindingFlags hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        void Check(bool ok,string message){if(!ok)throw new Exception(message);}
        IEnumerable<T> Find<T>(DependencyObject parent) where T:DependencyObject{for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var child=VisualTreeHelper.GetChild(parent,i);if(child is T value)yield return value;foreach(var nested in Find<T>(child))yield return nested;}}
        foreach(var (file,meta,label) in new[]{("files/fifa_ng_db.db","files/fifa_ng_db-meta.xml","fc26"),("files/FC27/fifa_ng_db.db","files/FC27/fifa_ng_db-meta.xml","fc27"),("files/FC27/Squads20261031154004222","files/FC27/fifa_ng_db-meta.xml","squad")}){
            var db=DatabaseDocument.Open(Path.Combine(root,file),Path.Combine(root,meta));var catalog=new FootballCatalog(db);var source=catalog.Rows("teams").Single(r=>FootballCatalog.Value(r,"teamid")=="1");var originalKits=TeamCreation.KitRows(catalog,source).ToDictionary(r=>r,r=>r.ItemArray.ToArray());
            var model=new MainViewModel();var shell=new Window();var controller=new WorkspaceController(model,shell);var modules=new ModulesWindow(db,"teams");controller.Push(modules);
            TeamWorkspace Create(){var results=(ListBox)modules.FindName("Results");results.SelectedItem=results.Items.Cast<EntityItem>().Single(t=>t.Id=="1");typeof(ModulesWindow).GetMethod("CreateClick",hidden)!.Invoke(modules,new object[]{modules,new RoutedEventArgs()});return model.Page as TeamWorkspace??throw new Exception("Team creation did not open editor");}
            int originalCount=catalog.Rows("teams").Count();var abandoned=Create();string abandonedId=catalog.Rows("teams").Single(r=>r.RowState==DataRowState.Added).Field<string>("teamid")!;controller.Clear();Check(catalog.Rows("teams").Count()==originalCount&&!catalog.Rows("teamkits").Any(r=>FootballCatalog.Value(r,"teamtechid")==abandonedId),"Workspace abandonment did not roll back creation");
            modules=new ModulesWindow(db,"teams");controller.Push(modules);var page=Create();string id=catalog.Rows("teams").Single(r=>r.RowState==DataRowState.Added).Field<string>("teamid")!;
            foreach(string language in new[]{"fr","en"}){
                UiText.Current.SetLanguage(language);
                foreach(string section in new[]{"Information","Club","Squad","Tactics","Appearance","Stadium"}){typeof(TeamWorkspace).GetMethod("Show",hidden)!.Invoke(page,new object[]{section});LinkedUiRenderChecks.Render(page,"team-created-"+label+"-"+language+"-"+section);}
            }
            typeof(TeamWorkspace).GetMethod("Show",hidden)!.Invoke(page,new object[]{"Appearance"});page.UpdateLayout();
            var kits=Find<ListBox>(page).Single(b=>Equals(b.Tag,"appearance-kit-list"));Check(kits.Items.Count==originalKits.Count,"Creation did not copy all kits");
            Find<TextBox>(page).Single(b=>Equals(b.Tag,"teamcolorprimr")).Text="19";
            typeof(TeamWorkspace).GetMethod("Show",hidden)!.Invoke(page,new object[]{"Information"});page.UpdateLayout();var teamName=Find<TextBox>(page).Single(b=>Equals(b.Tag,"teamname"));string initialName=teamName.Text;UiText.Current.SetLanguage("fr");Check(teamName.Text==initialName,"Business team name changed language");teamName.Text="Independent Test Club";
            Check((bool)typeof(TeamWorkspace).GetMethod("ApplyEdits",hidden)!.Invoke(page,null)!,"New team failed Apply");controller.Pop();Check(catalog.Rows("teams").Any(r=>FootballCatalog.Value(r,"teamid")==id),"Validated team was removed on leaving");
            foreach(var pair in originalKits)Check(pair.Key.ItemArray.SequenceEqual(pair.Value),"Editing created team changed source kit");
            string output=Path.Combine(root,"Checks/output/team-created-ui-"+label+"-"+Guid.NewGuid().ToString("N")+(db.IsSquad?".squad":".db"));db.SaveAs(output);var saved=new FootballCatalog(DatabaseDocument.Open(output,Path.Combine(root,meta)));Check(FootballCatalog.Value(saved.Rows("teams").Single(r=>FootballCatalog.Value(r,"teamid")==id),"teamname")=="Independent Test Club","Created team name lost");
            controller.Clear();shell.Close();WorkspaceController.Current=null;Console.WriteLine("PASS team creation UI "+label+": real Create action, editor tabs, abandonment rollback, Apply commit, independent kit edits and file roundtrip");
        }
    }
}
