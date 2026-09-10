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
    [STAThread]
    private static void Main(string[] args)
    {
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
        Render("home");
        var editingButton=(System.Windows.Controls.Button)window.FindName("EditingButton");
        editingButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        if(model.Screen!="Editing")throw new Exception("Editing navigation failed");
        Render("editing");
        if(((System.Windows.Controls.Button)window.FindName("BaseDbButton")).Visibility!=Visibility.Visible||((System.Windows.Controls.Button)window.FindName("SelectedDbButton")).Visibility!=Visibility.Visible)throw new Exception("Database choice cards missing");
        model.Screen="Home";
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
        RenderControl(modules,"modules",1200,760);
        var catalog=new FootballCatalog(doc);var player=doc.Tables.Single(t=>t.Name=="players");
        var editor=new ATLink.Views.EntityEditor(catalog,player,player.Data.Rows[0],catalog.PlayerName(player.Data.Rows[0]));
        RenderControl(editor,"player-editor",1080,720);
        Console.WriteLine("PASS module and entity editor rendering");
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
        var squadTeam=new ATLink.Views.TeamWorkspace(squadCatalog,squadCatalog.Entities("teams").First(t=>t.Id=="1"));
        typeof(ATLink.Views.TeamWorkspace).GetMethod("Show",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(squadTeam,new object[]{"Formations"});
        RenderControl(squadTeam,"squad-team-formations",1200,900);
        var squadPitch=(System.Windows.Controls.Canvas?)typeof(ATLink.Views.TeamWorkspace).GetField("pitch",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(squadTeam);
        if(squadPitch is null||squadPitch.Children.OfType<System.Windows.Controls.Border>().Count()!=11)throw new Exception("Squad formation pitch must display eleven players");
        if(squadDocument.HasChanges)throw new Exception("Viewing Squad formation mutated data");
        Console.WriteLine("PASS selected Squad formations render eleven players without changing the document");
        window.Close();app.Shutdown();
    }
}



