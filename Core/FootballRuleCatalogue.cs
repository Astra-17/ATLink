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
        new("standings_pointswin",ATLink.Core.UiText.Get("Points for a win"),ATLink.Core.UiText.Get("Table & tie-breaks"),FootballRuleInput.Number,ATLink.Core.UiText.Get("Points awarded for winning a match."),true,true),
        new("standings_pointsdraw",ATLink.Core.UiText.Get("Points for a draw"),ATLink.Core.UiText.Get("Table & tie-breaks"),FootballRuleInput.Number,ATLink.Core.UiText.Get("Points awarded when neither team wins."),true,true),
        new("standings_pointsloss",ATLink.Core.UiText.Get("Points for a defeat"),ATLink.Core.UiText.Get("Table & tie-breaks"),FootballRuleInput.Number,ATLink.Core.UiText.Get("Points awarded for losing a match."),false,true),
        new("standings_sort",ATLink.Core.UiText.Get("Tie-break order"),ATLink.Core.UiText.Get("Table & tie-breaks"),FootballRuleInput.OrderedChoices,ATLink.Core.UiText.Get("Criteria are applied in the order shown."),true,true),
        new("rule_numsubsbench",ATLink.Core.UiText.Get("Players on the bench"),ATLink.Core.UiText.Get("Substitutions"),FootballRuleInput.Number,ATLink.Core.UiText.Get("Number of substitutes named for a match."),true,Maximum:30),
        new("rule_numsubsmatch",ATLink.Core.UiText.Get("Substitutions per match"),ATLink.Core.UiText.Get("Substitutions"),FootballRuleInput.Number,ATLink.Core.UiText.Get("Maximum number of players replaced during a match."),true,Maximum:30),
        new("rule_numsubmatchinterruptions",ATLink.Core.UiText.Get("Substitution windows"),ATLink.Core.UiText.Get("Substitutions"),FootballRuleInput.Number,ATLink.Core.UiText.Get("Number of interruptions available for substitutions."),false,Maximum:30),
        new("rule_allowadditionalsub",ATLink.Core.UiText.Get("Additional substitute"),ATLink.Core.UiText.Get("Substitutions"),FootballRuleInput.Toggle,ATLink.Core.UiText.Get("Allow the game's additional substitution rule in knockout matches."),KnockoutOnly:true),
        new("rule_bookings",ATLink.Core.UiText.Get("Cards"),ATLink.Core.UiText.Get("Discipline"),FootballRuleInput.Toggle,ATLink.Core.UiText.Get("Enable yellow and red cards."),true),
        new("rule_numyellowstored",ATLink.Core.UiText.Get("Yellow cards before suspension"),ATLink.Core.UiText.Get("Discipline"),FootballRuleInput.Number,ATLink.Core.UiText.Get("Accumulated yellow cards that trigger a suspension.")),
        new("rule_numgamesbanredmin",ATLink.Core.UiText.Get("Minimum red-card suspension"),ATLink.Core.UiText.Get("Discipline"),FootballRuleInput.Number,ATLink.Core.UiText.Get("Minimum number of matches missed after a straight red card.")),
        new("rule_numgamesbanredmax",ATLink.Core.UiText.Get("Maximum red-card suspension"),ATLink.Core.UiText.Get("Discipline"),FootballRuleInput.Number,ATLink.Core.UiText.Get("Maximum number of matches missed after a straight red card.")),
        new("rule_numgamesbandoubleyellowmin",ATLink.Core.UiText.Get("Minimum second-yellow suspension"),ATLink.Core.UiText.Get("Discipline"),FootballRuleInput.Number,ATLink.Core.UiText.Get("Minimum ban for a sending-off after two yellow cards.")),
        new("rule_numgamesbandoubleyellowmax",ATLink.Core.UiText.Get("Maximum second-yellow suspension"),ATLink.Core.UiText.Get("Discipline"),FootballRuleInput.Number,ATLink.Core.UiText.Get("Maximum ban for a sending-off after two yellow cards.")),
        new("rule_numgamesbanyellowsmin",ATLink.Core.UiText.Get("Minimum accumulated-card suspension"),ATLink.Core.UiText.Get("Discipline"),FootballRuleInput.Number,ATLink.Core.UiText.Get("Minimum ban after reaching the yellow-card threshold.")),
        new("rule_numgamesbanyellowsmax",ATLink.Core.UiText.Get("Maximum accumulated-card suspension"),ATLink.Core.UiText.Get("Discipline"),FootballRuleInput.Number,ATLink.Core.UiText.Get("Maximum ban after reaching the yellow-card threshold.")),
        new("rule_offsides",ATLink.Core.UiText.Get("Offside"),ATLink.Core.UiText.Get("Match"),FootballRuleInput.Toggle,ATLink.Core.UiText.Get("Apply the offside rule.")),
        new("rule_injuries",ATLink.Core.UiText.Get("Injuries"),ATLink.Core.UiText.Get("Match"),FootballRuleInput.Toggle,ATLink.Core.UiText.Get("Allow players to be injured during matches.")),
        new("match_matchimportance",ATLink.Core.UiText.Get("Match importance"),ATLink.Core.UiText.Get("Match"),FootballRuleInput.Number,ATLink.Core.UiText.Get("Game importance rating from 0 to 100.")),
        new("schedule_internationaldependency",ATLink.Core.UiText.Get("Allow matches during men's international windows"),ATLink.Core.UiText.Get("Calendar"),FootballRuleInput.Toggle,ATLink.Core.UiText.Get("Allow this competition to use dates reserved for men's international matches. Check player availability separately."),On:"NONE",Off:"MEN"),
        new("match_endruleko1leg",ATLink.Core.UiText.Get("Single-match tie resolution"),ATLink.Core.UiText.Get("Knockout"),FootballRuleInput.MatchEnding,ATLink.Core.UiText.Get("Extra time and penalties for a knockout match."),true,KnockoutOnly:true),
        new("match_endruleko2leg1",ATLink.Core.UiText.Get("First leg"),ATLink.Core.UiText.Get("Knockout"),FootballRuleInput.MatchEnding,ATLink.Core.UiText.Get("The tie continues in the return match."),KnockoutOnly:true),
        new("match_endruleko2leg2",ATLink.Core.UiText.Get("Return-leg tie resolution"),ATLink.Core.UiText.Get("Knockout"),FootballRuleInput.MatchEnding,ATLink.Core.UiText.Get("Use aggregate scores, then the selected tie-resolution rules."),true,KnockoutOnly:true),
        new("advance_random_draw_event",ATLink.Core.UiText.Get("Random draw"),ATLink.Core.UiText.Get("Qualification"),FootballRuleInput.Toggle,ATLink.Core.UiText.Get("Use the native random-draw event for advancement."),On:"1",Off:"0"),
        new("advance_maxteamsassoc",ATLink.Core.UiText.Get("Maximum teams per association"),ATLink.Core.UiText.Get("Qualification"),FootballRuleInput.Number,ATLink.Core.UiText.Get("Limit qualifiers from the same association. Zero is preserved as a game-defined value.")),
        new("advance_pointskeep",ATLink.Core.UiText.Get("Carry points from the previous phase"),ATLink.Core.UiText.Get("Qualification"),FootballRuleInput.GroupReference,ATLink.Core.UiText.Get("Available between table phases with matching groups."),TablesOnly:true),
        new("advance_pointskeeppercentage",ATLink.Core.UiText.Get("Points carried (%)"),ATLink.Core.UiText.Get("Qualification"),FootballRuleInput.Number,ATLink.Core.UiText.Get("Percentage of previous-phase points retained."),TablesOnly:true),
        new("advance_pointskeeprounding",ATLink.Core.UiText.Get("Points rounding"),ATLink.Core.UiText.Get("Qualification"),FootballRuleInput.OrderedChoices,ATLink.Core.UiText.Get("Round retained points up or down."),TablesOnly:true),
        new("rule_suspension",ATLink.Core.UiText.Get("Discipline reference"),ATLink.Core.UiText.Get("Internal"),FootballRuleInput.ObjectReference,ATLink.Core.UiText.Get("Reference to the object that owns the discipline rules, not an on/off setting."),ExpertOnly:true),
        new("rule_fixedmatchesdates",ATLink.Core.UiText.Get("Fixed-date behaviour"),ATLink.Core.UiText.Get("Internal"),FootballRuleInput.ObjectReference,ATLink.Core.UiText.Get("Scheduling configuration is retained in Expert."),ExpertOnly:true)
    ];
    public static readonly IReadOnlyDictionary<string,string> Sorts=new Dictionary<string,string>
    {
        ["POINTS"]=ATLink.Core.UiText.Get("Points"),["GOALDIFF"]=ATLink.Core.UiText.Get("Goal difference"),["GOALSFOR"]=ATLink.Core.UiText.Get("Goals scored"),["WINS"]=ATLink.Core.UiText.Get("Wins"),["GOALSAWAY"]=ATLink.Core.UiText.Get("Away goals scored"),["H2HPOINTS"]=ATLink.Core.UiText.Get("Head-to-head points"),["H2HGOALDIFF"]=ATLink.Core.UiText.Get("Head-to-head goal difference"),["H2HGOALSFOR"]=ATLink.Core.UiText.Get("Head-to-head goals scored"),["TEAMRATING"]=ATLink.Core.UiText.Get("Team rating"),["PREVRANK"]=ATLink.Core.UiText.Get("Previous ranking")
    };
    public static IEnumerable<FootballRuleEntry> Scalar(FootballPhaseKind? kind)=>Entries.Where(e=>e.Applies(kind)&&e.Input is FootballRuleInput.Number or FootballRuleInput.Toggle&&e.Key!="advance_pointskeeppercentage");
    public static bool IsEditableKey(string key)=>Entries.Any(e=>e.Key==key&&!e.ExpertOnly);
    public static void Validate(string key,IReadOnlyList<string> values,FootballPhaseKind? kind)
    {
        var entry=Entries.SingleOrDefault(e=>e.Key==key&&!e.ExpertOnly)??throw new InvalidDataException(ATLink.Core.UiText.Get("This option is managed in Expert."));
        if(!entry.Applies(kind))throw new InvalidDataException(ATLink.Core.UiText.Concat(entry.Label, ATLink.Core.UiText.Get(" is not compatible with this type of phase.")));
        if(values.Count==0)return;
        if(entry.Input==FootballRuleInput.Number&&(values.Count!=1||!int.TryParse(values[0],out int n)||n<entry.Minimum||n>entry.Maximum))throw new InvalidDataException(ATLink.Core.UiText.Format("{0}: enter a whole number from {1} to {2}.", entry.Label, entry.Minimum, entry.Maximum));
        if(entry.Input==FootballRuleInput.Toggle&&(values.Count!=1||values[0]!=entry.On&&values[0]!=entry.Off))throw new InvalidDataException(ATLink.Core.UiText.Concat(entry.Label, ATLink.Core.UiText.Get(": choose enabled or disabled.")));
        if(key=="standings_sort"&&(values.Any(v=>!Sorts.ContainsKey(v))||values.Distinct().Count()!=values.Count))throw new InvalidDataException(ATLink.Core.UiText.Get("Choose distinct, supported table criteria in the required order."));
        if(entry.Input==FootballRuleInput.MatchEnding)
        {
            var allowed=key=="match_endruleko2leg1"?new[]{"END"}:key=="match_endruleko2leg2"?new[]{"AGG","AWAY","ET","ET_AWAY","PENS"}:new[]{"ET","PENS"};
            if(values.Any(v=>!allowed.Contains(v))||values.Distinct().Count()!=values.Count)throw new InvalidDataException(ATLink.Core.UiText.Get("Check the knockout tie-resolution options."));
        }
        if(key=="advance_pointskeeprounding"&&(values.Count!=1||values[0] is not("UP" or "DOWN")))throw new InvalidDataException(ATLink.Core.UiText.Get("Choose round up or round down."));
    }
    public static void ValidateSet(IReadOnlyDictionary<string,string[]> rules,FootballPhaseKind? kind)
    {
        foreach(var rule in rules)Validate(rule.Key,rule.Value,kind);
        int? Read(string key)=>rules.TryGetValue(key,out var v)&&int.TryParse(v.FirstOrDefault(),out int n)?n:null;
        if(Read("rule_numsubsmatch") is int subs&&Read("rule_numsubsbench") is int bench&&subs>bench)throw new InvalidDataException(ATLink.Core.UiText.Get("Substitutions cannot exceed the number of bench players."));
        foreach(string prefix in new[]{"rule_numgamesbanred","rule_numgamesbandoubleyellow","rule_numgamesbanyellows"})if(Read(prefix+"min") is int min&&Read(prefix+"max") is int max&&min>max)throw new InvalidDataException(ATLink.Core.UiText.Get("A minimum suspension cannot exceed its maximum."));
    }
}

