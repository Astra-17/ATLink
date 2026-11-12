using System.Data;
using System.Globalization;
namespace ATLink.Core;

public readonly record struct CompdataObjectId(int Value);
public readonly record struct DatabaseLeagueId(int Value);
public readonly record struct DatabaseCompetitionId(int Value);
public enum SeasonParameterKind { Count, Position, Team, League, Competition, Group, Nation, Coefficient }
public sealed record SeasonParameter(string Label,SeasonParameterKind Kind);
public sealed record SeasonAction(string Code,string Label,bool End,int TargetKind,SeasonParameter[] Parameters);
public sealed record FootballSeasonTask(string Action,CompdataObjectId Target,int[] Parameters,int? OriginalRow=null);
public sealed record FootballSeasonSnapshot(IReadOnlyList<FootballSeasonTask> Tasks,IReadOnlyList<int> ManagedRows,string OriginalText,int UnknownCount);
public enum FootballCalendarMode { Preserve, Generate, AssignedFixtures, None, ManualMatchdays, ExactFixtures }
public sealed record FootballMatchdayDraft(int PhaseIndex,int Round,DateOnly Date,string Kickoff,int Minimum,int Maximum);
public sealed record FootballFixtureDraft(int PhaseIndex,DateOnly Date,string Kickoff,string Home,string Away);
public sealed record FootballCalendarPhase(int Index,string Name,int Rounds,int Matches)
{
    public string Key {get;init;}="";
}
public sealed record FootballCalendarPlan(FootballCalendarMode Mode,DateOnly FirstDate,int IntervalDays,string Kickoff)
{
    public FootballSchedulingOptions? SimulationOptions {get;init;}
    public IReadOnlyList<InternationalFriendlySlot> FriendlyReplacements {get;init;}=[];
    public IReadOnlyList<FootballMatchdayDraft> Matchdays {get;init;}=[];
    public IReadOnlyList<FootballFixtureDraft> Fixtures {get;init;}=[];
}
public sealed record FootballSeasonLinks(DatabaseCompetitionId? Promotion=null,DatabaseCompetitionId? Relegation=null,CompdataObjectId? Playoff=null,int? StartMonth=null);
public sealed record FootballStandingSeed(string TeamId,int Position);
public sealed record FootballBehaviourPlan(IReadOnlyList<FootballSeasonTask> Tasks,IReadOnlyList<FootballStandingSeed> InitialStandings,bool AutomaticResults,FootballSeasonLinks Links,FootballCalendarPlan Calendar);

public static class FootballSeason
{
    public static readonly SeasonAction[] Actions=[
        new("FillWithTeam","A selected club",false,5,[new("Place",SeasonParameterKind.Position),new("Club",SeasonParameterKind.Team)]),
        new("FillFromLeague","All clubs from a league",false,5,[new("League",SeasonParameterKind.League)]),
        new("FillFromLeagueInOrder","Clubs from a league in order",false,5,[new("League",SeasonParameterKind.League)]),
        new("FillFromSpecialTeams","Special team pool",false,5,[new("Number of teams",SeasonParameterKind.Count)]),
        new("FillFromSpecialTeamsWithNation","Special team pool by country",false,5,[new("Number of teams",SeasonParameterKind.Count),new("Country",SeasonParameterKind.Nation)]),
        new("FillFromLeagueMaxFromCountry","League with a country limit",false,5,[new("League",SeasonParameterKind.League),new("Number of teams",SeasonParameterKind.Count),new("Maximum per country",SeasonParameterKind.Count)]),
        new("FillFromTopCoefficientCountry","Teams from a country coefficient rank",false,5,[new("Country coefficient rank",SeasonParameterKind.Coefficient),new("Number of teams",SeasonParameterKind.Count),new("Allocation place",SeasonParameterKind.Position)]),
        new("FillFromCompTable","Qualifiers from a competition",false,5,[new("Source competition",SeasonParameterKind.Competition),new("Number of teams",SeasonParameterKind.Count)]),
        new("FillFromCompTableBackupLeague","Qualifiers with a backup league",false,5,[new("Source competition",SeasonParameterKind.Competition),new("Backup league",SeasonParameterKind.League),new("Number of teams",SeasonParameterKind.Count)]),
        new("FillFromCompTableBackup","Qualifiers with a backup competition",false,5,[new("Source competition",SeasonParameterKind.Competition),new("Backup competition",SeasonParameterKind.Competition),new("Number of teams",SeasonParameterKind.Count)]),
        new("ClearLeagueStats","Clear previous league statistics",false,4,[new("League",SeasonParameterKind.League)]),
        new("UpdateTable","Save a finishing position for next season",true,3,[new("Results group",SeasonParameterKind.Group),new("Finishing position",SeasonParameterKind.Position),new("Next-season table position",SeasonParameterKind.Position)]),
        new("UpdateLeagueTable","Update a league table from this phase",true,4,[new("League",SeasonParameterKind.League)]),
        new("UpdateLeagueStats","Update league statistics after the season",true,4,[new("League",SeasonParameterKind.League)])
    ];
    public static int DatabaseId(CompetitionProjectContext c,int competition)=>int.TryParse(c.Objects.Single(o=>o.Id==competition&&o.Kind==3).Code.TrimStart('C','c'),out int id)?id:throw new InvalidDataException("This competition has no database identity.");
    public static FootballSeasonSnapshot Read(CompetitionProjectContext c,int competition)
    {
        var tasks=new List<FootballSeasonTask>();var rows=new List<int>();int unknown=0;
        foreach(var (r,index) in c.Rows("tasks.txt").Select((r,i)=>(r,i)).Where(x=>x.r.Length>=7&&x.r[0]==competition.ToString()))
        {
            var action=Actions.FirstOrDefault(a=>a.Code==r[2]);
            if(action is null||r.Length!=7||r[1]!=(action.End?"end":"start")||!int.TryParse(r[3],out int target)||!r.Skip(4).Take(3).All(v=>int.TryParse(v,out _))||r.Skip(4+action.Parameters.Length).Any(v=>v!="0")){unknown++;continue;}
            var task=new FootballSeasonTask(action.Code,new(target),r.Skip(4).Take(action.Parameters.Length).Select(int.Parse).ToArray(),index);
            // Preserve unsupported native values verbatim rather than normalizing on read.
            try{Validate(c,competition,task);tasks.Add(task);rows.Add(index);}catch{unknown++;}
        }
        return new(tasks,rows,c.Project.Files.Single(f=>f.RelativePath=="tasks.txt").Text,unknown);
    }
    static bool HasLeague(CompetitionProjectContext c,int id)=>c.Base?.Tables.Single(t=>t.Name=="leagues").Data.Rows.Cast<DataRow>().Any(r=>FootballCatalog.Value(r,"leagueid")==id.ToString())==true||c.Inserts.Any(i=>i.Table=="leagues"&&i.Values["leagueid"]==id.ToString());
    static int Capacity(CompetitionProjectContext c,int group)=>c.Rows("standings.txt").Count(r=>r[0]==group.ToString());
    public static void Validate(CompetitionProjectContext c,int competition,FootballSeasonTask task)
    {
        var action=Actions.SingleOrDefault(a=>a.Code==task.Action)??throw new InvalidDataException("This automation remains in Expert.");
        var target=c.Objects.SingleOrDefault(o=>o.Id==task.Target.Value);
        bool crossTable=c.Game=="FC27"&&task.Action=="UpdateTable";
        if(target is null||target.Kind!=action.TargetKind||!crossTable&&!c.Scope(competition).Contains(target.Id))
            throw new InvalidDataException("Choose a compatible destination for this rule.");
        if(task.Parameters.Length!=action.Parameters.Length)throw new InvalidDataException("Complete all option fields.");
        for(int i=0;i<task.Parameters.Length;i++)
        {
            int n=task.Parameters[i];if(n<=0)throw new InvalidDataException(action.Parameters[i].Label+": choose a positive value.");
            bool valid=action.Parameters[i].Kind switch
            {
                SeasonParameterKind.League=>HasLeague(c,n),
                SeasonParameterKind.Team=>c.Teams.Any(t=>t.Id==n.ToString()),
                SeasonParameterKind.Competition=>c.Objects.Any(o=>o.Id==n&&o.Kind is 3 or 6),
                SeasonParameterKind.Group=>c.Objects.Any(o=>o.Id==n&&o.Kind==5&&c.Scope(competition).Contains(n)),
                SeasonParameterKind.Nation=>c.Base?.Tables.FirstOrDefault(t=>t.Name=="nations")?.Data.Rows.Cast<DataRow>().Any(r=>FootballCatalog.Value(r,"nationid")==n.ToString())==true,
                _=>n<=128
            };
            if(!valid)throw new InvalidDataException("Choose a valid "+action.Parameters[i].Label.ToLowerInvariant()+".");
        }
        if(task.Action=="FillWithTeam"&&task.Parameters[0]>Capacity(c,target.Id))throw new InvalidDataException("This place is outside the phase's participant capacity.");
        if(task.Action=="UpdateTable"&&task.Parameters[1]>Capacity(c,task.Parameters[0]))throw new InvalidDataException("This finishing position does not exist in the results group.");
        // A country cap can span several entry pools (for example 2 + 2 Dutch clubs).
        // FC27's native recruitment tasks also use caps larger than one task's allocation.
    }
    public static string[] Native(CompetitionProjectContext c,int competition,FootballSeasonTask task)
    {
        Validate(c,competition,task);var action=Actions.Single(a=>a.Code==task.Action);
        return TeamSourceRules.Create(c.Project,competition,task.Target.Value,task.Action,task.Parameters.Select(n=>n.ToString()).ToArray());
    }
    public static string Describe(CompetitionProjectContext c,FootballSeasonTask task)
    {
        string League(int n)=>n<0?"this competition's new league":c.LeagueName(n.ToString());
        string Group(int n)=>n==0?"the final results group":c.ObjectPath(n);
        int P(int i)=>task.Parameters.ElementAtOrDefault(i);
        return task.Action switch
        {
            "FillWithTeam"=>$"At tournament start, add {c.TeamName(P(1).ToString())}.",
            "FillFromLeague" or "FillFromLeagueInOrder"=>$"At tournament start, take clubs from {League(P(0))}.",
            "FillFromCompTable"=>$"At tournament start, take {P(1)} qualifiers from {c.ObjectName(P(0))}.",
            "FillFromCompTableBackupLeague"=>$"Take {P(2)} qualifiers from {c.ObjectName(P(0))}; use {League(P(1))} as backup.",
            "FillFromCompTableBackup"=>$"Take {P(2)} qualifiers from {c.ObjectName(P(0))}; use {c.ObjectName(P(1))} as backup.",
            "FillFromSpecialTeams"=>$"At tournament start, select {P(0)} teams from the special team pool.",
            "FillFromSpecialTeamsWithNation"=>$"At tournament start, select {P(0)} special-pool teams from {c.Project.Labels?.Name(new CompetitionObject(P(1),2,"","NationName_"+P(1),0))??"the selected country"}.",
            "FillFromLeagueMaxFromCountry"=>$"Select {P(1)} teams from {League(P(0))}, with at most {P(2)} per country.",
            "FillFromTopCoefficientCountry"=>$"Select {P(1)} teams from the country ranked {P(0)} by coefficient, using entry position {P(2)}.",
            "ClearLeagueStats"=>$"At tournament start, clear previous statistics for {League(P(0))}.",
            "UpdateTable"=>$"At season end, save finishing position {P(1)} from {Group(P(0))} as position {P(2)} in {c.ObjectName(task.Target.Value)} for next season.",
            "UpdateLeagueTable"=>$"At season end, update the standings of {League(P(0))} using this phase.",
            "UpdateLeagueStats"=>$"At season end, update the statistics of {League(P(0))} using this phase.",
            _=>Actions.Single(a=>a.Code==task.Action).Label+": "+string.Join(", ",task.Parameters)
        };
    }
    public static void ApplyTasks(CompetitionProjectContext c,int competition,FootballSeasonSnapshot original,IReadOnlyList<FootballSeasonTask> tasks)
    {
        if(c.Project.Files.Single(f=>f.RelativePath=="tasks.txt").Text!=original.OriginalText)throw new InvalidDataException("Season rules changed. Reload the season preview before applying.");
        var managed=original.ManagedRows.ToHashSet();if(tasks.Any(t=>t.OriginalRow.HasValue&&!managed.Contains(t.OriginalRow.Value))||tasks.Where(t=>t.OriginalRow.HasValue).GroupBy(t=>t.OriginalRow).Any(g=>g.Count()>1))throw new InvalidDataException("A season rule cannot be reused twice.");
        foreach(var task in tasks)_=Native(c,competition,task);
        var fixedPlaces=tasks.Where(t=>t.Action=="FillWithTeam").ToArray();if(fixedPlaces.GroupBy(t=>(t.Target,t.Parameters[0])).Any(g=>g.Count()>1))throw new InvalidDataException("Two clubs cannot occupy the same place.");
        if(tasks.Where(t=>t.Action=="UpdateTable").GroupBy(t=>(t.Target,t.Parameters[2])).Any(g=>g.Count()>1))throw new InvalidDataException("Two finishing positions cannot replace the same next-season table position.");
        var existingWriters=c.Rows("tasks.txt").Where(r=>r.Length==7&&r[0]!=competition.ToString()&&r[1]=="end"&&r[2]=="UpdateTable")
            .Select(r=>(r[3],r[6])).ToHashSet();
        if(tasks.Any(t=>t.Action=="UpdateTable"&&existingWriters.Contains((t.Target.Value.ToString(),t.Parameters[2].ToString()))))
            throw new InvalidDataException("Another competition already writes to that next-season table position.");
        c.Edit("tasks.txt",table=>
        {
            int cursor=0;
            foreach(int index in original.ManagedRows){var row=table.Data.Rows[index];if(cursor<tasks.Count){var task=tasks[cursor++];var before=original.Tasks.First(t=>t.OriginalRow==index);if(task.OriginalRow==index&&task.Action==before.Action&&task.Target==before.Target&&task.Parameters.SequenceEqual(before.Parameters))continue;var values=Native(c,competition,task);for(int i=0;i<7;i++)if(row[i].ToString()!=values[i])row[i]=values[i];}else row.Delete();}
            foreach(var task in tasks.Skip(cursor))table.Data.Rows.Add(Native(c,competition,task).Cast<object>().ToArray());
        });
    }
    public static void ConfigureTwoGroupExchange(CompetitionProjectContext c,int upper,int lower)
    {
        if(c.Game!="FC27"||upper==lower||!c.CompetitionMappings.Any(m=>m.CompdataObjectId==upper)||!c.CompetitionMappings.Any(m=>m.CompdataObjectId==lower))
            throw new InvalidDataException("Choose two distinct FC27 competitions created in this project.");
        int[] Groups(int id)
        {
            var phases=FootballCompetitionAdapter.Read(c,id).Phases;
            var first=phases.FirstOrDefault();
            if(first is not {Kind:FootballPhaseKind.Groups,Participants:20,Groups:2}||first.NativeGroups.Any(g=>Capacity(c,g)!=10))
                throw new InvalidDataException("Each competition needs an opening phase of two groups of ten.");
            var teams=c.Rows("initteams.txt").Where(r=>r[0]==id.ToString()).OrderBy(r=>int.Parse(r[1])).ToArray();
            if(teams.Length!=20||!teams.Select(r=>int.Parse(r[1])).SequenceEqual(Enumerable.Range(0,20))||
                teams.Select(r=>r[2]).Distinct().Count()!=20||teams.Any(r=>!c.Teams.Any(t=>t.Id==r[2])))
                throw new InvalidDataException("Each competition needs 20 distinct clubs in its initial standings.");
            return first.NativeGroups.ToArray();
        }
        var upperGroups=Groups(upper);var lowerGroups=Groups(lower);
        if(c.Rows("initteams.txt").Where(r=>r[0]==upper.ToString()||r[0]==lower.ToString()).Select(r=>r[2]).Distinct().Count()!=40)
            throw new InvalidDataException("A club cannot start in both competitions.");
        c.Execute("Configure promotion and relegation",()=>
        {
            void Write(int source,int destination,int[] groups,int retainedStart,int exchangeRank)
            {
                var pools=FootballQualifications.EntryPools(c,source);
                if(pools.Count!=1||Capacity(c,pools[0])!=20)
                    throw new InvalidDataException("Each competition needs a single preparation pool of 20 clubs.");
                int pool=pools[0];var snapshot=Read(c,source);
                if(snapshot.UnknownCount>0||snapshot.Tasks.Any(t=>t.Action switch
                {
                    "FillWithTeam"=>t.Target.Value!=pool,
                    "FillFromCompTable"=>t.Target.Value!=pool||t.Parameters[0]!=source,
                    "UpdateTable"=>t.Target.Value!=source&&t.Target.Value!=destination,
                    _=>t.Action.StartsWith("Fill",StringComparison.Ordinal)
                }))
                    throw new InvalidDataException("Existing entry rules or special tasks require review before replacing them.");
                var tasks=new List<FootballSeasonTask>{new("FillFromCompTable",new(pool),[source,20])};
                tasks.AddRange(snapshot.Tasks.Where(t=>!t.Action.StartsWith("Fill",StringComparison.Ordinal)&&t.Action!="UpdateTable"));
                for(int group=0;group<2;group++)
                {
                    for(int place=1;place<=9;place++)
                        tasks.Add(new("UpdateTable",new(source),[groups[group],retainedStart+place-1,group*9+place]));
                    tasks.Add(new("UpdateTable",new(destination),[groups[group],exchangeRank,19+group]));
                }
                ApplyTasks(c,source,snapshot,tasks);
            }
            Write(upper,lower,upperGroups,1,10);
            Write(lower,upper,lowerGroups,2,1);
            foreach(int group in upperGroups)c.SetRule(group,"info_label_slot_releg",["10"]);
            foreach(int group in lowerGroups)
            {
                c.SetRule(group,"info_label_slot_promo",["1"]);
                c.SetRule(group,"info_label_slot_releg",[]);
            }
        });
    }

    public static IReadOnlyList<FootballSeasonTask> ProposeResults(CompetitionProjectContext c,int competition)
    {
        if(c.Game!="FC27")throw new InvalidDataException("Automatic season profiles have not been validated for this game.");
        var phases=FootballCompetitionAdapter.Read(c,competition).Phases;var last=phases.LastOrDefault()??throw new InvalidDataException("Add a played phase first.");
        if(last.NativeGroups.Count!=1||last.Special||last.Kind==FootballPhaseKind.Knockout&&last.Participants!=2)throw new InvalidDataException("A complete final or a single final table is needed to propose next-season results. Keep custom classification rules in Expert.");
        // Evidence: FC27 Premier League 818 (clear/update stats + table positions), FA Cup 511 (final's winner and runner-up).
        string profileCode=last.Kind==FootballPhaseKind.Knockout?"C201":"C13";
        var profile=c.Objects.SingleOrDefault(o=>o.Kind==3&&o.Code==profileCode)??throw new InvalidDataException("The verified FC27 season profile is unavailable in this project.");
        var evidenceModel=FootballCompetitionAdapter.Read(c,profile.Id);var evidenceFinal=evidenceModel.Phases.LastOrDefault();
        var evidence=c.Rows("tasks.txt").Where(r=>r.Length==7&&r[0]==profile.Id.ToString()).ToArray();
        bool Evidence(string action)=>evidence.Any(r=>r[2]==action&&r[3]==evidenceFinal?.NativeId?.ToString()&&r[4]==DatabaseId(c,profile.Id).ToString());
        if(evidenceFinal is null||evidenceFinal.NativeGroups.Count!=1||!evidence.Any(r=>r[1]=="end"&&r[2]=="UpdateTable"&&r[3]==profile.Id.ToString()&&r[4]==evidenceFinal.NativeGroups[0].ToString()&&r[5]=="1"&&r[6]=="1"))throw new InvalidDataException("The native final-results mapping could not be verified. Configure the season operations explicitly instead.");
        var result=new List<FootballSeasonTask>();int group=last.NativeGroups[0],size=Capacity(c,group),db=DatabaseId(c,competition);
        if(last.Kind==FootballPhaseKind.League&&HasLeague(c,db)&&Evidence("ClearLeagueStats")&&Evidence("UpdateLeagueStats"))result.Add(new("ClearLeagueStats",new(last.NativeId!.Value),[db]));
        for(int rank=1;rank<=size;rank++)result.Add(new("UpdateTable",new(competition),[group,rank,rank]));
        if(last.Kind==FootballPhaseKind.League&&HasLeague(c,db)&&Evidence("UpdateLeagueStats"))result.Add(new("UpdateLeagueStats",new(last.NativeId!.Value),[db]));
        foreach(var task in result)Validate(c,competition,task);return result;
    }
    public static void ApplyNativeChampionMarkers(CompetitionProjectContext c,int competition)
    {
        var last=FootballCompetitionAdapter.Read(c,competition).Phases.Last();if(last.NativeGroups.Count!=1)return;
        string code=last.Kind==FootballPhaseKind.Knockout?"C201":"C13";var source=c.Objects.Single(o=>o.Kind==3&&o.Code==code);var group=FootballCompetitionAdapter.Read(c,source.Id).Phases.Last().NativeGroups.Single();
        foreach(string key in new[]{"info_slot_champ","info_label_slot_champ"})if(c.Rows("settings.txt").Any(r=>r[0]==group.ToString()&&r[1]==key&&r[2]=="1"))c.SetRule(last.NativeGroups[0],key,["1"]);
    }
    public static void ApplyInitialStandings(CompetitionProjectContext c,int competition,IReadOnlyList<FootballStandingSeed> rows)
    {
        if(rows.Select(r=>r.Position).Distinct().Count()!=rows.Count||rows.Where(r=>r.TeamId!="-1").Select(r=>r.TeamId).Distinct().Count()!=rows.Count(r=>r.TeamId!="-1")||rows.Any(r=>r.Position<0||r.TeamId!="-1"&&!c.Teams.Any(t=>t.Id==r.TeamId)))throw new InvalidDataException("Initial standings need distinct installed clubs and distinct positions starting at zero.");
        if(c.Rows("initteams.txt").Where(r=>r[0]==competition.ToString()).Select(r=>(int.Parse(r[1]),r[2])).SequenceEqual(rows.OrderBy(r=>r.Position).Select(r=>(r.Position,r.TeamId))))return;
        c.Edit("initteams.txt",t=>{foreach(var row in t.Data.Rows.Cast<DataRow>().Where(r=>(string)r[0]==competition.ToString()).ToArray())row.Delete();foreach(var row in rows.OrderBy(r=>r.Position))t.Data.Rows.Add(competition.ToString(),row.Position.ToString(),row.TeamId);});
    }
    public static void ApplyLinks(CompetitionProjectContext c,int competition,FootballSeasonLinks links,FootballSeasonLinks? previous=null)
    {
        int own=DatabaseId(c,competition);
        if(links.Promotion is {} promotion&&links.Relegation is {} relegation&&promotion==relegation)throw new InvalidDataException("Promotion and relegation must lead to different competitions.");
        foreach(var (key,id) in new[]{("info_league_promo",links.Promotion),("info_league_releg",links.Relegation)})
        {
            if(previous is not null&&id==(key=="info_league_promo"?previous.Promotion:previous.Relegation))continue;
            if(id is { } target&&(target.Value==own||!c.Objects.Any(o=>o.Kind==3&&o.Code=="C"+target.Value)||!(c.Base?.Tables.Single(t=>t.Name=="competition").Data.Rows.Cast<DataRow>().Any(r=>FootballCatalog.Value(r,"competitionid")==target.Value.ToString())==true||c.Inserts.Any(i=>i.Table=="competition"&&i.Values["competitionid"]==target.Value.ToString()))))throw new InvalidDataException("Choose a different installed competition for promotion or relegation.");
            c.SetRule(competition,key,id is {} value?[value.Value.ToString()]:[]);
        }
        if(previous is null||links.Playoff!=previous.Playoff)
        {
            if(links.Playoff is {} playoff&&(playoff.Value==competition||!c.Objects.Any(o=>o.Kind==3&&o.Id==playoff.Value)))throw new InvalidDataException("Choose a different installed competition for the playoff.");
            c.SetRule(competition,"schedule_forcecomp",links.Playoff is {} p?[p.Value.ToString()]:[]);
        }
        if(previous is null||links.StartMonth!=previous.StartMonth)
        {
            if(links.StartMonth is int month){if(month is <1 or >12)throw new InvalidDataException("Choose a valid season month.");c.SetRule(competition,"schedule_seasonstartmonth",[CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(month).ToUpperInvariant()]);}else c.SetRule(competition,"schedule_seasonstartmonth",[]);
        }
    }
}
