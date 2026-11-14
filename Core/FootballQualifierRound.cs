namespace ATLink.Core;

public sealed record FootballQualifierRound(FootballPhaseSettings Settings,
    IReadOnlyDictionary<string,string[]> Rules, IReadOnlyList<string> ResetRules);

public static class FootballQualifierRounds
{
    public static void Apply(CompetitionProjectContext c,int competition,IReadOnlyList<FootballQualifierRound>? rounds,bool created)
    {
        if(rounds is null||rounds.Count==0)return;
        var phases=FootballCompetitionAdapter.Read(c,competition).Phases;
        if(rounds.Count!=phases.Count)throw new InvalidDataException(ATLink.Core.UiText.Get("Configure all five qualifying rounds."));
        for(int i=0;i<rounds.Count;i++)
        {
            var draft=rounds[i];int stage=phases[i].NativeId!.Value;
            foreach(var key in draft.ResetRules)
            {
                if(!FootballRuleCatalogue.IsEditableKey(key))throw new InvalidDataException(ATLink.Core.UiText.Get("Unknown qualifying rule."));
                c.SetRule(stage,key,[]);
            }
            foreach(var rule in draft.Rules)
            {
                FootballRuleCatalogue.Validate(rule.Key,rule.Value,FootballPhaseKind.Knockout);
                c.SetRule(stage,rule.Key,rule.Value);
            }
            FootballPhaseSettingsAdapter.Apply(c,stage,draft.Settings,created,phases[i].NativeGroups);
        }
    }
}
