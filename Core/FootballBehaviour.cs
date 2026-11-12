using System.Data;
namespace ATLink.Core;

public static class FootballBehaviour
{
    public static IReadOnlyList<string> Warnings(CompetitionProjectContext c)
    {
        var notes=new List<string>();
        foreach(var mapping in c.CompetitionMappings)
        {
            int id=mapping.CompdataObjectId;if(!c.Objects.Any(o=>o.Id==id))continue;
            var tasks=c.Rows("tasks.txt").Where(r=>r[0]==id.ToString()).ToArray();string name=c.ObjectName(id);
            if(!tasks.Any(r=>r[1]=="start"&&r[2].StartsWith("Fill")))notes.Add(name+": incoming teams are still to be configured; empty places are not automatic byes.");
            var parsed=FootballSeason.Read(c,id);int declared=parsed.Tasks.Where(t=>t.Action.StartsWith("Fill")).Sum(t=>SourceCount(c,t));int capacity=FootballCompetitionAdapter.Read(c,id).ParticipantCount;
            var scope=c.Scope(id);
            declared+=c.Rows("advancement.txt").Where(r=>!scope.Contains(int.Parse(r[0]))&&scope.Contains(int.Parse(r[2]))&&r[1]!="0")
                .Select(r=>(r[2],r[3])).Distinct().Count();
            if(parsed.UnknownCount==0&&declared>0&&declared!=capacity)notes.Add(name+$": entry sources describe {declared} teams for {capacity} places. Review the remaining or excess entries before installation.");
            if(!tasks.Any(r=>r[1]=="end"))notes.Add(name+": no end-of-season result update has been configured.");
            if(c.Calendar(id).Count==0)notes.Add(name+": no calendar slots or assigned matches have been configured yet.");
            if(mapping.DatabaseLeagueId is int league&&c.Base is not null&&!new FootballCatalog(c.Base).Rows("leagueteamlinks").Any(r=>FootballCatalog.Value(r,"leagueid")==league.ToString()))notes.Add(name+": the new DB league has no club memberships yet. Existing memberships were preserved; review intentional league/career integration before installation.");
            foreach(var source in tasks.Where(r=>r.Length>=6&&r[1]=="start"&&r[2]=="FillFromCompTable"&&int.TryParse(r[4],out _)&&int.TryParse(r[5],out _)))
                if(c.Rows("initteams.txt").Count(r=>r[0]==source[4])<int.Parse(source[5]))notes.Add(name+": "+c.ObjectName(int.Parse(source[4]))+" has fewer initial ranked teams than the requested qualifiers. Review the first season's entries or a backup source.");
            if(FootballSeason.Read(c,id).UnknownCount>0)notes.Add(name+": special native automation is preserved and needs review in Expert after integration.");
        }
        return notes.Distinct().ToArray();
    }
    public static FootballCalendarPlan DefaultCalendar(CompetitionProjectContext c,int template)
    {
        var scope=c.Scope(template);var slots=c.Rows("schedule.txt").Where(r=>scope.Contains(int.Parse(r[0]))&&!FootballCompetitionAdapter.IsHidden(c,int.Parse(r[0]))).ToArray();
        var days=slots.Select(r=>int.Parse(r[1])).Distinct().Order().ToArray();
        int interval=days.Zip(days.Skip(1),(a,b)=>b-a).Where(n=>n>0&&n<=365).DefaultIfEmpty(7).GroupBy(n=>n).OrderByDescending(g=>g.Count()).ThenBy(g=>g.Key).First().Key;
        string time=(slots.FirstOrDefault()?.ElementAtOrDefault(5)??"1500").PadLeft(4,'0');
        // Import native time/spacing; new seasons start independently of the day-zero origin.
        return new(FootballCalendarMode.Generate,c.SeasonStart.AddDays(7),interval,time);
    }
    public static FootballCalendarPlan ExistingCalendar(CompetitionProjectContext c,int competition)
    {
        var plan=DefaultCalendar(c,competition);var slots=c.Calendar(competition).Where(s=>s.FixedDate is null&&!FootballCompetitionAdapter.IsHidden(c,s.ObjectId)).ToArray();
        return plan with{FirstDate=slots.Select(s=>s.Date(c.ReferenceDate)).DefaultIfEmpty(c.SeasonStart.AddDays(7)).Min()};
    }
    public static int SourceCount(CompetitionProjectContext c,FootballSeasonTask task)=>task.Action switch
    {
        "FillWithTeam"=>1,
        "FillFromLeague" or "FillFromLeagueInOrder"=>c.Base is null?0:new FootballCatalog(c.Base).Rows("leagueteamlinks").Count(r=>FootballCatalog.Value(r,"leagueid")==task.Parameters[0].ToString()),
        "FillFromCompTable"=>task.Parameters[1],
        "FillFromCompTableBackup" or "FillFromCompTableBackupLeague"=>task.Parameters[2],
        "FillFromLeagueMaxFromCountry" or "FillFromTopCoefficientCountry"=>task.Parameters[1],
        "FillFromSpecialTeams" or "FillFromSpecialTeamsWithNation"=>task.Parameters[0],
        _=>0
    };
    public static void ValidateEntries(CompetitionProjectContext c,int capacity,IReadOnlyList<FootballSeasonTask> tasks,IReadOnlyList<string> fixedTeams)
    {
        var fills=tasks.Where(t=>t.Action.StartsWith("Fill",StringComparison.Ordinal)).ToArray();
        int total=fixedTeams.Count+fills.Sum(t=>SourceCount(c,t));if(total!=0&&total!=capacity)throw new InvalidDataException($"The entry rules provide {total} teams. This format needs {capacity}. Adjust the clubs or qualification counts.");
        var named=fixedTeams.Concat(fills.Where(t=>t.Action=="FillWithTeam").Select(t=>t.Parameters[1].ToString())).ToArray();
        if(named.Distinct().Count()!=named.Length)throw new InvalidDataException("A club cannot be selected twice in the entry rules.");
        if(named.Any(id=>!c.Teams.Any(t=>t.Id==id)))throw new InvalidDataException("Choose clubs installed in the selected game.");
    }
    public static void ApplyNew(CompetitionProjectContext c,int competition,FootballBehaviourPlan? plan,IReadOnlyList<string> fixedTeams)
    {
        var model=FootballCompetitionAdapter.Read(c,competition);var requested=plan?.Tasks??[];
        ValidateEntries(c,model.Phases[0].Participants,requested,fixedTeams);
        var allocation=c.Objects.Single(o=>o.Kind==5&&model.HiddenObjects.Contains(o.Id));var final=model.Phases[^1];
        var tasks=new List<FootballSeasonTask>();int nextPlace=fixedTeams.Count+1;
        if(!c.Rows("tasks.txt").Any(r=>r[0]==competition.ToString()&&r[2]=="FillWithTeam"))
            for(int i=0;i<fixedTeams.Count;i++)tasks.Add(new("FillWithTeam",new(allocation.Id),[i+1,int.Parse(fixedTeams[i])]));
        foreach(var task in requested)
        {
            var action=FootballSeason.Actions.Single(a=>a.Code==task.Action);int target=task.Target.Value;
            if(target==0)target=action.TargetKind==5?allocation.Id:action.TargetKind==3?competition:final.NativeId!.Value;
            var parameters=task.Parameters.ToArray();for(int p=0;p<parameters.Length;p++)if(action.Parameters[p].Kind==SeasonParameterKind.League&&parameters[p]==-1)parameters[p]=FootballSeason.DatabaseId(c,competition);
            if(task.Action=="UpdateTable"&&parameters[0]==0){if(final.NativeGroups.Count!=1)throw new InvalidDataException("Choose an explicit results group for this format.");parameters[0]=final.NativeGroups[0];}
            if(task.Action=="FillWithTeam")parameters[0]=nextPlace;
            var resolved=task with{Target=new(target),Parameters=parameters,OriginalRow=null};tasks.Add(resolved);nextPlace+=SourceCount(c,resolved);
        }
        bool hasClassification=tasks.Any(t=>t.Action=="UpdateTable");if(plan?.AutomaticResults!=false)foreach(var proposed in FootballSeason.ProposeResults(c,competition))if(!(hasClassification&&proposed.Action=="UpdateTable")&&!tasks.Any(t=>t.Action==proposed.Action&&t.Target==proposed.Target&&t.Parameters.SequenceEqual(proposed.Parameters)))tasks.Add(proposed);
        if(plan?.AutomaticResults!=false||c.Game=="FC27"&&final is {Kind:FootballPhaseKind.Knockout,Participants:2})
            FootballSeason.ApplyNativeChampionMarkers(c,competition);
        var original=FootballSeason.Read(c,competition);FootballSeason.ApplyTasks(c,competition,original,original.Tasks.Concat(tasks).ToArray());
        var initialStandings=plan?.InitialStandings??[];
        if(c.Game=="FC27"&&initialStandings.Count==0)
        {
            var namedTeams=fixedTeams.Concat(requested.Where(task=>task.Action=="FillWithTeam").Select(task=>task.Parameters[1].ToString())).ToArray();
            if(namedTeams.Length==model.Phases[0].Participants)
                initialStandings=namedTeams.Select((team,position)=>new FootballStandingSeed(team,position)).ToArray();
        }
        if(plan is not null||initialStandings.Count>0)
            FootballSeason.ApplyInitialStandings(c,competition,initialStandings);
        if(plan is not null)
        {
            if(plan.Calendar.SimulationOptions is {} options)c.SchedulingOptions[competition]=options;
            FootballInternationalFriendlies.RemoveSelected(c,plan.Calendar.FriendlyReplacements);
            FootballSeason.ApplyLinks(c,competition,plan.Links);
            if(plan.Calendar.Mode is FootballCalendarMode.ManualMatchdays or FootballCalendarMode.ExactFixtures)FootballExactCalendar.Apply(c,competition,plan.Calendar);
            if(plan.Calendar.Mode==FootballCalendarMode.AssignedFixtures)GenerateFixtures(c,competition,fixedTeams,plan.Calendar);
        }
    }
    public static void AddAssignedFixture(CompetitionProjectContext c,int competition,int phase,DateOnly date,string kickoff,string home,string away)
    {
        var played=FootballCompetitionAdapter.Read(c,competition).Phases.FirstOrDefault(p=>p.NativeId==phase);
        if(played is null||played.Special)throw new InvalidDataException("Choose a supported played phase in this competition.");
        if(home==away||!c.Teams.Any(t=>t.Id==home)||!c.Teams.Any(t=>t.Id==away))throw new InvalidDataException("Choose two different installed clubs.");
        string code=c.Objects.Single(o=>o.Id==competition).Code.ToLowerInvariant(),stage=c.Objects.Single(o=>o.Id==phase).Code.ToLowerInvariant(),time=TournamentBuilder.Time(kickoff);
        string path=Path.Combine("schedules",$"{code}_{stage}_{date.Year}.txt");
        if(c.Rows(path).Any(r=>r[0]==date.ToString("yyyyMMdd")&&r[1]==time&&(r[2]==home||r[3]==home||r[2]==away||r[3]==away)))throw new InvalidDataException("One of these clubs already has a fixture at this date and time in this phase.");
        c.Edit(path,t=>t.Data.Rows.Add(date.ToString("yyyyMMdd"),time,home,away));
    }
    public static void GenerateFixtures(CompetitionProjectContext c,int competition,IReadOnlyList<string> teams,FootballCalendarPlan calendar)
    {
        var first=FootballCompetitionAdapter.Read(c,competition).Phases.First();
        if(first.Kind==FootballPhaseKind.Knockout||teams.Count!=first.Participants)throw new InvalidDataException("Assigned first-stage fixtures need a league/group phase with every club selected explicitly.");
        int size=first.Participants/first.Groups;string code=c.Objects.Single(o=>o.Id==competition).Code.ToLowerInvariant(),phase=c.Objects.Single(o=>o.Id==first.NativeId).Code.ToLowerInvariant();
        for(int group=0;group<first.Groups;group++)foreach(var fixture in TournamentBuilder.RoundRobin(teams.Skip(group*size).Take(size).ToArray(),first.Legs==2))
        {
            var date=calendar.FirstDate.AddDays((fixture.Round-1)*calendar.IntervalDays);string path=Path.Combine("schedules",$"{code}_{phase}_{date.Year}.txt");
            c.Edit(path,t=>t.Data.Rows.Add(date.ToString("yyyyMMdd"),TournamentBuilder.Time(calendar.Kickoff),fixture.Home,fixture.Away));
        }
    }
    public static FootballSeasonLinks ReadLinks(CompetitionProjectContext c,int competition)
    {
        int? Read(string key)=>int.TryParse(c.EffectiveRules(competition).FirstOrDefault(r=>r.Key==key).Value,out int n)&&n>0?n:null;
        string? month=c.EffectiveRules(competition).FirstOrDefault(r=>r.Key=="schedule_seasonstartmonth").Value;
        int m=Array.FindIndex(System.Globalization.CultureInfo.InvariantCulture.DateTimeFormat.AbbreviatedMonthNames,n=>n.Equals(month,StringComparison.OrdinalIgnoreCase))+1;
        return new(Read("info_league_promo") is int p?new DatabaseCompetitionId(p):null,Read("info_league_releg") is int r?new DatabaseCompetitionId(r):null,Read("schedule_forcecomp") is int f?new CompdataObjectId(f):null,m is >=1 and <=12?m:null);
    }
    public static void RegenerateCalendar(CompetitionProjectContext c,int competition,FootballCalendarPlan calendar,bool confirmed)
    {
        if(!confirmed)throw new InvalidDataException("Review and confirm replacing the competition's relative calendar slots.");
        var scope=c.Scope(competition);if(c.Rows("settings.txt").Any(r=>scope.Contains(int.Parse(r[0]))&&r[1]=="schedule_use_dates_comp"))throw new InvalidDataException("This competition uses a shared calendar. Keep its links and edit its shared slots in Calendar.");
        if(c.Calendar(competition).Any(s=>s.FixedDate.HasValue))throw new InvalidDataException("Assigned fixtures are preserved. Edit them in Calendar instead of regenerating their dates.");
        if(calendar.IntervalDays is <1 or >365||calendar.FirstDate<c.ReferenceDate)throw new InvalidDataException("Choose a calendar start on or after the illustrative reference and a positive interval.");
        string time=TournamentBuilder.Time(calendar.Kickoff);var model=FootballCompetitionAdapter.Read(c,competition);
        if(model.Phases.Any(p=>p.Special))throw new InvalidDataException("This special format keeps its scheduling in Expert.");
        int day=calendar.FirstDate.DayNumber-c.ReferenceDate.DayNumber;
        c.Edit("schedule.txt",t=>{foreach(var row in t.Data.Rows.Cast<DataRow>().Where(r=>scope.Contains(int.Parse((string)r[0]))&&!FootballCompetitionAdapter.IsHidden(c,int.Parse((string)r[0]))).ToArray())row.Delete();
            if(calendar.Mode==FootballCalendarMode.None)return;
            foreach(var phase in model.Phases){int size=phase.Participants/phase.Groups;int rounds=phase.Kind==FootballPhaseKind.Knockout?phase.Legs:(size%2==0?size-1:size)*phase.Legs;int matches=phase.Kind==FootballPhaseKind.Knockout?phase.Participants/2:phase.Groups*(size/2);
                for(int round=1;round<=rounds;round++){t.Data.Rows.Add(phase.NativeId!.Value.ToString(),day.ToString(),round.ToString(),matches.ToString(),matches.ToString(),time);day+=calendar.IntervalDays;}}
        });
    }
}
