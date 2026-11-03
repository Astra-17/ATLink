using System.Collections.ObjectModel;
using System.IO;
using ATLink.Core;
namespace ATLink.ViewModels;
public sealed class CalendarMatchCard:FootballNotify
{
    public FootballPlannedMatch Original {get;}
    public string Title=>Original.HomeLabel+" — "+Original.AwayLabel;
    public string Phase {get;}
    public string Status=>Original.Confirmed?"Known fixture":Original.Home is null||Original.Away is null?"Possible matchday":"Simulated fixture";
    public string Venue {get;}
    public string VenueNote=>FootballPhaseSettingsAdapter.HostLimitation;
    public IReadOnlyList<string> Times {get;}
    string time;public string Time{get=>time;set{if(value is null)return;time=value;Notify();}}
    public string Conflict {get;set;}="";
    public void SetConflict(string message){Conflict=message;Notify(nameof(Conflict));}
    public CalendarMatchCard(FootballPlannedMatch match,string phase,string venue,IReadOnlyList<string> times)
    {Original=match;Phase=phase+" · Matchday "+match.Round;Venue=venue;time=match.Kickoff[..2]+":"+match.Kickoff[2..];Times=times.Contains(time)?times:times.Append(time).Order().ToArray();}
    public FootballPlannedMatch Snapshot(DateOnly date)=>Original with{Date=date,Kickoff=FootballExactCalendar.Time(Time),Locked=true};
}
public sealed record CalendarExternalRow(string Title,string Status,string Provenance,IReadOnlyList<CalendarTeamIcon> Teams);
public sealed class CalendarDayViewModel:FootballNotify
{
    readonly CalendarBoardViewModel board;readonly IReadOnlyList<FootballPlannedMatch> original;
    readonly Dictionary<Guid,FootballPlannedMatch> draft;
    public DateOnly Date {get;}
    public bool CanEdit {get;}
    public string Title=>Date.ToString("dddd, dd MMMM yyyy",System.Globalization.CultureInfo.GetCultureInfo("en-GB"));
    public string Reference=>board.Reference;
    public IReadOnlyList<CalendarExternalRow> External {get;}
    public IReadOnlyList<FootballCalendarPhase> Phases {get;}
    public ObservableCollection<CalendarMatchCard> Cards {get;}=[];
    public string Error{get;private set;}="";
    public Func<bool>? ConfirmRemoval {get;set;}
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
        External=board.ExternalOn(date).Select(e=>new CalendarExternalRow(e.CompetitionName+(e.Kickoff is null?"":" · "+e.Kickoff[..2]+":"+e.Kickoff[2..]),e.Status==FootballScheduleStatus.KnownFixture?"Known fixture":"Possible matchday",e.Provenance,e.Teams.Where(t=>board.Participants().Contains(t)).Select(t=>new CalendarTeamIcon(t,board.TeamName(t))).ToArray())).ToArray();
        phase=phases.FirstOrDefault(p=>original.Any(m=>m.PhaseKey==p.Key&&m.Date==date))??phases.FirstOrDefault();round=original.FirstOrDefault(m=>m.PhaseKey==phase?.Key&&m.Date==date)?.Round??1;
        foreach(var match in original.Where(m=>m.Date==date))Cards.Add(Card(match));RefreshCounts();RefreshConflicts();
    }
    CalendarMatchCard Card(FootballPlannedMatch m)
    {
        var card=new CalendarMatchCard(m,Phases.First(p=>p.Key==m.PhaseKey).Name,board.Venue(m.PhaseKey),board.TimeChoices);card.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(CalendarMatchCard.Time))RefreshConflicts();};return card;
    }
    void SaveCards(){foreach(var card in Cards)draft[card.Original.Id]=card.Snapshot(Date);}
    void RefreshCounts(){setting=true;try{count=Cards.Count(c=>c.Original.PhaseKey==Phase?.Key&&c.Original.Round==Round);Notify(nameof(Counts));Notify(nameof(Count));}finally{setting=false;}}
    void RefreshConflicts()
    {
        try{SaveCards();var conflicts=board.Check(draft.Values.ToArray());foreach(var card in Cards)card.SetConflict(string.Join("\n",conflicts.Where(c=>c.Match==card.Original.Id).Select(c=>c.Message).Distinct().Take(4)));Error=string.Join("\n",conflicts.Where(c=>c.Blocking&&Cards.Any(m=>m.Original.Id==c.Match)).Select(c=>c.Message).Distinct().Take(8));}catch(Exception ex){Error=ex.Message;}Notify(nameof(Error));
    }
    public bool Apply()
    {
        try{if(!CanEdit)throw new InvalidDataException("This is a simulation preview. Apply or discard the proposal in the calendar first.");SaveCards();board.Commit(draft.Values.ToArray());return true;}catch(Exception ex){Error=ex.Message;Notify(nameof(Error));return false;}
    }
}
