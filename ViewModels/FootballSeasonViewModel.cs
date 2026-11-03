using System.IO;
using System.Collections.ObjectModel;
using ATLink.Core;
namespace ATLink.ViewModels;
public sealed record FootballChoice(int Id,string Name){public override string ToString()=>Name;}
public sealed record SeasonTaskRow(FootballSeasonTask Task,string Description){public bool IsEnd=>FootballSeason.Actions.Single(a=>a.Code==Task.Action).End;}
public sealed class FootballSeasonViewModel:FootballNotify
{
    public CompetitionProjectContext Context {get;}
    public int? CompetitionId {get;}
    public bool Existing=>CompetitionId.HasValue;
    public bool CanSuggest=>Context.Game=="FC27";
    readonly Action? changed;
    FootballSeasonSnapshot? original;
    FootballSeasonLinks originalLinks=new();
    FootballCalendarPlan? originalCalendar;
    string? reviewStamp;string? qualificationStamp;
    string ReviewState()=>System.Text.Json.JsonSerializer.Serialize(new{Rules=Tasks.Select(t=>new{t.Task.Action,Target=t.Task.Target.Value,t.Task.Parameters}),Initial=InitialTeams.Select(t=>t.Id),ReplaceInitial,Promotion=Promotion?.Id,Relegation=Relegation?.Id,Playoff=Playoff?.Id,Month=StartMonth?.Id,FirstDate,Interval,Kickoff,CalendarMode});
    string QualificationState()=>System.Text.Json.JsonSerializer.Serialize(new{Group=ResultsGroup?.Id,Kind=QualificationKind?.Key,QualifierCount,Destination=QualificationDestination?.Id,Pool=DestinationPool?.Id});
    public ObservableCollection<SeasonTaskRow> Tasks {get;}=[];
    public SeasonTaskRow? SelectedTask {get;set;}
    public ObservableCollection<FootballChoice> InitialTeams {get;}=[];
    public FootballChoice? SelectedInitial {get;set;}public FootballChoice? InitialClub {get;set;}
    public IReadOnlyList<FootballChoice> Clubs {get;}
    public IReadOnlyList<FootballChoice> Leagues {get;}
    public IReadOnlyList<FootballChoice> Competitions {get;}
    public IReadOnlyList<FootballChoice> Countries {get;}
    public IReadOnlyList<FootballChoice> LinkCompetitions {get;}
    public IReadOnlyList<FootballChoice> Months {get;}=[new(0,"Competition default"),..Enumerable.Range(1,12).Select(n=>new FootballChoice(n,System.Globalization.CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(n)))];
    public FootballChoice? Promotion {get;set;}public FootballChoice? Relegation {get;set;}public FootballChoice? Playoff {get;set;}public FootballChoice? StartMonth {get;set;}
    public bool AutomaticResults {get;set;}=true;
    public int Capacity {get;set;}=128;public int FinalCapacity {get;set;}=128;
    public bool ReplaceInitial {get;set;}
    public bool ConfirmChanges {get;set;}
    public string Error {get;private set;}="";public string Preview {get;private set;}="";
    public string ProfileNote=>Existing?$"Imported without changes. {original?.UnknownCount??0} special native rules remain preserved in Expert.":"Guided FC27 season profile: final standings are saved for next season. A single league table also updates its own league statistics. Special final classifications require explicit rules.";
    public IReadOnlyList<string> CalendarModes=>Existing?["Generate available slots","No generated calendar"]:["Generate available slots","Assign first-stage fixtures","No generated calendar"];
    public string CalendarMode {get;set;}="Generate available slots";
    public DateTime? FirstDate {get;set;}
    public IReadOnlyList<string> Intervals {get;}=Enumerable.Range(1,365).Select(n=>n.ToString()).ToArray();public string Interval {get;set;}="7";
    public string Kickoff {get;set;}="15:00";
    public string ReferenceLabel=>$"Illustrative reference: {Context.ReferenceDate:dd/MM/yyyy}. These are not confirmed game dates.";
    public IReadOnlyList<FootballChoice> PlayedPhases=>CompetitionId is int id?FootballCompetitionAdapter.Read(Context,id).Phases.Where(p=>!p.Special).Select(p=>new FootballChoice(p.NativeId!.Value,p.Name)).ToArray():[];
    public FootballChoice? FixturePhase {get;set;}public FootballChoice? HomeClub {get;set;}public FootballChoice? AwayClub {get;set;}
    public DateTime? FixtureDate {get;set;}public string FixtureKickoff {get;set;}="15:00";public string FixtureMessage {get;private set;}="";
    public void AddAssignedMatch()
    {
        try
        {
            if(HasPending)throw new InvalidDataException("Apply or cancel the pending season settings before adding an assigned match.");
            if(CompetitionId is not int id||FixturePhase is null||HomeClub is null||AwayClub is null||!FixtureDate.HasValue)throw new InvalidDataException("Choose a phase, date and two clubs.");
            Context.Execute("Add assigned match",()=>FootballBehaviour.AddAssignedFixture(Context,id,FixturePhase.Id,DateOnly.FromDateTime(FixtureDate.Value),FixtureKickoff,HomeClub.Id.ToString(),AwayClub.Id.ToString()));changed?.Invoke();Load();FixtureMessage="Assigned match added to the draft. Undo is available.";Notify(nameof(FixtureMessage));
        }
        catch(Exception ex){FixtureMessage=ex.Message;Notify(nameof(FixtureMessage));}
    }
    public IReadOnlyList<FootballQualificationKind> QualificationKinds=>FootballQualifications.Available(Context);
    public FootballQualificationKind? QualificationKind {get;set;}
    public IReadOnlyList<FootballChoice> ResultsGroups=>CompetitionId is int id?FootballCompetitionAdapter.Read(Context,id).Phases.LastOrDefault()?.NativeGroups.Select(g=>new FootballChoice(g,Context.ObjectPath(g))).ToArray()??[]:[];
    FootballChoice? resultsGroup;public FootballChoice? ResultsGroup{get=>resultsGroup;set{if(resultsGroup==value)return;resultsGroup=value;Notify();Notify(nameof(PlaceCounts));int limit=PlaceCounts.Count;if(!int.TryParse(QualifierCount,out int n)||n>limit)QualifierCount=limit.ToString();Notify(nameof(QualifierCount));}}
    public IReadOnlyList<string> PlaceCounts=>Enumerable.Range(1,Math.Max(1,ResultsGroup is {} g?Context.Rows("standings.txt").Count(r=>r[0]==g.Id.ToString()):1)).Select(n=>n.ToString()).ToArray();public string QualifierCount {get;set;}="1";
    FootballChoice? destination;public FootballChoice? QualificationDestination{get=>destination;set{if(destination==value)return;destination=value;Notify();Notify(nameof(DestinationPools));DestinationPool=DestinationPools.FirstOrDefault();Notify(nameof(DestinationPool));}}
    public IReadOnlyList<FootballChoice> DestinationPools=>QualificationDestination is {Id:>0} dest?FootballQualifications.EntryPools(Context,dest.Id).Select((g,i)=>new FootballChoice(g,"Incoming team pool "+(i+1))).ToArray():[];
    public FootballChoice? DestinationPool {get;set;}
    public string QualificationPreview {get;private set;}="";
    public void ReviewQualification()
    {
        QualificationPreview=$"The top {QualifierCount} teams in {ResultsGroup?.Name??"the final table"} qualify for {QualificationDestination?.Name??"the selected competition"}. The destination's entry source will stay linked to this competition's previous-season results. An initial ranking may be needed for its first season.";if(ResultsGroup is {} group&&QualificationKind is {} kind&&int.TryParse(QualifierCount,out int count))QualificationPreview+=FootballStandingLabels.QualificationChanges(Context,group.Id,kind.Key,count);qualificationStamp=QualificationState();ConfirmChanges=false;Notify(nameof(ConfirmChanges));Notify(nameof(QualificationPreview));
    }
    public void ConnectQualification()
    {
        try
        {
            if(CompetitionId is not int id||ResultsGroup is null||QualificationKind is null||QualificationDestination is not {Id:>0} dest||DestinationPool is null||!int.TryParse(QualifierCount,out int count))throw new InvalidDataException("Choose a final results group, a qualification type, a destination and a number of teams.");
            if(!ConfirmChanges||qualificationStamp!=QualificationState())throw new InvalidDataException("Review and confirm the current qualification changes first.");
            if(original is not null&&(Tasks.Count!=original.Tasks.Count||Tasks.Select(t=>t.Task).Zip(original.Tasks).Any(p=>p.First.Action!=p.Second.Action||p.First.Target!=p.Second.Target||!p.First.Parameters.SequenceEqual(p.Second.Parameters))))throw new InvalidDataException("Apply the pending season rules before connecting another competition.");
            Context.Execute("Connect competition qualifiers",()=>FootballQualifications.ConnectTopTeams(Context,id,ResultsGroup.Id,count,QualificationKind.Key,dest.Id,DestinationPool.Id));changed?.Invoke();Load();Preview="Qualification connected in the draft. Export and validate in game.";Notify(nameof(Preview));
        }
        catch(Exception ex){Error=ex.Message;Notify(nameof(Error));}
    }
    public FootballSeasonViewModel(CompetitionProjectContext c,int? competition=null,Action? changed=null)
    {
        Context=c;CompetitionId=competition;this.changed=changed;
        Clubs=c.Teams.Select(t=>new FootballChoice(int.Parse(t.Id),t.Name)).ToArray();
        Leagues=c.Base is null?[]:new FootballCatalog(c.Base).Entities("leagues").Select(l=>new FootballChoice(int.Parse(l.Id),c.LeagueName(l.Id))).ToArray();
        Competitions=[new(0,"None"),..c.Objects.Where(o=>o.Kind==3).Select(o=>new FootballChoice(o.Id,o.DisplayName))];
        LinkCompetitions=[new(0,"None"),..c.Objects.Where(o=>o.Kind==3&&int.TryParse(o.Code.TrimStart('C','c'),out _)).Select(o=>new FootballChoice(int.Parse(o.Code[1..]),o.DisplayName))];
        if(competition is int existingId)
        {
            var importedLinks=FootballBehaviour.ReadLinks(c,existingId);
            foreach(int n in new[]{importedLinks.Promotion?.Value??0,importedLinks.Relegation?.Value??0}.Where(n=>n>0&&!LinkCompetitions.Any(x=>x.Id==n)))LinkCompetitions=[..LinkCompetitions,new FootballChoice(n,"Existing destination (Expert)")];
            if(importedLinks.Playoff is {} reference&&!Competitions.Any(x=>x.Id==reference.Value))Competitions=[..Competitions,new FootballChoice(reference.Value,"Existing playoff (Expert)")];
        }
        Countries=c.Base?.Tables.FirstOrDefault(t=>t.Name=="nations")?.Data.Rows.Cast<System.Data.DataRow>().Select(r=>int.Parse(FootballCatalog.Value(r,"nationid"))).Where(n=>n>0).Select(n=>new FootballChoice(n,c.Project.Labels?.Name(new CompetitionObject(n,2,"","NationName_"+n,0))??"Country "+n)).OrderBy(n=>n.Name).ToArray()??[];
        FirstDate=c.ReferenceDate.AddDays(7).ToDateTime(TimeOnly.MinValue);Promotion=Relegation=LinkCompetitions[0];Playoff=Competitions[0];StartMonth=Months[0];
        if(competition.HasValue){Load();FixturePhase=PlayedPhases.FirstOrDefault();FixtureDate=FirstDate;ResultsGroup=ResultsGroups.FirstOrDefault();QualificationKind=QualificationKinds.FirstOrDefault();}
    }
    public void SetCalendar(FootballCalendarPlan plan){FirstDate=plan.FirstDate.ToDateTime(TimeOnly.MinValue);Interval=plan.IntervalDays.ToString();Kickoff=plan.Kickoff.Length==4?plan.Kickoff[..2]+":"+plan.Kickoff[2..]:plan.Kickoff;Notify(nameof(FirstDate));Notify(nameof(Interval));Notify(nameof(Kickoff));}
    public void Load()
    {
        if(CompetitionId is not int id)return;original=FootballSeason.Read(Context,id);var sporting=FootballCompetitionAdapter.Read(Context,id);Capacity=sporting.ParticipantCount;FinalCapacity=sporting.Phases.LastOrDefault() is {} final?final.Kind==FootballPhaseKind.Knockout?2:final.Participants/Math.Max(1,final.Groups):128;Tasks.Clear();foreach(var task in original.Tasks)Tasks.Add(new(task,FootballSeason.Describe(Context,task)));AutomaticResults=false;
        originalLinks=FootballBehaviour.ReadLinks(Context,id);Promotion=LinkCompetitions.FirstOrDefault(x=>x.Id==originalLinks.Promotion?.Value)??LinkCompetitions[0];Relegation=LinkCompetitions.FirstOrDefault(x=>x.Id==originalLinks.Relegation?.Value)??LinkCompetitions[0];Playoff=Competitions.FirstOrDefault(x=>x.Id==originalLinks.Playoff?.Value)??Competitions[0];StartMonth=Months.FirstOrDefault(x=>x.Id==originalLinks.StartMonth)??Months[0];
        InitialTeams.Clear();foreach(var row in Context.Rows("initteams.txt").Where(r=>r[0]==id.ToString()).OrderBy(r=>int.Parse(r[1])))InitialTeams.Add(new(int.Parse(row[2]),Context.TeamName(row[2])));
        Error="";ReplaceInitial=false;ConfirmChanges=false;SetCalendar(FootballBehaviour.ExistingCalendar(Context,id));originalCalendar=Calendar();NotifyAll();
    }
    void NotifyAll(){foreach(string key in new[]{nameof(Promotion),nameof(Relegation),nameof(Playoff),nameof(StartMonth),nameof(AutomaticResults),nameof(ReplaceInitial),nameof(ConfirmChanges),nameof(ProfileNote),nameof(Preview),nameof(Error)})Notify(key);}
    public FootballSeasonLinks Links()=>new(Promotion is {Id:>0}?new DatabaseCompetitionId(Promotion.Id):null,Relegation is {Id:>0}?new DatabaseCompetitionId(Relegation.Id):null,Playoff is {Id:>0}?new CompdataObjectId(Playoff.Id):null,StartMonth is {Id:>0}?StartMonth.Id:null);
    public FootballCalendarPlan Calendar()=>new(CalendarMode switch{"No generated calendar"=>FootballCalendarMode.None,"Assign first-stage fixtures"=>FootballCalendarMode.AssignedFixtures,_=>FootballCalendarMode.Generate},FirstDate.HasValue?DateOnly.FromDateTime(FirstDate.Value):throw new InvalidDataException("Choose the first illustrative match date."),int.TryParse(Interval,out int days)?days:throw new InvalidDataException("Choose the days between matchdays."),TournamentBuilder.Time(Kickoff));
    public bool CalendarChanged{get{try{return originalCalendar is not null&&Calendar()!=originalCalendar;}catch{return true;}}}
    public bool HasPending=>Existing&&(ReplaceInitial||Links()!=originalLinks||CalendarChanged||original is not null&&(Tasks.Count!=original.Tasks.Count||Tasks.Select(t=>t.Task).Zip(original.Tasks).Any(p=>p.First.Action!=p.Second.Action||p.First.Target!=p.Second.Target||!p.First.Parameters.SequenceEqual(p.Second.Parameters))));
    public FootballBehaviourPlan Plan(FootballCalendarPlan? calendar=null)=>new(Tasks.Select(t=>t.Task).ToArray(),ReplaceInitial?InitialTeams.Select((t,i)=>new FootballStandingSeed(t.Id.ToString(),i)).ToArray():[],AutomaticResults,Links(),calendar??Calendar());
    public void SetTask(FootballSeasonTask task,SeasonTaskRow? replace=null)
    {
        if(CompetitionId is int id)FootballSeason.Validate(Context,id,task);
        if(replace is null)Tasks.Add(new(task,Describe(task)));else Tasks[Tasks.IndexOf(replace)]=new(task,Describe(task));RefreshPreview();
    }
    string Describe(FootballSeasonTask task)=>FootballSeason.Describe(Context,task).Replace("Unknown stage","the final results group");
    public void RemoveTask(){if(SelectedTask is {} row)Tasks.Remove(row);RefreshPreview();}
    public void MoveTask(int direction){if(SelectedTask is not {} row)return;int i=Tasks.IndexOf(row),next=i+direction;if(next>=0&&next<Tasks.Count)Tasks.Move(i,next);RefreshPreview();}
    public void AddInitial(){if(InitialClub is not null&&!InitialTeams.Any(t=>t.Id==InitialClub.Id))InitialTeams.Add(InitialClub);ReplaceInitial=true;Notify(nameof(ReplaceInitial));RefreshPreview();}
    public void RemoveInitial(){if(SelectedInitial is not null)InitialTeams.Remove(SelectedInitial);ReplaceInitial=true;Notify(nameof(ReplaceInitial));RefreshPreview();}
    public void MoveInitial(int direction){if(SelectedInitial is not {} row)return;int i=InitialTeams.IndexOf(row),next=i+direction;if(next>=0&&next<InitialTeams.Count)InitialTeams.Move(i,next);ReplaceInitial=true;Notify(nameof(ReplaceInitial));RefreshPreview();}
    public void UseSelectedClubs(IEnumerable<CompetitionTeamVm> clubs){InitialTeams.Clear();foreach(var club in clubs)InitialTeams.Add(new(int.Parse(club.Id),club.Name));ReplaceInitial=true;Notify(nameof(ReplaceInitial));RefreshPreview();}
    public void Propose()
    {
        try{if(CompetitionId is not int id)return;var proposal=FootballSeason.ProposeResults(Context,id);bool hasClassification=Tasks.Any(t=>t.Task.Action=="UpdateTable");foreach(var task in proposal)if(!(task.Action=="UpdateTable"&&hasClassification)&&!Tasks.Any(t=>t.Task.Action==task.Action&&t.Task.Target==task.Target&&t.Task.Parameters.SequenceEqual(task.Parameters)))Tasks.Add(new(task,Describe(task)));RefreshPreview();}
        catch(Exception ex){Error=ex.Message;Notify(nameof(Error));}
    }
    public void RefreshPreview()
    {
        Preview=string.Join("\n",Tasks.Select((t,i)=>$"{i+1}. {t.Description}"));if(!Existing&&AutomaticResults)Preview+="\nAutomatic profile: save final results for next season; for a single league, reset/update that league's statistics.";
        Preview+=$"\nInitial standings: {(ReplaceInitial?InitialTeams.Count+" explicitly ordered teams":"preserve existing / none for a new competition")}.\n";
        if(Promotion is {Id:>0})Preview+=$"Promotion destination: {Promotion.Name}.\n";if(Relegation is {Id:>0})Preview+=$"Relegation destination: {Relegation.Name}.\n";if(Playoff is {Id:>0})Preview+=$"Playoff: {Playoff.Name}.\n";
        if(CalendarChanged)Preview+=$"Calendar replacement: {FirstDate:dd/MM/yyyy}, {Interval} days between rounds, {Kickoff}. {ReferenceLabel}";
        reviewStamp=ReviewState();ConfirmChanges=false;Notify(nameof(ConfirmChanges));Error="";Notify(nameof(Preview));Notify(nameof(Error));
    }
    public void Apply()
    {
        try
        {
            if(CompetitionId is not int id||original is null)return;if(!ConfirmChanges||reviewStamp!=ReviewState())throw new InvalidDataException("Review and confirm the current season behaviour before applying.");
            var tasks=Tasks.Select(t=>t.Task).ToArray();var links=Links();
            Context.Execute("Change season behaviour",()=>{FootballSeason.ApplyTasks(Context,id,original,tasks);if(ReplaceInitial)FootballSeason.ApplyInitialStandings(Context,id,InitialTeams.Select((t,i)=>new FootballStandingSeed(t.Id.ToString(),i)).ToArray());if(links!=originalLinks)FootballSeason.ApplyLinks(Context,id,links,originalLinks);if(CalendarChanged)FootballBehaviour.RegenerateCalendar(Context,id,Calendar(),true);});
            changed?.Invoke();Load();Preview="Season behaviour applied in the project draft. Export to write files.";Notify(nameof(Preview));
        }
        catch(Exception ex){Error=ex.Message;Notify(nameof(Error));}
    }
    public void Regenerate()
    {
        try{if(CompetitionId is not int id)return;if(!ConfirmChanges||reviewStamp!=ReviewState())throw new InvalidDataException("Review and confirm the current calendar changes first.");Context.Execute("Generate competition calendar",()=>FootballBehaviour.RegenerateCalendar(Context,id,Calendar(),ConfirmChanges));changed?.Invoke();Load();Preview="Relative slots regenerated. Reference dates remain illustrative.";Notify(nameof(Preview));}
        catch(Exception ex){Error=ex.Message;Notify(nameof(Error));}
    }
}
