using System.Globalization;
namespace ATLink.Core;

public enum FootballSettingMode { Preserve, Inherit, Value }
public enum FootballYellowCards { Preserve, CarryForward, ClearBeforePhase }
public enum FootballVenuePolicy { Default, SingleStadium, HostStadiums }
public sealed record FootballPhaseSetting(FootballSettingMode Mode=FootballSettingMode.Preserve,string? Value=null);
public sealed record FootballFixtureVenue(string Home,string Away,DateOnly Date,int StadiumId);
public sealed record FootballPhaseSettings
{
    public FootballPhaseSetting Prize {get;init;}=new();
    public FootballPhaseSetting Stadium {get;init;}=new();
    public FootballVenuePolicy VenuePolicy {get;init;}
    public FootballYellowCards YellowCards {get;init;}
    public IReadOnlyList<int> HostStadiums {get;init;}=[];
    public IReadOnlyList<FootballFixtureVenue> FixtureVenues {get;init;}=[];
}
public sealed record FootballStadium(int Id,string Name){public override string ToString()=>Name;}

/// <summary>Only established native mappings are written. Unsupported policies cannot disappear at export.</summary>
public static class FootballPhaseSettingsAdapter
{
    sealed class StadiumCache {public IReadOnlyList<FootballStadium> Items=[];}
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CompetitionProjectContext,StadiumCache> stadiumCache=new();
    public const string HostLimitation="Multiple host stadiums and per-match venue assignments are unavailable: the supplied native fixture format has no verified stadium field. A single stadium can be selected for a phase.";
    public const string YellowLimitation="Clearing accumulated yellow cards while keeping existing suspensions is unavailable: no verified FC27 conversion was found. Existing discipline references are preserved.";
    public static IReadOnlyList<FootballStadium> Stadiums(CompetitionProjectContext c)=>stadiumCache.GetValue(c,project=>new(){Items=project.Base is null?[]:new FootballCatalog(project.Base).Entities("stadiums").Where(s=>int.TryParse(s.Id,out _)).Select(s=>new FootballStadium(int.Parse(s.Id),s.Name.TrimStart('_'))).OrderBy(s=>s.Name).ToArray()}).Items;
    public static FootballPhaseSettings Read(CompetitionProjectContext c,int phase)=>new()
    {
        Prize=new(Value:Local(c,phase,"info_prize_money")),
        Stadium=new(Value:Local(c,phase,"match_stadium"))
    };
    static string? Local(CompetitionProjectContext c,int phase,string key)
    {
        var rows=c.Rows("settings.txt").Where(r=>r[0]==phase.ToString()&&r[1]==key).ToArray();
        return rows.Length==1?rows[0][2]:null;
    }
    public static void Validate(CompetitionProjectContext c,FootballPhaseSettings settings,int? phase=null)
    {
        if(settings.VenuePolicy==FootballVenuePolicy.HostStadiums||settings.HostStadiums.Count>0||settings.FixtureVenues.Count>0)throw new InvalidDataException(HostLimitation);
        if(settings.YellowCards==FootballYellowCards.ClearBeforePhase)throw new InvalidDataException(YellowLimitation);
        if(c.Game!="FC27"&&(settings.Prize.Mode!=FootballSettingMode.Preserve||settings.Stadium.Mode!=FootballSettingMode.Preserve||settings.YellowCards!=FootballYellowCards.Preserve))throw new InvalidDataException("These phase settings are not verified for "+c.Game+". Existing native values stay preserved in Expert.");
        if(settings.Prize.Mode==FootballSettingMode.Value&&(!int.TryParse(settings.Prize.Value,NumberStyles.None,CultureInfo.InvariantCulture,out int money)||money<0))throw new InvalidDataException("Prize money: enter a whole amount from 0 to 2,147,483,647 without a currency symbol.");
        if(settings.Stadium.Mode==FootballSettingMode.Value&&(!int.TryParse(settings.Stadium.Value,out int stadium)||!Stadiums(c).Any(s=>s.Id==stadium)))throw new InvalidDataException("Choose a stadium available in this game's database.");
        if(settings.Stadium.Mode==FootballSettingMode.Value&&phase is int parent)
        {
            var groups=c.Objects.Where(o=>o.Kind==5&&o.ParentId==parent).Select(o=>o.Id.ToString()).ToHashSet();
            if(c.Rows("settings.txt").Any(r=>groups.Contains(r[0])&&r[1]=="match_stadium"&&r[2]!=settings.Stadium.Value))throw new InvalidDataException("This phase has group-specific stadium overrides. They are preserved in Expert; review them before imposing one stadium for the whole phase.");
        }
    }
    public static void Apply(CompetitionProjectContext c,int phase,FootballPhaseSettings settings,bool created=false)
    {
        Validate(c,settings,phase);
        void Write(string key,FootballPhaseSetting setting)
        {
            if(setting.Mode==FootballSettingMode.Inherit)c.SetRule(phase,key,[]);
            if(setting.Mode==FootballSettingMode.Value)c.SetRule(phase,key,[setting.Value!]);
        }
        if(created&&c.Game=="FC27"&&settings.Prize.Mode==FootballSettingMode.Preserve)c.SetRule(phase,"info_prize_money",["0"]);
        Write("info_prize_money",settings.Prize);Write("match_stadium",settings.Stadium);
        // CarryForward deliberately keeps the original discipline owner and acquired suspensions.
    }
    public static string Amount(string? value)=>decimal.TryParse(value,NumberStyles.None,CultureInfo.InvariantCulture,out decimal n)&&n>=0?n>=1000000?(n/1000000).ToString("0.##",CultureInfo.InvariantCulture)+" M":n>=1000?(n/1000).ToString("0.##",CultureInfo.InvariantCulture)+" K":n.ToString("N0",CultureInfo.GetCultureInfo("en-US")):"Native value (Expert)";
}
