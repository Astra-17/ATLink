using System.Data;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ATLink.Core;
using ATLink.Views;
internal static class TeamGroupingUiChecks
{
    static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    public static void Run(string root){
        var app=new Application();app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("pack://application:,,,/ATLink;component/Themes/Studio.xaml")});
        void Check(bool ok,string message){if(!ok)throw new Exception(message);}
        IEnumerable<T> Find<T>(DependencyObject d) where T:DependencyObject{
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(d);i++){var child=VisualTreeHelper.GetChild(d,i);if(child is T value)yield return value;foreach(var nested in Find<T>(child))yield return nested;}
        }
        foreach(var (file,meta,label) in new[]{
            ("files/fifa_ng_db.db","files/fifa_ng_db-meta.xml","fc26"),
            ("files/FC27/fifa_ng_db.db","files/FC27/fifa_ng_db-meta.xml","fc27"),
            ("files/FC27/Squads20261031154004222","files/FC27/fifa_ng_db-meta.xml","squad")}){
            var db=DatabaseDocument.Open(Path.Combine(root,file),Path.Combine(root,meta));var catalog=new FootballCatalog(db);var team=catalog.Entities("teams").First(t=>t.Id=="1");
            var page=new TeamWorkspace(catalog,team);var tabs=(WrapPanel)page.FindName("TabBar");var host=(ScrollViewer)page.FindName("Host");
            void Show(string key){typeof(TeamWorkspace).GetMethod("Show",Private)!.Invoke(page,new object[]{key});page.UpdateLayout();}
            void Tactics(string key){typeof(TeamWorkspace).GetMethod("ShowTacticsSection",Private)!.Invoke(page,new object[]{key});page.UpdateLayout();}
            var binds=(List<(Field Field,DataRow Row,Func<string> Get)>)typeof(TeamWorkspace).GetField("binds",Private)!.GetValue(page)!;
            int initialCount=binds.Count;
            Check(tabs.Children.Cast<Button>().Select(b=>(string)b.Tag).SequenceEqual(new[]{"Information","Club","Squad","Tactics","Appearance","Stadium"}),"Incorrect grouped tabs");
            Check(!db.HasChanges,"Opening grouped team editor changed database");
            foreach(string language in new[]{"fr","en"}){
                UiText.Current.SetLanguage(language);
                page=new TeamWorkspace(catalog,team);tabs=(WrapPanel)page.FindName("TabBar");host=(ScrollViewer)page.FindName("Host");
                binds=(List<(Field Field,DataRow Row,Func<string> Get)>)typeof(TeamWorkspace).GetField("binds",Private)!.GetValue(page)!;
                Check(binds.Count==initialCount,"Language changed field inventory");
                foreach(string key in new[]{"Information","Club","Squad","Tactics","Appearance","Stadium"}){
                    Show(key);LinkedUiRenderChecks.Render(page,"team-grouped-"+label+"-"+language+"-"+key);
                    if(key=="Squad"){var rosterGrid=Find<DataGrid>(host).Single(g=>Equals(g.Tag,"squad-grid"));Check(rosterGrid.ActualHeight>70&&rosterGrid.Columns.First().ActualWidth>=60,"Small window collapsed roster or columns");}
                }
                Show("Tactics");Tactics("Organisation");
                var buildUp=Find<ComboBox>(host).Single(c=>Equals(c.Tag,"buildupplay"));
                Check(!buildUp.IsEditable&&buildUp.Items.Cast<object>().Select(c=>c.GetType().GetProperty("Id")!.GetValue(c)?.ToString()).SequenceEqual(new[]{"1","2","3"}),"Build-up choices must exclude zero and raw codes");
                Check(buildUp.Items.Cast<object>().Select(c=>c.GetType().GetProperty("Name")!.GetValue(c)?.ToString()).SequenceEqual(language=="fr"?new[]{"Passe courte","Équilibré","Contre"}:new[]{"Short passing","Balanced","Counter"}),"Build-up labels missing");
                string originalBuildUp=FootballCatalog.Value(team.Row,"buildupplay");foreach(string code in new[]{"1","2","3"}){buildUp.SelectedValue=code;Check(binds.Single(b=>b.Field.Name=="buildupplay").Get()==code,"Build-up selection did not preserve game code");}buildUp.SelectedValue=originalBuildUp;
                Show("Tactics");Tactics("Formation");LinkedUiRenderChecks.Render(page,"team-grouped-"+label+"-"+language+"-Formation");
                var pitch=(Canvas)typeof(TeamWorkspace).GetField("pitch",Private)!.GetValue(page)!;
                Check(pitch.ActualHeight>100&&pitch.Children.OfType<Border>().Count()==11,"Formation lost constrained pitch or eleven players");
                Check(host.VerticalScrollBarVisibility==ScrollBarVisibility.Disabled,"Formation gained outer vertical scrolling");
                Show("Club");Show("Tactics");Check(host.VerticalScrollBarVisibility==ScrollBarVisibility.Disabled,"Tactics subtab not remembered");
                Tactics("Responsibilities");LinkedUiRenderChecks.Render(page,"team-grouped-"+label+"-"+language+"-Responsibilities");
                Tactics("Organisation");
            }
            Check(!db.HasChanges&&binds.Count==initialCount,"Navigation changed data or duplicated field bindings");
            Check(binds.GroupBy(b=>(b.Row,b.Field.Name)).All(g=>g.Count()==1),"Duplicate field controls after grouping");
            Show("Squad");
            var squadGrid=Find<DataGrid>(host).Single(g=>Equals(g.Tag,"squad-grid"));
            var ratingSamples=squadGrid.Items.Cast<TeamWorkspace.SquadRow>().OrderBy(p=>int.TryParse(p.Overall,out int n)?n:0).ToArray();
            foreach(var sample in new[]{ratingSamples.First(),ratingSamples.Last()}){squadGrid.SelectedItem=sample;page.UpdateLayout();var badge=Find<Border>(host).Single(b=>Equals(b.Tag,"squad-player-rating"));Check(((SolidColorBrush)badge.BorderBrush).Color==((SolidColorBrush)RatingColors.BorderFor(int.Parse(sample.Overall))).Color,"Squad rating does not use player editor colours");var photo=Find<ContentControl>(host).Single(c=>Equals(c.Tag,"squad-player-photo"));Check(ReferenceEquals(photo.Style,page.FindResource("PlayerPortraitFrame"))&&((Image)photo.Content).Clip is RectangleGeometry{RadiusX:37,RadiusY:37},"Squad photo does not use shared cropped portrait frame");}
            var firstSquad=(TeamWorkspace.SquadRow)squadGrid.Items[0];string squadPlayerId=firstSquad.Id;
            squadGrid.SelectedItem=firstSquad;page.UpdateLayout();
            Find<TextBox>(host).Single(b=>Equals(b.Tag,"jerseynumber")).Text="97";
            Find<TextBox>(host).Single(b=>Equals(b.Tag,"leagueappearances")).Text="12";
            Find<TextBox>(host).Single(b=>Equals(b.Tag,"leaguegoals")).Text="4";
            Find<TextBox>(host).Single(b=>Equals(b.Tag,"form")).Text="4";
            squadGrid.SelectedIndex=1;squadGrid.SelectedItem=firstSquad;page.UpdateLayout();
            Check(Find<TextBox>(host).Single(b=>Equals(b.Tag,"jerseynumber")).Text=="97","Selection lost roster changes");
            var squadSearch=Find<TextBox>(host).Single(b=>Equals(b.Tag,"squad-search"));squadSearch.Text=squadPlayerId;
            Check(squadGrid.Items.Count==1,"Roster ID search failed");squadSearch.Text="missing-player-xyz";Check(squadGrid.Items.Count==0,"Roster empty search failed");squadSearch.Text="";
            var positionFilter=Find<ComboBox>(host).Single(c=>Equals(c.Tag,"squad-position"));positionFilter.SelectedValue=firstSquad.Position;
            Check(squadGrid.Items.Cast<TeamWorkspace.SquadRow>().All(p=>p.Position==firstSquad.Position),"Roster position filter failed");positionFilter.SelectedIndex=0;
            Check(!db.HasChanges,"Roster editing changed source before Apply");
            Show("Appearance");
            var kitList=Find<ListBox>(host).Single(c=>Equals(c.Tag,"appearance-kit-list"));Check(kitList.Items.Count>0,"Team kits missing");
            var firstKitItem=(ListBoxItem)kitList.Items[0];kitList.SelectedItem=firstKitItem;page.UpdateLayout();
            var kitRow=(DataRow)firstKitItem.Tag.GetType().GetProperty("Row")!.GetValue(firstKitItem.Tag)!;
            string kitId=FootballCatalog.Value(kitRow,"teamkitid"),kitTeam=FootballCatalog.Value(kitRow,"teamtechid");
            Find<TextBox>(host).Single(b=>Equals(b.Tag,"powid")).Text="1001";
            Find<TextBox>(host).Single(b=>Equals(b.Tag,"teamcolorprimr")).Text="17";
            Find<TextBox>(host).Single(b=>Equals(b.Tag,"numberfonttype")).Text="12";
            var lockKit=Find<CheckBox>(host).Single(c=>Equals(c.Tag,"islocked"));bool kitLocked=lockKit.IsChecked!=true;lockKit.IsChecked=kitLocked;
            if(kitList.Items.Count>1){kitList.SelectedIndex=1;kitList.SelectedItem=firstKitItem;page.UpdateLayout();}
            Check(Find<TextBox>(host).Single(b=>Equals(b.Tag,"teamcolorprimr")).Text=="17","Kit selection lost pending colour");
            Check(!db.HasChanges,"Appearance changed source before Apply");
            kitList.SelectedItem=kitList.Items.Cast<ListBoxItem>().Single(i=>ReferenceEquals(i.Tag,firstKitItem.Tag));page.UpdateLayout();
            var usedKitIds=catalog.Rows("teamkits").Select(r=>long.Parse(FootballCatalog.Value(r,"teamkitid"),System.Globalization.CultureInfo.InvariantCulture)).ToHashSet();
            var existingKitTypes=catalog.Rows("teamkits").Where(r=>FootballCatalog.Value(r,"teamtechid")==team.Id||FootballCatalog.Value(r,"teamtechid")==FootballCatalog.Value(team.Row,"assetid")).Select(r=>long.Parse(FootballCatalog.Value(r,"teamkittypetechid"))).ToHashSet();
            long expectedKitType=0;while(existingKitTypes.Contains(expectedKitType))expectedKitType++;
            int kitCount=kitList.Items.Count;typeof(TeamWorkspace).GetMethod("AddAppearanceKit",Private)!.Invoke(page,null);page.UpdateLayout();
            Check(kitList.Items.Count==kitCount+1,"Kit template copy failed");
            Check(Find<TextBox>(host).Single(b=>Equals(b.Tag,"teamcolorprimr")).Text=="17","New kit lost template draft values");
            Check(Find<TextBox>(host).Single(b=>Equals(b.Tag,"teamkitid")).Text!=kitId,"Template copy reused kit ID");
            var clonedKit=(DataRow)((ListBoxItem)kitList.SelectedItem).Tag.GetType().GetProperty("Row")!.GetValue(((ListBoxItem)kitList.SelectedItem).Tag)!;
            Check(FootballCatalog.Value(clonedKit,"teamkittypetechid")==expectedKitType.ToString(),"New kit reused template type instead of next available type");
            string cloneId=FootballCatalog.Value(clonedKit,"teamkitid");DatabaseDocument.ValidateKeys(catalog.Table("teamkits"));
            long assignedKitId=long.Parse(cloneId,System.Globalization.CultureInfo.InvariantCulture);
            Check(assignedKitId>0&&!usedKitIds.Contains(assignedKitId),"Kit ID must be positive and free");
            if(usedKitIds.Max()<catalog.Table("teamkits").Fields.Single(f=>f.Name=="teamkitid").Maximum)Check(assignedKitId==usedKitIds.Max()+1,"Kit ID did not follow existing IDs");
            clonedKit["teamkitid"]=kitId;bool rejectedDuplicate=false;
            try{DatabaseDocument.ValidateKeys(catalog.Table("teamkits"));}catch(InvalidDataException){rejectedDuplicate=true;}finally{clonedKit["teamkitid"]=cloneId;}
            Check(rejectedDuplicate,"Duplicate club/kit pair accepted");
            Check(binds.GroupBy(b=>(b.Row,b.Field.Name)).All(g=>g.Count()==1),"Kit navigation duplicated bindings");
            typeof(TeamWorkspace).GetMethod("DeleteAppearanceKit",Private)!.Invoke(page,null);page.UpdateLayout();
            Check(kitList.Items.Count==kitCount&&clonedKit.RowState==DataRowState.Detached,"New kit deletion failed");
            Check(!binds.Any(b=>ReferenceEquals(b.Row,clonedKit)),"Deleted kit bindings retained");
            kitList.SelectedItem=kitList.Items.Cast<ListBoxItem>().Single(i=>ReferenceEquals(i.Tag,firstKitItem.Tag));page.UpdateLayout();
            typeof(TeamWorkspace).GetMethod("AddAppearanceKit",Private)!.Invoke(page,null);page.UpdateLayout();
            var persistedClone=(DataRow)((ListBoxItem)kitList.SelectedItem).Tag.GetType().GetProperty("Row")!.GetValue(((ListBoxItem)kitList.SelectedItem).Tag)!;
            string persistedCloneId=FootballCatalog.Value(persistedClone,"teamkitid");
            Check(FootballCatalog.Value(persistedClone,"teamkittypetechid")==expectedKitType.ToString(),"Deleted kit type was not made available again");
            var existingItem=kitList.Items.Cast<ListBoxItem>().First(i=>!ReferenceEquals(i.Tag,firstKitItem.Tag)&&!ReferenceEquals((DataRow)i.Tag.GetType().GetProperty("Row")!.GetValue(i.Tag)!,persistedClone));
            var removedKit=(DataRow)existingItem.Tag.GetType().GetProperty("Row")!.GetValue(existingItem.Tag)!;
            string removedKitId=FootballCatalog.Value(removedKit,"teamkitid"),removedKitTeam=FootballCatalog.Value(removedKit,"teamtechid");
            kitList.SelectedItem=existingItem;page.UpdateLayout();typeof(TeamWorkspace).GetMethod("DeleteAppearanceKit",Private)!.Invoke(page,null);page.UpdateLayout();
            Check(removedKit.RowState==DataRowState.Deleted&&!binds.Any(b=>ReferenceEquals(b.Row,removedKit)),"Existing kit deletion failed");
            Show("Information");
            var primaryLeague=Find<TextBox>(host).Single(b=>Equals(b.Tag,"leagueid"));
            Check(primaryLeague.Text=="13","Arsenal primary league is missing");
            var associatedCountry=Find<TextBox>(host).Single(b=>Equals(b.Tag,"associatedcountry"));
            string england=catalog.NationNames().GetValueOrDefault("14","14");
            Check(associatedCountry.IsReadOnly&&associatedCountry.Text==england,"League country missing or editable independently of league");
            var title=Find<TextBox>(host).Single(b=>Equals(b.Tag,"teamname"));string oldName=title.Text;title.Text="Grouped Team Test";
            Check(Find<TextBox>(host).Single(b=>Equals(b.Tag,"teamid")).IsReadOnly,"Team ID unlocked");
            Check(!Find<TextBox>(host).Any(b=>Equals(b.Tag,"teamstadiumcapacity")),"Capacity still in Identity");
            Show("Stadium");var capacity=Find<TextBox>(host).Single(b=>Equals(b.Tag,"teamstadiumcapacity"));capacity.Text="45000";
            Show("Club");
            Check(!Find<TextBox>(host).Any(b=>Equals(b.Tag,"attackrating")||Equals(b.Tag,"overallrating")),"Ratings still visible in Club");
            var attack=Find<TextBox>(host).Single(b=>Equals(b.Tag,"domesticprestige"));attack.Text="10";
            var prestigeSlider=Find<Slider>(host).Single(b=>Equals(b.Tag,"domesticprestige-slider"));
            Check(prestigeSlider.Value==10,"Club number did not update slider");prestigeSlider.Value=11;Check(attack.Text=="11","Club slider did not update number");
            Find<TextBox>(host).Single(b=>Equals(b.Tag,"leaguetitles")).Text="15";
            Show("Information");Check(title.Text=="Grouped Team Test","Tab switch lost text");
            var before=team.Row.ItemArray.ToArray();Show("Club");attack.Text="invalid";
            bool Apply()=>(bool)typeof(TeamWorkspace).GetMethod("ApplyEdits",Private)!.Invoke(page,null)!;
            Check(!Apply()&&team.Row.ItemArray.SequenceEqual(before),"Invalid field partially applied");
            attack.Text="11";
            Show("Tactics");Tactics("Organisation");
            var weakTraits=Find<Expander>(host).Single(e=>Equals(e.Tag,"trait1vweak"));weakTraits.IsExpanded=true;page.UpdateLayout();
            Check(Find<CheckBox>(weakTraits).Count()==23,"Named tactical trait list is incomplete");
            long expectedWeakTraits=long.Parse(FootballCatalog.Value(team.Row,"trait1vweak"),System.Globalization.CultureInfo.InvariantCulture);
            foreach(int bit in new[]{0,9}){var option=Find<CheckBox>(weakTraits).Single(c=>Equals(c.Tag,"trait1vweak-"+bit));option.IsChecked=option.IsChecked!=true;expectedWeakTraits^=1L<<bit;}
            Check(binds.Single(b=>ReferenceEquals(b.Row,team.Row)&&b.Field.Name=="trait1vweak").Get()==expectedWeakTraits.ToString(System.Globalization.CultureInfo.InvariantCulture),"Named trait toggles changed unrelated bits");weakTraits.IsExpanded=false;
            var depthSlider=Find<Slider>(host).Single(b=>Equals(b.Tag,"defensivedepth-slider"));depthSlider.Value=55;
            Check(Find<TextBox>(host).Single(b=>Equals(b.Tag,"defensivedepth")).Text=="55","Tactics slider did not update defensive depth");
            var positionSection=Find<Expander>(host).Single(e=>Equals(e.Tag,"formation-position-editor"));positionSection.IsExpanded=true;page.UpdateLayout();
            var positionSlot=Find<ComboBox>(host).Single(c=>Equals(c.Tag,"formation-position-slot"));positionSlot.SelectedIndex=1;page.UpdateLayout();
            var xInput=Find<TextBox>(host).Single(b=>Equals(b.Tag,"formation-position-x"));var yInput=Find<TextBox>(host).Single(b=>Equals(b.Tag,"formation-position-y"));
            xInput.Text="0.42";yInput.Text="0.67";string manualPosition=Array.IndexOf(PlayerProfileImport.PositionCodes,"CM").ToString(System.Globalization.CultureInfo.InvariantCulture);
            Find<ComboBox>(host).Single(c=>Equals(c.Tag,"formation-position-code")).SelectedValue=manualPosition;
            var formationBeforePositionApply=((FormationEditor)typeof(TeamWorkspace).GetField("formation",Private)!.GetValue(page)!).Formation.ItemArray.ToArray();
            xInput.Text="1.1";Check(!Apply(),"Invalid formation coordinate accepted");Check(((FormationEditor)typeof(TeamWorkspace).GetField("formation",Private)!.GetValue(page)!).Formation.ItemArray.SequenceEqual(formationBeforePositionApply),"Invalid coordinate partially wrote formation");xInput.Text="0.42";
            Tactics("Formation");
            var formation=(FormationEditor)typeof(TeamWorkspace).GetField("formation",Private)!.GetValue(page)!;
            Check(Math.Abs(formation.Slots[1].X-.42)<.00001&&Math.Abs(formation.Slots[1].Y-.67)<.00001&&formation.Slots[1].Position==manualPosition,"Manual position draft not updated");
            var pitchForDrop=(Canvas)typeof(TeamWorkspace).GetField("pitch",Private)!.GetValue(page)!;
            Check(pitchForDrop.Children.OfType<Border>().All(card=>card.AllowDrop),"Pitch slots are not drop targets");
            typeof(TeamWorkspace).GetMethod("ShowFormationDropZones",Private)!.Invoke(page,new object[]{true});Check(pitchForDrop.Children.OfType<System.Windows.Shapes.Rectangle>().Count(r=>Equals(r.Tag,"formation-drop-zone"))==11,"Pitch drop zones missing");typeof(TeamWorkspace).GetMethod("ShowFormationDropZones",Private)!.Invoke(page,new object[]{false});
            var benchSwap=formation.BenchSlots.First();string benchIdBefore=benchSwap.PlayerId,starterIdBefore=formation.Slots[0].PlayerId;
            typeof(TeamWorkspace).GetMethod("ExchangeFormationPlayers",Private)!.Invoke(page,new object[]{benchSwap,formation.Slots[0]});
            Check(formation.Slots[0].PlayerId==benchIdBefore&&benchSwap.PlayerId==starterIdBefore,"Bench-to-pitch exchange failed");
            typeof(TeamWorkspace).GetMethod("ExchangeFormationPlayers",Private)!.Invoke(page,new object[]{benchSwap,formation.Slots[0]});
            Check(formation.Slots[0].PlayerId==starterIdBefore,"Pitch-to-bench exchange failed");
            string first=formation.Slots[0].PlayerId,second=formation.Slots[1].PlayerId;
            typeof(TeamWorkspace).GetMethod("ExchangeFormationPlayers",Private)!.Invoke(page,new object[]{formation.Slots[0],formation.Slots[1]});
            Check(formation.Slots[0].PlayerId==second&&formation.Slots[1].PlayerId==first,"Player permutation broken");
            Tactics("Responsibilities");
            var captain=Find<ComboBox>(host).FirstOrDefault(c=>Equals(c.Tag,"captainid"));
            string? selectedCaptain=null;
            if(captain is not null&&captain.Items.Count>1){captain.SelectedIndex=captain.SelectedIndex==0?1:0;selectedCaptain=captain.SelectedValue?.ToString();}
            Check(Apply(),"Grouped editor failed to apply: "+((TextBlock)page.FindName("ErrorText")).Text);
            Check(FootballCatalog.Value(team.Row,"teamname")=="Grouped Team Test"&&FootballCatalog.Value(team.Row,"teamstadiumcapacity")=="45000","Moved fields not applied");
            if(selectedCaptain is not null)Check(FootballCatalog.Value(formation.Sheet,"captainid")==selectedCaptain,"Formation overrode responsibilities");
            string output=Path.Combine(root,"Checks/output/team-grouped-"+Guid.NewGuid().ToString("N")+"-"+label+(db.IsSquad?".squad":".db"));db.SaveAs(output);
            var saved=new FootballCatalog(DatabaseDocument.Open(output,Path.Combine(root,meta))).Rows("teams").Single(r=>FootballCatalog.Value(r,"teamid")==team.Id);
            Check(FootballCatalog.Value(saved,"teamname")=="Grouped Team Test"&&FootballCatalog.Value(saved,"trait1vweak")==expectedWeakTraits.ToString(System.Globalization.CultureInfo.InvariantCulture)&&FootballCatalog.Value(saved,"defensivedepth")=="55"&&FootballCatalog.Value(saved,"domesticprestige")=="11"&&FootballCatalog.Value(saved,"leaguetitles")=="15"&&FootballCatalog.Value(saved,"teamstadiumcapacity")=="45000","Grouped changes lost after reopening");
            var savedRoster=new FootballCatalog(DatabaseDocument.Open(output,Path.Combine(root,meta))).Rows("teamplayerlinks").Single(r=>FootballCatalog.Value(r,"teamid")==team.Id&&FootballCatalog.Value(r,"playerid")==squadPlayerId);
            Check(FootballCatalog.Value(savedRoster,"jerseynumber")=="97"&&FootballCatalog.Value(savedRoster,"leagueappearances")=="12"&&FootballCatalog.Value(savedRoster,"leaguegoals")=="4"&&FootballCatalog.Value(savedRoster,"form")=="4","Roster changes lost after reopening");
            var savedKit=new FootballCatalog(DatabaseDocument.Open(output,Path.Combine(root,meta))).Rows("teamkits").First(r=>FootballCatalog.Value(r,"teamkitid")==kitId&&FootballCatalog.Value(r,"teamtechid")==kitTeam);
            Check(FootballCatalog.Value(savedKit,"powid")=="1001"&&FootballCatalog.Value(savedKit,"teamcolorprimr")=="17"&&FootballCatalog.Value(savedKit,"numberfonttype")=="12"&&FootballCatalog.Value(savedKit,"islocked")== (kitLocked?"1":"0"),"Kit settings lost after reopening");
            var savedKits=new FootballCatalog(DatabaseDocument.Open(output,Path.Combine(root,meta))).Rows("teamkits").ToArray();
            Check(!savedKits.Any(r=>FootballCatalog.Value(r,"teamkitid")==removedKitId&&FootballCatalog.Value(r,"teamtechid")==removedKitTeam),"Deleted kit returned after reopening");
            Check(savedKits.Any(r=>FootballCatalog.Value(r,"teamkitid")==persistedCloneId&&FootballCatalog.Value(r,"teamkittypetechid")==expectedKitType.ToString()&&FootballCatalog.Value(r,"teamtechid")==team.Id&&FootballCatalog.Value(r,"teamcolorprimr")=="17"),"New kit missing after reopening");
            var savedFormation=new FootballCatalog(DatabaseDocument.Open(output,Path.Combine(root,meta))).Rows("formations").Single(r=>FootballCatalog.Value(r,"teamid")==team.Id);
            Check(Math.Abs(double.Parse(FootballCatalog.Value(savedFormation,"offset1x"),System.Globalization.CultureInfo.InvariantCulture)-.42)<.00001&&Math.Abs(double.Parse(FootballCatalog.Value(savedFormation,"offset1y"),System.Globalization.CultureInfo.InvariantCulture)-.67)<.00001&&FootballCatalog.Value(savedFormation,"position1")==manualPosition,"Manual formation coordinates or position lost after reopening");
            Console.WriteLine("PASS grouped team "+label+": six tabs, three tactics sections, field inventory, unchanged navigation, preserved draft, club and tactics sliders, honours, validation, formation and file roundtrip");
        }
    }
}
