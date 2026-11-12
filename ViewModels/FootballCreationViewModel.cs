using System.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using ATLink.Core;
namespace ATLink.ViewModels;
public sealed record FootballTypeChoice(FootballCompetitionKind Kind,string Name,string Description);
public sealed class FootballCreationViewModel:FootballNotify
{
    public CompetitionProjectContext Context {get;}
    readonly Action<int> created;
    readonly IReadOnlyList<FootballProfile> profiles;
    public IReadOnlyList<FootballTypeChoice> Types {get;}=[new(FootballCompetitionKind.League,"League","A table, points and a champion."),new(FootballCompetitionKind.Cup,"Cup","Knockout rounds leading to a final."),new(FootballCompetitionKind.GroupsAndKnockout,"Groups + knockout","Group qualifiers advance to knockout rounds."),new(FootballCompetitionKind.Custom,"Custom format","Combine league, group and knockout phases.")];
    FootballTypeChoice? type;public FootballTypeChoice? Type{get=>type;set{if(type==value)return;type=value;Notify();Notify(nameof(Regions));if(!Regions.Any(r=>r.Id==Region?.Id))region=Regions.FirstOrDefault();Notify(nameof(Region));Configure(true);}}
    FootballRegion? region;public FootballRegion? Region{get=>region;set{region=value;Notify();Configure(false);}}
    public IReadOnlyList<FootballRegion> Regions=>profiles.Where(p=>p.Kind==Type?.Kind).Select(p=>p.Region).Distinct().Select(id=>new FootballRegion(id,Context.ObjectName(id))).OrderBy(r=>r.Name).ToArray();
    string name="New competition";public string Name{get=>name;set{name=value;Notify();}}
    public FootballFormatViewModel Format {get;}
    public FootballSeasonViewModel Season {get;}
    public CreationCalendarViewModel Calendar {get;}
    public FootballMatchupsViewModel Matchups {get;}
    public bool HasMatchups=>Format.Phases.Any(p=>p.Knockout)||Matchups.NeedsReview;
    public FootballRulesViewModel GeneralRules {get;private set;}
    public string Error {get;private set;}="";public string Review {get;private set;}="";
    readonly CompetitionTeamVm[] availableTeams;
    public ICollectionView Teams {get;}
    readonly HashSet<string> selectedAvailableIds=[];
    public IReadOnlyList<CompetitionTeamVm> SelectedAvailableTeams=>availableTeams.Where(t=>selectedAvailableIds.Contains(t.Id)).ToArray();
    public bool FilteringTeams {get;private set;}
    public event Action? AvailableSelectionChanged;
    string search="";public string Search{get=>search;set{if(search==value)return;search=value;Notify();FilteringTeams=true;try{Teams.Refresh();}finally{FilteringTeams=false;}AvailableSelectionChanged?.Invoke();}}
    public void SelectAvailable(IEnumerable<CompetitionTeamVm> added,IEnumerable<CompetitionTeamVm> removed)
    {
        if(FilteringTeams)return;foreach(var team in removed)selectedAvailableIds.Remove(team.Id);foreach(var team in added)selectedAvailableIds.Add(team.Id);
    }
    public ObservableCollection<CompetitionTeamVm> Participants {get;}=[];
    public CompetitionTeamVm? SelectedAvailable {get;set;}public CompetitionTeamVm? SelectedParticipant {get;set;}
    public string ParticipantSummary=>$"{Participants.Count} teams chosen. Leave this list empty to configure qualifications later.";
    int EntryCapacity(int total)=>Participants.Count==total?total:total-(Links?.ReservedQualifierPlaces??0);
    public void RefreshEntries()=>RefreshParticipantCounter();
    public string ParticipantCounter=>$"{Participants.Count}/{(int.TryParse(Format.Phases.FirstOrDefault()?.Participants,out int total)?EntryCapacity(total):0)}";
    FootballPhaseViewModel? countedPhase;
    void RefreshParticipantCounter()
    {
        var first=Format.Phases.FirstOrDefault();
        if(!ReferenceEquals(first,countedPhase))
        {
            if(countedPhase is not null)countedPhase.PropertyChanged-=ParticipantCapacityChanged;
            countedPhase=first;if(countedPhase is not null)countedPhase.PropertyChanged+=ParticipantCapacityChanged;
        }
        Notify(nameof(ParticipantCounter));Notify(nameof(ParticipantSummary));
    }
    void ParticipantCapacityChanged(object? sender,System.ComponentModel.PropertyChangedEventArgs e)
    {
        if(e.PropertyName==nameof(FootballPhaseViewModel.Participants))Notify(nameof(ParticipantCounter));
    }
    public bool Completed {get;private set;}
    public Guid DraftKey {get;}=Guid.NewGuid();
    public FootballCreationViewModel? ParentDraft {get;}
    public FootballCreationLinksViewModel Links {get;}
    public FootballCompetitionNamesViewModel Names {get;}=new();
    public bool GroupStageObjective {get;set;}=true;
    public bool RegionalRegistration {get;set;}
    readonly Action<FootballCreationViewModel>? draftAccepted;
    public int CreatedCompetitionId {get;private set;}
    public FootballCreationViewModel(CompetitionProjectContext c,int? preferredRegion,Action<int> created,
        FootballCreationViewModel? parentDraft=null,Action<FootballCreationViewModel>? draftAccepted=null)
    {
        ParentDraft=parentDraft;this.draftAccepted=draftAccepted;
        Context=c;this.created=created;availableTeams=c.Teams.Select(t=>new CompetitionTeamVm(t)).ToArray();Teams=new ListCollectionView(availableTeams){Filter=item=>item is CompetitionTeamVm team&&team.Name.Contains(Search,StringComparison.CurrentCultureIgnoreCase)};profiles=FootballCompetitionAdapter.Profiles(c);Format=new(c,false);Format.Phases.CollectionChanged+=(_,_)=>RefreshParticipantCounter();Participants.CollectionChanged+=(_,_)=>RefreshParticipantCounter();Season=new(c);Matchups=new(c,()=>Format.Snapshot(),()=>Participants.Select(t=>t.Id).ToArray());Matchups.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(FootballMatchupsViewModel.NeedsReview))Notify(nameof(HasMatchups));};Calendar=new(c,Season,()=>Matchups.Layout());Format.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(FootballFormatViewModel.PreviewText))Notify(nameof(HasMatchups));};GeneralRules=new(null,new Dictionary<string,string[]>());Type=Types[0];
        Calendar.Board.Participants=SimulationParticipants;
        Calendar.Board.TieEditors=()=>Format.Phases.Where(p=>p.Knockout).SelectMany(p=>p.EssentialRounds.SelectMany((r,i)=>r.TieVenues.Select(t=>new CalendarBoardViewModel.TieEditor(p.DraftKey.ToString("N")+"/"+i,t)))).ToArray();
        Calendar.Board.FixtureAssignmentAllowed=()=>Season.Tasks.All(t=>!t.Task.Action.StartsWith("Fill")||t.Task.Action=="FillWithTeam");
        if(Regions.FirstOrDefault(r=>r.Id==preferredRegion) is {} preferred)Region=preferred;
        Links=new(this);
    }
    IReadOnlyList<string> SimulationParticipants()
    {
        var ids=Participants.Select(t=>t.Id).ToList();
        foreach(var row in Season.Tasks)
        {
            var task=row.Task;
            if(task.Action=="FillWithTeam")ids.Add(task.Parameters[1].ToString());
            if(task.Action is "FillFromLeague" or "FillFromLeagueInOrder"&&Context.Base is not null)ids.AddRange(new FootballCatalog(Context.Base).Rows("leagueteamlinks").Where(r=>FootballCatalog.Value(r,"leagueid")==task.Parameters[0].ToString()).Select(r=>FootballCatalog.Value(r,"teamid")));
            if(task.Action.StartsWith("FillFromCompTable"))ids.AddRange(Context.Rows("initteams.txt").Where(r=>r[0]==task.Parameters[0].ToString()&&r[2]!="-1").OrderBy(r=>int.Parse(r[1])).Take(task.Parameters[^1]).Select(r=>r[2]));
        }
        return ids.Distinct().ToArray();
    }
    FootballProfile? Profile=>Type is null||Region is null?null:FootballCompetitionAdapter.SelectProfile(Context,profiles,Type.Kind,Region.Id);
    void Configure(bool reset)
    {
        if(Profile is not {} profile){Error="This format is not yet supported for this region in the selected game.";Notify(nameof(Error));return;}
        Season.SetCalendar(FootballBehaviour.DefaultCalendar(Context,profile.TemplateId));
        if(reset){Season.Tasks.Clear();Season.InitialTeams.Clear();Season.ReplaceInitial=false;Season.AutomaticResults=true;}
        Format.TemplateId=profile.TemplateId;var defaults=FootballRulesViewModel.Effective(Context,profile.TemplateId);GeneralRules=new(null,defaults);Notify(nameof(GeneralRules));
        if(!reset&&Format.Phases.Count>0){try{Format.SetPhases(Format.Snapshot(),defaults);Error="";Notify(nameof(Error));return;}catch{}}
        var league=new FootballPhase{Name="League",Kind=FootballPhaseKind.League,Participants=20,Groups=1,Legs=2};
        var cup=new FootballPhase{Name="Knockout rounds",Kind=FootballPhaseKind.Knockout,Participants=16,Rounds=4,Legs=1,FinalLegs=1};
        var groups=new FootballPhase{Name="Group stage",Kind=FootballPhaseKind.Groups,Participants=32,Groups=8,QualifiersPerGroup=2,Legs=2};
        Format.SetPhases(Type!.Kind switch{FootballCompetitionKind.League=>[league],FootballCompetitionKind.Cup=>[cup],FootballCompetitionKind.GroupsAndKnockout=>[groups,cup],_=>[groups with {Participants=8,Groups=2,QualifiersPerGroup=2},cup with{Participants=4,Rounds=2}]},defaults);
        Participants.Clear();selectedAvailableIds.Clear();SelectedAvailable=null;AvailableSelectionChanged?.Invoke();Error="";Notify(nameof(Error));Notify(nameof(ParticipantSummary));
    }
    public void AddTeam()
    {
        var chosen=SelectedAvailableTeams;if(chosen.Count==0&&SelectedAvailable is not null)chosen=[SelectedAvailable];
        var additions=chosen.Where(t=>!Participants.Any(p=>p.Id==t.Id)).ToArray();int capacity=int.TryParse(Format.Phases.FirstOrDefault()?.Participants,out int n)?n:0;int remaining=Math.Max(0,capacity-Participants.Count);
        if(additions.Length>remaining){Error=$"Only {remaining} team place(s) remain. Select fewer teams or increase the competition size.";Notify(nameof(Error));return;}
        foreach(var team in additions)Participants.Add(team);selectedAvailableIds.Clear();SelectedAvailable=null;AvailableSelectionChanged?.Invoke();Error="";Notify(nameof(Error));Notify(nameof(ParticipantSummary));
    }
    public void RemoveTeam(){if(SelectedParticipant is not null)Participants.Remove(SelectedParticipant);Notify(nameof(ParticipantSummary));}
    public bool ValidateStep(int step)
    {
        try
        {
            if(Profile is null)throw new InvalidDataException("Choose a supported competition type and region.");
            if(step>=1){_ =Format.Artwork.Snapshot();if(string.IsNullOrWhiteSpace(Name)||Name.IndexOfAny([',','\n','\r'])>=0)throw new InvalidDataException("Enter a competition name without commas or line breaks.");var layout=Format.Snapshot();Season.Capacity=layout[0].Participants;Season.FinalCapacity=layout[^1].Kind==FootballPhaseKind.Knockout?2:layout[^1].Participants/layout[^1].Groups;FootballCompetitionCompiler.Validate(layout);if(Type?.Kind is FootballCompetitionKind.Cup or FootballCompetitionKind.GroupsAndKnockout&&layout[^1].Output!=1)throw new InvalidDataException("Add enough knockout rounds to reach the final and one winner.");}
            if(step>=2){Matchups.Validate();var phases=Matchups.Layout();GeneralRules.ConfigureEndings(phases.Any(p=>p.Kind==FootballPhaseKind.Knockout&&(p.Legs==1||p.FinalLegs==1)),phases.Any(p=>p.Kind==FootballPhaseKind.Knockout&&(p.Legs==2||p.FinalLegs==2)));FootballBehaviour.ValidateEntries(Context,EntryCapacity(phases[0].Participants),Season.Tasks.Select(t=>t.Task).ToArray(),Participants.Select(t=>t.Id).ToArray());if(Season.AutomaticResults&&(phases[^1].Kind==FootballPhaseKind.Knockout?phases[^1].Output!=1:phases[^1].Groups!=1))throw new InvalidDataException("Automatic season results need a final or one final table. Add a final phase, or disable automatic results and configure the classification explicitly.");_ =Season.Plan(Calendar.Plan());if(Calendar.Automatic&&Season.CalendarMode=="Assign first-stage fixtures"&&(Participants.Count!=phases[0].Participants||phases[0].Kind==FootballPhaseKind.Knockout))throw new InvalidDataException("Assigned fixtures need a league/group first phase and every club selected explicitly.");_ =GeneralRules.Overrides();}
            Error="";Notify(nameof(Error));return true;
        }
        catch(Exception ex){Error=ex.Message;Notify(nameof(Error));return false;}
    }
    public bool ValidateJourneyStep(int step)
    {
        if(step>=3)return ValidateStep(2);
        if(!ValidateStep(1))return false;
        try
        {
            var phases=Format.Snapshot();
            if(step>=1){FootballBehaviour.ValidateEntries(Context,EntryCapacity(phases[0].Participants),Season.Tasks.Select(t=>t.Task).ToArray(),Participants.Select(t=>t.Id).ToArray());Matchups.Validate();}
            if(step>=2)
            {
                _ =Calendar.Plan();
                if(Calendar.Automatic&&Season.CalendarMode=="Assign first-stage fixtures"&&(Participants.Count!=phases[0].Participants||phases[0].Kind==FootballPhaseKind.Knockout))throw new InvalidDataException("Assigned fixtures need a league/group first phase and every club selected explicitly.");
            }
            Error="";Notify(nameof(Error));return true;
        }
        catch(Exception ex){Error=ex.Message;Notify(nameof(Error));return false;}
    }
    public void RefreshPhaseDefaults()
    {
        if(Profile is not {} profile)return;
        try{var phases=Format.Snapshot();GeneralRules.ConfigureEndings(phases.Any(p=>p.Kind==FootballPhaseKind.Knockout&&(p.Legs==1||p.FinalLegs==1)),phases.Any(p=>p.Kind==FootballPhaseKind.Knockout&&(p.Legs==2||p.FinalLegs==2)));var defaults=FootballRulesViewModel.Effective(Context,profile.TemplateId).ToDictionary(p=>p.Key,p=>p.Value);foreach(var entry in GeneralRules.Overrides())defaults[entry.Key]=entry.Value;Format.SetPhases(phases,defaults);}
        catch(Exception ex){Error=ex.Message;Notify(nameof(Error));}
    }
    public void UpdateReview()
    {
        try
        {
            var visited=new HashSet<Guid>();
            foreach(var draft in Links.Drafts())draft.Links.SynchronizeCalendar(visited,[]);
            foreach(var draft in Links.Drafts())draft.Links.Review();
        }
        catch(Exception ex){Error=ex.Message;Notify(nameof(Error));return;}
        if(!ValidateStep(2))return;
        Season.RefreshPreview();var phases=Format.Snapshot();Review=$"{Name}\n{Region?.Name} · {Type?.Name}\n\n"+string.Join(" → ",phases.Select(p=>$"{p.Name} ({p.Participants} teams)"))+"\n\n"+string.Join("\n",phases.Select(p=>$"• {p.Name}: {p.Summary}; "+(p.Output>0?$"{p.Output} advance":"final standings")))+$"\n\n{Participants.Count} selected teams. "+(Participants.Count==0?"Participants can be configured later.":"Existing domestic league memberships stay unchanged.")+"\n\nInternal preparation and native connections are generated automatically. Create saves a project draft; Export writes a new DB/LOC/competition project. Game installation and testing remain separate.";Review+="\n\nSeason behaviour\n"+Season.Preview+"\n"+Season.ReferenceLabel;Review+="\n\n"+Calendar.Summary();Review+="\n\nInternational calendar\n"+Calendar.International.Summary;Review+="\n\nPhase essentials\n"+string.Join("\n",Format.Phases.Select(p=>p.Name+"\n"+p.EssentialsSummary));Review+="\n\nMatchups\n"+Matchups.Summary;Review+="\n\nLinked competitions\n"+string.Join("\n\n",Links.Drafts().Select(d=>d.Name+"\n"+d.Links.Summary));Review+="\n\nArtwork\n"+string.Join("\n",Links.Drafts().Select(d=>d.Name+": "+d.Format.Artwork.Summary));Review+="\n\nNames\n"+string.Join("\n",Links.Drafts().SelectMany(d=>d.Names.Snapshot(Context,d.Name)).Select(n=>n.Language+": "+n.FullName+" / "+n.ShortName));Notify(nameof(Review));
    }
    public FootballCompetitionDraft SnapshotDraft()=>new(DraftKey,Type?.Kind??throw new InvalidDataException("Choose a competition type."),
        Region?.Id??throw new InvalidDataException("Choose a region."),Name,Matchups.Layout(),Participants.Select(t=>t.Id).ToArray(),
        GeneralRules.Overrides(),Season.Plan(Calendar.Plan())){Names=Names.Snapshot(Context,Name),GroupStageObjective=GroupStageObjective,RegionalRegistration=RegionalRegistration,Artwork=Format.Artwork.Snapshot()};
    public FootballCreationGraph GraphPlan()
    {
        var drafts=Links.Drafts().ToArray();var visited=new HashSet<Guid>();
        foreach(var draft in drafts)draft.Links.SynchronizeCalendar(visited,[]);
        foreach(var draft in drafts)
        {
            draft.Links.RefreshChoices();draft.Links.Validate();
            if(!draft.ValidateStep(2))throw new InvalidDataException(draft.Name+": "+draft.Error);
        }
        return new(drafts.Select(d=>d.SnapshotDraft()).ToArray(),drafts.Select(d=>d.Links.NeighbourPlan()).OfType<FootballDraftNeighbours>().ToArray(),
            drafts.Select(d=>d.Links.PlayoffPlan()).OfType<FootballDraftPlayoffs>().ToArray(),
            drafts.Select(d=>d.Links.CalendarPlan()).OfType<FootballDraftCalendar>().ToArray())
            {InternationalMoves=drafts.SelectMany(d=>d.Calendar.International.Moves).ToArray()};
    }
    public void Create()
    {
        if(Completed||!ValidateStep(2)||Type is null||Region is null)return;
        try
        {
            if(ParentDraft is not null)
            {
                Links.Validate();draftAccepted?.Invoke(this);Completed=true;return;
            }
            var ids=FootballCompetitionGraph.Create(Context,GraphPlan());
            CreatedCompetitionId=ids[DraftKey];Completed=true;created(CreatedCompetitionId);
        }
        catch(Exception ex){Error=ex.Message;Notify(nameof(Error));}
    }
}
