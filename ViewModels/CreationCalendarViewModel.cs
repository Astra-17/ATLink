using System.IO;
using System.Collections.ObjectModel;
using ATLink.Core;
namespace ATLink.ViewModels;
public sealed class CreationMatchday:FootballNotify
{
    public void UpdateSchedule(DateOnly date,string time){Date=date.ToDateTime(TimeOnly.MinValue);Kickoff=time[..2]+":"+time[2..];Notify(nameof(Date));Notify(nameof(Kickoff));}
    public string PhaseKey {get;set;}="";public int PhaseIndex {get;set;}public string PhaseName {get;set;}="";public int Round {get;set;}
    public string Label=>"Matchday "+Round;public DateTime? Date {get;set;}public string Kickoff {get;set;}="15:00";
    public string Minimum {get;set;}="0";public string Maximum {get;set;}="1";public int Capacity {get;set;}
    public IReadOnlyList<string> Counts=>Enumerable.Range(0,Capacity+1).Select(n=>n.ToString()).ToArray();
    public FootballMatchdayDraft Snapshot()=>new(PhaseIndex,Round,Date.HasValue?DateOnly.FromDateTime(Date.Value):throw new InvalidDataException(PhaseName+" · "+Label+": choose a date."),FootballExactCalendar.Time(Kickoff),int.TryParse(Minimum,out int min)?min:throw new InvalidDataException("Choose the minimum match capacity."),int.TryParse(Maximum,out int max)?max:throw new InvalidDataException("Choose the maximum match capacity."));
}
public sealed record CreationCalendarCard(FootballCalendarPhase Phase,IReadOnlyList<CreationMatchday> Rows);
public sealed record CreationFixture(string PhaseKey,int PhaseIndex,string PhaseName,DateOnly Date,string Kickoff,FootballChoice Home,FootballChoice Away)
{
    public Guid? PlannedId {get;init;}public int? Round {get;init;}
    public string Description=>$"{Date:dd'/'MM'/'yyyy} · {Kickoff[..2]}:{Kickoff[2..]} · {Home.Name} — {Away.Name}";
}
public sealed class CreationCalendarViewModel:FootballNotify
{
    public CalendarBoardViewModel Board {get;}
    public void NotifyBoardEdit(){signature=Shape(Phases);Refresh();}
    readonly CompetitionProjectContext context;readonly Func<IReadOnlyList<FootballPhase>> layout;
    public FootballSeasonViewModel Season {get;}
    public IReadOnlyList<string> Modes {get;}=["Automatic schedule","Choose matchday dates","Exact fixtures"];
    string mode="Automatic schedule";public string Mode{get=>mode;set{if(value is null||mode==value||!Modes.Contains(value))return;mode=value;Error="";Notify(nameof(Error));Notify();Notify(nameof(Manual));Notify(nameof(Exact));Notify(nameof(Automatic));Notify(nameof(CanAssign));if(Manual&&Matchdays.Count==0)BuildMatchdays();}}
    public bool CanAssign=>Manual||Exact;
    public bool Manual=>Mode=="Choose matchday dates";public bool Exact=>Mode=="Exact fixtures";public bool Automatic=>!Manual&&!Exact;
    public ObservableCollection<CreationMatchday> Matchdays {get;}=[];
    public ObservableCollection<CreationFixture> Fixtures {get;}=[];
    public IReadOnlyList<FootballCalendarPhase> Phases {get;private set;}=[];
    public IReadOnlyList<CreationCalendarCard> Cards=>Phases.Select(p=>new CreationCalendarCard(p,Matchdays.Where(r=>r.PhaseKey==p.Key).ToArray())).ToArray();
    public FootballCalendarPhase? FixturePhase {get;set;}public DateTime? FixtureDate {get;set;}public string FixtureTime {get;set;}="15:00";
    public FootballChoice? Home {get;set;}public FootballChoice? Away {get;set;}public CreationFixture? SelectedFixture {get;set;}
    public IReadOnlyList<FootballChoice> Clubs=>Season.Clubs;
    public string Error {get;private set;}="";public string Note {get;private set;}="";
    FootballCalendarPhase? bulkPhase;
    public FootballCalendarPhase? BulkPhase{get=>bulkPhase;set{if(value is not null)bulkPhase=value;}}
    public DateTime? BulkDate {get;set;}
    public string BulkTime {get;set;}="15:00";
    public void AdjustPhase(bool moveDates)
    {
        try
        {
            if(!Manual)throw new InvalidDataException("Choose matchday dates before adjusting a phase.");
            var plan=Plan();var phase=Phases.FirstOrDefault(p=>p.Key==BulkPhase?.Key)??throw new InvalidDataException("Choose a phase.");
            int delta=0;string? time=null;
            if(moveDates){if(!BulkDate.HasValue)throw new InvalidDataException("Choose the phase's new first date.");delta=DateOnly.FromDateTime(BulkDate.Value).DayNumber-plan.Matchdays.Where(r=>r.PhaseIndex==phase.Index).Min(r=>r.Date.DayNumber);}
            else time=FootballExactCalendar.Time(BulkTime);
            var days=plan.Matchdays.Select(r=>r.PhaseIndex==phase.Index?r with{Date=r.Date.AddDays(delta),Kickoff=time??r.Kickoff}:r).ToArray();
            var fixtures=plan.Fixtures.Select(f=>f.PhaseIndex==phase.Index?f with{Date=f.Date.AddDays(delta),Kickoff=time??f.Kickoff}:f).ToArray();
            FootballExactCalendar.Validate(context,Phases,plan with{Matchdays=days,Fixtures=fixtures});
            for(int i=0;i<Matchdays.Count;i++)Matchdays[i].UpdateSchedule(days[i].Date,days[i].Kickoff);
            for(int i=0;i<Fixtures.Count;i++)if(Fixtures[i].PhaseKey==phase.Key)Fixtures[i]=Fixtures[i] with{Date=Fixtures[i].Date.AddDays(delta),Kickoff=time??Fixtures[i].Kickoff};
            Error="";Note=moveDates?"Phase dates moved together. Spacing and kick-off times were preserved.":"Kick-off applied to this phase's slots and assigned fixtures. Dates were preserved.";Notify(nameof(Error));Notify(nameof(Note));
        }
        catch(Exception ex){Error=ex.Message;Notify(nameof(Error));}
    }
    string signature="";
    string Shape(IReadOnlyList<FootballCalendarPhase> phases)=>string.Join("|",phases.Select(p=>$"{p.Key}:{p.Rounds}:{p.Matches}"));
    public CreationCalendarViewModel(CompetitionProjectContext context,FootballSeasonViewModel season,Func<IReadOnlyList<FootballPhase>> layout){this.context=context;Season=season;this.layout=layout;Board=new(this,context,layout);FixtureDate=context.ReferenceDate.AddDays(7).ToDateTime(TimeOnly.MinValue);}
    public void Synchronize()
    {
        try
        {
            var phases=FootballExactCalendar.Phases(layout());var selected=FixturePhase?.Key;Phases=phases;
            FixturePhase=phases.FirstOrDefault(p=>p.Key==selected)??phases.FirstOrDefault();Notify(nameof(Phases));Notify(nameof(FixturePhase));Notify(nameof(Cards));
            BulkPhase=phases.FirstOrDefault(p=>p.Key==BulkPhase?.Key)??phases.FirstOrDefault();Notify(nameof(BulkPhase));
            if(signature.Length>0&&signature!=Shape(phases)){Note="The format changed. Update matchdays to keep compatible dates and add the new rounds. Fixtures linked to removed phases must be removed.";Notify(nameof(Note));}
        }
        catch(Exception ex){Error=ex.Message;Notify(nameof(Error));}
    }
    public void BuildMatchdays(bool reset=false)
    {
        try
        {
            Synchronize();var defaults=Season.Calendar();var old=reset?Array.Empty<CreationMatchday>():Matchdays.ToArray();var rows=new List<CreationMatchday>();int offset=0;
            foreach(var phase in Phases)for(int round=1;round<=phase.Rounds;round++,offset++)
            {
                var kept=old.Where(r=>r.PhaseKey==phase.Key&&r.Round==round).ToArray();
                if(kept.Length>0)foreach(var row in kept){row.PhaseIndex=phase.Index;row.PhaseName=phase.Name;row.Capacity=phase.Matches;row.Minimum=Math.Min(int.Parse(row.Minimum),phase.Matches).ToString();row.Maximum=Math.Min(int.Parse(row.Maximum),phase.Matches).ToString();rows.Add(row);}
                else rows.Add(new(){PhaseKey=phase.Key,PhaseIndex=phase.Index,PhaseName=phase.Name,Round=round,Capacity=phase.Matches,Date=defaults.FirstDate.AddDays(offset*defaults.IntervalDays).ToDateTime(TimeOnly.MinValue),Kickoff=FootballExactCalendar.Time(defaults.Kickoff)[..2]+":"+FootballExactCalendar.Time(defaults.Kickoff)[2..],Minimum="0",Maximum=phase.Matches.ToString()});
            }
            Matchdays.Clear();foreach(var row in rows)Matchdays.Add(row);signature=Shape(Phases);if(Fixtures.Count==0&&Matchdays.FirstOrDefault() is {} first){FixtureDate=first.Date;FixtureTime=first.Kickoff;Notify(nameof(FixtureDate));Notify(nameof(FixtureTime));}Error="";Note="Choose each date and kick-off time below. These rows are available matchday slots, not confirmed team fixtures.";Refresh();
        }
        catch(Exception ex){Error=ex.Message;Refresh();}
    }
    void Refresh(){Notify(nameof(Cards));Notify(nameof(Error));Notify(nameof(Note));}
    public void SplitSlot(CreationMatchday row)
    {
        try{var time=FootballExactCalendar.Time(row.Kickoff);string later=((int.Parse(time[..2])+1)%24).ToString("D2")+time[2..];row.Minimum="0";Matchdays.Add(new(){PhaseKey=row.PhaseKey,PhaseIndex=row.PhaseIndex,PhaseName=row.PhaseName,Round=row.Round,Date=row.Date,Kickoff=later,Minimum="0",Maximum=row.Capacity.ToString(),Capacity=row.Capacity});Refresh();}
        catch(Exception ex){Error=ex.Message;Refresh();}
    }
    public void RemoveSlot(CreationMatchday row){Matchdays.Remove(row);Refresh();}
    public void AddFixture()
    {
        try
        {
            Synchronize();if(FixturePhase is null||!FixtureDate.HasValue||Home is null||Away is null)throw new InvalidDataException("Choose a phase, a date and both clubs.");
            var next=new CreationFixture(FixturePhase.Key,FixturePhase.Index,FixturePhase.Name,DateOnly.FromDateTime(FixtureDate.Value),FootballExactCalendar.Time(FixtureTime),Home,Away);
            var all=Fixtures.Append(next).Select(f=>new FootballFixtureDraft(Phases.FirstOrDefault(p=>p.Key==f.PhaseKey)?.Index??throw new InvalidDataException("Remove fixtures belonging to a removed phase."),f.Date,f.Kickoff,f.Home.Id.ToString(),f.Away.Id.ToString())).ToArray();
            FootballExactCalendar.Validate(context,Phases,new FootballCalendarPlan(FootballCalendarMode.Generate,context.ReferenceDate,1,"1500") with{Mode=Manual?FootballCalendarMode.ManualMatchdays:FootballCalendarMode.ExactFixtures,Matchdays=Manual?Matchdays.Select(r=>r.Snapshot()).ToArray():[],Fixtures=all});Fixtures.Add(next);Error="";Note="Exact dates are stored with the fixtures. Schedule the remaining rounds with matchday slots if their teams are not yet known.";Refresh();
        }
        catch(Exception ex){Error=ex.Message;Refresh();}
    }
    public void RemoveFixture(){if(SelectedFixture is not null)Fixtures.Remove(SelectedFixture);}
    public FootballCalendarPlan Plan()
    {
        Synchronize();if(Automatic)return Season.Calendar() with{SimulationOptions=Board.Edited?Board.Options():null};Board.ValidateDraft();var defaults=new FootballCalendarPlan(FootballCalendarMode.Generate,context.ReferenceDate,1,"1500"){SimulationOptions=Board.Edited?Board.Options():null};
        if(Manual&&signature!=Shape(Phases))throw new InvalidDataException("Update the matchday calendar to match the current format before creating the competition.");
        var plan=defaults with{Mode=Manual?FootballCalendarMode.ManualMatchdays:FootballCalendarMode.ExactFixtures,Matchdays=Manual?Matchdays.Select(r=>r.Snapshot()).ToArray():[],Fixtures=CanAssign?Fixtures.Select(f=>{var phase=Phases.FirstOrDefault(p=>p.Key==f.PhaseKey)??throw new InvalidDataException("Remove fixtures belonging to a removed phase.");return new FootballFixtureDraft(phase.Index,f.Date,f.Kickoff,f.Home.Id.ToString(),f.Away.Id.ToString());}).ToArray():[]};
        FootballExactCalendar.Validate(context,Phases,plan);Error="";Notify(nameof(Error));return plan;
    }
    public string Summary()
    {
        var plan=Plan();return plan.Mode switch
        {
            FootballCalendarMode.ManualMatchdays=>"Custom matchday dates\n"+string.Join("\n",plan.Matchdays.Select(r=>$"• {Phases[r.PhaseIndex].Name} · Matchday {r.Round}: {r.Date:dd'/'MM'/'yyyy} at {r.Kickoff[..2]}:{r.Kickoff[2..]}"))+(Fixtures.Count>0?"\nAssigned fixtures\n"+string.Join("\n",Fixtures.Select(f=>$"• {f.PhaseName}: {f.Description}")):""),
            FootballCalendarMode.ExactFixtures=>"Exact fixtures\n"+string.Join("\n",Fixtures.Select(f=>$"• {f.PhaseName}: {f.Description}"))+"\nOnly the listed fixtures are scheduled. Unlisted rounds remain to be configured.",
            _=>$"{Season.CalendarMode}: first date {plan.FirstDate:dd'/'MM'/'yyyy}, interval {plan.IntervalDays} days, kick-off {plan.Kickoff}."
        };
    }
}
