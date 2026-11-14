using System.Data;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ATLink;
using ATLink.Core;
using ATLink.Services;
using ATLink.ViewModels;
using ATLink.Views;

static class InterfaceLanguageUiChecks
{
    public static void Run(string root)
    {
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
        InterfaceLocalization.Initialize();UiText.Current.SetLanguage("en");
        string output=Path.Combine(root,"Checks/output/interface-language");Directory.CreateDirectory(output);
        string temporary=Path.Combine(output,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temporary);
        try
        {
            var literal=new TextBlock{Text=UiText.Get("Save")};
            var bound=new TextBlock();bound.SetBinding(TextBlock.TextProperty,new Binding(nameof(ResourceModel.Label)){Source=new ResourceModel()});
            var editor=new TextBox{Text="Save"};
            var button=new Button{Content=UiText.Get("Back")};
            var panel=new StackPanel();foreach(var item in new FrameworkElement[]{literal,bound,editor,button})panel.Children.Add(item);
            var host=new Window{Content=panel,Width=500,Height=300,Left=-30000,Top=-30000,ShowActivated=false};host.Show();Pump();
            UiText.Current.SetLanguage("fr");Pump();
            Check(literal.Text=="Enregistrer"&&bound.Text=="Joueurs"&&(string)button.Content=="Retour","Immediate C# labels and bindings");
            Check(editor.Text=="Save","Editable user text remains unchanged");
            host.Content=null;Pump();host.Content=panel;Pump();
            UiText.Current.SetLanguage("en");Pump();Check(literal.Text=="Save"&&bound.Text=="Players","Unloaded pages retain resource identities");host.Close();

            foreach(string game in new[]{"FC26","FC27"})
            {
                var store=new ApplicationConfigurationStore(Path.Combine(temporary,game+".json"),Path.Combine(root,"files"));
                store.Save(store.Current.WithGame(game,store.Game(game) with {Configured=true}) with {InterfaceLanguage="en"});
                var data=store.CreateData(game);var docs=data.OpenBase("eng_us");
                var window=new MainWindow(store){Left=-30000,Top=-30000,ShowActivated=false};
                var model=(MainViewModel)window.DataContext;
                model.Load(docs.Main,docs.Loc);
                var catalog=new FootballCatalog(docs.Main);
                var player=catalog.Entities("players").First();
                var playerEditor=new PlayerEditor(catalog,player);
                WorkspaceController.Open(playerEditor);
                var draft=playerEditor.Draft.Attributes.First();draft.Text=draft.Text=="78"?"79":"78";
                var table=docs.Main.Tables.Single(t=>t.Name=="players");table.Data.Rows[0]["potential"]="88";
                DataRow english=LocRow(docs.Loc);english["sourcetext"]="English in-memory edit";
                model.TableFilter="players";var page=model.Page;
                model.SearchColumn="playerid";model.SearchText=player.Id;model.ExactMatch=true;
                var selectedTable=model.SelectedTable;var rowFilter=model.Rows!.RowFilter;
                window.Show();Pump();
                Check(Apply(window,store.Current with {InterfaceLanguage="fr"}),game+" switches to French");Pump();
                var french=model.Localization!;LocRow(french)["sourcetext"]="Modification française en mémoire";
                Check(Path.GetFileNameWithoutExtension(french.SourcePath)=="fre_fr"&&ReferenceEquals(model.Document,docs.Main)&&ReferenceEquals(model.Page,page),game+" keeps the open DB and editor");
                Check(draft.Text is "78" or "79"&&ReferenceEquals(playerEditor.Draft.Attributes.First(),draft)&&model.TableFilter=="players",game+" preserves draft and filter");
                Check(ReferenceEquals(model.SelectedTable,selectedTable)&&model.Rows!.RowFilter==rowFilter&&model.SearchText==player.Id&&model.ExactMatch,game+" preserves the selected table and search");
                Check(catalog.NationNames().Values.Any(name=>name=="Angleterre"),game+" reads French country names from LOC");
                Check(WindowAppearance.GetSettingsCommand(window) is not null,game+" keeps settings accessible from the editor");
                Render((FrameworkElement)window.Content,Path.Combine(output,game+"-fr.png"),1400,850);
                Apply(window,store.Current with {InterfaceLanguage="en"});Pump();
                Check(ReferenceEquals(model.Localization,docs.Loc)&&(string)english["sourcetext"]=="English in-memory edit",game+" retains English LOC edits");
                Render((FrameworkElement)window.Content,Path.Combine(output,game+"-en.png"),1400,850);
                Apply(window,store.Current with {InterfaceLanguage="fr"});Pump();
                Check(ReferenceEquals(model.Localization,french)&&(string)LocRow(french)["sourcetext"]=="Modification française en mémoire"&&docs.Main.HasChanges,game+" retains French LOC and DB edits");
                if(game=="FC27")
                {
                    var team=new TeamWorkspace(catalog,catalog.Entities("teams").First());WorkspaceController.Open(team);Pump();
                    Render((FrameworkElement)window.Content,Path.Combine(output,"team-fr.png"),1400,850);
                    Apply(window,store.Current with {InterfaceLanguage="en"});Pump();
                    Check(ReferenceEquals(model.Page,team),"Team editor stays open when changing language");
                    var project=new CompetitionProjectContext(CompdataProject.Open(data.CompdataFolder),game,docs.Main,docs.Loc,data.Metadata,data.Languages.Single(l=>l.Code=="eng_us").MetadataPath);
                    project.SetLanguages(data.Languages);
                    var studio=new CompetitionStudio(project);WorkspaceController.Open(studio);Pump();
                    var format=studio.Model.FootballFormat;var phases=format.Phases;
                    if(phases.Count>0)phases[0].Name="Unapplied custom phase";
                    var season=studio.Model.Season;if(season is not null)season.ReplaceInitial=true;
                    project.Execute("test checkpoint",()=>project.ReferenceConfirmed=true);
                    byte[] savedPreferences=File.ReadAllBytes(store.Path);
                    project.SetLanguages(data.Languages.Where(language=>language.Code=="eng_us").ToArray());
                    try{Apply(window,store.Current with {InterfaceLanguage="fr"});throw new Exception("Expected unavailable project LOC rejection");}
                    catch(TargetInvocationException ex)when(ex.InnerException is InvalidDataException){}
                    Check(UiText.Current.Language=="en"&&savedPreferences.SequenceEqual(File.ReadAllBytes(store.Path))&&ReferenceEquals(model.Page,studio),"Unavailable project LOC preserves language, settings and open project");
                    project.SetLanguages(data.Languages);
                    Apply(window,store.Current with {InterfaceLanguage="fr"});Pump();
                    Check(ReferenceEquals(model.Page,studio)&&ReferenceEquals(studio.Model.FootballFormat,format)&&ReferenceEquals(format.Phases,phases)&&project.CanUndo,"Competition editor, format draft and undo are preserved");
                    Check(season is null||ReferenceEquals(studio.Model.Season,season)&&season.ReplaceInitial,"Pending season draft is preserved");
                    Check(project.DisplayLanguage=="fre_fr"&&Path.GetFileNameWithoutExtension(project.Localization!.SourcePath)=="eng_us","Competition display language is independent of export language");
                    var localizedPhase=new FootballPhaseViewModel(new FootballPhase{Name="Test",Kind=FootballPhaseKind.Knockout,Participants=4,Rounds=2,Legs=2,FinalLegs=1},new Dictionary<string,string[]>());
                    Check(localizedPhase.Snapshot().Legs==2&&localizedPhase.Snapshot().FinalLegs==1,"French choices preserve home/away and final-leg semantics");
                    localizedPhase.Legs="Single match";Check(localizedPhase.Snapshot().Legs==1,"Stable choice keys remain accepted in French");
                    var calendarDraft=new CreationCalendarViewModel(project,new FootballSeasonViewModel(project),()=>[localizedPhase.Snapshot()]);
                    calendarDraft.Mode="Exact fixtures";Check(calendarDraft.Exact,"French calendar uses exact fixtures when selected");
                    calendarDraft.Mode="Choose matchday dates";Check(calendarDraft.Manual,"French calendar uses manual matchdays when selected");
                    var phaseSelection=studio.Model.CalendarPhase?.Id;
                    foreach(string code in new[]{"en","fr","en","fr"}){Apply(window,store.Current with {InterfaceLanguage=code});Pump();}
                    Check(studio.Model.CalendarPhase?.Id==phaseSelection&&ReferenceEquals(studio.Model.Season,season)&&project.CanUndo,"Repeated switches preserve calendar selection, season draft and undo");
                    Render((FrameworkElement)window.Content,Path.Combine(output,"competition-fr.png"),1400,850);
                    foreach(string code in new[]{"en","fr"})
                    {
                        Apply(window,store.Current with {InterfaceLanguage=code});Pump();
                        var settings=new ConfigurationWindow(store,game,false,_=>false){Left=-30000,Top=-30000,ShowActivated=false};settings.Show();Pump();
                        Render((FrameworkElement)settings.Content,Path.Combine(output,"settings-"+code+".png"),560,530);settings.Close();
                    }
                }
                model.Close();window.Close();
            }
            var missing=new ApplicationConfigurationStore(Path.Combine(temporary,"missing.json"),Path.Combine(temporary,"missing-data"));
            missing.Save(missing.Current.WithGame("FC27",missing.Game("FC27") with {Configured=true}) with {InterfaceLanguage="en"});
            var failure=new MainWindow(missing);byte[] before=File.ReadAllBytes(missing.Path);
            try{Apply(failure,missing.Current with {InterfaceLanguage="fr"});throw new Exception("Expected missing LOC failure");}
            catch(TargetInvocationException ex)when(ex.InnerException is InvalidDataException or FileNotFoundException){}
            Check(before.SequenceEqual(File.ReadAllBytes(missing.Path))&&UiText.Current.Language=="en","Missing LOC leaves disk and active language unchanged");failure.Close();
            Console.WriteLine("PASS interface language UI checks; renders: "+output);
        }
        finally{app.Shutdown();Directory.Delete(temporary,true);UiText.Current.SetLanguage("en");}
    }
    sealed class ResourceModel{public string Label=>UiText.Get("Players");}
    static DataRow LocRow(DatabaseDocument document)=>document.Tables.First(t=>t.Data.Columns.Contains("sourcetext")&&t.RowCount>0).Data.Rows[0];
    static bool Apply(MainWindow window,ApplicationConfiguration config)=>(bool)typeof(MainWindow).GetMethod("ApplySettings",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[config])!;
    static void Pump()
    {
        var frame=new DispatcherFrame();
        Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>frame.Continue=false));
        Dispatcher.PushFrame(frame);
    }
    static void Render(FrameworkElement element,string path,int width,int height)
    {
        element.Measure(new Size(width,height));element.Arrange(new Rect(0,0,width,height));element.UpdateLayout();Pump();
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(element);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(path);encoder.Save(file);
    }
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);Console.WriteLine("PASS "+message);}
}
