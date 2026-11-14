using System.Collections.ObjectModel;
using System.IO;
using ATLink.Core;
namespace ATLink.ViewModels;
public sealed class CalendarMatchCard:FootballNotify
{
    public FootballPlannedMatch Original {get;}
    public string Title=>Original.HomeLabel+" — "+Original.AwayLabel;
    public string Phase {get;}
    public string Status=>Original.Confirmed?ATLink.Core.UiText.Get("Known fixture"):Original.Home is null||Original.Away is null?ATLink.Core.UiText.Get("Possible matchday"):ATLink.Core.UiText.Get("Simulated fixture");
    public string Venue {get;}
    public FootballTieVenueViewModel? TieVenue {get;init;}
    public bool HasTieVenue=>TieVenue is not null;
    public bool NoTieVenue=>TieVenue is null;
    public string VenueNote {get;init;}=FootballPhaseSettingsAdapter.HostLimitation;
    public IReadOnlyList<string> Times {get;}
    bool canEditTime=true;public bool CanEditTime{get=>canEditTime;set{if(canEditTime==value)return;canEditTime=value;Notify();}}
    string time;public string Time{get=>time;set{if(value is null)return;time=value;Notify();}}
    public string Conflict {get;set;}="";
    public void SetConflict(string message){Conflict=message;Notify(nameof(Conflict));}
    public CalendarMatchCard(FootballPlannedMatch match,string phase,string venue,IReadOnlyList<string> times)
    {Original=match;Phase=ATLink.Core.UiText.Concat(phase, ATLink.Core.UiText.Get(" · Matchday "), match.Round);Venue=venue;time=match.Kickoff[..2]+":"+match.Kickoff[2..];Times=times.Contains(time)?times:times.Append(time).Order().ToArray();}
    public FootballPlannedMatch Snapshot(DateOnly date)=>Original with{Date=date,Kickoff=FootballExactCalendar.Time(Time),Locked=true};
}
public sealed record CalendarExternalRow(string Title,string Status,string Provenance,IReadOnlyList<CalendarTeamIcon> Teams);
public sealed class CalendarFriendlySlotVm
{
    public required InternationalFriendlySlot Slot {get;init;}
    public string Description=>Slot.Description;
    public bool Replace {get;set;}
}
public sealed class CalendarDayViewModel:FootballNotify
{
    readonly CalendarBoardViewModel board;readonly IReadOnlyList<FootballPlannedMatch> original;
    readonly Dictionary<Guid,FootballPlannedMatch> draft;
    readonly Dictionary<(string Phase,int Tie),FootballTieVenueViewModel> tieDraft=[];
    public DateOnly Date {get;}
    public bool CanEdit {get;}
    bool updatingTimes;
    bool fixedHours;public bool FixedHours{get=>fixedHours;set{if(!CanEdit||fixedHours==value)return;fixedHours=value;Notify();Notify(nameof(CanEditFixedTime));ApplyFixedTime();}}
    string fixedTime="15:00";public string FixedTime{get=>fixedTime;set{if(!CanEdit||value is null||value==fixedTime)return;fixedTime=value;Notify();if(FixedHours)ApplyFixedTime();}}
    public bool CanEditFixedTime=>CanEdit&&FixedHours;
    public IReadOnlyList<string> FixedTimeChoices {get;private set;}=[];
    void ApplyFixedTime()
    {
        updatingTimes=true;
        try{foreach(var card in Cards){card.CanEditTime=CanEdit&&!FixedHours;if(FixedHours)card.Time=FixedTime;}}
        finally{updatingTimes=false;}
        RefreshConflicts();
    }
    public string Title=>Date.ToString("dddd, dd MMMM yyyy",UiText.Current.DisplayCulture);
    public string Reference=>board.Reference;
    public IReadOnlyList<CalendarExternalRow> External {get;}
    public IReadOnlyList<CalendarFriendlySlotVm> FriendlySlots {get;}
    public bool HasFriendlySlots=>FriendlySlots.Count>0;
    public IReadOnlyList<InternationalCalendarEntry> OtherInternationalEntries {get;}
    public bool HasOtherInternationalEntries=>OtherInternationalEntries.Count>0;
    public IReadOnlyList<FootballCalendarPhase> Phases {get;}
    public ObservableCollection<CalendarMatchCard> Cards {get;}=[];
    public string Error{get;private set;}="";
    public Func<bool>? ConfirmRemoval {get;set;}
    public Func<string,bool>? ConfirmFriendlyReplacement {get;set;}
    FootballCalendarPhase? phase;public FootballCalendarPhase? Phase{get=>phase;set{if(value is null||phase?.Key==value.Key)return;phase=value;round=1;Notify();Notify(nameof(Rounds));Notify(nameof(Round));RefreshCounts();}}
    public IReadOnlyList<int> Rounds=>Enumerable.Range(1,Phase?.Rounds??1).ToArray();
    int round=1;public int Round{get=>round;set{if(value<1||value==round)return;round=value;Notify();RefreshCounts();}}
    public IReadOnlyList<int> Counts=>Enumerable.Range(0,Eligible().Count()+1).ToArray();
    int count;bool setting;
    public int Count{get=>count;set
    {
        if(!CanEdit||setting||value==count||value<0)return;
        var pool=Eligible().ToArray();if(value>pool.Length)return;
        var existing=Cards.Where(c=>c.Original.PhaseKey==Phase?.Key&&c.Original.Round==Round).ToArray();
        if(value<existing.Length&&ConfirmRemoval?.Invoke()!=true){Notify();return;}
        SaveCards();
        foreach(var card in existing.Skip(value)){draft[card.Original.Id]=card.Original with{Date=null,Confirmed=false,Locked=false};Cards.Remove(card);}
        foreach(var match in pool.Where(m=>!Cards.Any(c=>c.Original.Id==m.Id)).Take(Math.Max(0,value-existing.Length)))
        {var moved=match with{Date=Date};draft[match.Id]=moved;Cards.Add(Card(moved));}
        count=value;Notify();RefreshConflicts();
    }}
    IEnumerable<FootballPlannedMatch> Eligible()=>draft.Values.Where(m=>m.PhaseKey==Phase?.Key&&m.Round==Round&&(m.Date==Date||!m.Locked||m.Date is null)).OrderByDescending(m=>m.Date==Date).ThenBy(m=>m.Match);
    public CalendarDayViewModel(CalendarBoardViewModel board,IReadOnlyList<FootballCalendarPhase> phases,DateOnly date,bool preview=false)
    {
        this.board=board;CanEdit=!preview;Date=date;Phases=phases;original=(preview?board.Proposal?.Matches??board.Matches:board.Matches).ToArray();draft=original.ToDictionary(m=>m.Id);
        FriendlySlots=board.FriendlySlotsOn(date).Select(s=>new CalendarFriendlySlotVm{Slot=s,Replace=board.FriendlyReplacements.Any(r=>r.Key==s.Key)}).ToArray();
        OtherInternationalEntries=board.InternationalOn(date).Where(entry=>ATLink.Core.UiText.Source(entry.Kind)!="Friendly"||entry.Slot.FixedDate.HasValue).ToArray();
        External=board.ExternalOn(date).Select(e=>new CalendarExternalRow(e.CompetitionName+(e.Kickoff is null?"":" · "+e.Kickoff[..2]+":"+e.Kickoff[2..]),e.Status==FootballScheduleStatus.KnownFixture?ATLink.Core.UiText.Get("Known fixture"):ATLink.Core.UiText.Get("Possible matchday"),e.Provenance,e.Teams.Where(t=>board.Participants().Contains(t)).Select(t=>new CalendarTeamIcon(t,board.TeamName(t))).ToArray())).ToArray();
        phase=phases.FirstOrDefault(p=>original.Any(m=>m.PhaseKey==p.Key&&m.Date==date))??phases.FirstOrDefault();round=original.FirstOrDefault(m=>m.PhaseKey==phase?.Key&&m.Date==date)?.Round??1;
        var dayMatches=original.Where(m=>m.Date==date).ToArray();var times=dayMatches.Select(m=>m.TimeLabel).Distinct().ToArray();
        fixedHours=board.FixedDayTimes.ContainsKey(date);fixedTime=board.FixedDayTimes.GetValueOrDefault(date)??(times.Length==1?times[0]:board.PreferredTime);
        FixedTimeChoices=board.TimeChoices.Concat(times).Append(fixedTime).Distinct().Order().ToArray();
        foreach(var match in dayMatches)Cards.Add(Card(match));RefreshCounts();RefreshConflicts();
    }
    CalendarMatchCard Card(FootballPlannedMatch m)
    {
        var key=(m.PhaseKey,m.Match);if(!tieDraft.ContainsKey(key)&&board.TieVenue(m) is {} venue)tieDraft[key]=venue.Copy();
        var card=new CalendarMatchCard(m,Phases.First(p=>p.Key==m.PhaseKey).Name,board.Venue(m.PhaseKey),board.TimeChoices.Concat(FixedTimeChoices).Distinct().Order().ToArray()){CanEditTime=CanEdit&&!FixedHours,TieVenue=tieDraft.GetValueOrDefault(key),VenueNote=board.TieVenueNote(m)};if(FixedHours)card.Time=FixedTime;card.PropertyChanged+=(_,e)=>{if(!updatingTimes&&e.PropertyName==nameof(CalendarMatchCard.Time))RefreshConflicts();};return card;
    }
    void SaveCards(){foreach(var card in Cards)draft[card.Original.Id]=card.Snapshot(Date);}
    void RefreshCounts(){setting=true;try{count=Cards.Count(c=>c.Original.PhaseKey==Phase?.Key&&c.Original.Round==Round);Notify(nameof(Counts));Notify(nameof(Count));}finally{setting=false;}}
    void RefreshConflicts()
    {
        try{SaveCards();var conflicts=board.Check(draft.Values.ToArray());foreach(var card in Cards)card.SetConflict(ATLink.Core.UiText.Join("\n",conflicts.Where(c=>c.Match==card.Original.Id).Select(c=>c.Message).Distinct().Take(4)));Error=ATLink.Core.UiText.Join("\n",conflicts.Where(c=>c.Blocking&&Cards.Any(m=>m.Original.Id==c.Match)).Select(c=>c.Message).Distinct().Take(8));}catch(Exception ex){Error=ex.Message;}Notify(nameof(Error));
    }
    public bool Apply()
    {
        try
        {
            if(!CanEdit)throw new InvalidDataException(ATLink.Core.UiText.Get("This is a simulation preview. Apply or discard the proposal in the calendar first."));SaveCards();
            var selected=FriendlySlots.Where(s=>s.Replace).Select(s=>s.Slot).ToArray();
            if(selected.Length>0&&Cards.Count==0)throw new InvalidDataException(ATLink.Core.UiText.Get("Add at least one match of your competition on this day before replacing an international friendly slot."));
            var added=selected.Where(s=>!board.FriendlyReplacements.Any(r=>r.Key==s.Key)).ToArray();
            if(added.Length>0&&ConfirmFriendlyReplacement?.Invoke(ATLink.Core.UiText.Concat(ATLink.Core.UiText.Get("Remove these entire international friendly slots from the exported schedule? All national teams using the selected slots are affected.\n\n"), ATLink.Core.UiText.Join("\n",added.Select(s=>s.Description))))!=true)return false;
            var dayKeys=FriendlySlots.Select(s=>s.Slot.Key).ToHashSet();
            var fixedTimes=board.FixedDayTimes.ToDictionary();if(FixedHours)fixedTimes[Date]=FixedTime;else fixedTimes.Remove(Date);
            var tieChanges=tieDraft.Where(t=>Cards.Any(c=>c.Original.PhaseKey==t.Key.Phase&&c.Original.Match==t.Key.Tie)).Select(t=>new CalendarBoardViewModel.TieState(t.Key.Phase,t.Value.Snapshot())).ToArray();
            board.Commit(draft.Values.ToArray(),board.FriendlyReplacements.Where(s=>!dayKeys.Contains(s.Key)).Concat(selected).ToArray(),fixedTimes,tieChanges);return true;
        }
        catch(Exception ex){Error=ex.Message;Notify(nameof(Error));return false;}
    }
}
