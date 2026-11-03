using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Media;
using ATLink.Core;
using ATLink.Views;
namespace ATLink.ViewModels;

public sealed record CalendarTeamIcon(string Id,string Name)
{
    public ImageSource? Crest=>EntityImages.Crest(Id);
}
public sealed record CalendarMonthDay(DateOnly Date,bool InMonth,IReadOnlyList<CalendarTeamIcon> Teams,int Extra,string Caption,string Problems)
{
    public string Day=>Date.Day.ToString();public string More=>Extra>0?"+"+Extra:"";public double Opacity=>InMonth?1:.4;
    public string Tooltip=>Date.ToString("dd MMM yyyy",CultureInfo.GetCultureInfo("en-GB"))+"\n"+Caption+"\n"+Problems;
}
public sealed class CalendarWeekday
{
    public DayOfWeek Day {get;init;}public string Name=>Day.ToString();public bool Allowed {get;set;}=true;
}
public sealed class CalendarBoardViewModel:FootballNotify
{
    readonly CreationCalendarViewModel calendar;readonly CompetitionProjectContext context;readonly Func<IReadOnlyList<FootballPhase>> layout;
    public Func<IReadOnlyList<string>> Participants {get;set;}=()=>[];
    public Func<bool> FixtureAssignmentAllowed {get;set;}=()=>true;
    public FootballScheduleIndex Index {get;private set;}=new([],[],"");
    public IReadOnlyList<FootballPlannedMatch> Matches {get;private set;}=[];
    public IReadOnlyList<CalendarMonthDay> Days {get;private set;}=[];
    public IReadOnlyList<FootballScheduleConflict> Conflicts=>Check(Matches);
    public IReadOnlyList<FootballScheduleConflict> Check(IReadOnlyList<FootballPlannedMatch> matches)
    {
        var fixedConflicts=FootballSchedulePlanner.Conflicts(matches.Where(m=>m.Locked||m.Confirmed).ToArray(),Index,RestDays).ToHashSet();
        return FootballSchedulePlanner.Conflicts(matches,Index,RestDays).Select(c=>c.Blocking&&!fixedConflicts.Contains(c)?c with{Blocking=false,Message="Risk in unfixed suggested dates: "+c.Message}:c).ToArray();
    }
    public string TeamName(string id)=>context.TeamName(id);
    public IReadOnlyList<string> TimeChoices {get;}=Enumerable.Range(0,96).Select(n=>$"{n/4:D2}:{n%4*15:D2}").ToArray();
    public IReadOnlyList<int> RestChoices {get;}=Enumerable.Range(0,8).ToArray();
    public ObservableCollection<CalendarWeekday> Weekdays {get;}=new(Enumerable.Range(0,7).Select(i=>new CalendarWeekday{Day=(DayOfWeek)((i+1)%7)}));
    public DateTime? Start {get;set;}public DateTime? End {get;set;}public string PreferredTime {get;set;}="15:00";
    int restDays=3;public int RestDays{get=>restDays;set{restDays=value;Notify();RefreshDays();}}
    public bool Busy{get;private set;}public int Progress{get;private set;}public string Error{get;private set;}="";
    public bool CanWork=>!Busy&&context.Game=="FC27";
    public string Coverage=>Index.Fingerprint.Length==0?"Load availability to compare the selected teams' other competitions.":$"{Index.Events.Count} external engagements indexed · {Index.Warnings.Count} coverage notes. No career-save or postponement prediction.";
    public string CoverageDetails=>string.Join("\n",Index.Warnings);
    public string ParticipantCoverage
    {
        get{try{int required=layout().FirstOrDefault()?.Participants??0;int resolved=Participants().Count;return $"{resolved}/{required} participants resolved. "+(resolved==required?"Named opening-round pairings are a reproducible proposal; group allocation may still be simulated.":"Unresolved entry rules prevent a complete team-availability guarantee. Future qualifiers remain possible participants.");}catch{return "Complete the competition format to resolve its participant coverage.";}}
    }
    public string Reference=>calendar.Season.ReferenceLabel+" Game postponements are not predictable from DB/Compdata.";
    DateOnly month;public string MonthTitle=>month.ToString("MMMM yyyy",CultureInfo.GetCultureInfo("en-GB"));
    public IReadOnlyList<FootballChoice> TeamChoices=>[new(0,"All participating teams"),..Participants().Distinct().Select(id=>new FootballChoice(int.Parse(id),context.TeamName(id)))];
    FootballChoice? team;public FootballChoice? Team{get=>team;set{if(value is null)return;team=value;Notify();RefreshDays();}}
    public IReadOnlyList<FootballCalendarPhase> PhaseChoices=>[new(-1,"All phases",0,0){Key="all"},..calendar.Phases];
    FootballCalendarPhase? phase;public FootballCalendarPhase? Phase{get=>phase;set{if(value is null)return;phase=value;Notify();RefreshDays();}}
    CancellationTokenSource? cancel;Task? loading;
    sealed class IndexCache {public FootballScheduleIndex? Value;}
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CompetitionProjectContext,IndexCache> cache=new();
    public bool Edited {get;private set;}
    public FootballScheduleProposal? Proposal {get;private set;}
    public string ProposalText=>Proposal is null?"":$"{Proposal.Matches.Count(m=>m.Date.HasValue)} planned matches · {Proposal.Unplaced.Count} unplaced · {Proposal.Conflicts.Count(c=>c.Blocking)} blocking conflicts · {Proposal.Conflicts.Count(c=>!c.Blocking)} risks\n"+string.Join("\n",Proposal.Unplaced.Select(id=>"Unplaced: "+Proposal.Matches.First(m=>m.Id==id).HomeLabel+" — "+Proposal.Matches.First(m=>m.Id==id).AwayLabel+" · "+Proposal.Reasons.GetValueOrDefault(id,"No admissible date.")))+"\n"+string.Join("\n",Proposal.Conflicts.Select(c=>c.Message).Distinct().Take(30));
    public string ProposalDates=>Proposal is null?"":string.Join("\n",Proposal.Matches.Where(m=>m.Date.HasValue).Select(m=>$"{m.Date:dd'/'MM'/'yyyy} {m.Kickoff[..2]}:{m.Kickoff[2..]} · {m.HomeLabel} — {m.AwayLabel}"));
    public bool HasProposal=>Proposal is not null;
    string proposalStamp="";
    string legacyStamp="";
    string LegacyStamp()=>System.Text.Json.JsonSerializer.Serialize(new{Rows=calendar.Matchdays.Select(r=>new{r.PhaseKey,r.Round,r.Date,r.Kickoff,r.Maximum}),Fixtures=calendar.Fixtures});
    string Stamp()=>System.Text.Json.JsonSerializer.Serialize(new{Matches,Options=Options(),Layout=layout(),Participants=Participants(),Index=Index.Fingerprint});
    sealed record Draft(IReadOnlyList<FootballPlannedMatch> Matches,CreationMatchday[] Rows,CreationFixture[] Fixtures,string Mode);
    readonly Stack<Draft> undo=[];readonly Stack<Draft> redo=[];
    public bool CanUndo=>undo.Count>0;public bool CanRedo=>redo.Count>0;
    public CalendarBoardViewModel(CreationCalendarViewModel calendar,CompetitionProjectContext context,Func<IReadOnlyList<FootballPhase>> layout)
    {
        this.calendar=calendar;this.context=context;this.layout=layout;team=new(0,"All participating teams");phase=new(-1,"All phases",0,0){Key="all"};month=new(context.ReferenceDate.Year,context.ReferenceDate.Month,1);Start=context.ReferenceDate.ToDateTime(TimeOnly.MinValue);End=context.ReferenceDate.AddYears(1).AddDays(-1).ToDateTime(TimeOnly.MinValue);
    }
    public void Synchronize()
    {
        string currentLegacy=LegacyStamp();bool reimport=currentLegacy!=legacyStamp;
        var phases=layout();var simulated=FootballFixtureSimulation.Build(phases,Participants(),context.TeamName);var old=Matches.ToDictionary(m=>m.Id);
        Matches=simulated.Select(m=>old.TryGetValue(m.Id,out var kept)?m with{Date=reimport?null:kept.Date,Kickoff=kept.Kickoff,Locked=!reimport&&kept.Locked,Confirmed=false}:m).ToArray();
        var merged=Matches.ToList();
        foreach(var fixture in calendar.Fixtures)
        {
            int at=merged.FindIndex(m=>fixture.PlannedId==m.Id||m.PhaseKey==fixture.PhaseKey&&m.Home==fixture.Home.Id.ToString()&&m.Away==fixture.Away.Id.ToString());
            if(at<0)at=merged.FindIndex(m=>m.PhaseKey==fixture.PhaseKey&&!m.Confirmed&&(fixture.Round is null||fixture.Round==m.Round));
            if(at>=0)merged[at]=merged[at] with{Home=fixture.Home.Id.ToString(),Away=fixture.Away.Id.ToString(),HomeLabel=fixture.Home.Name,AwayLabel=fixture.Away.Name,Date=fixture.Date,Kickoff=fixture.Kickoff,Locked=true,Confirmed=true};
        }
        // Capacity slots are projected once into match cards, not one card for every possible slot combination.
        foreach(var group in merged.Where(m=>!m.Confirmed).GroupBy(m=>(m.PhaseKey,m.Round)))
        {
            var slots=calendar.Matchdays.Where(r=>r.PhaseKey==group.Key.PhaseKey&&r.Round==group.Key.Round&&r.Date.HasValue).ToArray();int pos=0;
            foreach(var row in slots)if(int.TryParse(row.Maximum,out int max))for(int n=0;n<max-merged.Count(m=>m.Confirmed&&m.PhaseKey==row.PhaseKey&&m.Round==row.Round&&m.Date==DateOnly.FromDateTime(row.Date!.Value)&&m.Kickoff==FootballExactCalendar.Time(row.Kickoff))&&pos<group.Count();n++)
            {
                    var item=group.ElementAt(pos++);int at=merged.FindIndex(m=>m.Id==item.Id);if(!Edited||reimport)merged[at]=item with{Date=DateOnly.FromDateTime(row.Date!.Value),Kickoff=FootballExactCalendar.Time(row.Kickoff)};
            }
        }
        Matches=merged;legacyStamp=currentLegacy;Notify(nameof(TeamChoices));Notify(nameof(PhaseChoices));Notify(nameof(ParticipantCoverage));RefreshDays();
    }
    public void RefreshDays()
    {
        if(month==default)return;
        var shown=Proposal?.Matches??Matches;var conflicts=Proposal?.Conflicts??Conflicts;var first=month.AddDays(-(((int)month.DayOfWeek+6)%7));var participating=Participants().ToHashSet();
        Days=Enumerable.Range(0,42).Select(n=>
        {
            var date=first.AddDays(n);var own=shown.Where(m=>m.Date==date&&(phase is null||phase.Index<0||m.PhaseKey==phase.Key)).Where(m=>team is null||team.Id==0||(m.Teams.Count>0?m.Teams:m.PossibleTeams).Contains(team.Id.ToString())).ToArray();
            var external=Index.Events.Where(e=>e.Date==date&&e.Teams.Intersect(participating).Any()).Where(e=>team is null||team.Id==0||e.Teams.Contains(team.Id.ToString())).ToArray();
            var ids=own.SelectMany(m=>m.Teams.Count>0?m.Teams:m.PossibleTeams).Concat(external.SelectMany(e=>e.Teams.Intersect(participating))).Distinct().ToArray();
            string caption=(own.Length>0?$"{own.Length} planned · ":"")+(external.Length>0?$"{external.Count(e=>e.Status==FootballScheduleStatus.KnownFixture)} known / {external.Count(e=>e.Status==FootballScheduleStatus.SimulatedFixture)} simulated / {external.Count(e=>e.Status==FootballScheduleStatus.PossibleMatchday)} possible":"");
            return new CalendarMonthDay(date,date.Month==month.Month,ids.Take(6).Select(id=>new CalendarTeamIcon(id,context.TeamName(id))).ToArray(),Math.Max(0,ids.Length-6),caption,string.Join("\n",conflicts.Where(c=>own.Any(m=>m.Id==c.Match)).Select(c=>c.Message).Distinct().Take(3)));
        }).ToArray();Notify(nameof(Days));Notify(nameof(MonthTitle));Notify(nameof(CanUndo));Notify(nameof(CanRedo));
    }
    public void ChangeMonth(int amount){month=month.AddMonths(amount);RefreshDays();}
    public void FirstMonth(){var date=Start.HasValue?DateOnly.FromDateTime(Start.Value):context.ReferenceDate;month=new(date.Year,date.Month,1);RefreshDays();}
    public async Task LoadAvailability()
    {
        if(loading is not null){await loading;return;}
        string fingerprint=FootballAvailability.Fingerprint(context);if(Index.Fingerprint==fingerprint)return;
        if(cache.GetValue(context,_=>new()).Value is {} cached&&cached.Fingerprint==fingerprint){Index=cached;Notify(nameof(Coverage));Notify(nameof(CoverageDetails));RefreshDays();return;}
        cancel=new();Busy=true;Progress=0;Notify(nameof(Busy));Notify(nameof(CanWork));
        var progress=new Progress<int>(p=>{Progress=p;Notify(nameof(Progress));});
        loading=Load();try{await loading;}finally{loading=null;Busy=false;Notify(nameof(Busy));Notify(nameof(CanWork));}
        async Task Load()
        {
            try{Index=await Task.Run(()=>FootballAvailability.Build(FootballAvailability.Snapshot(context),progress,cancel.Token));cache.GetValue(context,_=>new()).Value=Index;Error="";Notify(nameof(Coverage));Notify(nameof(CoverageDetails));RefreshDays();}
            catch(OperationCanceledException){Error="Availability loading cancelled. No calendar changes applied.";}
            catch(Exception ex){Error=ex.Message;}
            Notify(nameof(Error));
        }
    }
    public void CancelLoad()=>cancel?.Cancel();
    public FootballSchedulingOptions Options()=>new(Start.HasValue?DateOnly.FromDateTime(Start.Value):throw new InvalidDataException("Choose a first date."),End.HasValue?DateOnly.FromDateTime(End.Value):throw new InvalidDataException("Choose a last date."),RestDays,FootballExactCalendar.Time(PreferredTime),Weekdays.Where(d=>d.Allowed).Select(d=>d.Day).ToArray());
    public async Task Simulate()
    {
        try
        {
            await LoadAvailability();if(Index.Fingerprint!=FootballAvailability.Fingerprint(context))throw new InvalidDataException("Load current availability before simulating.");Synchronize();var options=Options();var matches=Matches.ToArray();
            Busy=true;Progress=0;Notify(nameof(Busy));Notify(nameof(CanWork));Notify(nameof(Progress));cancel=new();proposalStamp=Stamp();var progress=new Progress<int>(p=>{Progress=p;Notify(nameof(Progress));});Proposal=await Task.Run(()=>FootballSchedulePlanner.Place(matches,Index,options,cancel.Token,progress));Error="";Notify(nameof(ProposalText));Notify(nameof(ProposalDates));Notify(nameof(HasProposal));RefreshDays();
        }
        catch(Exception ex){Error=ex is OperationCanceledException?"Simulation cancelled. The draft was not changed.":ex.Message;Notify(nameof(Error));}
        finally{Busy=false;Notify(nameof(Busy));Notify(nameof(CanWork));}
    }
    public void ApplyProposal()
    {
        try{if(Proposal is null)return;if(proposalStamp!=Stamp()||Index.Fingerprint!=FootballAvailability.Fingerprint(context))throw new InvalidDataException("The draft or scheduling options changed. Simulate again before applying.");if(Proposal.Unplaced.Count>0||Proposal.Conflicts.Any(c=>c.Blocking))throw new InvalidDataException("Resolve the blocking conflicts and unplaced matches before applying this proposal.");Commit(Proposal.Matches);Proposal=null;Notify(nameof(HasProposal));}
        catch(Exception ex){Error=ex.Message;Notify(nameof(Error));}
    }
    public void DiscardProposal(){Proposal=null;Notify(nameof(HasProposal));RefreshDays();}
    Draft Capture()=>new(Matches.ToArray(),calendar.Matchdays.Select(r=>new CreationMatchday{PhaseKey=r.PhaseKey,PhaseIndex=r.PhaseIndex,PhaseName=r.PhaseName,Round=r.Round,Date=r.Date,Kickoff=r.Kickoff,Minimum=r.Minimum,Maximum=r.Maximum,Capacity=r.Capacity}).ToArray(),calendar.Fixtures.ToArray(),calendar.Mode);
    public void Commit(IReadOnlyList<FootballPlannedMatch> matches)
    {
        var blocking=Check(matches).Where(c=>c.Blocking).ToArray();if(blocking.Length>0)throw new InvalidDataException(blocking[0].Message);
        undo.Push(Capture());redo.Clear();Matches=matches.ToArray();Edited=true;Proposal=null;Notify(nameof(HasProposal));WriteSlots();RefreshDays();
    }
    void WriteSlots()
    {
        calendar.Mode="Choose matchday dates";calendar.Matchdays.Clear();
        foreach(var group in Matches.Where(m=>m.Date.HasValue).GroupBy(m=>(m.PhaseKey,m.PhaseIndex,m.Round,Date:m.Date!.Value,m.Kickoff)))
        {
            var p=calendar.Phases.Single(p=>p.Key==group.Key.PhaseKey);calendar.Matchdays.Add(new(){PhaseKey=p.Key,PhaseIndex=p.Index,PhaseName=p.Name,Round=group.Key.Round,Date=group.Key.Date.ToDateTime(TimeOnly.MinValue),Kickoff=group.Key.Kickoff,Minimum="0",Maximum=group.Count().ToString(),Capacity=p.Matches});
        }
        calendar.Fixtures.Clear();foreach(var m in Matches.Where(m=>m.Confirmed&&m.Date.HasValue&&m.Home is not null&&m.Away is not null))calendar.Fixtures.Add(new(m.PhaseKey,m.PhaseIndex,calendar.Phases.Single(p=>p.Key==m.PhaseKey).Name,m.Date!.Value,m.Kickoff,new(int.Parse(m.Home!),m.HomeLabel),new(int.Parse(m.Away!),m.AwayLabel)){PlannedId=m.Id,Round=m.Round});
        calendar.NotifyBoardEdit();
        legacyStamp=LegacyStamp();
    }
    public void UseFixtures()
    {
        ValidateFixtureAssignment();
        var assignable=Matches.Where(m=>m.Home is not null&&m.Away is not null&&m.Date.HasValue).ToArray();if(assignable.Length==0)throw new InvalidDataException("No fully named simulated fixtures are available. Future qualifiers stay unassigned.");
        Commit(Matches.Select(m=>assignable.Any(a=>a.Id==m.Id)?m with{Confirmed=true,Locked=true}:m).ToArray());
    }
    public void ValidateFixtureAssignment()
    {
        if(!FixtureAssignmentAllowed())throw new InvalidDataException("Dynamic entry sources remain rules. Use the date simulation, or choose fixed participants before freezing named pairings.");
        if(Index.Fingerprint!=FootballAvailability.Fingerprint(context))throw new InvalidDataException("Refresh availability before confirming fixtures.");
    }
    void Restore(Draft draft)
    {
        Proposal=null;Notify(nameof(HasProposal));Matches=draft.Matches;calendar.Mode=draft.Mode;calendar.Matchdays.Clear();foreach(var row in draft.Rows)calendar.Matchdays.Add(row);calendar.Fixtures.Clear();foreach(var f in draft.Fixtures)calendar.Fixtures.Add(f);calendar.NotifyBoardEdit();legacyStamp=LegacyStamp();Edited=true;RefreshDays();
    }
    public void Undo(){if(undo.TryPop(out var draft)){redo.Push(Capture());Restore(draft);}}
    public void Redo(){if(redo.TryPop(out var draft)){undo.Push(Capture());Restore(draft);}}
    public string Venue(string phaseKey)
    {
        var expanded=FootballExactCalendar.Phases(layout());var p=expanded.Single(p=>p.Key==phaseKey);int offset=0;
        foreach(var source in layout()){int count=source.Kind==FootballPhaseKind.Knockout?source.Rounds:1;if(p.Index<offset+count){var setting=source.RoundSettings.ElementAtOrDefault(p.Index-offset)?.Stadium;if(setting?.Mode==FootballSettingMode.Value&&int.TryParse(setting.Value,out int id))return FootballPhaseSettingsAdapter.Stadiums(context).FirstOrDefault(s=>s.Id==id)?.Name??"Unavailable stadium";break;}offset+=count;}
        return "Competition default / usual home grounds";
    }
    public IReadOnlyList<FootballEngagement> ExternalOn(DateOnly date)=>Index.Events.Where(e=>e.Date==date&&e.Teams.Intersect(Participants()).Any()).ToArray();
    public void ValidateDraft()
    {
        if(!Edited)return;
        Synchronize();
        if(Index.Fingerprint!=FootballAvailability.Fingerprint(context))throw new InvalidDataException("Availability changed. Reload it and review the planned matches.");
        if(Matches.Any(m=>m.Date is null))throw new InvalidDataException("Some matches remain unplaced. Schedule them before creating the competition.");
        var blocking=FootballSchedulePlanner.Conflicts(Matches,Index,RestDays).FirstOrDefault(c=>c.Blocking);if(blocking is not null)throw new InvalidDataException(blocking.Message+" Adjust the remaining suggested dates or simulate a valid schedule before creation.");
    }
}
