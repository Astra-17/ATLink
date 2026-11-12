namespace ATLink.Core;

public enum FootballRuleInput { Number, Toggle, OrderedChoices, MatchEnding, GroupReference, ObjectReference }
public sealed record FootballRuleEntry(string Key,string Label,string Category,FootballRuleInput Input,string Help,bool Essential=false,bool TablesOnly=false,bool KnockoutOnly=false,bool ExpertOnly=false,int Minimum=0,int Maximum=100,string On="on",string Off="off")
{
    public bool Applies(FootballPhaseKind? kind)=>!ExpertOnly&&(kind is null||(!TablesOnly||kind is FootballPhaseKind.League or FootballPhaseKind.Groups)&&(!KnockoutOnly||kind==FootballPhaseKind.Knockout));
}
public static class FootballRuleCatalogue
{
    // Sources: native FC27 settings, DBM settings-display/settings-validation,
    // and the supplied Create League guide. Historical limits are not treated as FC27 limits.
    public static readonly FootballRuleEntry[] Entries=[
        new("standings_pointswin","Points for a win","Table & tie-breaks",FootballRuleInput.Number,"Points awarded for winning a match.",true,true),
        new("standings_pointsdraw","Points for a draw","Table & tie-breaks",FootballRuleInput.Number,"Points awarded when neither team wins.",true,true),
        new("standings_pointsloss","Points for a defeat","Table & tie-breaks",FootballRuleInput.Number,"Points awarded for losing a match.",false,true),
        new("standings_sort","Tie-break order","Table & tie-breaks",FootballRuleInput.OrderedChoices,"Criteria are applied in the order shown.",true,true),
        new("rule_numsubsbench","Players on the bench","Substitutions",FootballRuleInput.Number,"Number of substitutes named for a match.",true,Maximum:30),
        new("rule_numsubsmatch","Substitutions per match","Substitutions",FootballRuleInput.Number,"Maximum number of players replaced during a match.",true,Maximum:30),
        new("rule_numsubmatchinterruptions","Substitution windows","Substitutions",FootballRuleInput.Number,"Number of interruptions available for substitutions.",false,Maximum:30),
        new("rule_allowadditionalsub","Additional substitute","Substitutions",FootballRuleInput.Toggle,"Allow the game's additional substitution rule in knockout matches.",KnockoutOnly:true),
        new("rule_bookings","Cards","Discipline",FootballRuleInput.Toggle,"Enable yellow and red cards.",true),
        new("rule_numyellowstored","Yellow cards before suspension","Discipline",FootballRuleInput.Number,"Accumulated yellow cards that trigger a suspension."),
        new("rule_numgamesbanredmin","Minimum red-card suspension","Discipline",FootballRuleInput.Number,"Minimum number of matches missed after a straight red card."),
        new("rule_numgamesbanredmax","Maximum red-card suspension","Discipline",FootballRuleInput.Number,"Maximum number of matches missed after a straight red card."),
        new("rule_numgamesbandoubleyellowmin","Minimum second-yellow suspension","Discipline",FootballRuleInput.Number,"Minimum ban for a sending-off after two yellow cards."),
        new("rule_numgamesbandoubleyellowmax","Maximum second-yellow suspension","Discipline",FootballRuleInput.Number,"Maximum ban for a sending-off after two yellow cards."),
        new("rule_numgamesbanyellowsmin","Minimum accumulated-card suspension","Discipline",FootballRuleInput.Number,"Minimum ban after reaching the yellow-card threshold."),
        new("rule_numgamesbanyellowsmax","Maximum accumulated-card suspension","Discipline",FootballRuleInput.Number,"Maximum ban after reaching the yellow-card threshold."),
        new("rule_offsides","Offside","Match",FootballRuleInput.Toggle,"Apply the offside rule."),
        new("rule_injuries","Injuries","Match",FootballRuleInput.Toggle,"Allow players to be injured during matches."),
        new("match_matchimportance","Match importance","Match",FootballRuleInput.Number,"Game importance rating from 0 to 100."),
        new("schedule_internationaldependency","Allow matches during men's international windows","Calendar",FootballRuleInput.Toggle,"Allow this competition to use dates reserved for men's international matches. Check player availability separately.",On:"NONE",Off:"MEN"),
        new("match_endruleko1leg","Single-match tie resolution","Knockout",FootballRuleInput.MatchEnding,"Extra time and penalties for a knockout match.",true,KnockoutOnly:true),
        new("match_endruleko2leg1","First leg","Knockout",FootballRuleInput.MatchEnding,"The tie continues in the return match.",KnockoutOnly:true),
        new("match_endruleko2leg2","Return-leg tie resolution","Knockout",FootballRuleInput.MatchEnding,"Use aggregate scores, then the selected tie-resolution rules.",true,KnockoutOnly:true),
        new("advance_random_draw_event","Random draw","Qualification",FootballRuleInput.Toggle,"Use the native random-draw event for advancement.",On:"1",Off:"0"),
        new("advance_maxteamsassoc","Maximum teams per association","Qualification",FootballRuleInput.Number,"Limit qualifiers from the same association. Zero is preserved as a game-defined value."),
        new("advance_pointskeep","Carry points from the previous phase","Qualification",FootballRuleInput.GroupReference,"Available between table phases with matching groups.",TablesOnly:true),
        new("advance_pointskeeppercentage","Points carried (%)","Qualification",FootballRuleInput.Number,"Percentage of previous-phase points retained.",TablesOnly:true),
        new("advance_pointskeeprounding","Points rounding","Qualification",FootballRuleInput.OrderedChoices,"Round retained points up or down.",TablesOnly:true),
        new("rule_suspension","Discipline reference","Internal",FootballRuleInput.ObjectReference,"Reference to the object that owns the discipline rules, not an on/off setting.",ExpertOnly:true),
        new("rule_fixedmatchesdates","Fixed-date behaviour","Internal",FootballRuleInput.ObjectReference,"Scheduling configuration is retained in Expert.",ExpertOnly:true)
    ];
    public static readonly IReadOnlyDictionary<string,string> Sorts=new Dictionary<string,string>
    {
        ["POINTS"]="Points",["GOALDIFF"]="Goal difference",["GOALSFOR"]="Goals scored",["WINS"]="Wins",["GOALSAWAY"]="Away goals scored",["H2HPOINTS"]="Head-to-head points",["H2HGOALDIFF"]="Head-to-head goal difference",["H2HGOALSFOR"]="Head-to-head goals scored",["TEAMRATING"]="Team rating",["PREVRANK"]="Previous ranking"
    };
    public static IEnumerable<FootballRuleEntry> Scalar(FootballPhaseKind? kind)=>Entries.Where(e=>e.Applies(kind)&&e.Input is FootballRuleInput.Number or FootballRuleInput.Toggle&&e.Key!="advance_pointskeeppercentage");
    public static bool IsEditableKey(string key)=>Entries.Any(e=>e.Key==key&&!e.ExpertOnly);
    public static void Validate(string key,IReadOnlyList<string> values,FootballPhaseKind? kind)
    {
        var entry=Entries.SingleOrDefault(e=>e.Key==key&&!e.ExpertOnly)??throw new InvalidDataException("This option is managed in Expert.");
        if(!entry.Applies(kind))throw new InvalidDataException(entry.Label+" is not compatible with this type of phase.");
        if(values.Count==0)return;
        if(entry.Input==FootballRuleInput.Number&&(values.Count!=1||!int.TryParse(values[0],out int n)||n<entry.Minimum||n>entry.Maximum))throw new InvalidDataException($"{entry.Label}: enter a whole number from {entry.Minimum} to {entry.Maximum}.");
        if(entry.Input==FootballRuleInput.Toggle&&(values.Count!=1||values[0]!=entry.On&&values[0]!=entry.Off))throw new InvalidDataException(entry.Label+": choose enabled or disabled.");
        if(key=="standings_sort"&&(values.Any(v=>!Sorts.ContainsKey(v))||values.Distinct().Count()!=values.Count))throw new InvalidDataException("Choose distinct, supported table criteria in the required order.");
        if(entry.Input==FootballRuleInput.MatchEnding)
        {
            var allowed=key=="match_endruleko2leg1"?new[]{"END"}:key=="match_endruleko2leg2"?new[]{"AGG","AWAY","ET","ET_AWAY","PENS"}:new[]{"ET","PENS"};
            if(values.Any(v=>!allowed.Contains(v))||values.Distinct().Count()!=values.Count)throw new InvalidDataException("Check the knockout tie-resolution options.");
        }
        if(key=="advance_pointskeeprounding"&&(values.Count!=1||values[0] is not("UP" or "DOWN")))throw new InvalidDataException("Choose round up or round down.");
    }
    public static void ValidateSet(IReadOnlyDictionary<string,string[]> rules,FootballPhaseKind? kind)
    {
        foreach(var rule in rules)Validate(rule.Key,rule.Value,kind);
        int? Read(string key)=>rules.TryGetValue(key,out var v)&&int.TryParse(v.FirstOrDefault(),out int n)?n:null;
        if(Read("rule_numsubsmatch") is int subs&&Read("rule_numsubsbench") is int bench&&subs>bench)throw new InvalidDataException("Substitutions cannot exceed the number of bench players.");
        foreach(string prefix in new[]{"rule_numgamesbanred","rule_numgamesbandoubleyellow","rule_numgamesbanyellows"})if(Read(prefix+"min") is int min&&Read(prefix+"max") is int max&&min>max)throw new InvalidDataException("A minimum suspension cannot exceed its maximum.");
    }
}

