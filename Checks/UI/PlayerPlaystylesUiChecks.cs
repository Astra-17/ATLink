using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using ATLink.Core;
using ATLink.Views;
internal static class PlayerPlaystylesUiChecks
{
    public static void Run(string root){
        var app=new Application();app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("pack://application:,,,/ATLink;component/Themes/Studio.xaml")});
        var db=DatabaseDocument.Open(Path.Combine(root,"files/FC27/fifa_ng_db.db"),Path.Combine(root,"files/FC27/fifa_ng_db-meta.xml"));var catalog=new FootballCatalog(db);
        var player=catalog.Entities("players").First();
        player.Row["trait1"]="1";player.Row["icontrait1"]="2";player.Row["trait2"]="65537";player.Row["icontrait2"]="65538";
        var original=player.Row.ItemArray.ToArray();
        var editor=new PlayerEditor(catalog,player);var tabs=(TabControl)editor.FindName("EditorTabs");tabs.SelectedIndex=2;
        LinkedUiRenderChecks.Render(editor,"player-playstyles");
        IEnumerable<T> Find<T>(DependencyObject d) where T:DependencyObject{
            for(int i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(d);i++){
                var c=System.Windows.Media.VisualTreeHelper.GetChild(d,i);if(c is T t)yield return t;
                foreach(var n in Find<T>(c))yield return n;
            }
        }
        var styles=Find<PlayerPlaystylesEditor>(editor).Single();
        var running=Find<ComboBox>(styles).Single(c=>Equals(c.Tag,"runstylecode"));
        var codeBoxes=Find<TextBox>(styles).Where(b=>b.Tag is string key&&new[]{"runningcode1","runningcode2","finishingcode1","finishingcode2","animfreekickstartposcode","animpenaltiesstartposcode"}.Contains(key)).ToDictionary(b=>(string)b.Tag,b=>b);
        var runPreset=Find<ComboBox>(styles).Single(c=>Equals(c.Tag,"runningpreset"));
        var finishPreset=Find<ComboBox>(styles).Single(c=>Equals(c.Tag,"finishingpreset"));
        var references=new[]{
            ("Cristiano Ronaldo",0,2,0,82,0),("Lionel Messi",0,18,2,101,0),
            ("Eden Hazard",0,0,0,44,25),("Luka Modrić",0,0,0,0,0),("Ronaldinho",0,14,0,0,0)};
        string Read(string key)=>key=="runstylecode"?(string)running.SelectedValue:codeBoxes[key].Text;
        void Choose(ComboBox combo,string name)=>combo.SelectedItem=combo.Items.Cast<ComboBoxItem>().Single(i=>i.IsEnabled&&Equals(i.Content,name));
        foreach(var reference in references){
            string finishingBefore=Read("finishingcode1")+","+Read("finishingcode2");
            Choose(runPreset,reference.Item1);
            if(Read("runstylecode")!=reference.Item2.ToString()||Read("runningcode1")!=reference.Item3.ToString()||Read("runningcode2")!=reference.Item4.ToString())throw new Exception("Wrong run preset "+reference.Item1);
            if(Read("finishingcode1")+","+Read("finishingcode2")!=finishingBefore)throw new Exception("Run preset affected finishing");
            string runBefore=Read("runstylecode")+","+Read("runningcode1")+","+Read("runningcode2");
            Choose(finishPreset,reference.Item1);
            if(Read("finishingcode1")!=reference.Item5.ToString()||Read("finishingcode2")!=reference.Item6.ToString())throw new Exception("Wrong finish preset "+reference.Item1);
            if(Read("runstylecode")+","+Read("runningcode1")+","+Read("runningcode2")!=runBefore)throw new Exception("Finish preset affected run");
            if(!player.Row.ItemArray.SequenceEqual(original))throw new Exception("Preset escaped draft");
        }
        Choose(runPreset,"Eden Hazard");
        string shared=(string)((ComboBoxItem)runPreset.SelectedItem).Content;
        if(shared!="-")throw new Exception("Zero run codes displayed a player name");
        Choose(finishPreset,"Luka Modrić");
        if((string)((ComboBoxItem)finishPreset.SelectedItem).Content!="-")throw new Exception("Zero finishing codes displayed a player name");
        if(runPreset.Items.Cast<ComboBoxItem>().Any(i=>i.Content is string label&&label.Contains(" / ")))throw new Exception("Combined names still offered");
        codeBoxes["runningcode1"].Text="99999";
        if((string)((ComboBoxItem)runPreset.SelectedItem).Content!="-")throw new Exception("Unknown codes displayed a player name");
        codeBoxes["runningcode1"].Text="invalid";
        if(editor.TryApply()||!player.Row.ItemArray.SequenceEqual(original))throw new Exception("Invalid animation partially applied");
        foreach(var pair in codeBoxes)pair.Value.Text=FootballCatalog.Value(player.Row,pair.Key);
        running.SelectedValue=FootballCatalog.Value(player.Row,"runstylecode");
        if(styles.HasChanges)throw new Exception("Restoring codes remained dirty");
        codeBoxes["runningcode1"].Text="18";codeBoxes["runningcode2"].Text="2";
        string oldRunning=FootballCatalog.Value(player.Row,"runstylecode");
        if((string?)running.SelectedValue!=oldRunning)throw new Exception("Running style not loaded");
        running.SelectedValue=oldRunning=="0"?"1":"0";
        string expectedRunning=(string)running.SelectedValue;
        var shotBoxes=Find<TextBox>(styles).Where(b=>b.Tag is string key&&new[]{"finishingcode1","finishingcode2","animfreekickstartposcode","animpenaltiesstartposcode"}.Contains(key)).ToArray();
        if(shotBoxes.Length!=4)throw new Exception("Missing shooting animations");
        var expectedShots=shotBoxes.ToDictionary(b=>(string)b.Tag,b=>b.Text=="1"?"2":"1");
        foreach(var box in shotBoxes)box.Text=expectedShots[(string)box.Tag];
        if(styles.Tiles.Count!=36)throw new Exception("Expected 36 playstyles");
        if(styles.Tiles[0].State!=1||styles.Tiles[1].State!=2||styles.Tiles[30].State!=1||styles.Tiles[31].State!=2)throw new Exception("Existing styles not loaded");
        foreach(var tile in styles.Tiles){
            if(!File.Exists(Path.Combine(root,"Resource/Playstyles",tile.Id+"-gray.png")))throw new Exception("Gray asset missing");
            int initial=tile.State;
            for(int state=1;state<=3;state++){
                tile.Button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                if(tile.State!=(initial+state)%3||!ReferenceEquals(tile.Icon.Source,tile.Images[(initial+state)%3]))throw new Exception("Incorrect cycle "+tile.Id);
            }
        }
        void SetState(PlayerPlaystylesEditor.StyleTile tile,int state){while(tile.State!=state)tile.Button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));}
        SetState(styles.Tiles[0],2);SetState(styles.Tiles[1],1);SetState(styles.Tiles[29],2);SetState(styles.Tiles[30],0);SetState(styles.Tiles[31],1);SetState(styles.Tiles[35],2);
        LinkedUiRenderChecks.Render(editor,"player-playstyles-states");
        tabs.SelectedIndex=0;tabs.SelectedIndex=2;LinkedUiRenderChecks.Render(editor,"player-playstyles-return");
        if(styles.Tiles[0].State!=2||styles.Tiles[1].State!=1)throw new Exception("Tab change lost preview");
        styles.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        UiText.Current.SetLanguage("fr");LinkedUiRenderChecks.Render(editor,"player-playstyles-fr");
        if(styles.Tiles[0].State!=2||styles.Tiles[1].State!=1)throw new Exception("Language switch lost preview");
        if(styles.Tiles[0].Label.Text!="Tir en finesse")throw new Exception("French playstyle labels did not update");
        UiText.Current.SetLanguage("en");LinkedUiRenderChecks.Render(editor,"player-playstyles-en");
        if(!player.Row.ItemArray.SequenceEqual(original))throw new Exception("Preview modified player data");
        if(!styles.HasChanges)throw new Exception("Playstyles draft missing");
        editor.Draft.Potential.Text="invalid";
        if(editor.TryApply()||!player.Row.ItemArray.SequenceEqual(original))throw new Exception("Invalid attributes allowed partial style apply");
        editor.Draft.Potential.Text=FootballCatalog.Value(player.Row,"potential");
        if(!editor.TryApply()||styles.HasChanges)throw new Exception("Apply failed");
        if(FootballCatalog.Value(player.Row,"trait1")!="2"||FootballCatalog.Value(player.Row,"icontrait1")!=(536870912|1).ToString()||FootballCatalog.Value(player.Row,"trait2")!="65538"||FootballCatalog.Value(player.Row,"icontrait2")!="65568")throw new Exception("Wrong masks or unknown bits lost");
        if(FootballCatalog.Value(player.Row,"runstylecode")!=expectedRunning)throw new Exception("Running style not saved");
        foreach(var pair in expectedShots)if(FootballCatalog.Value(player.Row,pair.Key)!=pair.Value)throw new Exception("Shot animation not saved");
        if(FootballCatalog.Value(player.Row,"runningcode1")!="18"||FootballCatalog.Value(player.Row,"runningcode2")!="2")throw new Exception("New run codes not saved");
        string path=Path.Combine(root,"Checks/output/player-playstyles-test.db");File.WriteAllBytes(path,db.Serialize());
        var reopened=DatabaseDocument.Open(path,Path.Combine(root,"files/FC27/fifa_ng_db-meta.xml"));var saved=new FootballCatalog(reopened).Entities("players").Single(p=>p.Id==player.Id);
        foreach(string field in new[]{"trait1","trait2","icontrait1","icontrait2","runstylecode","runningcode1","runningcode2","finishingcode1","finishingcode2","animfreekickstartposcode","animpenaltiesstartposcode"})if(FootballCatalog.Value(saved.Row,field)!=FootballCatalog.Value(player.Row,field))throw new Exception("Styles lost after reopening");
        foreach(System.Data.DataColumn column in player.Row.Table.Columns)if(!new[]{"trait1","trait2","icontrait1","icontrait2","runstylecode","runningcode1","runningcode2","finishingcode1","finishingcode2","animfreekickstartposcode","animpenaltiesstartposcode"}.Contains(column.ColumnName)&&!Equals(player.Row[column],original[column.Ordinal]))throw new Exception("Unrelated field changed: "+column.ColumnName);
        Choose(runPreset,"Lionel Messi");Choose(finishPreset,"Cristiano Ronaldo");
        UiText.Current.SetLanguage("fr");LinkedUiRenderChecks.Render(editor,"player-animation-presets-fr");
        if((string)((ComboBoxItem)runPreset.SelectedItem).Content!="Lionel Messi"||(string)((ComboBoxItem)finishPreset.SelectedItem).Content!="Cristiano Ronaldo")throw new Exception("Preset names changed with language");
        UiText.Current.SetLanguage("en");LinkedUiRenderChecks.Render(editor,"player-animation-presets-en");
        var incompleteDb=DatabaseDocument.Open(path,Path.Combine(root,"files/FC27/fifa_ng_db-meta.xml"));
        var incompleteCatalog=new FootballCatalog(incompleteDb);incompleteCatalog.Table("players").Data.Columns.Remove("runningcode2");
        var incompleteEditor=new PlayerEditor(incompleteCatalog,incompleteCatalog.Entities("players").First());
        var incompleteTabs=(TabControl)incompleteEditor.FindName("EditorTabs");incompleteTabs.SelectedIndex=2;
        LinkedUiRenderChecks.Render(incompleteEditor,"player-animation-presets-missing-field");
        var unavailable=Find<ComboBox>(incompleteEditor).Single(c=>Equals(c.Tag,"runningpreset"));
        if(unavailable.Items.Cast<ComboBoxItem>().Any(i=>i.IsEnabled))throw new Exception("Missing field allowed run preset");
        if(unavailable.Items.Cast<ComboBoxItem>().Skip(1).Any(i=>i.ToolTip is null))throw new Exception("Missing incompatibility explanation");
        Console.WriteLine("PASS 36 styles, 72 original assets, 36 gray variants, all three-state cycles, draft isolation, atomic apply, unknown-bit preservation, five animation presets, missing-field compatibility and DB roundtrip");
    }
}
