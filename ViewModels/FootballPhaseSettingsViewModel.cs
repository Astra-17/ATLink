using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using ATLink.Core;
namespace ATLink.ViewModels;

public sealed record FootballSettingChoice(string Label,FootballSettingMode Mode){public override string ToString()=>Label;}
public sealed record FootballPolicyChoice(string Label,bool Available,string Explanation){public override string ToString()=>Label;}
public sealed class FootballPhaseSettingsViewModel:FootballNotify
{
    CompetitionProjectContext? context;
    readonly FootballPhaseSettings original;
    readonly int? nativeId;
    readonly List<FootballTieVenueViewModel> ties=[];
    readonly HashSet<int> discardedTies=[];
    public IReadOnlyList<FootballTieVenueViewModel> AllTieVenues=>ties;
    public void DiscardTiesAfter(int count){foreach(var tie in ties.Skip(count))discardedTies.Add(tie.Ordinal);if(ties.Count>count)ties.RemoveRange(count,ties.Count-count);}
    int tieCount;bool twoLegs;
    public IReadOnlyList<FootballTieVenueViewModel> TieVenues=>ties.Take(tieCount).ToArray();
    public void ConfigureTies(int count,bool bothLegs)
    {
        tieCount=count;twoLegs=bothLegs;if(context is null)return;
        while(ties.Count<count)
        {
            int index=ties.Count;var tie=new FootballTieVenueViewModel(context,(!discardedTies.Contains(index)?original.TieVenues.FirstOrDefault(t=>t.Ordinal==index):null)??new(index,new()),()=>CurrentVenue,bothLegs);
            tie.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(FootballTieVenueViewModel.Summary)){Notify(nameof(Summary));Notify(nameof(Error));}};ties.Add(tie);
        }
        foreach(var tie in ties)tie.TwoLegs=bothLegs;
    }
    string CurrentVenue=>SingleStadium?Stadium?.Name??"Choose a stadium":VenueMode.Mode==FootballSettingMode.Inherit?InheritedVenue:NativeVenue;
    public string Name {get;set;}="Phase";
    public bool Supported=>context?.Game=="FC27";
    public string SupportNote=>Supported?"Amounts are native game values. Currency, recipients and cumulative distribution are not confirmed; existing distribution settings stay unchanged.":"These phase settings are not verified for this game. Native values remain preserved in Expert.";
    public IReadOnlyList<FootballSettingChoice> PrizeModes {get;}=[new("Keep existing value",FootballSettingMode.Preserve),new("Competition default",FootballSettingMode.Inherit),new("Set prize amount",FootballSettingMode.Value)];
    FootballSettingChoice prizeMode;
    public FootballSettingChoice PrizeMode{get=>prizeMode;set{if(value is null||value==prizeMode)return;prizeMode=value;Updated();}}
    string prize;
    public string Prize{get=>prize;set{prize=value;Updated();}}
    public bool CustomPrize=>PrizeMode.Mode==FootballSettingMode.Value;
    bool setDrawDate,resetDrawDate;
    public bool SetDrawDate {get=>setDrawDate;set{setDrawDate=value;if(value)ResetDrawDate=false;Notify();}}
    public bool ResetDrawDate {get=>resetDrawDate;set{resetDrawDate=value;if(value)SetDrawDate=false;Notify();}}
    public DateTime? DrawDate {get;set;}
    public string InheritedPrize {get;private set;}="Competition default";
    public string NativePrize {get;private set;}="No local prize override";
    public string NativeVenue {get;private set;}="Usual home grounds";
    public string DisciplineOrigin {get;private set;}="Competition discipline rules";
    public IReadOnlyList<FootballSettingChoice> VenueModes {get;}=[new("Keep existing venue",FootballSettingMode.Preserve),new("Competition default / usual home grounds",FootballSettingMode.Inherit),new("One selected stadium",FootballSettingMode.Value)];
    FootballSettingChoice venueMode;
    public FootballSettingChoice VenueMode{get=>venueMode;set{if(value is null||value==venueMode)return;venueMode=value;Updated();}}
    public bool SingleStadium=>VenueMode.Mode==FootballSettingMode.Value;
    string stadiumSearch="";
    public string StadiumSearch{get=>stadiumSearch;set{stadiumSearch=value;Notify();Notify(nameof(StadiumChoices));}}
    IReadOnlyList<FootballStadium> stadiums=[];
    public IReadOnlyList<FootballStadium> StadiumChoices=>stadiums.Where(s=>s.Name.Contains(StadiumSearch,StringComparison.CurrentCultureIgnoreCase)||s.Id.ToString()==StadiumSearch).ToArray();
    FootballStadium? stadium;
    public FootballStadium? Stadium{get=>stadium;set{stadium=value;Updated();}}
    public string InheritedVenue {get;private set;}="Usual home grounds";
    public IReadOnlyList<FootballPolicyChoice> HostChoices {get;}=[new("Selected host stadiums",false,FootballPhaseSettingsAdapter.HostLimitation)];
    public IReadOnlyList<FootballPolicyChoice> DisciplineChoices {get;}=[new("Keep existing discipline rules / carry yellow cards forward",true,"Existing discipline rules and acquired suspensions remain unchanged."),new("Clear accumulated yellow cards before this phase",false,FootballPhaseSettingsAdapter.YellowLimitation)];
    public FootballPolicyChoice DisciplineChoice=>DisciplineChoices[0];
    public string HostLimitation=>FootballPhaseSettingsAdapter.HostLimitation;
    public string YellowLimitation=>FootballPhaseSettingsAdapter.YellowLimitation;
    public string Error{get{try{_ =Snapshot();return "";}catch(Exception ex){return ex.Message;}}}
    public string PrizeError=>CustomPrize&&(!int.TryParse(CleanPrize(),NumberStyles.None,CultureInfo.InvariantCulture,out int n)||n<0)?"Enter a whole amount from 0 to 2,147,483,647.":"";
    public string VenueError
    {
        get
        {
            if(!SingleStadium)return "";
            try{if(context is null||Stadium is null)return "Choose a stadium.";FootballPhaseSettingsAdapter.Validate(context,new(){Stadium=new(FootballSettingMode.Value,Stadium.Id.ToString())},nativeId);return "";}catch(Exception ex){return ex.Message;}
        }
    }
    public string Summary=>"Prize money: "+(CustomPrize?FootballPhaseSettingsAdapter.Amount(CleanPrize()):PrizeMode.Mode==FootballSettingMode.Inherit?InheritedPrize:NativePrize)+" · Venue: "+CurrentVenue+" · Existing discipline retained"+(TieVenues.Count>0?"\n"+string.Join("\n",TieVenues.Select(t=>t.Summary)):"");
    public FootballPhaseSettingsViewModel(FootballPhaseSettings settings,bool created,int? nativeId)
    {
        original=settings;this.nativeId=nativeId;
        prizeMode=PrizeModes.Single(m=>m.Mode==(created&&settings.Prize.Mode==FootballSettingMode.Preserve?FootballSettingMode.Value:settings.Prize.Mode));
        prize=int.TryParse(settings.Prize.Value,NumberStyles.None,CultureInfo.InvariantCulture,out int amount)?amount.ToString("N0",CultureInfo.GetCultureInfo("en-US")):settings.Prize.Value??"0";venueMode=VenueModes.Single(m=>m.Mode==(created&&settings.Stadium.Mode==FootballSettingMode.Preserve?FootballSettingMode.Inherit:settings.Stadium.Mode));
    }
    public void Configure(CompetitionProjectContext c,IReadOnlyDictionary<string,string[]> defaults)
    {
        context=c;stadiums=FootballPhaseSettingsAdapter.Stadiums(c);
        if(DrawDate is null&&int.TryParse(original.DrawDate.Value,out int offset))DrawDate=c.ReferenceDate.AddDays(offset).ToDateTime(TimeOnly.MinValue);
        if(nativeId is int own&&c.Objects.FirstOrDefault(o=>o.Id==own) is {} obj)defaults=c.EffectiveRules(obj.ParentId).GroupBy(r=>r.Key).ToDictionary(g=>g.Key,g=>g.Select(r=>r.Value).ToArray());
        if(int.TryParse(original.Stadium.Value,out int id))stadium=stadiums.FirstOrDefault(s=>s.Id==id);
        string Venue(string? v)=>int.TryParse(v,out int n)?stadiums.FirstOrDefault(s=>s.Id==n)?.Name??"Unavailable stadium ("+v+")":"Usual home grounds";
        InheritedPrize=FootballPhaseSettingsAdapter.Amount(defaults.GetValueOrDefault("info_prize_money")?.FirstOrDefault()??"0");
        InheritedVenue=Venue(defaults.GetValueOrDefault("match_stadium")?.FirstOrDefault());
        NativePrize=original.Prize.Value is null?InheritedPrize:FootballPhaseSettingsAdapter.Amount(original.Prize.Value);
        NativeVenue=original.Stadium.Value is null?InheritedVenue:Venue(original.Stadium.Value);
        if(nativeId is int phase)
        {
            var prizeRows=c.Rows("settings.txt").Where(r=>r[0]==phase.ToString()&&r[1]=="info_prize_money").ToArray();
            if(prizeRows.Length>1)NativePrize="Multiple native values (Expert)";
            var venues=c.Rows("settings.txt").Where(r=>r[0]==phase.ToString()&&r[1]=="match_stadium").ToArray();if(venues.Length>1)NativeVenue="Multiple native values (Expert)";
            var owner=c.EffectiveRules(phase).FirstOrDefault(r=>r.Key=="rule_suspension").Value;
            DisciplineOrigin=owner is null?"Existing inherited discipline rules":"Discipline managed by "+(int.TryParse(owner,out int n)?c.ObjectName(n):"an existing native reference");
        }
        if(!Supported){prizeMode=PrizeModes[0];venueMode=VenueModes[0];}
        ConfigureTies(tieCount,twoLegs);
    }
    string CleanPrize()=>Prize.Replace(" ","").Replace("\u00a0","").Replace("\u202f","").Replace(",","");
    public FootballPhaseSettings Snapshot()
    {
        if(ties.Skip(tieCount).Any(t=>t.Snapshot().Stadium is var s&&(s.Mode==FootballSettingMode.Value||s.Value is not null)))throw new InvalidDataException("A removed tie has a selected stadium. Restore it and reset its venue before changing the format.");
        var result=original with{Prize=new(PrizeMode.Mode,PrizeMode.Mode==FootballSettingMode.Value?CleanPrize():original.Prize.Value),Stadium=new(VenueMode.Mode,VenueMode.Mode==FootballSettingMode.Value?Stadium?.Id.ToString():original.Stadium.Value),TieVenues=context is null?original.TieVenues:TieVenues.Select(t=>t.Snapshot()).ToArray()};
        if(SetDrawDate)
        {
            if(context is null||DrawDate is null)throw new InvalidDataException("Choose a draw date.");
            result=result with{DrawDate=new(FootballSettingMode.Value,(DateOnly.FromDateTime(DrawDate.Value).DayNumber-context.ReferenceDate.DayNumber).ToString())};
        }
        else if(ResetDrawDate)result=result with{DrawDate=new(FootballSettingMode.Inherit)};
        if(context is not null)FootballPhaseSettingsAdapter.Validate(context,result,nativeId);
        else if(CustomPrize&&(!int.TryParse(CleanPrize(),NumberStyles.None,CultureInfo.InvariantCulture,out int n)||n<0))throw new InvalidDataException("Prize money must be a non-negative whole amount.");
        return result;
    }
    void Updated(){foreach(var tie in ties)tie.RefreshInherited();Notify(nameof(Prize));Notify(nameof(PrizeMode));Notify(nameof(VenueMode));Notify(nameof(Stadium));Notify(nameof(CustomPrize));Notify(nameof(SingleStadium));Notify(nameof(Summary));Notify(nameof(Error));Notify(nameof(PrizeError));Notify(nameof(VenueError));}
}
