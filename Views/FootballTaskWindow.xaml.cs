using System.IO;
using System.Windows;
using System.Windows.Controls;
using ATLink.Core;
using ATLink.ViewModels;
namespace ATLink.Views;
public partial class FootballTaskWindow:Window
{
    readonly FootballSeasonViewModel model;readonly FootballSeasonTask? original;
    public FootballSeasonTask? Result {get;private set;}
    public sealed class ParameterChoice{public required string Label{get;init;}public required IReadOnlyList<FootballChoice> Options{get;init;}public FootballChoice? Selected{get;set;}}
    public FootballTaskWindow(FootballSeasonViewModel model,FootballSeasonTask? original=null)
    {
        this.model=model;this.original=original;InitializeComponent();ActionChoice.ItemsSource=FootballSeason.Actions.Where(a=>model.Context.Game=="FC27"||a.Code!="FillFromLeagueInOrder").ToArray();
        ActionChoice.SelectedItem=FootballSeason.Actions.FirstOrDefault(a=>a.Code==original?.Action)??FootballSeason.Actions[0];
    }
    string Label(CompetitionObject o)
    {
        if(FootballCompetitionAdapter.IsHidden(model.Context,o.Id))return "Incoming teams · "+model.Context.ObjectName(o.ParentId==model.CompetitionId?o.Id:model.CompetitionId??o.ParentId);
        return model.Context.ObjectPath(o.Id);
    }
    IReadOnlyList<FootballChoice> TargetOptions(SeasonAction action)
    {
        if(model.CompetitionId is not int id)return [new(0,action.TargetKind==5?"Incoming teams":action.TargetKind==3?"This competition":"Final played phase")];
        if(action.Code=="UpdateTable"&&model.Context.Game=="FC27")
            return model.Competitions.Where(choice=>choice.Id>0).OrderByDescending(choice=>choice.Id==id).ToArray();
        var scope=model.Context.Scope(id);return model.Context.Objects.Where(o=>scope.Contains(o.Id)&&o.Kind==action.TargetKind).Select(o=>new FootballChoice(o.Id,Label(o))).ToArray();
    }
    IReadOnlyList<FootballChoice> Options(SeasonParameter parameter,SeasonAction action)
    {
        var c=model.Context;
        return parameter.Kind switch
        {
            SeasonParameterKind.Team=>model.Clubs,
            SeasonParameterKind.League=>!model.Existing&&action.Code is "ClearLeagueStats" or "UpdateLeagueStats" or "UpdateLeagueTable"?[new(-1,"This competition's new league"),..model.Leagues]:model.Leagues,
            SeasonParameterKind.Competition=>model.Competitions.Where(x=>x.Id>0).ToArray(),
            SeasonParameterKind.Nation=>model.Countries,
            SeasonParameterKind.Coefficient=>Enumerable.Range(1,128).Select(n=>new FootballChoice(n,n.ToString())).ToArray(),
            SeasonParameterKind.Group=>model.CompetitionId is int id?c.Objects.Where(o=>o.Kind==5&&c.Scope(id).Contains(o.Id)&&!FootballCompetitionAdapter.IsHidden(c,o.Id)).Select(o=>new FootballChoice(o.Id,c.ObjectPath(o.Id))).ToArray():[new(0,"Final results group")],
            SeasonParameterKind.Position when model.CompetitionId is int id&&action.Code=="UpdateTable"&&parameter.Label=="Finishing position"=>
                Enumerable.Range(1,LargestPlayedGroup(id)).Select(n=>new FootballChoice(n,n.ToString())).ToArray(),
            SeasonParameterKind.Position when model.Existing&&action.Code=="UpdateTable"=>Enumerable.Range(1,128).Select(n=>new FootballChoice(n,n.ToString())).ToArray(),
            _=>!model.Existing&&action.Code=="FillWithTeam"&&parameter.Kind==SeasonParameterKind.Position?[new(1,"Next free place")]:Enumerable.Range(1,Math.Max(1,model.Existing&&TargetChoice.SelectedItem is FootballChoice t&&action.TargetKind==5?model.Context.Rows("standings.txt").Count(r=>r[0]==t.Id.ToString()):parameter.Label=="Finishing position"?model.FinalCapacity:model.Capacity)).Select(n=>new FootballChoice(n,n.ToString())).ToArray()
        };
    }
    int LargestPlayedGroup(int id)
    {
        var context=model.Context;
        var scope=context.Scope(id);
        var groups=context.Objects.Where(o=>o.Kind==5&&scope.Contains(o.Id)&&!FootballCompetitionAdapter.IsHidden(context,o.Id))
            .Select(o=>o.Id.ToString()).ToHashSet();
        return context.Rows("standings.txt").Where(row=>groups.Contains(row[0]))
            .GroupBy(row=>row[0]).Select(group=>group.Count()).DefaultIfEmpty(1).Max();
    }
    void ActionChanged(object sender,SelectionChangedEventArgs e)
    {
        if(TargetChoice is null||ActionChoice.SelectedItem is not SeasonAction action)return;var targets=TargetOptions(action);TargetChoice.ItemsSource=targets;TargetChoice.SelectedItem=targets.FirstOrDefault(t=>t.Id==original?.Target.Value)??targets.FirstOrDefault();
        Parameters.ItemsSource=action.Parameters.Select((p,i)=>{var options=Options(p,action);return new ParameterChoice{Label=p.Label,Options=options,Selected=options.FirstOrDefault(o=>o.Id==original?.Parameters.ElementAtOrDefault(i))??options.FirstOrDefault()};}).ToArray();
    }
    public bool ConfirmRule()
    {
        try
        {
            if(ActionChoice.SelectedItem is not SeasonAction action||TargetChoice.SelectedItem is not FootballChoice target)throw new InvalidDataException("Choose a rule and its destination.");
            var fields=Parameters.Items.Cast<ParameterChoice>().ToArray();if(fields.Any(p=>p.Selected is null))throw new InvalidDataException("Complete every field using a named choice.");
            Result=new(action.Code,new(target.Id),fields.Select(p=>p.Selected!.Id).ToArray(),original?.OriginalRow);
            if(model.CompetitionId is int id)FootballSeason.Validate(model.Context,id,Result);return true;
        }
        catch(Exception ex){ErrorText.Text=ex.Message;return false;}
    }
    void Confirm(object sender,RoutedEventArgs e){if(ConfirmRule())DialogResult=true;}
    void Cancel(object sender,RoutedEventArgs e)=>DialogResult=false;
}
