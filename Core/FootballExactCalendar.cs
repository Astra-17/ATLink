namespace ATLink.Core;
public static class FootballExactCalendar
{
    public static string Time(string value)
    {
        if(value is null||!System.Text.RegularExpressions.Regex.IsMatch(value,@"^(?:[01]\d|2[0-3]):?[0-5]\d$"))throw new InvalidDataException(ATLink.Core.UiText.Get("Enter a valid kick-off time in HH:mm format."));
        return TournamentBuilder.Time(value);
    }
    public static IReadOnlyList<FootballCalendarPhase> Phases(IReadOnlyList<FootballPhase> phases)
    {
        var result=new List<FootballCalendarPhase>();
        foreach(var phase in phases)
        {
            if(phase.Kind==FootballPhaseKind.Knockout)
            {
                int teams=phase.Participants;for(int r=0;r<phase.Rounds;r++,teams/=2)result.Add(new(result.Count,phase.Rounds==1?phase.Name:teams==2?phase.Name+" · Final":phase.Name+" · Round of "+teams,teams==2?phase.FinalLegs:phase.Legs,teams/2){Key=phase.DraftKey.ToString("N")+"/"+r});
            }
            else{int size=phase.Participants/phase.Groups;result.Add(new(result.Count,phase.Name,(size%2==0?size-1:size)*phase.Legs,phase.Groups*(size/2)){Key=phase.DraftKey.ToString("N")});}
        }
        return result;
    }
    public static void Validate(CompetitionProjectContext c,IReadOnlyList<FootballCalendarPhase> phases,FootballCalendarPlan plan)
    {
        if(plan.Mode==FootballCalendarMode.ManualMatchdays)
        {
            if(plan.Matchdays.Count==0)throw new InvalidDataException(ATLink.Core.UiText.Get("Choose a date for every matchday."));
            foreach(var row in plan.Matchdays)
            {
                var phase=phases.SingleOrDefault(p=>p.Index==row.PhaseIndex)??throw new InvalidDataException(ATLink.Core.UiText.Get("The competition format changed. Update the calendar phases."));
                if(row.Round<1||row.Round>phase.Rounds)throw new InvalidDataException(ATLink.Core.UiText.Concat(phase.Name, ATLink.Core.UiText.Get(": choose an existing matchday.")));
                if(row.Date<c.ReferenceDate)throw new InvalidDataException(ATLink.Core.UiText.Concat(phase.Name, ATLink.Core.UiText.Get(": choose a date on or after the illustrative reference date.")));
                if(row.Minimum<0||row.Maximum<row.Minimum||row.Maximum>phase.Matches)throw new InvalidDataException(ATLink.Core.UiText.Concat(phase.Name, ATLink.Core.UiText.Get(": check the match capacity for this time slot.")));
                _=Time(row.Kickoff);
            }
            if(plan.Matchdays.GroupBy(r=>(r.PhaseIndex,r.Round,r.Date,Time:Time(r.Kickoff))).Any(g=>g.Count()>1))throw new InvalidDataException(ATLink.Core.UiText.Get("The same matchday time slot appears twice."));
            foreach(var phase in phases)for(int round=1;round<=phase.Rounds;round++)
            {
                var slots=plan.Matchdays.Where(r=>r.PhaseIndex==phase.Index&&r.Round==round).ToArray();
                if(slots.Length==0||slots.Sum(s=>s.Maximum)<phase.Matches||slots.Sum(s=>s.Minimum)>phase.Matches)throw new InvalidDataException(ATLink.Core.UiText.Format("{0} · Matchday {1}: provide enough available places for {2} matches.", phase.Name, round, phase.Matches));
            }
        }
        if(plan.Mode==FootballCalendarMode.ExactFixtures||plan.Fixtures.Count>0)
        {
            if(plan.Fixtures.Count==0)throw new InvalidDataException(ATLink.Core.UiText.Get("Add at least one exact fixture, or choose a matchday calendar."));
            foreach(var fixture in plan.Fixtures)
            {
                if(!phases.Any(p=>p.Index==fixture.PhaseIndex))throw new InvalidDataException(ATLink.Core.UiText.Get("A fixture refers to a phase that is no longer in this format."));
                if(fixture.Home==fixture.Away||!c.Teams.Any(t=>t.Id==fixture.Home)||!c.Teams.Any(t=>t.Id==fixture.Away))throw new InvalidDataException(ATLink.Core.UiText.Get("Choose two different installed clubs for each fixture."));
                _=Time(fixture.Kickoff);
                if(plan.Mode==FootballCalendarMode.ManualMatchdays&&!plan.Matchdays.Any(r=>r.PhaseIndex==fixture.PhaseIndex&&r.Date==fixture.Date&&Time(r.Kickoff)==Time(fixture.Kickoff)))throw new InvalidDataException(ATLink.Core.UiText.Get("Add a matchday time slot matching each assigned fixture's phase, date and kick-off."));
            }
            if(plan.Fixtures.SelectMany(f=>new[]{(f.Date,Team:f.Home),(f.Date,Team:f.Away)}).GroupBy(x=>x).Any(g=>g.Count()>1))throw new InvalidDataException(ATLink.Core.UiText.Get("A club cannot play two fixtures on the same day."));
            foreach(var phase in phases)if(plan.Fixtures.Count(f=>f.PhaseIndex==phase.Index)>phase.Rounds*phase.Matches)throw new InvalidDataException(ATLink.Core.UiText.Concat(phase.Name, ATLink.Core.UiText.Get(": too many fixtures for this format.")));
            if(plan.Mode==FootballCalendarMode.ManualMatchdays)foreach(var group in plan.Fixtures.GroupBy(f=>(f.PhaseIndex,f.Date,Time:Time(f.Kickoff))))if(group.Count()>plan.Matchdays.Where(r=>r.PhaseIndex==group.Key.PhaseIndex&&r.Date==group.Key.Date&&Time(r.Kickoff)==group.Key.Time).Sum(r=>r.Maximum))throw new InvalidDataException(ATLink.Core.UiText.Get("Assigned fixtures exceed the capacity of their matchday time slot."));
        }
    }
    public static void Apply(CompetitionProjectContext c,int competition,FootballCalendarPlan plan)
    {
        var model=FootballCompetitionAdapter.Read(c,competition);var phases=model.Phases.Select((p,i)=>new FootballCalendarPhase(i,p.Name,p.Kind==FootballPhaseKind.Knockout?p.Legs:((p.Participants/p.Groups)%2==0?p.Participants/p.Groups-1:p.Participants/p.Groups)*p.Legs,p.Kind==FootballPhaseKind.Knockout?p.Participants/2:p.Groups*(p.Participants/p.Groups/2))).ToArray();
        Validate(c,phases,plan);
        if(plan.Mode==FootballCalendarMode.ManualMatchdays)
            foreach(var row in plan.Matchdays)c.Edit("schedule.txt",t=>t.Data.Rows.Add(model.Phases[row.PhaseIndex].NativeId!.Value.ToString(),(row.Date.DayNumber-c.ReferenceDate.DayNumber).ToString(),row.Round.ToString(),row.Minimum.ToString(),row.Maximum.ToString(),Time(row.Kickoff)));
        if(plan.Mode==FootballCalendarMode.ExactFixtures||plan.Fixtures.Count>0)
            foreach(var fixture in plan.Fixtures)FootballBehaviour.AddAssignedFixture(c,competition,model.Phases[fixture.PhaseIndex].NativeId!.Value,fixture.Date,fixture.Kickoff,fixture.Home,fixture.Away);
    }
}
