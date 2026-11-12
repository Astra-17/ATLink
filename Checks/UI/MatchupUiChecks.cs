using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ATLink.Core;
using ATLink.Views;
using ATLink.ViewModels;
internal static class MatchupUiChecks
{
    public static void Run(CompetitionProjectContext context,Action<FrameworkElement,string,int,int> render)
    {
        void Check(bool ok,string message){if(!ok)throw new Exception(message);}
        void Reject(Action action,string message){try{action();throw new Exception(message);}catch(InvalidDataException){}}
        IEnumerable<DependencyObject> Descendants(DependencyObject node){for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++){var child=VisualTreeHelper.GetChild(node,i);yield return child;foreach(var nested in Descendants(child))yield return nested;}}
        void Next(FootballCreationWizard w)=>typeof(FootballCreationWizard).GetMethod("Next",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(w,[w,new RoutedEventArgs()]);
        var wizard=new FootballCreationWizard(context,84,_=>{});var model=wizard.Model;model.Type=model.Types.Single(t=>t.Kind==FootballCompetitionKind.GroupsAndKnockout);model.Region=model.Regions.Single(r=>r.Id==84);
        model.Format.SetPhases([new(){Name="Groups",Kind=FootballPhaseKind.Groups,Participants=20,Groups=2,QualifiersPerGroup=8},new(){Name="Knockout",Kind=FootballPhaseKind.Knockout,Participants=16,Rounds=4}]);
        foreach(var t in context.Teams.Take(20))model.Participants.Add(new(t));var steps=(TabControl)wizard.FindName("Steps");steps.SelectedIndex=1;render(wizard,"matchups-team-ids",1400,900);
        Check(Descendants(wizard).OfType<TextBlock>().Any(t=>t.Text=="("+model.Participants[0].Id+")"),"Team ID missing from team lists");
        Next(wizard);Check(steps.SelectedIndex==1&&((TabItem)((TabControl)wizard.FindName("TeamsAndRulesTabs")).SelectedItem).Header.ToString()=="Matchups","Next skipped matchups");
        var vm=model.Matchups;Check(vm.Rounds.Count==4&&vm.SelectedModel=="Current automatic","Initial all-round model");vm.SelectedModel="Crossed seeds";vm.ApplyModel();vm.Validate();
        string Pair(FootballMatchupSource s)=>(s.Ordinal==0?"A":"B")+s.Rank;
        Check(string.Join(",",vm.Rounds[0].Ties.Select(t=>Pair(t.Home.Selected!.Source)+"-"+Pair(t.Away.Selected!.Source)))=="A1-B8,A4-B5,A2-B7,A3-B6,B1-A8,B4-A5,B2-A7,B3-A6","UI crossed model differs from Super League");
        var quarter=vm.Rounds[1];var old=quarter.Seats[0].Selected!.Source;var chosen=quarter.Seats[2].Selected!;quarter.Seats[0].Selected=chosen;Check(quarter.Seats[0].Selected!.Source==chosen.Source&&quarter.Seats[2].Selected!.Source==old,"Choosing an occupied source did not swap places");vm.Validate();
        vm.Undo();Check(vm.Rounds[1].Seats[0].Selected!.Source==old,"Matchup undo");vm.Redo();Check(vm.Rounds[1].Seats[0].Selected!.Source==chosen.Source,"Matchup redo");
        var simulation=FootballFixtureSimulation.Build(vm.Layout(),model.Participants.Select(t=>t.Id).ToArray(),context.TeamName);Check(simulation.First(m=>m.PhaseIndex==2).HomeLabel==vm.Rounds[1].Seats[0].Selected!.Label,"Calendar labels ignored custom next-round source");
        string before=vm.Summary;vm.SelectedModel="Reproducible draw";vm.Seed="UI-draw";vm.ConfirmReplacement=_=>false;vm.ApplyModel();Check(vm.Summary==before,"Declined replacement changed matchups");vm.ConfirmReplacement=_=>true;vm.ApplyModel();string draw=vm.Summary;vm.ApplyModel();Check(vm.Summary==draw,"UI draw not reproducible");
        render(wizard,"matchups-full",1400,950);render(wizard,"matchups-small",960,680);Next(wizard);Check(steps.SelectedIndex==2,"Next did not leave validated matchups");
        var cup=new FootballCreationWizard(context,507,_=>{});var cm=cup.Model;cm.Type=cm.Types.Single(t=>t.Kind==FootballCompetitionKind.Cup);cm.Format.Phases[0].Participants="4";foreach(var t in context.Teams.Take(4))cm.Participants.Add(new(t));cm.Matchups.Synchronize();var entry=cm.Matchups.Rounds[0];entry.Seats[0].Selected=entry.Seats[2].Selected;
        var removed=cm.Participants[0];cm.Participants.RemoveAt(0);cm.Participants.Add(new(context.Teams.Skip(4).First()));cm.Matchups.Synchronize();Reject(cm.Matchups.Validate,"Removed entry club silently replaced");Check(cm.Matchups.Rounds[0].Seats.Any(s=>!s.Valid),"Missing source not highlighted");cm.Matchups.SelectedModel="Current automatic";cm.Matchups.ConfirmReplacement=_=>true;cm.Matchups.ApplyModel();cm.Matchups.Validate();
        cm.Format.Phases.Clear();cm.Format.SetPhases([new(){Name="League",Kind=FootballPhaseKind.League,Participants=4,QualifiersPerGroup=0}]);cm.Matchups.Synchronize();Check(cm.Matchups.NeedsReview&&cm.HasMatchups,"Removed customized rounds silently discarded");cm.Matchups.ApplyModel();cm.Matchups.Validate();Check(!cm.HasMatchups,"Pure league retained empty matchup step");
        var dynamic=new FootballCreationWizard(context,507,_=>{});dynamic.Model.Type=dynamic.Model.Types.Single(t=>t.Kind==FootballCompetitionKind.Cup);dynamic.Model.Format.Phases[0].Participants="4";dynamic.Model.Format.Phases[0].Legs="Home and away";dynamic.Model.Matchups.Synchronize();Check(dynamic.Model.Matchups.Rounds[0].Seats[0].Selected!.Label=="Entry slot 1","Unknown entry treated as confirmed club");dynamic.Model.Matchups.Validate();
        Console.WriteLine("PASS matchup UI: team IDs; Teams → Matchups → Schedule; Super League preset; all-round swaps; custom simulation labels; reproducible draw and replacement confirmation; history; stale participants; removed rounds; dynamic entries; 1400/960 renders.");
    }
}
