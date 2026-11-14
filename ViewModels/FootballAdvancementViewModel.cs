using System.IO;
using System.Collections.ObjectModel;
using ATLink.Core;

namespace ATLink.ViewModels;

public sealed record FootballAdvancementField(FootballRuleField Field)
{
    public IReadOnlyList<string> Choices=>Enumerable.Range(Field.Definition.Minimum,Field.Definition.Maximum-Field.Definition.Minimum+1).Select(n=>n.ToString()).ToArray();
}
public sealed record FootballRoundingChoice(string Code,string Label);
public sealed class FootballAdvancementSort:FootballNotify
{
    readonly FootballAdvancementPhase owner;
    public CompetitionSortVm Sort {get;}
    public string Label=>Sort.Label;
    public bool Selected{get=>owner.Phase.Rules.Sorting.Any(s=>s.Code==Sort.Code);set{owner.SetSort(Sort,value);Notify();}}
    public FootballAdvancementSort(FootballAdvancementPhase owner,CompetitionSortVm sort){this.owner=owner;Sort=sort;}
    public void Refresh()=>Notify(nameof(Selected));
}
public sealed class FootballAdvancementPhase:FootballNotify
{
    public FootballPhaseViewModel Phase {get;}
    public string Name=>Phase.Name;
    public string Summary {get;}
    public string CarryNote {get;}
    public bool CanCarry {get;}
    public bool Tables=>Phase.Tables;
    public bool CarryPoints{get=>Phase.CarryPoints;set{Phase.CarryPoints=value;Notify();}}
    public IReadOnlyList<FootballAdvancementField> Fields {get;}
    public IReadOnlyList<FootballAdvancementSort> Sorts {get;}
    public IReadOnlyList<FootballRoundingChoice> RoundingChoices {get;}=[new("DOWN",ATLink.Core.UiText.Get("Round down")),new("UP",ATLink.Core.UiText.Get("Round up"))];
    public FootballAdvancementPhase(FootballPhaseViewModel phase,FootballPhaseViewModel? previous,FootballPhaseViewModel? next)
    {
        Phase=phase;
        Summary=next is null?ATLink.Core.UiText.Get("Final phase: no further internal qualification."):phase.Knockout?ATLink.Core.UiText.Format("{0} winner(s) → {1}", phase.ForwardTeams, next.Name):ATLink.Core.UiText.Format("Top {0} from each group → {1} · {2} teams", phase.Qualifiers, next.Name, phase.ForwardTeams);
        CanCarry=previous is not null&&previous.Tables&&phase.Tables&&phase.Groups==previous.Groups;
        CarryNote=CanCarry?ATLink.Core.UiText.Get("Keep points from the corresponding previous group. Teams cannot be mixed between groups when this is enabled."):ATLink.Core.UiText.Get("Points carryover requires a preceding table phase with the same number of groups.");
        Fields=phase.Rules.Fields.Where(f=>ATLink.Core.UiText.Source(f.Definition.Category)=="Qualification").Select(f=>new FootballAdvancementField(f)).ToArray();
        Sorts=phase.Rules.SortChoices.Select(s=>new FootballAdvancementSort(this,s)).ToArray();
    }
    public void SetSort(CompetitionSortVm sort,bool selected)
    {
        var rules=Phase.Rules;rules.DefaultSorting=false;
        var existing=rules.Sorting.FirstOrDefault(s=>s.Code==sort.Code);
        if(selected&&existing is null)rules.Sorting.Add(sort);
        if(!selected&&existing is not null)rules.Sorting.Remove(existing);
        foreach(var item in Sorts)item.Refresh();
    }
    public void Move(CompetitionSortVm sort,int direction)
    {
        var rules=Phase.Rules;rules.SelectedSort=rules.Sorting.FirstOrDefault(s=>s.Code==sort.Code);rules.MoveSort(direction);
    }
    public string Review=>ATLink.Core.UiText.Concat(Name+": "+Summary+"\n", ATLink.Core.UiText.Join("\n",Phase.Rules.ActiveOptions.Where(o=>ATLink.Core.UiText.Source(o.Definition.Category)=="Qualification"||o.Definition.Key=="standings_sort").Select(o=>"• "+o.Label+": "+o.Value+" ("+o.Origin+")")), (CarryPoints?ATLink.Core.UiText.Format("\n• Carry {0}% of points, rounded {1}", Phase.CarryPercentage, Phase.CarryRounding.ToLowerInvariant()):""));
}
public sealed class FootballAdvancementViewModel:FootballNotify
{
    readonly FootballFormatViewModel format;
    public FootballMatchupsViewModel Matchups {get;}
    public ObservableCollection<FootballAdvancementPhase> Phases {get;}=[];
    public string Error {get;private set;}="";
    public FootballAdvancementViewModel(FootballFormatViewModel format,FootballMatchupsViewModel matchups){this.format=format;Matchups=matchups;}
    public void Synchronize()
    {
        Matchups.Synchronize();Phases.Clear();
        for(int i=0;i<format.Phases.Count;i++)Phases.Add(new(format.Phases[i],i>0?format.Phases[i-1]:null,i+1<format.Phases.Count?format.Phases[i+1]:null));
        Notify(nameof(Review));
    }
    public string Review=>ATLink.Core.UiText.Join("\n\n",Phases.Select(p=>p.Review));
    public void Validate()
    {
        try
        {
            Matchups.Validate();var layout=Matchups.Layout(format.Snapshot());FootballCompetitionCompiler.Validate(layout);
            foreach(var phase in Phases)
            {
                if(phase.Tables&&!phase.Phase.Rules.DefaultSorting&&phase.Phase.Rules.Sorting.Count==0)throw new InvalidDataException(ATLink.Core.UiText.Concat(phase.Name, ATLink.Core.UiText.Get(": select at least one ranking criterion, or use Competition default.")));
                if(phase.CarryPoints&&!phase.CanCarry)throw new InvalidDataException(phase.Name+": "+phase.CarryNote);
            }
            Error="";Notify(nameof(Error));
        }
        catch(Exception ex){Error=ex.Message;Notify(nameof(Error));throw;}
    }
}
