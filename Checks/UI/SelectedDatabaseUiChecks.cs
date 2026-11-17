using System.IO;
using System.Windows;
using System.Windows.Controls;
using ATLink.Core;
using ATLink.Views;
internal static class SelectedDatabaseUiChecks
{
    public static void Run(string root)
    {
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("pack://application:,,,/ATLink;component/Themes/Studio.xaml")});
        var data=new StudioData(Path.Combine(root,"files"),game:"FC27");
        foreach(string file in new[]{"Squads20261107005754706","CmMgrC20261112203530317"}){
            var (db,loc)=data.OpenSelected(Path.Combine(root,"files/FC27",file),"eng_us");db.SetDisplayLocalization(loc);var catalog=new FootballCatalog(db);
            foreach(string language in new[]{"fr","en"}){
                UiText.Current.SetLanguage(language);var modules=new ModulesWindow(db,"players");LinkedUiRenderChecks.Render(modules,"selected-"+file+"-"+language);
                var notice=(TextBlock)modules.FindName("CareerNotice");if((notice.Visibility==Visibility.Visible)!=db.IsCareer)throw new Exception("Missing career export notice");
                if(db.IsCareer&&!notice.Text.Contains(language=="fr"?"Base carrière":"Career database"))throw new Exception("Career notice not translated");
                var editor=new PlayerEditor(catalog,catalog.Entities("players").First());LinkedUiRenderChecks.Render(editor,"selected-player-"+file+"-"+language);
                var team=new TeamWorkspace(catalog,catalog.Entities("teams").First());LinkedUiRenderChecks.Render(team,"selected-team-"+file+"-"+language);team.Dispose();
                if(db.HasChanges)throw new Exception("Opening selected save editors changed data");
            }
            Console.WriteLine("PASS selected save UI "+file+": player list, player and team editors, FR/EN notice, unchanged data");
        }
    }
}
