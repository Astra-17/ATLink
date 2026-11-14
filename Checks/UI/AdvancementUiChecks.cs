using System.IO;
using System.Windows;
using System.Windows.Controls;
using ATLink.Core;
using ATLink.ViewModels;
using ATLink.Views;

internal static class AdvancementUiChecks
{
    static void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS "+message);}
    static void Reject(Action action,string message){try{action();}catch(InvalidDataException){Console.WriteLine("PASS "+message);return;}throw new Exception("Expected refusal: "+message);}
    public static void Run(string root)
    {
        var app=new Application();app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("pack://application:,,,/ATLink;component/Themes/Studio.xaml")});
        string data=Path.Combine(root,"files/FC27"),meta=Path.Combine(data,"fifa_ng_db-meta.xml"),loc=Path.Combine(data,"loc/eng_us-meta.xml");
        using var c=new CompetitionProjectContext(CompdataProject.Open(Path.Combine(data,"compdata")),"FC27",DatabaseDocument.Open(Path.Combine(data,"fifa_ng_db.db"),meta),DatabaseDocument.Open(Path.Combine(data,"loc/eng_us.db"),loc),meta,loc);
        var original=c.Project.Files.ToDictionary(f=>f.RelativePath,f=>f.Text);
        var wizard=new FootballCreationWizard(c,84,_=>{});var m=wizard.Model;
        m.Type=m.Types.Single(t=>t.Kind==FootballCompetitionKind.GroupsAndKnockout);m.Region=m.Regions.Single(r=>r.Id==84);m.Name="Advancement check";
        m.Format.SetPhases([new(){Name="Groups",Kind=FootballPhaseKind.Groups,Participants=20,Groups=2,QualifiersPerGroup=2},new(){Name="Knockout",Kind=FootballPhaseKind.Knockout,Participants=4,Rounds=2}]);
        foreach(var t in c.Teams.Take(20))m.Participants.Add(new(t));
        m.Advancement.Synchronize();var routes=m.Matchups;var entry=routes.AllRounds[0];
        Check(routes.AllRounds.Count==3&&routes.Rounds.Count==2&&entry.Groups.Count==2,"All phases have destination cards; Teams matchups remain knockout-only");
        var first=entry.Seats[0].Selected!.Source;var other=entry.Seats[10].Selected!;entry.Seats[0].Selected=other;
        Check(entry.Seats[10].Selected!.Source==first,"Choosing an occupied team swaps the two group places");
        routes.Undo();Check(entry.Seats[0].Selected!.Source==first,"Group allocation Undo");routes.Redo();Check(entry.Seats[0].Selected!.Source==other.Source,"Group allocation Redo");
        routes.SelectedModel="Crossed seeds";routes.ApplyModel();
        var semi=routes.Rounds[0];Check(semi.Seats[0].Selected!.Source.Ordinal==0&&semi.Seats[0].Selected!.Source.Rank==1&&semi.Seats[1].Selected!.Source.Ordinal==1&&semi.Seats[1].Selected!.Source.Rank==2,"Crossed seeds keeps A1–B2 / B1–A2");
        var card=m.Advancement.Phases[0];Check(card.Sorts.Count==FootballRuleCatalogue.Sorts.Count,"Every supported ranking criterion has a checkbox");
        var rules=card.Phase.Rules;rules.Sorting.Clear();card.SetSort(new("POINTS","Points"),true);card.SetSort(new("WINS","Wins"),true);card.SetSort(new("GOALDIFF","Goal difference"),true);card.Move(new("GOALDIFF","Goal difference"),-1);
        Check(rules.Overrides()["standings_sort"].SequenceEqual(new[]{"POINTS","GOALDIFF","WINS"}),"Ranking order is shared with Settings and preserves priority");
        var draw=card.Fields.Single(f=>f.Field.Definition.Key=="advance_random_draw_event").Field;draw.UseDefault=false;draw.Enabled=true;
        var max=card.Fields.Single(f=>f.Field.Definition.Key=="advance_maxteamsassoc");max.Field.UseDefault=false;max.Field.Value="2";
        Check(max.Choices.SequenceEqual(Enumerable.Range(0,101).Select(n=>n.ToString()))&&rules.Overrides()["advance_random_draw_event"].Single()=="1","Draw uses a checkbox; association limits use validated choices");
        var simulation=FootballFixtureSimulation.Build(routes.Layout(),m.Participants.Select(t=>t.Id).ToArray(),c.TeamName);
        var actualGroupA=entry.Seats.Take(10).Select(s=>s.Selected!.Source.TeamId!).ToHashSet();
        var simulatedGroupA=simulation.Where(s=>s.PhaseIndex==0&&s.Match<5).SelectMany(s=>s.Teams).ToHashSet();
        Check(simulatedGroupA.SetEquals(actualGroupA),"Simulated group fixtures respect customized allocations");
        m.Advancement.Validate();
        var tabs=(TabControl)wizard.FindName("Steps");tabs.SelectedIndex=(int)FootballCreationStep.Advancement;
        Check(tabs.SelectedIndex==(int)FootballCreationStep.Advancement,m.Error);
        var view=new FootballAdvancementEditor{DataContext=m.Advancement};LinkedUiRenderChecks.Render(view,"advancement-routes");
        var inner=(TabControl)((DockPanel)view.Content).Children.OfType<TabControl>().Single();inner.SelectedIndex=1;LinkedUiRenderChecks.Render(view,"advancement-rules");
        Check(c.Project.Files.All(f=>f.Text==original[f.RelativePath]),"Viewing and editing a creation draft leaves native files unchanged");
        m.UpdateReview();Check(m.Review.Contains("Tie-break order")&&m.Review.Contains(other.Label),"Review includes ranking options and destinations");
        m.Create();Check(m.Completed,m.Error);
        int id=m.CreatedCompetitionId;var created=FootballCompetitionAdapter.Read(c,id);
        int groupA=created.Phases[0].NativeGroups[0];var inbound=c.Rows("advancement.txt").Where(r=>r[2]==groupA.ToString()).OrderBy(r=>int.Parse(r[3])).ToArray();
        Check(inbound.Length==10&&inbound[0][1]=="11"&&inbound[9][1]=="10","Native group allocation uses explicit source slots");
        Check(c.Rows("settings.txt").Where(r=>r[0]==created.Phases[0].NativeId.ToString()&&r[1]=="standings_sort").Select(r=>r[2]).SequenceEqual(new[]{"POINTS","GOALDIFF","WINS"}),"Native settings retain ordered criteria");
        Check(created.Phases[0].Matchups.Single().Seats[0].TeamId==other.Source.TeamId,"Read-back reconstructs group allocation");
        c.Execute("Assigned fixtures check",()=>FootballBehaviour.GenerateFixtures(c,id,m.Participants.Select(t=>t.Id).ToArray(),new(FootballCalendarMode.AssignedFixtures,new DateOnly(2026,8,19),7,"2100")));
        string fixtureFile=c.Project.Files.Single(f=>Path.GetFileName(f.RelativePath).StartsWith(c.Objects.Single(o=>o.Id==id).Code.ToLowerInvariant()+"_")).RelativePath;
        Check(c.Rows(fixtureFile).Where(r=>r[0]=="20260819").Take(5).SelectMany(r=>new[]{r[2],r[3]}).ToHashSet().SetEquals(actualGroupA),"Assigned native fixtures use the same customized groups as the simulation");c.Undo();
        string output=Path.Combine(root,"Checks/output/advancement-project-"+Guid.NewGuid().ToString("N"));c.Export(output);
        using(var reopened=c.OpenProject(output))Check(FootballCompetitionAdapter.Read(reopened,id).Phases[0].Matchups.Single().Seats[0].TeamId==other.Source.TeamId,"Export/reopen preserves destinations");
        c.Undo();Check(!c.Objects.Any(o=>o.Id==id),"Creation and advancement are one project transaction");c.Redo();Check(c.Objects.Any(o=>o.Id==id),"Project Redo restores competition and advancement");c.Undo();
        var custom=new FootballCreationViewModel(c,84,_=>{});custom.Type=custom.Types.Single(t=>t.Kind==FootballCompetitionKind.Custom);custom.Region=custom.Regions.Single(r=>r.Id==84);
        custom.Format.SetPhases([new(){Name="First groups",Kind=FootballPhaseKind.Groups,Participants=8,Groups=2,QualifiersPerGroup=2},new(){Name="Second groups",Kind=FootballPhaseKind.Groups,Participants=4,Groups=2,QualifiersPerGroup=1},new(){Name="Final",Kind=FootballPhaseKind.Knockout,Participants=2}]);
        custom.Advancement.Synchronize();var carry=custom.Advancement.Phases[1];Check(carry.CanCarry&&!custom.Advancement.Phases[0].CanCarry,"Points carryover only between compatible table phases");
        carry.CarryPoints=true;carry.Phase.CarryPercentage="50";carry.Phase.CarryRounding="UP";
        custom.Matchups.Allocate(custom.Matchups.AllRounds[1],false,true);custom.Advancement.Validate();
        Check(custom.SnapshotDraft().Phases[1].CarryPoints&&custom.SnapshotDraft().Phases[1].CarryPercentage==50,"Points carryover and rounding reach the compiler draft");
        var table=custom.Matchups.AllRounds[1];table.Seats[0].Selected=table.Seats[2].Selected;
        Reject(custom.Advancement.Validate,"Mixing previous groups with points carryover is refused");custom.Matchups.Undo();custom.Advancement.Validate();
        custom.Name="Carryover advancement check";custom.Create();Check(custom.Completed,custom.Error);
        var carryNative=FootballCompetitionAdapter.Read(c,custom.CreatedCompetitionId);int from=carryNative.Phases[0].NativeGroups[0],to=carryNative.Phases[1].NativeGroups[0];
        Check(c.Rows("advancement.txt").Where(r=>r[2]==to.ToString()).All(r=>r[0]==from.ToString())&&carryNative.Phases[1].CarryPoints&&carryNative.Phases[1].CarryPercentage==50&&carryNative.Phases[1].CarryRounding=="UP","Native carryover groups and 50% rounded-up settings agree");c.Undo();
        table.Seats[0].Selected=table.Seats[2].Selected;carry.CarryPoints=false;custom.Advancement.Validate();
        custom.Format.Phases.RemoveAt(1);custom.Matchups.Synchronize();Reject(custom.Matchups.Validate,"Removed customized table phase requires review");
        var dynamic=new FootballCreationViewModel(c,84,_=>{});dynamic.Type=dynamic.Types.Single(t=>t.Kind==FootballCompetitionKind.Cup);dynamic.Advancement.Synchronize();dynamic.Advancement.Validate();
        Check(dynamic.Matchups.Rounds[0].Seats[0].Selected!.Source.TeamId is null,"Dynamic entries remain entry slots, not fixed clubs");
        using(var reference=c.OpenProject(Path.Combine(root,"files/export/SL-CL-REF-55-03")))CreationLinkedGraphUiChecks.Run(c,reference,customAdvancement:true);
        Console.WriteLine("PASS visual advancement, shared Settings, native conversion, preservation, history and responsive layout. Game validation remains separate.");
    }
}
