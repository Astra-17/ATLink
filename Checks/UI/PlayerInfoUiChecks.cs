using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ATLink.Core;
using ATLink.Views;
internal static class PlayerInfoUiChecks
{
    public static void Run(string root)
    {
        var app=new Application();app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("pack://application:,,,/ATLink;component/Themes/Studio.xaml")});
        string data=Path.Combine(root,"files/FC27");var db=DatabaseDocument.Open(Path.Combine(data,"fifa_ng_db.db"),Path.Combine(data,"fifa_ng_db-meta.xml"));var catalog=new FootballCatalog(db);var player=catalog.Entities("players").First();player.Row["role5"]="199";var original=player.Row.ItemArray.ToArray();
        void Check(bool v,string m){if(!v)throw new Exception(m);}
        IEnumerable<T> Find<T>(DependencyObject d) where T:DependencyObject{for(int i=0;i<VisualTreeHelper.GetChildrenCount(d);i++){var c=VisualTreeHelper.GetChild(d,i);if(c is T t)yield return t;foreach(var n in Find<T>(c))yield return n;}}
        var editor=new PlayerEditor(catalog,player);LinkedUiRenderChecks.Render(editor,"player-info");var info=Find<PlayerInfoEditor>(editor).Single();Check(!info.HasChanges,"Opening Info changed values");
        var roleChoices=Find<ComboBox>(info).Where(c=>c.Tag is string key&&System.Text.RegularExpressions.Regex.IsMatch(key,"^role[1-5]$")).ToArray();
        Check(roleChoices.Length==5&&roleChoices.All(c=>c.Items.Cast<FieldChoice>().Any(v=>v.Value=="0")&&c.Items.Cast<FieldChoice>().Any(v=>v.Value=="141"&&v.Label.Contains("++"))),"Missing named role lists or levels");
        Check(((FieldChoice)roleChoices.Single(c=>Equals(c.Tag,"role5")).SelectedItem).Value=="199","Unknown current role not preserved");
        info.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));UiText.Current.SetLanguage("fr");
        LinkedUiRenderChecks.Render(editor,"player-info-roles-fr");
        Check(roleChoices[0].Items.Cast<FieldChoice>().Single(c=>c.Value=="141").Label=="ST Attaquant avancé++","French role name missing");
        Check(!info.HasChanges,"Role translation changed draft");UiText.Current.SetLanguage("en");
        var boxes=Find<TextBox>(info).Where(b=>b.Tag is string).ToDictionary(b=>(string)b.Tag,b=>b);
        boxes["wage"].Text="12345";Check(info.HasChanges&&FootballCatalog.Value(player.Row,"wage")!="12345","Wage did not stay in draft");boxes["birthdate"].Text="2025-02-30";Check(!editor.TryApply(),"Invalid date accepted");Check(player.Row.ItemArray.SequenceEqual(original),"Invalid apply partially changed player");
        boxes["birthdate"].Text="2003-12-06";Find<ComboBox>(info).Single(c=>Equals(c.Tag,"role1")).SelectedItem=Find<ComboBox>(info).Single(c=>Equals(c.Tag,"role1")).Items.Cast<FieldChoice>().Single(c=>c.Value=="141");Check(editor.TryApply(),"Valid Info apply failed");Check(FootballCatalog.Value(player.Row,"wage")=="12345"&&FifaDate.ToIso(FootballCatalog.Value(player.Row,"birthdate"))=="2003-12-06","Info changes not saved");Check(!info.HasChanges,"Applied draft still pending");
        foreach(var pair in new[]{("firstname","Jude"),("surname","Bellingham"),("commonname","Jude Test"),("playerjerseyname","BELLINGHAM")})boxes[pair.Item1].Text=pair.Item2;
        Check(info.HasChanges,"Name changes missing from draft");Check(editor.TryApply(),"Names failed to apply");
        var editedNames=catalog.Rows("editedplayernames").Single(r=>FootballCatalog.Value(r,"playerid")==player.Id);
        Check(FootballCatalog.Value(editedNames,"firstname")=="Jude"&&FootballCatalog.Value(editedNames,"surname")=="Bellingham"&&FootballCatalog.Value(editedNames,"commonname")=="Jude Test"&&FootballCatalog.Value(editedNames,"playerjerseyname")=="BELLINGHAM","Name columns not updated");
        Check(!info.HasChanges,"Names remained pending after apply");
        var preferredFoot=Find<ComboBox>(info).Single(c=>Equals(c.Tag,"preferredfoot"));
        string oldPreferredFoot=FootballCatalog.Value(player.Row,"preferredfoot");
        preferredFoot.SelectedItem=preferredFoot.Items.Cast<FieldChoice>().First(c=>c.Value!=oldPreferredFoot);
        string expectedPreferredFoot=((FieldChoice)preferredFoot.SelectedItem).Value;
        var tabs=(TabControl)editor.FindName("EditorTabs");tabs.SelectedIndex=1;LinkedUiRenderChecks.Render(editor,"player-abilities");
        var abilities=Find<PlayerAbilitiesEditor>(editor).Single();
        var choices=Find<ComboBox>(abilities).ToDictionary(c=>(string)c.Tag,c=>c);
        Check(!choices.ContainsKey("preferredfoot"),"Preferred foot remained in Attributes");
        Check(choices["weakfootabilitytypecode"].Items.Cast<FieldChoice>().Select(c=>c.Value).SequenceEqual(new[]{"1","2","3","4","5"}),"Weak foot must offer 1-5 stars");
        string oldFoot=FootballCatalog.Value(player.Row,"weakfootabilitytypecode");
        choices["weakfootabilitytypecode"].SelectedItem=choices["weakfootabilitytypecode"].Items.Cast<FieldChoice>().First(c=>c.Value!=oldFoot);
        choices["skillmoves"].SelectedItem=choices["skillmoves"].Items.Cast<FieldChoice>().Single(c=>c.Value=="4");
        string selectedFoot=((FieldChoice)choices["weakfootabilitytypecode"].SelectedItem).Value;
        Check(abilities.HasChanges&&FootballCatalog.Value(player.Row,"weakfootabilitytypecode")==oldFoot,"Ability edits escaped draft");
        editor.Draft.Potential.Text="invalid";Check(!editor.TryApply()&&FootballCatalog.Value(player.Row,"weakfootabilitytypecode")==oldFoot,"Invalid attributes partially applied abilities");
        editor.Draft.Potential.Text=FootballCatalog.Value(player.Row,"potential");
        Check(FootballCatalog.Value(player.Row,"preferredfoot")==oldPreferredFoot,"Info foot escaped draft");
        Check(editor.TryApply()&&!abilities.HasChanges,"Abilities failed to apply");
        Check(FootballCatalog.Value(player.Row,"preferredfoot")==expectedPreferredFoot,"Preferred foot from Info not saved");
        Check(FootballCatalog.Value(player.Row,"weakfootabilitytypecode")==selectedFoot&&FootballCatalog.Value(player.Row,"skillmoves")=="4","Ability codes not saved");
        string path=Path.Combine(root,"Checks/output/player-info-test.db");File.WriteAllBytes(path,db.Serialize());var reopened=DatabaseDocument.Open(path,Path.Combine(data,"fifa_ng_db-meta.xml"));Check(new FootballCatalog(reopened).Rows("players").Any(r=>FootballCatalog.Value(r,"playerid")==player.Id&&FootballCatalog.Value(r,"wage")=="12345"),"Info lost after reopen");
        Check(new FootballCatalog(reopened).Rows("players").Any(r=>FootballCatalog.Value(r,"playerid")==player.Id&&FootballCatalog.Value(r,"role1")=="141"),"Role code not persisted");
        Check(new FootballCatalog(reopened).Rows("players").Any(r=>FootballCatalog.Value(r,"playerid")==player.Id&&FootballCatalog.Value(r,"role5")=="199"),"Unknown role lost on save");
        var savedNames=new FootballCatalog(reopened).Rows("editedplayernames").Single(r=>FootballCatalog.Value(r,"playerid")==player.Id);
        Check(FootballCatalog.Value(savedNames,"firstname")=="Jude"&&FootballCatalog.Value(savedNames,"surname")=="Bellingham"&&FootballCatalog.Value(savedNames,"commonname")=="Jude Test"&&FootballCatalog.Value(savedNames,"playerjerseyname")=="BELLINGHAM","Names lost after DB roundtrip");
        var savedAbilities=new FootballCatalog(reopened).Rows("players").Single(r=>FootballCatalog.Value(r,"playerid")==player.Id);
        Check(FootballCatalog.Value(savedAbilities,"weakfootabilitytypecode")==selectedFoot&&FootballCatalog.Value(savedAbilities,"skillmoves")=="4","Abilities lost on reopen");
        Check(FootballCatalog.Value(savedAbilities,"preferredfoot")==expectedPreferredFoot,"Info preferred foot lost on reopen");
        var bellingham=catalog.Entities("players").First(p=>p.Id=="252371");
        var linked=catalog.Rows("teamplayerlinks").Where(r=>FootballCatalog.Value(r,"playerid")==bellingham.Id&&!catalog.NationalTeamIds.Contains(FootballCatalog.Value(r,"teamid"))).ToArray();
        Check(linked.Length>1,"Bellingham fixture must cover multiple team links");
        catalog.NationalTeamIds.Add(FootballCatalog.Value(linked[1],"teamid"));
        var bellinghamEditor=new PlayerEditor(catalog,bellingham);
        LinkedUiRenderChecks.Render(bellinghamEditor,"player-info-bellingham");
        var bellinghamInfo=Find<PlayerInfoEditor>(bellinghamEditor).Single();
        var clubs=Find<ComboBox>(bellinghamInfo).Single(c=>c.DisplayMemberPath=="Name"&&c.Tag is null);
        Check(clubs.SelectedIndex==0,"Multiple team links left club empty");
        Check(!bellinghamInfo.HasChanges,"Selecting default club changed the draft");
        var jersey=Find<ComboBox>(bellinghamInfo).Single(t=>Equals(t.Tag,"clubjerseynumber"));
        Check((string?)jersey.SelectedItem==FootballCatalog.Value(linked[0],"jerseynumber"),"Default club jersey missing");
        var national=Find<ComboBox>(bellinghamInfo).Single(c=>Equals(c.Tag,"nationaljerseynumber"));
        Check((string?)national.SelectedItem==FootballCatalog.Value(linked[1],"jerseynumber"),"National shirt number missing");
        var occupied=catalog.Rows("teamplayerlinks").Where(r=>FootballCatalog.Value(r,"teamid")==FootballCatalog.Value(linked[1],"teamid")&&FootballCatalog.Value(r,"playerid")!=bellingham.Id).Select(r=>FootballCatalog.Value(r,"jerseynumber")).ToHashSet();
        Check(national.Items.Cast<string>().All(n=>n==FootballCatalog.Value(linked[1],"jerseynumber")||!occupied.Contains(n)),"Occupied national numbers offered");
        string free=national.Items.Cast<string>().First(n=>!occupied.Contains(n));national.SelectedItem=free;
        Check((string?)jersey.SelectedItem==FootballCatalog.Value(linked[0],"jerseynumber"),"National change affected club");
        Check(bellinghamEditor.TryApply(),"National jersey apply failed");Check(FootballCatalog.Value(linked[1],"jerseynumber")==free,"National jersey not saved");
        Console.WriteLine("PASS player Info: draft isolation, invalid-date atomicity, apply, DB roundtrip and 1400/960 renders");
    }
}
