using ATLink.Core;
namespace ATLink.ViewModels;

public sealed class FootballQualifyingRoundViewModel
{
    readonly CompetitionProjectContext context;
    readonly IReadOnlyDictionary<string,string[]> original;
    public FootballPhaseSettingsViewModel Essentials {get;}
    public IReadOnlyList<FootballPhaseSettingsViewModel> EssentialRounds=>[Essentials];
    public FootballRulesViewModel Rules {get;}
    public string Summary=>Essentials.Summary;
    public FootballQualifyingRoundViewModel(CompetitionProjectContext context,string name,int? phase=null)
    {
        this.context=context;
        var defaults=FootballRulesViewModel.Effective(context,phase.HasValue?context.Objects.Single(o=>o.Id==phase).ParentId:0);
        original=phase.HasValue?FootballRulesViewModel.Local(context,phase.Value,FootballPhaseKind.Knockout):new Dictionary<string,string[]>();
        Rules=new(FootballPhaseKind.Knockout,defaults,original);Rules.ConfigureEndings(true,false);
        Essentials=new(phase.HasValue?FootballPhaseSettingsAdapter.Read(context,phase.Value):new(),!phase.HasValue,phase){Name=name};
        Essentials.Configure(context,defaults);
    }
    public FootballQualifierRound Snapshot()
    {
        var rules=Rules.Overrides();
        return new(Essentials.Snapshot(),rules,original.Keys.Except(rules.Keys).ToArray());
    }
}

