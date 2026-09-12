using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ATLink;
using ATLink.Core;
using ATLink.ViewModels;

internal static class Program
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr hwnd,uint message,IntPtr wParam,IntPtr lParam);
    [System.Runtime.InteropServices.DllImport("user32.dll")]static extern IntPtr GetCursor();
    [STAThread]
    private static void Main(string[] args)
    {
        if(args.Contains("--transfers")){TransferUiChecks.Run(Path.GetFullPath(args.FirstOrDefault(a=>a!="--transfers")??"."));return;}
        string root=Path.GetFullPath(args.Length>0?args[0]:".");
        var app=new Application();
        var window=new MainWindow();
        var model=(MainViewModel)window.DataContext;
        var content=(FrameworkElement)window.Content;
        string output=Path.Combine(root,"Checks/output/ui");Directory.CreateDirectory(output);
        void Render(string name,int width=1400,int height=810)
        {
            window.Width=width;window.Height=height;
            content.Measure(new Size(width,height));content.Arrange(new Rect(0,0,width,height));content.UpdateLayout();
            app.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
            content.Measure(new Size(width,height));content.Arrange(new Rect(0,0,width,height));content.UpdateLayout();
            var image=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);image.Render(content);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));
            using var file=File.Create(Path.Combine(output,name+".png"));encoder.Save(file);
            Console.WriteLine($"Rendered {name}");
        }
        var head=ATLink.Views.EntityImages.Player("1025") as BitmapImage;
        var crest=ATLink.Views.EntityImages.Crest("1") as BitmapImage;
        if(head is null||!head.UriSource.LocalPath.EndsWith(Path.Combine("head","1025.png"))||crest is null||!crest.UriSource.LocalPath.EndsWith(Path.Combine("crest","1.png")))throw new Exception("Bundled image ID resolution failed");
        if(!ReferenceEquals(head,ATLink.Views.EntityImages.Player("1025")))throw new Exception("Portrait cache failed");
        if((ATLink.Views.EntityImages.Player("../invalid") as BitmapImage)?.UriSource.LocalPath!=Path.Combine(AppContext.BaseDirectory,"Data","head","notfound.png")||(ATLink.Views.EntityImages.Crest("999999999") as BitmapImage)?.UriSource.LocalPath!=Path.Combine(AppContext.BaseDirectory,"Data","crest","notfound.png"))throw new Exception("Missing/invalid image fallback failed");
        var imageTable=new System.Data.DataTable();imageTable.Columns.Add("playerid");var imageRow=imageTable.Rows.Add("1025");
        if(!ReferenceEquals(head,ATLink.Views.EntityImages.For(new EntityItem("1025","Player","",imageRow))))throw new Exception("Player list image binding failed");
        Console.WriteLine("PASS portrait/crest ID mapping, packaged images, cache and missing-image fallback");
        var ratingCases=new[]{(1,"#C91C1C"),(50,"#C91C1C"),(51,"#E48921"),(60,"#E48921"),(61,"#EABA36"),(70,"#EABA36"),(71,"#1F9C19"),(80,"#1F9C19"),(81,"#10680C"),(99,"#10680C")};
        foreach(var (rating,expected) in ratingCases)
        {
            var brush=(System.Windows.Media.SolidColorBrush)ATLink.Views.RatingColors.For(rating);
            var expectedColor=(System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(expected);
            if(brush.Color!=expectedColor)throw new Exception($"Rating color {rating}: {brush.Color}, expected {expected}");
        }
        Console.WriteLine("PASS inclusive rating color boundaries 1-99");
        if(ATLink.Views.PlayerMarketValues.For("9009")!=800000m)throw new Exception("Enriched market value lookup failed");
        if(ATLink.Views.PlayerMarketValues.Format(180000000m)!="180 M €"||ATLink.Views.PlayerMarketValues.Format(25500m)!="25.5 K €"||ATLink.Views.PlayerMarketValues.Format(null)!="—")throw new Exception("Market value formatting failed");
        Console.WriteLine("PASS enriched market values and K/M euro formatting");
        if(model.Screen!="Editing")throw new Exception("Startup must show database selection");
        Render("editing");
        if(((System.Windows.Controls.Button)window.FindName("BaseDbButton")).Visibility!=Visibility.Visible||((System.Windows.Controls.Button)window.FindName("SelectedDbButton")).Visibility!=Visibility.Visible)throw new Exception("Database choice cards missing");
        var doc=DatabaseDocument.Open(Path.Combine(root,"files/fifa_ng_db.db"),Path.Combine(root,"files/fifa_ng_db-meta.xml"));
        model.Load(doc);Render("launcher");
        model.ShowTablesCommand.Execute(null);Render("tables");
        var defaultPlayerIds=model.GridRows.Select(r=>long.Parse((string)r["playerid"])).ToArray();
        if(!defaultPlayerIds.SequenceEqual(defaultPlayerIds.Order()))throw new Exception("Default playerid sort failed");
        model.SelectedTable=model.Tables.Single(t=>t.Name=="teams");
        model.SearchColumn="teamname";model.SearchText="Arsenal";
        if(!model.GridRows.Any(r=>(string)r["teamname"]=="Arsenal"))throw new Exception("Search failed");
        model.ExactMatch=true;
        if(model.GridRows.Count==0 || model.GridRows.Any(r=>!string.Equals((string)r["teamname"],"Arsenal",StringComparison.OrdinalIgnoreCase)))throw new Exception("Exact search failed");
        Render("search");
        model.SearchText="";model.PageSize=50;var first=model.GridRows[0];
        var defaultTeamIds=model.GridRows.Select(r=>long.Parse((string)r["teamid"])).ToArray();
        if(!defaultTeamIds.SequenceEqual(defaultTeamIds.Order()))throw new Exception("Default teamid sort failed");
        model.NextCommand.Execute(null);
        if(model.GridRows.Count!=50 || ReferenceEquals(first,model.GridRows[0]))throw new Exception("Pagination failed");
        model.Sort("teamname",true);
        if(model.GridRows[0]!=model.Rows![0])throw new Exception("Sort/paging failed");
        model.Sort("teamid",true);
        var numericIds=model.GridRows.Select(r=>long.Parse((string)r["teamid"])).ToArray();
        var expectedIds=model.Rows!.Cast<System.Data.DataRowView>().Select(r=>long.Parse((string)r["teamid"])).Order().Take(50).ToArray();
        if(!numericIds.SequenceEqual(expectedIds))throw new Exception("Numeric sort failed");
        model.Sort("teamid",false);
        if(long.Parse((string)model.GridRows[0]["teamid"])!=model.Rows.Cast<System.Data.DataRowView>().Max(r=>long.Parse((string)r["teamid"])))throw new Exception("Descending numeric sort failed");
        Render("tables-small",960,580);
        model.ShowLauncherCommand.Execute(null);
        if(model.Screen!="Launcher")throw new Exception("Home navigation failed");
        model.TableFilter="player";
        if(model.FilteredTables.Any(t=>!t.Name.Contains("player",StringComparison.OrdinalIgnoreCase)))throw new Exception("Table search failed");
        Console.WriteLine("PASS navigation, table search, exact search, pagination, global sorting and WPF rendering");
        void RenderControl(FrameworkElement control,string name,int width,int height)
        {
            control.Width=width;control.Height=height;
            control.Measure(new Size(width,height));control.Arrange(new Rect(0,0,width,height));control.UpdateLayout();
            app.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
            var picture=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);picture.Render(control);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(picture));using var file=File.Create(Path.Combine(output,name+".png"));encoder.Save(file);
            Console.WriteLine($"Rendered {name}");
        }
        var modules=new ATLink.Views.ModulesWindow(doc);
        RenderControl(modules,"modules",1536,960);
        typeof(ATLink.Views.ModulesWindow).GetMethod("Switch",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(modules,new object[]{"transfers"});
        app.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
        if(modules.FindName("PlayerContent") is not System.Windows.Controls.ContentControl transferHost||transferHost.Content is not ATLink.Views.TransfersPage transfers)
            throw new Exception("Transfers tab did not host TransfersPage");
        if(transfers.EmptyMessage.Visibility!=Visibility.Visible||transfers.Grid.Visibility!=Visibility.Collapsed)
            throw new Exception("Empty transfers state missing");
        if(transfers.VerifyButton.Content as string!="Verify"||transfers.AddManuallyButton.Content as string!="Add manually"||transfers.ApplyButton.Content as string!="Apply")
            throw new Exception("Transfers actions missing");
        RenderControl(modules,"transfers",1400,900);
        typeof(ATLink.Views.ModulesWindow).GetMethod("Switch",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(modules,new object[]{"players"});
        typeof(ATLink.Views.ModulesWindow).GetMethod("Switch",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(modules,new object[]{"transfers"});
        app.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
        if(modules.FindName("PlayerContent") is not System.Windows.Controls.ContentControl resetHost||resetHost.Content is not ATLink.Views.TransfersPage resetPage||resetPage.EmptyMessage.Visibility!=Visibility.Visible)
            throw new Exception("Leaving Transfers did not reset the page");
        Console.WriteLine("PASS transfers empty state, actions and reset on leave");
        var catalog=new FootballCatalog(doc);var player=doc.Tables.Single(t=>t.Name=="players");
        var editor=new ATLink.Views.EntityEditor(catalog,player,player.Data.Rows[0],catalog.PlayerName(player.Data.Rows[0]));
        RenderControl(editor,"player-editor",1080,720);
        catalog.LoadNationalTeams(Path.Combine(root,"files","FC26_NATIONAL_TEAM_IDS.csv"));
        var popupPlayerId=catalog.Rows("teamplayerlinks").Where(r=>!catalog.NationalTeamIds.Contains(FootballCatalog.Value(r,"teamid"))).GroupBy(r=>FootballCatalog.Value(r,"playerid")).First(g=>g.Count()==1).Key;
        var transferPopup=new ATLink.Views.PlayerTransferWindow(catalog,catalog.Entities("players").First(p=>p.Id==popupPlayerId));
        var clubBox=(System.Windows.Controls.ComboBox)transferPopup.FindName("Clubs");
        if(!clubBox.IsEditable || !System.Windows.Controls.VirtualizingPanel.GetIsVirtualizing(clubBox))
            throw new Exception("Club picker must support text entry and virtualization");
        transferPopup.Show();
        clubBox.IsDropDownOpen=true;
        app.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
        int realized=Enumerable.Range(0,clubBox.Items.Count).Count(i=>clubBox.ItemContainerGenerator.ContainerFromIndex(i) is not null);
        if(realized>=100)throw new Exception("Club picker eagerly realized "+realized+" clubs");
        Console.WriteLine($"PASS editable club picker: {realized} visible containers for {clubBox.Items.Count} clubs");
        var clickedClub=Enumerable.Range(0,clubBox.Items.Count)
            .Select(i=>clubBox.ItemContainerGenerator.ContainerFromIndex(i) as System.Windows.Controls.ComboBoxItem)
            .FirstOrDefault(item=>item is not null && item.DataContext is ATLink.Views.PlayerTransferWindow.ClubChoice club && club.Id!=(clubBox.SelectedItem as ATLink.Views.PlayerTransferWindow.ClubChoice)?.Id);
        if(clickedClub?.DataContext is not ATLink.Views.PlayerTransferWindow.ClubChoice mouseClub)throw new Exception("Club picker had no clickable destination");
        clickedClub.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice,0,System.Windows.Input.MouseButton.Left){RoutedEvent=UIElement.PreviewMouseLeftButtonDownEvent});
        app.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
        if(transferPopup.ChosenClub?.Id!=mouseClub.Id)throw new Exception("Mouse selection in the club picker did not choose that club");
        Console.WriteLine("PASS club picker mouse selection before typing");
        clubBox.IsDropDownOpen=false;
        clubBox.ApplyTemplate();
        var clubEditor=(System.Windows.Controls.TextBox)clubBox.Template.FindName("PART_EditableTextBox",clubBox);
        clubEditor.Focus();
        var typedClub=clubBox.Items.Cast<ATLink.Views.PlayerTransferWindow.ClubChoice>().Last();
        clubEditor.Text=typedClub.Name;
        app.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
        if(transferPopup.ChosenClub?.Id!=typedClub.Id)
            throw new Exception("Typing a complete club name did not resolve that club");
        clubEditor.Text=typedClub.Id;
        app.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
        if(transferPopup.ChosenClub?.Id!=typedClub.Id||clubBox.Items.Count!=1)
            throw new Exception("Typing a team ID did not filter and resolve that club");
        clubEditor.Text="";
        app.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
        if(transferPopup.ChosenClub is not null||clubBox.Items.Count<100)throw new Exception("Clearing club search retained the first selection");
        var secondClub=clubBox.Items.Cast<ATLink.Views.PlayerTransferWindow.ClubChoice>().First(c=>c.Id!=typedClub.Id);
        clubEditor.Text=secondClub.Id;
        app.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
        if(transferPopup.ChosenClub?.Id!=secondClub.Id||!clubBox.Items.Contains(secondClub))throw new Exception("Second club ID search failed");
        clubBox.SelectedItem=secondClub;
        app.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
        clubBox.IsDropDownOpen=false;
        clubBox.IsDropDownOpen=true;
        app.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
        if(clubBox.Items.Count<100)throw new Exception("Selecting a club trapped the dropdown on that club");
        clubEditor.Text="No such club 000000";
        app.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
        if(transferPopup.ChosenClub is not null||clubBox.Items.Count!=0)throw new Exception("Unknown text retained stale destination");
        clubEditor.Text=typedClub.Id;
        app.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
        if(transferPopup.ChosenClub?.Id!=typedClub.Id)throw new Exception("Search did not recover after no results");
        Console.WriteLine("PASS repeated club searches, selection/reopening, clearing, unknown text and recovery by ID");
        ((FrameworkElement)transferPopup.Content).Margin=new Thickness(0);
        RenderControl((FrameworkElement)transferPopup.Content,"player-transfer-popup",516,310);
        transferPopup.Close();
        typeof(MainWindow).GetMethod("OpenPlayers",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(window,null);
        if(model.Screen!="Page"||model.Page is not ATLink.Views.ModulesWindow)throw new Exception("Loaded DB must open players directly");
        Console.WriteLine("PASS direct players navigation, module and entity editor rendering");
        var formationWindow=new ATLink.Views.FormationWindow(catalog,"1");
        RenderControl(formationWindow,"formation",1180,760);
        Console.WriteLine("PASS formation editor rendering");
        var team=catalog.Entities("teams").First();
        RenderControl(new ATLink.Views.TeamWorkspace(catalog,team),"team-workspace",1200,800);
        var arsenalFormations=new ATLink.Views.TeamWorkspace(catalog,catalog.Entities("teams").First(t=>t.Id=="1"));
        typeof(ATLink.Views.TeamWorkspace).GetMethod("Show",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(arsenalFormations,new object[]{"Formations"});
        RenderControl(arsenalFormations,"team-formations",1200,900);
        Console.WriteLine("PASS team workspace rendering");
        CompdataFile Fixture(string path,string text)=>new(){RelativePath=path,Text=text,InitialText=text,Original=System.Text.Encoding.UTF8.GetBytes(text),Encoding=System.Text.Encoding.UTF8};
        var competition=new CompdataProject{SourceFolder=root,Files=new[]{Fixture("compobj.txt","0,0,W,World,-1\n1,2,E,England,0\n")}};
        RenderControl(new ATLink.Views.TournamentWizard(competition),"tournament-wizard",1200,800);
        RenderControl(new ATLink.Views.CompetitionManagerWindow(competition),"competition-manager",1200,800);
        if(competition.Files.Count!=1)throw new Exception("Cancelled competition editor mutated project");
        RenderControl(new ATLink.Views.PlayerImportWindow(),"player-import",900,800);
        RenderControl(new ATLink.Views.LocalizationWindow(),"localization",1100,720);
        Console.WriteLine("PASS tournament wizard, competition manager, profile importer, localization and cancel isolation");
        var studio=new StudioData(Path.Combine(AppContext.BaseDirectory,"Data"),Path.Combine(output,"unused-settings-"+Guid.NewGuid().ToString("N")+".json"));
        RenderControl(new ATLink.Views.SettingsPage(studio,false,_=>Task.CompletedTask),"settings",1100,720);
        RenderControl(new ATLink.Views.SettingsPage(studio,true,_=>Task.CompletedTask),"welcome",1100,720);
        var selectedTable=model.SelectedTable;
        var rowToKeep=doc.Tables.Single(t=>t.Name=="teams").Data.Rows[0];string originalName=(string)rowToKeep["teamname"];rowToKeep["teamname"]="Pending edit";
        model.SetLocalization(studio.OpenLocalization("eng_us"));
        if(model.SelectedTable!=selectedTable||(string)rowToKeep["teamname"]!="Pending edit")throw new Exception("Language switch lost DB edits/selection");
        model.SelectedTable=model.Localization!.Tables[0];
        model.SetLocalization(studio.OpenLocalization("fre_fr"));
        if(!model.Localization!.Tables.Contains(model.SelectedTable!)||model.Tables.Count!=doc.Tables.Count+model.Localization.Tables.Count)throw new Exception("Stale/duplicate LOC tables after language switch");
        rowToKeep["teamname"]=originalName;rowToKeep.AcceptChanges();
        Console.WriteLine("PASS Editing cards, settings/welcome, language replacement and preserved main DB edits");
        var squadDocument=studio.OpenSquad(Path.Combine(root,"files","Squads20260905195257141"),"eng_us").Main;
        var squadCatalog=new FootballCatalog(squadDocument);
        var browser=new ATLink.Views.PlayerBrowser(squadCatalog);
        var browserSearch=(System.Windows.Controls.TextBox)typeof(ATLink.Views.PlayerBrowser).GetField("search",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(browser)!;
        var browserGrid=(System.Windows.Controls.DataGrid)typeof(ATLink.Views.PlayerBrowser).GetField("grid",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(browser)!;
        if(browserGrid.Columns.Single(c=>Equals(c.Header,"VALUE")).SortMemberPath!="MarketValue")throw new Exception("Market values must sort by their numeric property");
        browserSearch.Text="81928";
        if(browserGrid.Items.Count!=1||browserGrid.SelectedItem is not ATLink.Views.PlayerBrowser.PlayerCard {Id:81928,Name:"Abdulla Abdullaev"})throw new Exception("Player browser Squad name/ID search");
        browserSearch.Text="no such player 99999999";
        if(browserGrid.Items.Count!=0)throw new Exception("Player browser empty results");
        browserSearch.Text="";
        var browserFilters=(System.Windows.Controls.StackPanel)typeof(ATLink.Views.PlayerBrowser).GetField("filters",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(browser)!;
        ((System.Windows.Controls.Button)browserFilters.Children[1]).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        if(browserGrid.Items.Count==0||browserGrid.Items.Cast<ATLink.Views.PlayerBrowser.PlayerCard>().Any(p=>p.Position!="GK"))throw new Exception("Goalkeeper filter");
        Console.WriteLine("PASS player browser search, selection, empty state and position filters");
        var squadTeam=new ATLink.Views.TeamWorkspace(squadCatalog,squadCatalog.Entities("teams").First(t=>t.Id=="1"));
        typeof(ATLink.Views.TeamWorkspace).GetMethod("Show",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(squadTeam,new object[]{"Formations"});
        RenderControl(squadTeam,"squad-team-formations",1200,900);
        var squadPitch=(System.Windows.Controls.Canvas?)typeof(ATLink.Views.TeamWorkspace).GetField("pitch",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(squadTeam);
        if(squadPitch is null||squadPitch.Children.OfType<System.Windows.Controls.Border>().Count()!=11)throw new Exception("Squad formation pitch must display eleven players");
        if(squadDocument.HasChanges)throw new Exception("Viewing Squad formation mutated data");
        Console.WriteLine("PASS selected Squad formations render eleven players without changing the document");
        var chromeProbe=new Window{Width=640,Height=400,Title="ATLink Studio",Background=Brushes.Black,
            Style=(Style)window.FindResource(typeof(Window)),ShowInTaskbar=false,
            Content=new System.Windows.Controls.TextBlock{Text="Window controls",Foreground=Brushes.White,Margin=new Thickness(24)}};
        chromeProbe.Show();
        app.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
        if(chromeProbe.WindowStyle!=WindowStyle.None || System.Windows.Shell.WindowChrome.GetWindowChrome(chromeProbe) is null)
            throw new Exception("Native title bar was not replaced");
        if(!ReferenceEquals(System.Windows.Input.Mouse.OverrideCursor,ATLink.Views.WindowAppearance.Pointer))
            throw new Exception("Custom pointer is not fixed");
        var hwnd=new System.Windows.Interop.WindowInteropHelper(chromeProbe).Handle;
        SendMessage(hwnd,0x20,hwnd,new IntPtr(1));
        var pointer=GetCursor();
        foreach(int hit in new[]{10,11,12,13,14,15,16,17})
        {
            SendMessage(hwnd,0x20,hwnd,new IntPtr(hit));
            if(pointer==IntPtr.Zero || GetCursor()!=pointer)throw new Exception("Resize border changed pointer");
        }
        SystemCommands.MaximizeWindowCommand.Execute(null,chromeProbe);
        if(chromeProbe.WindowState!=WindowState.Maximized)throw new Exception("Custom maximize command");
        SystemCommands.MaximizeWindowCommand.Execute(null,chromeProbe);
        if(chromeProbe.WindowState!=WindowState.Normal)throw new Exception("Custom restore command");
        SystemCommands.MinimizeWindowCommand.Execute(null,chromeProbe);
        if(chromeProbe.WindowState!=WindowState.Minimized)throw new Exception("Custom minimize command");
        chromeProbe.WindowState=WindowState.Normal;
        RenderControl((FrameworkElement)VisualTreeHelper.GetChild(chromeProbe,0),"custom-window-chrome",640,400);
        SystemCommands.CloseWindowCommand.Execute(null,chromeProbe);
        if(chromeProbe.IsVisible)throw new Exception("Custom close command");
        Console.WriteLine("PASS custom title bar, minimize/maximize/restore/close and fixed cursor on all resize edges");
        window.Close();app.Shutdown();
    }
}



