using System.Globalization;
namespace ATLink.Core;

public enum FootballSettingMode { Preserve, Inherit, Value }
public enum FootballYellowCards { Preserve, CarryForward, ClearBeforePhase }
public enum FootballVenuePolicy { Default, SingleStadium, HostStadiums }
public sealed record FootballPhaseSetting(FootballSettingMode Mode=FootballSettingMode.Preserve,string? Value=null);
public sealed record FootballFixtureVenue(string Home,string Away,DateOnly Date,int StadiumId);
public sealed record FootballTieVenue(int Ordinal,FootballPhaseSetting Stadium,int? NativeGroup=null,bool Protected=false,string Note="");
public sealed record FootballPhaseSettings
{
    public FootballPhaseSetting Prize {get;init;}=new();
    public FootballPhaseSetting DrawDate {get;init;}=new();
    public FootballPhaseSetting Stadium {get;init;}=new();
    public FootballVenuePolicy VenuePolicy {get;init;}
    public FootballYellowCards YellowCards {get;init;}
    public IReadOnlyList<int> HostStadiums {get;init;}=[];
    public IReadOnlyList<FootballFixtureVenue> FixtureVenues {get;init;}=[];
    public IReadOnlyList<FootballTieVenue> TieVenues {get;init;}=[];
}
public sealed record FootballStadium(int Id,string Name){public override string ToString()=>Name;}

/// <summary>Only established native mappings are written. Unsupported policies cannot disappear at export.</summary>
public static class FootballPhaseSettingsAdapter
{
    sealed class StadiumCache {public IReadOnlyList<FootballStadium> Items=[];}
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CompetitionProjectContext,StadiumCache> stadiumCache=new();
    public const string HostLimitation="Knockout ties can override the phase stadium, shared by both legs. Individual league/group fixtures and automatic host-stadium rotation are not supported.";
    public const string YellowLimitation="Clearing accumulated yellow cards while keeping existing suspensions is unavailable: no verified FC27 conversion was found. Existing discipline references are preserved.";
    public static IReadOnlyList<FootballStadium> Stadiums(CompetitionProjectContext c)=>stadiumCache.GetValue(c,project=>new(){Items=project.Base is null?[]:new FootballCatalog(project.Base).Entities("stadiums").Where(s=>int.TryParse(s.Id,out _)).Select(s=>new FootballStadium(int.Parse(s.Id),s.Name.TrimStart('_'))).OrderBy(s=>s.Name).ToArray()}).Items;
    public static FootballPhaseSettings Read(CompetitionProjectContext c,int phase)=>new()
    {
        Prize=new(Value:Local(c,phase,"info_prize_money")),
        DrawDate=new(Value:Local(c,phase,"schedule_stage_draw_date")),
        Stadium=new(Value:Local(c,phase,"match_stadium")),TieVenues=ReadTies(c,phase)
    };
    static IReadOnlyList<FootballTieVenue> ReadTies(CompetitionProjectContext c,int phase)
    {
        if(c.EffectiveRules(phase).FirstOrDefault(r=>r.Key=="match_stagetype").Value is not("KO1LEG" or "KO2LEGS" or "KO2LEG"))return [];
        return c.Objects.Where(o=>o.ParentId==phase&&o.Kind==5).OrderBy(o=>o.Id).Select((g,i)=>
        {
            var rows=c.Rows("settings.txt").Where(r=>r[0]==g.Id.ToString()&&r[1]=="match_stadium").ToArray();
            bool invalid=c.Rows("standings.txt").Count(r=>r[0]==g.Id.ToString())!=2||rows.Length>1||rows.Length==1&&(!int.TryParse(rows[0][2],out int id)||!Stadiums(c).Any(s=>s.Id==id));
            return new FootballTieVenue(i,new(Value:rows.Length==1?rows[0][2]:null),g.Id,invalid,invalid?"Existing venue or tie structure requires Expert; native values are preserved.":"");
        }).ToArray();
    }
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
        if(settings.DrawDate.Mode==FootballSettingMode.Value&&(!int.TryParse(settings.DrawDate.Value,out int day)||day<0))throw new InvalidDataException("Choose a draw date after the calendar origin.");
        if(settings.Stadium.Mode==FootballSettingMode.Value&&(!int.TryParse(settings.Stadium.Value,out int stadium)||!Stadiums(c).Any(s=>s.Id==stadium)))throw new InvalidDataException("Choose a stadium available in this game's database.");
        if(settings.TieVenues.Select(t=>t.Ordinal).Distinct().Count()!=settings.TieVenues.Count)throw new InvalidDataException("A tie has more than one venue setting.");
        foreach(var tie in settings.TieVenues)
        {
            if(tie.Stadium.Mode==FootballSettingMode.Preserve)continue;
            if(c.Game!="FC27"||tie.Protected)throw new InvalidDataException("This tie's native venue is preserved in Expert.");
            if(tie.Stadium.Mode==FootballSettingMode.Value&&(!int.TryParse(tie.Stadium.Value,out int id)||!Stadiums(c).Any(s=>s.Id==id)))throw new InvalidDataException("Choose an installed stadium for each customized tie.");
        }
    }
    public static void Apply(CompetitionProjectContext c,int phase,FootballPhaseSettings settings,bool created=false,IReadOnlyList<int>? tieGroups=null)
    {
        Validate(c,settings,phase);
        void Write(string key,FootballPhaseSetting setting)
        {
            if(setting.Mode==FootballSettingMode.Inherit)c.SetRule(phase,key,[]);
            if(setting.Mode==FootballSettingMode.Value)c.SetRule(phase,key,[setting.Value!]);
        }
        if(created&&c.Game=="FC27"&&settings.Prize.Mode==FootballSettingMode.Preserve)c.SetRule(phase,"info_prize_money",["0"]);
        if(created&&c.Game=="FC27")c.SetRule(phase,"info_prize_money_drop",[]);
        Write("info_prize_money",settings.Prize);Write("match_stadium",settings.Stadium);
        Write("schedule_stage_draw_date",settings.DrawDate);
        var changed=settings.TieVenues.Where(t=>t.Stadium.Mode!=FootballSettingMode.Preserve).ToArray();
        if(changed.Length>0)
        {
            if(tieGroups is null||c.EffectiveRules(phase).FirstOrDefault(r=>r.Key=="match_stagetype").Value is not("KO1LEG" or "KO2LEGS" or "KO2LEG"))throw new InvalidDataException("A verified knockout group mapping is required for tie venues.");
            foreach(var tie in changed)
            {
                if(tie.Ordinal<0||tie.Ordinal>=tieGroups.Count)throw new InvalidDataException("A customized tie was removed. Review its venue before changing the format.");
                int group=tieGroups[tie.Ordinal];
                if(tie.NativeGroup is int original&&original!=group||!c.Objects.Any(o=>o.Id==group&&o.Kind==5&&o.ParentId==phase)||c.Rows("standings.txt").Count(r=>r[0]==group.ToString())!=2)throw new InvalidDataException("The native tie mapping changed. Refresh before editing its venue.");
                c.SetRule(group,"match_stadium",tie.Stadium.Mode==FootballSettingMode.Inherit?[]:[tie.Stadium.Value!]);
            }
        }
        // CarryForward deliberately keeps the original discipline owner and acquired suspensions.
    }
    public static string Amount(string? value)=>decimal.TryParse(value,NumberStyles.None,CultureInfo.InvariantCulture,out decimal n)&&n>=0?n>=1000000?(n/1000000).ToString("0.##",CultureInfo.InvariantCulture)+" M":n>=1000?(n/1000).ToString("0.##",CultureInfo.InvariantCulture)+" K":n.ToString("N0",CultureInfo.GetCultureInfo("en-US")):"Native value (Expert)";
}
