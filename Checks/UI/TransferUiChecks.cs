using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
        var reportedSource=teams.First(r=>FootballCatalog.Value(r,"teamid")!=initial&&FootballCatalog.Value(r,"teamid")!=nextId);
        string reportedSourceName=FootballCatalog.Value(reportedSource,"teamname"),reportedSourceId=FootballCatalog.Value(reportedSource,"teamid");
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
        Require(page.Grid.Items.Count==1&&page.ApplyButton.IsEnabled,"Already-at-destination movement is omitted; remaining transfer stays visible");
        var kept=(TransferDraft)page.Grid.Items[0];
        Require(kept.OldClubId==initial&&kept.OldClubName==firstName,"Old club uses FC26 id and name");
        Require(kept.Destination?.Id==nextId,"Remaining movement is the real destination change");
        var pageClubs=(ClubOption[])typeof(TransfersPage).GetField("clubs",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(page)!;
        var sharedGridView=System.Windows.Data.CollectionViewSource.GetDefaultView(pageClubs);
        int clubsBefore=sharedGridView.Cast<object>().Count();
        var addManual=new TransferManualWindow(catalog,pageClubs,enriched,TransferContracts.Load());
        var manualDestination=(ComboBox)typeof(TransferManualWindow).GetField("destinations",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(addManual)!;
        manualDestination.Text="___filtered_manual_club___";Pump();Pump();
        Require(sharedGridView.Cast<object>().Count()==clubsBefore&&kept.Destination?.Id==nextId,
            "Filtering Add manually must not hide imported New Club values");
        addManual.Close();
        Task<IReadOnlyList<MarketTransfer>> FetchAlreadyLoan(IEnumerable<string> _,CancellationToken token)=>Task.FromResult<IReadOnlyList<MarketTransfer>>([
            new(1,candidate.TransfermarktName,"Unknown source",firstName,"",true,false,null,"Date de fin du prêt introuvable")]);
        var alreadyLoanPage=new TransfersPage(catalog,FetchAlreadyLoan);Complete(alreadyLoanPage.VerifyAsync());
        Require(alreadyLoanPage.Grid.Items.Count==0&&alreadyLoanPage.Unmatched.Count==0,"Loan already at destination is ignored even when its end date is unavailable");
        Task<IReadOnlyList<MarketTransfer>> FetchInvalidLoan(IEnumerable<string> _,CancellationToken token)=>Task.FromResult<IReadOnlyList<MarketTransfer>>([
            new(1,candidate.TransfermarktName,firstName,nextName,"",true,false,null,"Date de fin du prêt introuvable")]);
        var invalidLoanPage=new TransfersPage(catalog,FetchInvalidLoan);Complete(invalidLoanPage.VerifyAsync());
        Require(invalidLoanPage.Unmatched.Count==1&&invalidLoanPage.Unmatched[0].Reason==UnresolvedReasons.InvalidTransferData,"Applicable loan without end date keeps invalid_transfer_data");
        Task<IReadOnlyList<MarketTransfer>> FetchReclass(IEnumerable<string> _,CancellationToken token)=>Task.FromResult<IReadOnlyList<MarketTransfer>>([
            new(1,candidate.TransfermarktName,firstName,nextName,"arrival"),
            new(2,candidate.TransfermarktName,"Unrelated Club",nextName,"arrival"),
            new(3,candidate.TransfermarktName,"Old TM Club",firstName,"departure")]);
        var reclass=new TransfersPage(catalog,FetchReclass);
        Complete(reclass.VerifyAsync());
        Require(reclass.Grid.Items.Count==1&&((TransferDraft)reclass.Grid.Items[0]).Destination?.Id==nextId,"Real transfer remains when a sibling departure is already completed");
        Require(reclass.Grid.Items.Cast<TransferDraft>().All(d=>d.OldClubId==initial),"Mismatched Transfermarkt from-club is omitted when a departure targets the FC26 club");
        Require(page.Grid.Columns.Count==7&&page.Grid.Columns.Cast<DataGridColumn>().All(c=>c.Header as string!=""),"Type and terms columns are present; remove column is not in the grid");
        Require(page.Grid.CanUserSortColumns&&page.Grid.Columns.Cast<DataGridColumn>().All(c=>!string.IsNullOrWhiteSpace(c.SortMemberPath)),"Every column can sort");
        Require(page.SearchBox.Width==220&&page.SearchBox.MaxWidth==260,"Search box stays compact");
        Require(page.SearchBox.Parent is Grid host&&host.Parent is DockPanel bar&&
            bar.Children.OfType<Panel>().Any(p=>p.Children.Contains(page.SelectAllButton)&&p.Children.Contains(page.RemoveSelectedButton)),
            "Search shares the toolbar with Select all and Remove");
        page.SearchBox.Text=kept.PlayerName;
        Require(page.Grid.Items.Count==1,"Search keeps the matching transfer");
        page.SearchBox.Text="___no_such_transfer___";
        Require(page.Grid.Items.Count==0&&page.ApplyButton.IsEnabled,"Search isolates without dropping drafts");
        page.SearchBox.Text="";
        Require(page.Grid.Items.Count==1,"Clearing search restores the table");
        Require(kept.Number==candidate.ShirtNumber,"Verify fills Number from players_enrich.json");
        page.Grid.UnselectAll();
        Require(!page.RemoveSelectedButton.IsEnabled,"Remove is disabled without a selection");
        Require(page.SelectAllButton.IsEnabled&&page.SelectAllButton.Content as string=="Select all","Select all button");
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

        var loanDate=new DateOnly(2027,6,30);
        Task<IReadOnlyList<MarketTransfer>> FetchLoan(IEnumerable<string> _,CancellationToken token)=>Task.FromResult<IReadOnlyList<MarketTransfer>>([
            new(1,candidate.TransfermarktName,reportedSourceName,firstName,"arrival",true,true,loanDate)]);
        var loanPage=new TransfersPage(catalog,FetchLoan);Complete(loanPage.VerifyAsync());
        Require(loanPage.Grid.Items.Count==1&&loanPage.Grid.Items[0] is TransferDraft {IsLoan:true,IsLoanToBuy:true,LoanEnd:"30/06/2027"},"Automatic loan is visible with its terms");
        loanPage.ApplyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var loanRow=catalog.Rows("playerloans").Single(r=>FootballCatalog.Value(r,"playerid")==candidate.PlayerId);
        Require(FootballCatalog.Value(loanRow,"teamidloanedfrom")==reportedSourceId&&reportedSourceId!=nextId&&FootballCatalog.Value(loanRow,"loandateend")=="162427"&&FootballCatalog.Value(loanRow,"isloantobuy")=="1","WPF automatic loan writes Transfermarkt source into playerloans");
        var popup=new PlayerTransferWindow(catalog,catalog.Entities("players").Single(p=>p.Id==candidate.PlayerId));
        Require(popup.FindName("TransferMode") is ToggleButton&&popup.FindName("LoanMode") is ToggleButton&&popup.FindName("LoanEndDate") is ComboBox dates&&dates.Items.Count==22,"Player popup offers Transfer/Loan and dates through 2035");

        Task<IReadOnlyList<MarketTransfer>> FetchMissing(IEnumerable<string> _,CancellationToken token)=>Task.FromResult<IReadOnlyList<MarketTransfer>>([
            new(1,candidate.TransfermarktName,firstName,"__missing_destination__","arrival"),
            new(2,"__missing_player__",firstName,nextName,"arrival")]);
        var missingPage=new TransfersPage(catalog,FetchMissing);Complete(missingPage.VerifyAsync());
        Require(missingPage.Unmatched.Count==2,"Unmatched rows retained");
        Require(missingPage.Unmatched[0].PlayerId==candidate.PlayerId&&missingPage.Unmatched[1].PlayerId is null,"Only identified players carry database IDs");
        var manual=new PlayerTransferWindow(catalog,catalog.Entities("players").Single(p=>p.Id==candidate.PlayerId),true);
        Require(((StackPanel)manual.FindName("CurrentClubPanel")).Visibility==Visibility.Visible,"Current club visible");
        Require(((TextBlock)manual.FindName("CurrentClubName")).Text==firstName,"Current club read from the active DB after loan");
        Require(((TextBlock)manual.FindName("DestinationLabel")).Text=="New club"&&manual.ChosenClub is null,"Destination is separate and initially empty");
        ((ComboBox)manual.FindName("Clubs")).Text=nextId;
        ((ComboBox)manual.FindName("ContractYear")).Text="2028";
        app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>
        {
            typeof(PlayerTransferWindow).GetMethod("ConfirmClick",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(manual,[manual,new RoutedEventArgs()]);
            if(manual.IsVisible)manual.Close();
        }));
        Require(manual.ShowDialog()==true,"Manual resolution confirms");
        Require(FootballCatalog.Value(links[candidate.PlayerId].Single(),"teamid")==nextId,"Manual resolution changes the loaded DB");
        Console.WriteLine("PASS unmatched player identity, read-only current club, separate destination and manual confirmation");

        var requested=new List<string>();
        Task<IReadOnlyList<MarketTransfer>> FetchPerLeague(IEnumerable<string> urls,CancellationToken token)
        {
            requested.AddRange(urls);
            return Task.FromResult<IReadOnlyList<MarketTransfer>>([new(1,candidate.TransfermarktName,firstName,"__unknown_club__","arrival")]);
        }
        var independent=new TransfersPage(catalog,FetchPerLeague);
        var independentRows=(StackPanel)typeof(TransfersPage).GetField("leagueRows",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(independent)!;
        Complete(independent.VerifyAsync());
        var add=(Button)typeof(TransfersPage).GetField("addLeague",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(independent)!;
        add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var extraRow=(StackPanel)independentRows.Children[1];
        var extraCombo=(ComboBox)extraRow.Tag;extraCombo.SelectedIndex=1;
        Require(independent.Unmatched.Count==1,"Adding/changing a competition preserves existing results");
        Require(extraRow.Children[1] is Button {Content:"Verify"}&&extraRow.Children[2] is Button {Content:"Remove"}&&extraRow.Children[3] is System.Windows.Shapes.Ellipse,"Verify Remove dot order");
        var states=(System.Collections.IList)typeof(TransfersPage).GetField("leagueStates",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(independent)!;
        var verifyRow=typeof(TransfersPage).GetMethods(BindingFlags.NonPublic|BindingFlags.Instance).Single(m=>m.Name=="VerifyAsync"&&m.GetParameters().Length==1);
        Complete((Task)verifyRow.Invoke(independent,[states[1]])!);
        Require(requested.Count==2&&requested[0]!=requested[1]&&independent.Unmatched.Count==2,"Each verification requests only its own competition and retains other results");
        ((Button)extraRow.Children[2]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(independent.Unmatched.Count==1,"Removing competition removes only its own results");
        Console.WriteLine("PASS independent competition requests, preserved results and Verify/Remove/dot order");
        var pending=new TaskCompletionSource<IReadOnlyList<MarketTransfer>>();
        var stale=new TransfersPage(catalog,(_,_)=>pending.Task);
        var task=stale.VerifyAsync();
        Require(stale.DownloadProgress.Visibility==Visibility.Visible&&stale.DownloadProgress.IsIndeterminate,"Progress is visible while downloading");
        var staleRows=(StackPanel)typeof(TransfersPage).GetField("leagueRows",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(stale)!;
        ((ComboBox)((StackPanel)staleRows.Children[0]).Tag).SelectedIndex=2;
        pending.SetResult([new(1,candidate.TransfermarktName,"",nextName,"")]);
        Complete(task);
        Require(stale.DownloadProgress.Visibility==Visibility.Collapsed,"Progress stops after cancellation");
        Require(stale.Grid.Items.Count==0&&!stale.ApplyButton.IsEnabled,"Stale response cannot populate a different competition");
        Console.WriteLine("PASS WPF 33 choices, transfer/loan UI, automatic loan Apply, repeat-player sequence, stale response, selection invalidation and rendering");
        app.Shutdown();
    }
}
