using System.Globalization;
namespace ATLink.Core;

public enum FootballScheduleStatus { KnownFixture, SimulatedFixture, PossibleMatchday }
public sealed record FootballEngagement(string Id,int Competition,string CompetitionName,DateOnly Date,string? Kickoff,IReadOnlyList<string> Teams,FootballScheduleStatus Status,string Provenance);
public sealed record FootballScheduleIndex(IReadOnlyList<FootballEngagement> Events,IReadOnlyList<string> Warnings,string Fingerprint);
public sealed record FootballPlannedMatch(Guid Id,string PhaseKey,int PhaseIndex,int Round,int Match,string HomeLabel,string AwayLabel,string? Home,string? Away,IReadOnlyList<string> PossibleTeams,DateOnly? Date,string Kickoff,bool Locked=false,bool Confirmed=false)
{
    public IReadOnlyList<string> Teams=>new[]{Home,Away}.Where(t=>t is not null).Cast<string>().ToArray();
    public string TimeLabel=>Kickoff.Length==4?Kickoff[..2]+":"+Kickoff[2..]:Kickoff;
}
public sealed record FootballScheduleConflict(Guid Match,string Message,bool Blocking);
public sealed record FootballSchedulingOptions(DateOnly Start,DateOnly End,int RestDays,string Kickoff,IReadOnlyList<DayOfWeek> Days);
public sealed record FootballScheduleProposal(IReadOnlyList<FootballPlannedMatch> Matches,IReadOnlyList<FootballScheduleConflict> Conflicts,IReadOnlyList<Guid> Unplaced)
{
    public IReadOnlyDictionary<Guid,string> Reasons {get;init;}=new Dictionary<Guid,string>();
}

public static class FootballAvailability
{
    static string? NativeTime(string value)=>int.TryParse(value,out int n)&&n>=0&&n<=2359&&n%100<60?n.ToString("D4",CultureInfo.InvariantCulture):null;
    public static string Fingerprint(CompetitionProjectContext c)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(c.ReferenceDate+"|"+string.Join("|",c.Project.Files.Select(f=>f.RelativePath+":"+f.Text)))));
    public static CompetitionProjectContext Snapshot(CompetitionProjectContext c)=>new(new CompdataProject{SourceFolder=c.Project.SourceFolder,Files=c.Project.Files.Select(f=>new CompdataFile{RelativePath=f.RelativePath,Original=f.Original,Encoding=f.Encoding,InitialText=f.Text,Text=f.Text}).ToArray()},c.Game,c.Base,c.Localization,c.Metadata,c.LocMetadata){ReferenceDate=c.ReferenceDate,ReferenceConfirmed=c.ReferenceConfirmed};
    public static IReadOnlyList<string> Participants(CompetitionProjectContext c,int competition,List<string> warnings)
    {
        var result=new List<string>();var visiting=new HashSet<int>();
        string[] League(int id)=>c.Base is null?[]:new FootballCatalog(c.Base).Rows("leagueteamlinks").Where(r=>FootballCatalog.Value(r,"leagueid")==id.ToString()).Select(r=>FootballCatalog.Value(r,"teamid")).Distinct().ToArray();
        string[] Ranked(int id)=>c.Rows("initteams.txt").Where(r=>r[0]==id.ToString()&&r.Length>=3&&r[2]!="-1").OrderBy(r=>int.TryParse(r[1],out int n)?n:int.MaxValue).Select(r=>r[2]).Distinct().ToArray();
        void Read(int id)
        {
            if(!visiting.Add(id)){warnings.Add(c.ObjectName(competition)+": cyclic participant source; coverage incomplete.");return;}
            foreach(var row in c.Rows("tasks.txt").Where(r=>r.Length>=7&&r[0]==id.ToString()&&r[1]=="start"&&r[2].StartsWith("Fill")))
            {
                int P(int n)=>int.TryParse(row[n],out int value)?value:0;string[] teams=[];
                switch(row[2])
                {
                    case "FillWithTeam":teams=[row[5]];break;
                    case "FillFromLeague":case "FillFromLeagueInOrder":teams=League(P(4));break;
                    case "FillFromCompTable":teams=Ranked(P(4)).Take(P(5)).ToArray();if(teams.Length<P(5))warnings.Add(c.ObjectName(id)+": historical qualifiers are incomplete.");break;
                    case "FillFromCompTableBackup":case "FillFromCompTableBackupLeague":
                        teams=Ranked(P(4));if(teams.Length<P(6))teams=teams.Concat(row[2].EndsWith("League")?League(P(5)):Ranked(P(5))).Distinct().ToArray();teams=teams.Take(P(6)).ToArray();break;
                    default:warnings.Add(c.ObjectName(id)+": an unresolved participant-selection rule limits availability coverage.");break;
                }
                result.AddRange(teams.Where(t=>c.Teams.Any(x=>x.Id==t)));
            }
            if(!result.Any()&&c.Objects.FirstOrDefault(o=>o.Id==id) is {} obj&&int.TryParse(obj.Code.TrimStart('C','c'),out int db))result.AddRange(League(db));
        }
        Read(competition);return result.Distinct().ToArray();
    }
    public static FootballScheduleIndex Build(CompetitionProjectContext c,IProgress<int>? progress=null,CancellationToken cancellation=default)
    {
        var events=new List<FootballEngagement>();var warnings=new List<string>();var roots=c.Objects.Where(o=>o.Kind==3).ToArray();var seenSlots=new HashSet<string>();
        for(int n=0;n<roots.Length;n++)
        {
            cancellation.ThrowIfCancellationRequested();var root=roots[n];progress?.Report((n+1)*100/Math.Max(1,roots.Length));
            var teams=Participants(c,root.Id,warnings);var slots=c.Calendar(root.Id);
            if(c.EffectiveRules(root.Id).Any(r=>r.Key=="schedule_refcomp"))warnings.Add(root.DisplayName+": calendar reference behaviour may require game-side scheduling; only supplied comparable slots are projected.");
            foreach(var fixedSlot in slots.Where(s=>s.FixedDate.HasValue))
            {
                string key=fixedSlot.File+":"+fixedSlot.Row;if(!seenSlots.Add(key))continue;
                events.Add(new(key,root.Id,root.DisplayName,fixedSlot.FixedDate!.Value,NativeTime(fixedSlot.Time),[fixedSlot.Home!,fixedSlot.Away!],FootballScheduleStatus.KnownFixture,"Assigned fixture: "+fixedSlot.File));
            }
            if(teams.Count==0){warnings.Add(root.DisplayName+": participants could not be resolved; no free-time guarantee is available.");continue;}
            var model=FootballCompetitionAdapter.Read(c,root.Id);
            foreach(var phase in model.Phases)
            {
                var phaseSlots=slots.Where(s=>s.FixedDate is null&&(s.ObjectId==phase.NativeId||phase.NativeGroups.Contains(s.ObjectId))).ToArray();
                if(phaseSlots.Length==0&&phase==model.Phases.FirstOrDefault())phaseSlots=slots.Where(s=>s.Shared&&s.FixedDate is null).ToArray();
                foreach(var round in phaseSlots.GroupBy(s=>s.Round))
                {
                    if(phase.Kind==FootballPhaseKind.League&&phase.Groups==1&&phase==model.Phases.FirstOrDefault()&&teams.Count==phase.Participants&&round.Select(s=>s.Date(c.ReferenceDate)).Distinct().Count()==1)
                    {
                        var assigned=TournamentBuilder.RoundRobin(teams,phase.Legs==2).Where(f=>f.Round==round.Key).ToArray();
                        if(assigned.Length>0)
                        {
                            var times=round.Select(s=>NativeTime(s.Time)).Distinct().ToArray();int f=0;
                            foreach(var fixture in assigned)events.Add(new("sim:"+root.Id+":"+phase.NativeId+":"+round.Key+":"+f++,root.Id,root.DisplayName,round.First().Date(c.ReferenceDate),times.Length==1?times[0]:null,[fixture.Home,fixture.Away],FootballScheduleStatus.SimulatedFixture,"Reproducible round-robin proposal from resolved league participants; pairings are not native confirmed fixtures"));
                            continue;
                        }
                    }
                    // Several times on one day are alternatives/capacity, never several matches per club.
                    foreach(var day in round.GroupBy(s=>s.Date(c.ReferenceDate)))
                    {
                        string identity=string.Join(";",day.Select(s=>s.File+":"+s.Row).Order());
                        var times=day.Select(s=>NativeTime(s.Time)).Distinct().ToArray();
                        events.Add(new(identity,root.Id,root.DisplayName,day.Key,times.Length==1?times[0]:null,teams,FootballScheduleStatus.PossibleMatchday,"Native round "+round.Key+" · "+phase.Name+"; participant/qualification and slot allocation may change"));
                    }
                }
                if(phaseSlots.Length==0)warnings.Add(root.DisplayName+" · "+phase.Name+": no comparable native calendar was found.");
            }
        }
        warnings.Add(c.CalendarReferenceLabel+" Career-mode postponements and qualification results cannot be predicted.");
        return new(events.GroupBy(e=>e.Id).Select(g=>g.First() with{Teams=g.SelectMany(e=>e.Teams).Distinct().ToArray(),CompetitionName=string.Join(" / ",g.Select(e=>e.CompetitionName).Distinct())}).ToArray(),warnings.Distinct().ToArray(),Fingerprint(c));
    }
}

public static class FootballFixtureSimulation
{
    public static IReadOnlyList<FootballPlannedMatch> Build(IReadOnlyList<FootballPhase> phases,IReadOnlyList<string> participants,Func<string,string> name)
    {
        var result=new List<FootballPlannedMatch>();var calendar=FootballExactCalendar.Phases(phases);var stages=FootballMatchups.Stages(phases);int index=0;
        foreach(var phase in phases)
        {
            int count=phase.Kind==FootballPhaseKind.Knockout?phase.Rounds:1;
            for(int r=0;r<count;r++,index++)
            {
                var p=calendar[index];var fixtures=new List<(int Round,string? Home,string? Away,string H,string A,IReadOnlyList<string> Possible)>();
                var seeds=phase.Kind==FootballPhaseKind.Knockout?FootballMatchups.Seats(stages,index,participants):null;
                string? Club(FootballMatchupSource s)=>s.Kind==FootballMatchupSourceKind.Entry?s.TeamId??(participants.Count==stages[0].Participants?participants.ElementAtOrDefault(s.Ordinal):null):null;
                IReadOnlyList<string> Possible(FootballMatchupSource s)
                {
                    if(Club(s) is {} club)return [club];
                    var from=stages.FirstOrDefault(t=>t.Key==s.PhaseKey);
                    if(s.Kind==FootballMatchupSourceKind.Winner&&from is not null){var match=result.FirstOrDefault(m=>m.PhaseIndex==from.Index&&m.Match==s.Ordinal);if(match is not null)return match.Teams.Count>0?match.Teams:match.PossibleTeams;}
                    if(s.Kind==FootballMatchupSourceKind.GroupRank&&from?.Index==0&&participants.Count==from.Participants)return participants.Skip(s.Ordinal*(from.Participants/from.Phase.Groups)).Take(from.Participants/from.Phase.Groups).ToArray();
                    return participants.ToArray();
                }
                bool concrete=index==0&&participants.Count==phase.Participants;
                if(concrete&&phase.Kind is FootballPhaseKind.League or FootballPhaseKind.Groups)
                {
                    int size=phase.Participants/phase.Groups;
                    for(int g=0;g<phase.Groups;g++)foreach(var f in TournamentBuilder.RoundRobin(participants.Skip(g*size).Take(size).ToArray(),phase.Legs==2))fixtures.Add((f.Round,f.Home,f.Away,name(f.Home),name(f.Away),[f.Home,f.Away]));
                }
                else for(int leg=1;leg<=p.Rounds;leg++)for(int m=0;m<p.Matches;m++)
                {
                    string? home=seeds is not null?Club(seeds[2*m]):concrete?participants[2*m]:null,away=seeds is not null?Club(seeds[2*m+1]):concrete?participants[2*m+1]:null;
                    string h=seeds is not null?FootballMatchups.Label(seeds[2*m],stages,name):home is null?(index==0?"Participant "+(2*m+1):"Qualifier "+(2*m+1)+" from "+calendar[index-1].Name):name(home);
                    string a=seeds is not null?FootballMatchups.Label(seeds[2*m+1],stages,name):away is null?(index==0?"Participant "+(2*m+2):"Qualifier "+(2*m+2)+" from "+calendar[index-1].Name):name(away);
                    var possible=seeds is not null?Possible(seeds[2*m]).Concat(Possible(seeds[2*m+1])).Distinct().ToArray():participants.ToArray();
                    fixtures.Add((leg,leg==2?away:home,leg==2?home:away,leg==2?a:h,leg==2?h:a,possible));
                }
                var counters=new Dictionary<int,int>();
                foreach(var f in fixtures)
                {
                    int match=counters.GetValueOrDefault(f.Round);counters[f.Round]=match+1;
                    var bytes=System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(p.Key+":"+f.Round+":"+match));
                    result.Add(new(new Guid(bytes),p.Key,p.Index,f.Round,match,f.H,f.A,f.Home,f.Away,f.Possible,null,"1500"));
                }
            }
        }
        return result;
    }
}

public static class FootballSchedulePlanner
{
    public static DateTime At(DateOnly date,string time)=>date.ToDateTime(TimeOnly.ParseExact(FootballExactCalendar.Time(time),"HHmm",CultureInfo.InvariantCulture));
    public static IReadOnlyList<FootballScheduleConflict> Conflicts(IReadOnlyList<FootballPlannedMatch> matches,FootballScheduleIndex index,int restDays,Guid? focus=null)
    {
        var conflicts=new List<FootballScheduleConflict>();
        foreach(var match in matches.Where(m=>m.Date.HasValue&&(!focus.HasValue||m.Id==focus)))
        {
            var teams=match.Teams.Count>0?match.Teams:match.PossibleTeams;
            bool Near(DateOnly date,string? time)
            {
                if(date==match.Date)return true;var target=At(match.Date!.Value,match.Kickoff);
                if(time is not null)return Math.Abs((target-At(date,time)).TotalHours)<restDays*24;
                var start=date.ToDateTime(TimeOnly.MinValue);var end=start.AddDays(1);return (target<start?(start-target).TotalHours:target>=end?(target-end).TotalHours:0)<restDays*24;
            }
            foreach(var other in index.Events.Where(e=>e.Teams.Intersect(teams).Any()&&Near(e.Date,e.Kickoff)))
            {
                bool hard=other.Status==FootballScheduleStatus.KnownFixture&&match.Teams.Intersect(other.Teams).Any();
                conflicts.Add(new(match.Id,(hard?"Conflict: ":"Risk: ")+other.CompetitionName+" on "+other.Date.ToString("dd/MM/yyyy",CultureInfo.InvariantCulture)+" · "+other.Provenance,hard));
            }
            foreach(var other in matches.Where(m=>m.Id!=match.Id&&m.Date.HasValue&&Near(m.Date!.Value,m.Kickoff)))
                if(match.Teams.Intersect(other.Teams).Any())conflicts.Add(new(match.Id,"An assigned/simulated participant has another match on the same day or inside the required rest interval.",true));
            if(matches.Any(m=>m.Date.HasValue&&(m.PhaseIndex<match.PhaseIndex||m.PhaseIndex==match.PhaseIndex&&m.Round<match.Round)&&(m.Date>=match.Date||(At(match.Date!.Value,match.Kickoff)-At(m.Date!.Value,m.Kickoff)).TotalHours<restDays*24)))conflicts.Add(new(match.Id,"A later round must follow the preceding round and respect the required recovery interval.",true));
            if(matches.Any(m=>m.Locked&&m.Date.HasValue&&(m.PhaseIndex>match.PhaseIndex||m.PhaseIndex==match.PhaseIndex&&m.Round>match.Round)&&(match.Date>=m.Date||(At(m.Date!.Value,m.Kickoff)-At(match.Date!.Value,match.Kickoff)).TotalHours<restDays*24)))conflicts.Add(new(match.Id,"A manually fixed later round leaves insufficient time for this match and the required recovery.",true));
        }
        return conflicts.Distinct().ToArray();
    }
    public static FootballScheduleProposal Place(IReadOnlyList<FootballPlannedMatch> input,FootballScheduleIndex index,FootballSchedulingOptions options,CancellationToken cancellation=default,IProgress<int>? progress=null)
    {
        if(options.End<options.Start||options.RestDays is <0 or >7||options.Days.Count==0)throw new InvalidDataException("Choose a valid date range, allowed weekdays and 0–7 rest days.");
        string time=FootballExactCalendar.Time(options.Kickoff);var placed=input.Where(m=>m.Locked&&m.Date.HasValue).ToList();var unplaced=new List<Guid>();
        int lastPhase=-1,lastRound=-1,processed=placed.Count;DateOnly earliest=options.Start;var reasons=new Dictionary<Guid,string>();
        foreach(var group in input.GroupBy(m=>(m.PhaseIndex,m.Round)).OrderBy(g=>g.Key.PhaseIndex).ThenBy(g=>g.Key.Round))
        {
            if(lastPhase>=0&&(lastPhase!=group.Key.PhaseIndex||lastRound!=group.Key.Round))earliest=placed.Where(m=>m.PhaseIndex==lastPhase&&m.Round==lastRound&&m.Date.HasValue).Select(m=>m.Date!.Value.AddDays(Math.Max(1,options.RestDays))).DefaultIfEmpty(earliest).Max();
            foreach(var match in group.Where(m=>!m.Locked||!m.Date.HasValue))
            {
                cancellation.ThrowIfCancellationRequested();FootballPlannedMatch? best=null;int score=int.MaxValue;
                var rejected=new HashSet<string>();
                for(var date=earliest;date<=options.End;date=date.AddDays(1))
                {
                    cancellation.ThrowIfCancellationRequested();
                    if(!options.Days.Contains(date.DayOfWeek))continue;
                    var candidate=match with{Date=date,Kickoff=time};var conflicts=Conflicts(placed.Append(candidate).ToArray(),index,options.RestDays,candidate.Id).ToArray();
                    if(conflicts.Any(c=>c.Blocking)){foreach(var issue in conflicts.Where(c=>c.Blocking).Take(3))rejected.Add(issue.Message);continue;}
                    int distance=match.Date is DateOnly preferred?Math.Abs(preferred.DayNumber-date.DayNumber):date.DayNumber-earliest.DayNumber;
                    int cost=conflicts.Length*100000+distance;
                    if(cost<score){score=cost;best=candidate;}if(cost==0)break;
                }
                if(best is null){unplaced.Add(match.Id);placed.Add(match with{Date=null});reasons[match.Id]=earliest>options.End?"The preceding round and recovery period extend beyond the selected end date.":"No allowed date satisfies the selected weekdays and "+options.RestDays+" rest days. "+string.Join(" ",rejected.Take(3));}else placed.Add(best);
                progress?.Report(++processed*100/Math.Max(1,input.Count));
            }
            lastPhase=group.Key.PhaseIndex;lastRound=group.Key.Round;
        }
        return new(placed.OrderBy(m=>m.PhaseIndex).ThenBy(m=>m.Round).ThenBy(m=>m.Match).ToArray(),Conflicts(placed,index,options.RestDays),unplaced){Reasons=reasons};
    }
}
