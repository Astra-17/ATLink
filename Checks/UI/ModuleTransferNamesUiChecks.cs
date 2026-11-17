using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ATLink.Core;
using ATLink.Views;
internal static class ModuleTransferNamesUiChecks
{
    public static void Run(string root)
    {
        var app=new Application();app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("pack://application:,,,/ATLink;component/Themes/Studio.xaml")});SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
        void Check(bool ok,string message){if(!ok)throw new Exception(message);}
        IEnumerable<T> Find<T>(DependencyObject parent) where T:DependencyObject{for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var child=VisualTreeHelper.GetChild(parent,i);if(child is T found)yield return found;foreach(var item in Find<T>(child))yield return item;}}
        void Pump(){var frame=new DispatcherFrame();app.Dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);}
        var db=DatabaseDocument.Open(Path.Combine(root,"files/FC27/fifa_ng_db.db"),Path.Combine(root,"files/FC27/fifa_ng_db-meta.xml"));var catalog=new FootballCatalog(db);catalog.LoadNationalTeams(Path.Combine(root,"files/FC26_NATIONAL_TEAM_IDS.csv"));
        var enrich=EnrichedPlayers.Load();var links=catalog.Rows("teamplayerlinks").Where(r=>!catalog.NationalTeamIds.Contains(FootballCatalog.Value(r,"teamid"))).ToLookup(r=>FootballCatalog.Value(r,"playerid"));var loans=new[]{"playerloans","career_presignedcontract"}.SelectMany(catalog.Rows).Select(r=>FootballCatalog.Value(r,"playerid")).ToHashSet();
        var candidate=catalog.Entities("players").Select(p=>enrich.ById(p.Id)).First(p=>p is not null&&!loans.Contains(p.PlayerId)&&links[p.PlayerId].Count()==1&&enrich.FindByPersonName(p.TransfermarktName).Count==1)!;
        string oldId=FootballCatalog.Value(links[candidate.PlayerId].Single(),"teamid");var teams=catalog.Rows("teams").ToDictionary(r=>FootballCatalog.Value(r,"teamid"));string oldName=FootballCatalog.Value(teams[oldId],"teamname");string nextId=oldId=="1"?"5":"1",nextName=FootballCatalog.Value(teams[nextId],"teamname");
        foreach(string language in new[]{"fr","en"}){
            UiText.Current.SetLanguage(language);var modules=new ModulesWindow(db,"teams");var navigation=(StackPanel)modules.FindName("ModuleNavigation");
            Check(navigation.Children.OfType<Button>().Where(b=>b.Tag is "leagues" or "stadiums").All(b=>b.Visibility==Visibility.Collapsed&&!b.IsEnabled),"Hidden modules remain available in navigation");
            Check(((Button)modules.FindName("CreateButton")).Visibility==Visibility.Visible&&new[]{"EditButton","FormationButton","RelatedButton"}.All(name=>((Button)modules.FindName(name)).Visibility==Visibility.Collapsed),"Team toolbar contains unwanted actions");
            LinkedUiRenderChecks.Render(modules,"modules-teams-actions-"+language);
            Task<IReadOnlyList<MarketTransfer>> Fetch(IEnumerable<string> _,CancellationToken cancellation)=>Task.FromResult<IReadOnlyList<MarketTransfer>>([new(1,candidate.TransfermarktName,oldName,nextName,"")]);
            var transfers=new TransfersPage(catalog,Fetch);var pending=transfers.VerifyAsync();var clock=System.Diagnostics.Stopwatch.StartNew();while(!pending.IsCompleted){if(clock.Elapsed.TotalSeconds>45)throw new TimeoutException("Transfer verification timed out");Pump();}pending.GetAwaiter().GetResult();
            Check(transfers.Grid.Items.Count==1,"FC27 verification did not produce the expected transfer");LinkedUiRenderChecks.Render(transfers,"fc27-transfer-club-name-"+language);Pump();
            var destination=Find<ComboBox>(transfers.Grid).Single(c=>c.SelectedItem is ClubOption);var selected=(ClubOption)destination.SelectedItem;
            Check(selected.Id==nextId&&destination.Text==selected.Label&&destination.Text.Contains(nextName),"FC27 destination name is missing: "+destination.Text);
            Check(TextSearch.GetTextPath(destination)==nameof(ClubOption.Label),"Club label property path was translated");
            Check(!db.HasChanges,"Navigation or verification changed source data");Console.WriteLine("PASS module actions and FC27 destination club name "+language+" after Verify");
        }
    }
}
