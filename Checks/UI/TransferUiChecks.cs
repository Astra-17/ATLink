using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ATLink.Core;
using ATLink.Views;

internal static class TransferUiChecks
{
    static void Require(bool value,string message){if(!value)throw new Exception(message);}
    public static void Run(string root)
    {
        var app=new Application();
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
        void Pump()
        {
            var frame=new DispatcherFrame();
            app.Dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));
            Dispatcher.PushFrame(frame);
        }
        void Complete(Task task)
        {
            var timeout=System.Diagnostics.Stopwatch.StartNew();
            while(!task.IsCompleted)
            {
                if(timeout.Elapsed>TimeSpan.FromSeconds(45))throw new TimeoutException("UI verification timed out.");
                Pump();
            }
            task.GetAwaiter().GetResult();
        }
        var doc=DatabaseDocument.Open(Path.Combine(root,"files/fifa_ng_db.db"),Path.Combine(root,"files/fifa_ng_db-meta.xml"));
        var catalog=new FootballCatalog(doc);catalog.LoadNationalTeams(Path.Combine(root,"files/FC26_NATIONAL_TEAM_IDS.csv"));
        var names=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"files/players_enrich.json")));
        var loans=new[]{"playerloans","career_presignedcontract"}.SelectMany(catalog.Rows).Select(r=>FootballCatalog.Value(r,"playerid")).ToHashSet();
        var links=catalog.Rows("teamplayerlinks").Where(r=>!catalog.NationalTeamIds.Contains(FootballCatalog.Value(r,"teamid"))).ToLookup(r=>FootballCatalog.Value(r,"playerid"));
        var enriched=EnrichedPlayers.Load();
        var candidate=names.RootElement.GetProperty("players").EnumerateArray().Select(p=>enriched.ById(p.GetProperty("playerid").ToString()))
            .First(p=>p is not null&&!loans.Contains(p.PlayerId)&&links[p.PlayerId].Count()==1&&enriched.FindByPersonName(p.TransfermarktName).Count==1)!;
        string initial=FootballCatalog.Value(links[candidate.PlayerId].Single(),"teamid");
        var teams=catalog.Rows("teams").Where(r=>!catalog.NationalTeamIds.Contains(FootballCatalog.Value(r,"teamid"))).ToArray();
        string firstName=FootballCatalog.Value(teams.First(r=>FootballCatalog.Value(r,"teamid")==initial),"teamname");
        var next=teams.First(r=>FootballCatalog.Value(r,"teamid")!=initial);
        string nextName=FootballCatalog.Value(next,"teamname"),nextId=FootballCatalog.Value(next,"teamid");
        Task<IReadOnlyList<MarketTransfer>> Fetch(IEnumerable<string> _,CancellationToken token)=>Task.FromResult<IReadOnlyList<MarketTransfer>>([
            new(1,candidate.TransfermarktName,"Unknown source",firstName,""),
            new(2,candidate.TransfermarktName,firstName,nextName,"")]);
        var obligations=new List<System.Data.DataRow>();
        foreach(string tableName in new[]{"playerloans","career_presignedcontract"})
        {
            var table=doc.Tables.FirstOrDefault(t=>t.Name==tableName);
            if(table is null)continue;
            var row=TableEditing.Add(table);row["playerid"]=candidate.PlayerId;obligations.Add(row);
        }
        var page=new TransfersPage(catalog,Fetch);
        var rows=(StackPanel)typeof(TransfersPage).GetField("leagueRows",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(page)!;
        var combo=(ComboBox)((StackPanel)rows.Children[0]).Tag;
        Require(combo.Items.Count==33,"33 competition selector");
        Complete(page.VerifyAsync());
        Require(page.Grid.Items.Count==2&&page.ApplyButton.IsEnabled,"Repeated player's two movements must remain visible");
        var output=Path.Combine(root,"artifacts/ttlive-integration");Directory.CreateDirectory(output);
        page.Width=1380;page.Height=820;page.Measure(new Size(1380,820));page.Arrange(new Rect(0,0,1380,820));page.UpdateLayout();
        var bitmap=new RenderTargetBitmap(1380,820,96,96,PixelFormats.Pbgra32);bitmap.Render(page);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(output,"transfers-native.png")))encoder.Save(file);
        combo.SelectedIndex=1;
        Require(page.Grid.Items.Count==0&&!page.ApplyButton.IsEnabled,"Selection change invalidates imported movements");
        Complete(page.VerifyAsync());
        page.ApplyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(FootballCatalog.Value(links[candidate.PlayerId].Single(),"teamid")==nextId,"Apply changes actual catalog");
        Require(page.Grid.Items.Count==0,"Applied list clears");
        Require(obligations.All(r=>r.RowState is System.Data.DataRowState.Deleted or System.Data.DataRowState.Detached),
            "WPF Apply cancels authorized loan/presigned relations");

        var pending=new TaskCompletionSource<IReadOnlyList<MarketTransfer>>();
        var stale=new TransfersPage(catalog,(_,_)=>pending.Task);
        var task=stale.VerifyAsync();
        var staleRows=(StackPanel)typeof(TransfersPage).GetField("leagueRows",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(stale)!;
        ((ComboBox)((StackPanel)staleRows.Children[0]).Tag).SelectedIndex=2;
        pending.SetResult([new(1,candidate.TransfermarktName,"",nextName,"")]);
        Complete(task);
        Require(stale.Grid.Items.Count==0&&!stale.ApplyButton.IsEnabled,"Stale response cannot populate a different competition");
        Console.WriteLine("PASS WPF 33 choices, verify, repeat-player sequence, stale response, selection invalidation, native Apply and rendering");
        app.Shutdown();
    }
}
