namespace ATLink.Core;

public sealed record FootballStandingLabel(int Group,int Position,string Key,string Text,string Category,string Origin);
/// <summary>Read-only football labels, plus narrowly scoped native synchronization after sporting edits.</summary>
public static class FootballStandingLabels
{
    static readonly IReadOnlyDictionary<string,(string Label,string Category)> Keys=new Dictionary<string,(string,string)>
    {
        ["info_slot_champ"]=("Champion","Title"),["info_label_slot_champ"]=("Champion","Title"),
        ["info_slot_promo"]=("Promotion","Promotion"),["info_label_slot_promo"]=("Promotion","Promotion"),
        ["info_slot_promo_poss"]=("Promotion playoff","Playoff"),["info_label_slot_promo_playoff"]=("Promotion playoff","Playoff"),
        ["info_slot_releg"]=("Relegation","Relegation"),["info_label_slot_releg"]=("Relegation","Relegation"),
        ["info_slot_releg_poss"]=("Relegation playoff","Playoff"),["info_label_slot_releg_playoff"]=("Relegation playoff","Playoff"),
        ["info_label_slot_adv_group"]=("Qualification","Qualification"),["info_label_slot_adv_playoff"]=("Qualification playoff","Playoff"),
        ["info_label_slot_acl"]=("Asian continental qualification","Qualification"),["info_label_slot_uel"]=("Europa League qualification","Qualification")
    };
    static readonly IReadOnlyDictionary<string,string> Mirrors=new Dictionary<string,string>
    {
        ["info_slot_champ"]="info_label_slot_champ",["info_slot_promo"]="info_label_slot_promo",["info_slot_promo_poss"]="info_label_slot_promo_playoff",["info_slot_releg"]="info_label_slot_releg",["info_slot_releg_poss"]="info_label_slot_releg_playoff"
    };
    public static IReadOnlyList<FootballStandingLabel> Read(CompetitionProjectContext c,int competition)
    {
        var result=new List<FootballStandingLabel>();var model=FootballCompetitionAdapter.Read(c,competition);
        foreach(var phase in model.Phases)
        foreach(int group in phase.NativeGroups)
        {
            int size=c.Rows("standings.txt").Count(r=>r[0]==group.ToString());
            foreach(var rule in c.EffectiveRules(group).Where(r=>Keys.ContainsKey(r.Key)))
                if(int.TryParse(rule.Value,out int rank)&&rank>=1&&rank<=size)
                {var label=Keys[rule.Key];result.Add(new(group,rank,rule.Key,label.Label,label.Category,"Native competition setting"));}
            foreach(var route in c.Rows("advancement.txt").Where(r=>r[0]==group.ToString()))
                if(int.TryParse(route[1],out int rank)&&rank>=1&&rank<=size&&int.TryParse(route[2],out int destination)&&!FootballCompetitionAdapter.IsHidden(c,destination))
                    result.Add(new(group,rank,"route","Qualifies for "+c.ObjectPath(destination),"Qualification","Qualification route"));
        }
        return result.DistinctBy(r=>(r.Group,r.Position,r.Text)).OrderBy(r=>r.Group).ThenBy(r=>r.Position).ToArray();
    }
    public static void Sync(CompetitionProjectContext c,int competition,IReadOnlyList<FootballStandingLabel>? before=null)
    {
        if(c.Game!="FC27")return;
        var labels=Read(c,competition);var groups=FootballCompetitionAdapter.Read(c,competition).Phases.SelectMany(p=>p.NativeGroups).ToArray();
        if(before is not null)
        foreach(var stale in before.Where(l=>l.Key=="route"&&!labels.Any(n=>n.Key=="route"&&n.Group==l.Group&&n.Position==l.Position)))
        foreach(string key in new[]{"info_label_slot_adv_group","info_label_slot_adv_playoff"})
        {
            var values=c.Rows("settings.txt").Where(r=>r[0]==stale.Group.ToString()&&r[1]==key&&r[2]!=stale.Position.ToString()).Select(r=>r[2]).ToArray();c.SetRule(stale.Group,key,values);
        }
        foreach(int group in groups)
        foreach(var pair in Mirrors)
        {
            if(!c.Rows("settings.txt").Any(r=>r[1]==pair.Value))continue;
            var desired=labels.Where(l=>l.Group==group&&l.Key==pair.Key).Select(l=>l.Position.ToString()).Distinct().ToArray();
            var local=c.Rows("settings.txt").Where(r=>r[0]==group.ToString()&&r[1]==pair.Value).Select(r=>r[2]).ToList();
            if(before is not null)
                foreach(var previous in before.Where(l=>l.Group==group&&l.Key==pair.Key&&!desired.Contains(l.Position.ToString())))local.Remove(previous.Position.ToString());
            // Never replace customized or unknown markers. Derive missing visual labels only.
            foreach(string rank in desired)if(!local.Contains(rank))local.Add(rank);
            if(local.Count>0||before?.Any(l=>l.Group==group&&l.Key==pair.Key)==true)c.SetRule(group,pair.Value,local);
        }
        foreach(var phase in FootballCompetitionAdapter.Read(c,competition).Phases.Where(p=>p.Kind is FootballPhaseKind.Groups or FootballPhaseKind.League))
        foreach(int group in phase.NativeGroups)
        {
            foreach(var routes in c.Rows("advancement.txt").Where(r=>r[0]==group.ToString()&&int.TryParse(r[1],out int rank)&&rank>0).GroupBy(r=>c.EffectiveRules(int.Parse(r[2])).Any(v=>v.Key=="match_matchsituation"&&v.Value=="PLAYOFF")?"info_label_slot_adv_playoff":"info_label_slot_adv_group"))
            {
                if(!c.Rows("settings.txt").Any(r=>r[1]==routes.Key))continue;
                var values=c.Rows("settings.txt").Where(r=>r[0]==group.ToString()&&r[1]==routes.Key).Select(r=>r[2]).ToList();
                foreach(var route in routes)if(!values.Contains(route[1]))values.Add(route[1]);
                c.SetRule(group,routes.Key,values);
            }
        }
    }
    public static string QualificationChanges(CompetitionProjectContext c,int group,string key,int count)
    {
        var existing=c.Rows("settings.txt").Where(r=>r[0]==group.ToString()&&r[1].StartsWith("info_label_slot_")).Where(r=>int.TryParse(r[2],out int n)&&n<=count&&r[1]!=key).ToArray();
        return existing.Length==0?"":"\nReview existing labels at these places before confirming: "+string.Join(", ",existing.Select(r=>"place "+r[2]+" ("+(Keys.TryGetValue(r[1],out var known)?known.Label:"custom native label")+")"))+". Conflicting labels in the same category will be replaced by the selected qualification.";
    }
}
