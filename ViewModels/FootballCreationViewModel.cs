using System.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using ATLink.Core;
namespace ATLink.ViewModels;
public enum FootballCreationStep { Format, Identity, Path, Links, Teams, Schedule, Rewards, Rules, Advancement, Review }
public sealed class FootballTypeChoice(FootballCompetitionKind kind,string name,string description):FootballNotify
{
    public FootballCompetitionKind Kind=>kind;
    public string Name=>name;
    public string Description=>description;
    bool selected;public bool Selected{get=>selected;set{selected=value;Notify();}}
    public string Icon=>Kind switch
    {
        FootballCompetitionKind.League=>"M2,2 L30,2 30,30 2,30 Z M2,10 L30,10 M2,20 L30,20 M11,2 L11,30 M21,2 L21,30",
        FootballCompetitionKind.Cup=>"M8,2 L24,2 24,12 Q24,22 16,22 Q8,22 8,12 Z M8,5 L2,5 2,10 Q2,16 9,16 M24,5 L30,5 30,10 Q30,16 23,16 M16,22 L16,29 M9,30 L23,30",
        FootballCompetitionKind.GroupsAndKnockout=>"M1,2 L11,2 11,10 1,10 Z M1,22 L11,22 11,30 1,30 Z M11,6 L17,6 17,26 11,26 M17,16 L23,16 M23,12 L31,12 31,20 23,20 Z",
        _=>"M2,8 L30,8 M2,24 L30,24 M10,2 L10,14 M23,18 L23,30"
    };
}
public sealed class FootballCreationViewModel:FootballNotify
{
    public CompetitionProjectContext Context {get;}
    readonly Action<int> created;
    readonly IReadOnlyList<FootballProfile> profiles;
    public IReadOnlyList<FootballTypeChoice> Types {get;}=[new(FootballCompetitionKind.League,ATLink.Core.UiText.Get("Ligue"),ATLink.Core.UiText.Get("A table, points and a champion.")),new(FootballCompetitionKind.Cup,ATLink.Core.UiText.Get("Coupe"),ATLink.Core.UiText.Get("Knockout rounds leading to a final.")),new(FootballCompetitionKind.GroupsAndKnockout,ATLink.Core.UiText.Get("Groupe + \u00e9limination"),ATLink.Core.UiText.Get("Group qualifiers advance to knockout rounds.")),new(FootballCompetitionKind.Custom,ATLink.Core.UiText.Get("Custom"),ATLink.Core.UiText.Get("Combine league, group and knockout phases."))];
    FootballTypeChoice? type;public FootballTypeChoice? Type{get=>type;set{if(type==value)return;type=value;foreach(var choice in Types)choice.Selected=choice==value;Notify();Notify(nameof(IsCustom));Notify(nameof(Regions));if(!Regions.Any(r=>r.Id==Region?.Id))region=Regions.FirstOrDefault();Notify(nameof(Region));Configure(true);}}
    public bool IsCustom=>Type?.Kind==FootballCompetitionKind.Custom;
    FootballRegion? region;public FootballRegion? Region{get=>region;set{region=value;Notify();Configure(false);}}
    public IReadOnlyList<FootballRegion> Regions=>profiles.Where(p=>p.Kind==Type?.Kind).Select(p=>p.Region).Distinct().Select(id=>new FootballRegion(id,Context.ObjectName(id))).OrderBy(r=>r.Name).ToArray();
    string name=ATLink.Core.UiText.Get("New competition");public string Name{get=>name;set{name=value;Notify();}}
    public FootballFormatViewModel Format {get;}
    public FootballSeasonViewModel Season {get;}
    public CreationCalendarViewModel Calendar {get;}
    public FootballMatchupsViewModel Matchups {get;}
    public FootballAdvancementViewModel Advancement {get;}
    public bool HasMatchups=>Format.Phases.Any(p=>p.Knockout)||Matchups.NeedsReview;
    public FootballRulesViewModel GeneralRules {get;private set;}
    public string Error {get;private set;}="";public string Review {get;private set;}="";
    readonly CompetitionTeamVm[] availableTeams;
    public ICollectionView Teams {get;}
    readonly HashSet<string> selectedAvailableIds=[];
    public IReadOnlyList<CompetitionTeamVm> SelectedAvailableTeams=>availableTeams.Where(t=>selectedAvailableIds.Contains(t.Id)).ToArray();
    public bool FilteringTeams {get;private set;}
    public event Action? AvailableSelectionChanged;
    public void RefreshDisplayLanguage()
    {
        FilteringTeams=true;
        try{Teams.Refresh();Notify(nameof(SelectedAvailableTeams));}
        finally{FilteringTeams=false;}
        AvailableSelectionChanged?.Invoke();
    }
    string search="";public string Search{get=>search;set{if(search==value)return;search=value;Notify();FilteringTeams=true;try{Teams.Refresh();}finally{FilteringTeams=false;}AvailableSelectionChanged?.Invoke();}}
    public void SelectAvailable(IEnumerable<CompetitionTeamVm> added,IEnumerable<CompetitionTeamVm> removed)
    {
        if(FilteringTeams)return;foreach(var team in removed)selectedAvailableIds.Remove(team.Id);foreach(var team in added)selectedAvailableIds.Add(team.Id);
    }
    public ObservableCollection<CompetitionTeamVm> Participants {get;}=[];
    public CompetitionTeamVm? SelectedAvailable {get;set;}public CompetitionTeamVm? SelectedParticipant {get;set;}
    public string ParticipantSummary=>ATLink.Core.UiText.Format("{0} teams chosen. Leave this list empty to configure qualifications later.", Participants.Count);
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
        Context=c;Names.Configure(c);this.created=created;availableTeams=c.Teams.Select(t=>new CompetitionTeamVm(t){NameProvider=()=>c.TeamName(t.Id)}).ToArray();Teams=new ListCollectionView(availableTeams){Filter=item=>item is CompetitionTeamVm team&&team.Name.Contains(Search,StringComparison.CurrentCultureIgnoreCase)};profiles=FootballCompetitionAdapter.Profiles(c);Format=new(c,false);Format.Phases.CollectionChanged+=(_,_)=>RefreshParticipantCounter();Participants.CollectionChanged+=(_,_)=>RefreshParticipantCounter();Season=new(c);Matchups=new(c,()=>Format.NavigationSnapshot(),()=>Participants.Select(t=>t.Id).ToArray());Advancement=new(Format,Matchups);Matchups.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(FootballMatchupsViewModel.NeedsReview))Notify(nameof(HasMatchups));};Calendar=new(c,Season,()=>Matchups.Layout());Format.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(FootballFormatViewModel.PreviewText))Notify(nameof(HasMatchups));};GeneralRules=new(null,new Dictionary<string,string[]>());Type=Types[0];
        Calendar.Board.Participants=SimulationParticipants;
        Calendar.Board.TieEditors=()=>Format.Phases.Where(p=>p.Knockout).SelectMany(p=>p.EssentialRounds.SelectMany((r,i)=>r.TieVenues.Select(t=>new CalendarBoardViewModel.TieEditor(p.DraftKey.ToString("N")+"/"+i,t)))).ToArray();
        Calendar.Board.FixtureAssignmentAllowed=()=>Season.Tasks.All(t=>!ATLink.Core.UiText.Source(t.Task.Action).StartsWith("Fill")||t.Task.Action=="FillWithTeam");
        if(Regions.FirstOrDefault(r=>r.Id==preferredRegion) is {} preferred)Region=preferred;
        Links=new(this);
        Matchups.ReservedEntries=()=>Links.ReservedQualifierPlaces;
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
        if(Profile is not {} profile){Error=ATLink.Core.UiText.Get("This format is not yet supported for this region in the selected game.");Notify(nameof(Error));return;}
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
        if(additions.Length>remaining){Error=ATLink.Core.UiText.Format("Only {0} team place(s) remain. Select fewer teams or increase the competition size.", remaining);Notify(nameof(Error));return;}
        foreach(var team in additions)Participants.Add(team);selectedAvailableIds.Clear();SelectedAvailable=null;AvailableSelectionChanged?.Invoke();Error="";Notify(nameof(Error));Notify(nameof(ParticipantSummary));
    }
    public void RemoveTeam(){if(SelectedParticipant is not null)Participants.Remove(SelectedParticipant);Notify(nameof(ParticipantSummary));}
    public bool ValidateStep(int step)
    {
        try
        {
            if(Profile is null)throw new InvalidDataException(ATLink.Core.UiText.Get("Choose a supported competition type and region."));
            if(step>=1){_=Names.Snapshot(Context,Name);_ =Format.Artwork.Snapshot();if(string.IsNullOrWhiteSpace(Name)||Name.IndexOfAny([',','\n','\r'])>=0)throw new InvalidDataException(ATLink.Core.UiText.Get("Enter a competition name without commas or line breaks."));var layout=Format.Snapshot();Season.Capacity=layout[0].Participants;Season.FinalCapacity=layout[^1].Kind==FootballPhaseKind.Knockout?2:layout[^1].Participants/layout[^1].Groups;FootballCompetitionCompiler.Validate(layout);if(Type?.Kind is FootballCompetitionKind.Cup or FootballCompetitionKind.GroupsAndKnockout&&layout[^1].Output!=1)throw new InvalidDataException(ATLink.Core.UiText.Get("Add enough knockout rounds to reach the final and one winner."));}
            if(step>=2){Advancement.Synchronize();Advancement.Validate();Matchups.Validate();var phases=Matchups.Layout();GeneralRules.ConfigureEndings(phases.Any(p=>p.Kind==FootballPhaseKind.Knockout&&(p.Legs==1||p.FinalLegs==1)),phases.Any(p=>p.Kind==FootballPhaseKind.Knockout&&(p.Legs==2||p.FinalLegs==2)));FootballBehaviour.ValidateEntries(Context,EntryCapacity(phases[0].Participants),Season.Tasks.Select(t=>t.Task).ToArray(),Participants.Select(t=>t.Id).ToArray());if(Season.AutomaticResults&&(phases[^1].Kind==FootballPhaseKind.Knockout?phases[^1].Output!=1:phases[^1].Groups!=1))throw new InvalidDataException(ATLink.Core.UiText.Get("Automatic season results need a final or one final table. Add a final phase, or disable automatic results and configure the classification explicitly."));_ =Season.Plan(Calendar.Plan());if(Calendar.Automatic&&ATLink.Core.UiText.Source(Season.CalendarMode)=="Assign first-stage fixtures"&&(Participants.Count!=phases[0].Participants||phases[0].Kind==FootballPhaseKind.Knockout))throw new InvalidDataException(ATLink.Core.UiText.Get("Assigned fixtures need a league/group first phase and every club selected explicitly."));_ =GeneralRules.Overrides();}
            Error="";Notify(nameof(Error));return true;
        }
        catch(Exception ex){Error=ex.Message;Notify(nameof(Error));return false;}
    }
    public bool ValidateJourneyStep(int step)=>ValidateJourneyStep((FootballCreationStep)step);
    public bool ValidateJourneyStep(FootballCreationStep step)
    {
        try
        {
            switch(step)
            {
                case FootballCreationStep.Format:
                    if(Type is null)throw new InvalidDataException(ATLink.Core.UiText.Get("Choose a competition format."));break;
                case FootballCreationStep.Identity:
                    if(Profile is null)throw new InvalidDataException(ATLink.Core.UiText.Get("Choose a supported parent competition."));
                    if(string.IsNullOrWhiteSpace(Name)||Name.IndexOfAny([',','\n','\r'])>=0)throw new InvalidDataException(ATLink.Core.UiText.Get("Enter a competition name without commas or line breaks."));
                    _=Names.Snapshot(Context,Name);_=Format.Artwork.Snapshot();break;
                case FootballCreationStep.Path:
                    var structure=Format.StructureSnapshot();FootballCompetitionCompiler.Validate(structure);
                    Season.Capacity=structure[0].Participants;Season.FinalCapacity=structure[^1].Kind==FootballPhaseKind.Knockout?2:structure[^1].Participants/structure[^1].Groups;
                    if(Type?.Kind is FootballCompetitionKind.Cup or FootballCompetitionKind.GroupsAndKnockout&&structure[^1].Output!=1)throw new InvalidDataException(ATLink.Core.UiText.Get("Choose enough knockout rounds to reach a final and one winner."));break;
                case FootballCreationStep.Links: Links.Validate();break;
                case FootballCreationStep.Teams:
                    var phases=Format.StructureSnapshot();
                    FootballBehaviour.ValidateEntries(Context,EntryCapacity(phases[0].Participants),Season.Tasks.Select(t=>t.Task).ToArray(),Participants.Select(t=>t.Id).ToArray());
                    Matchups.Validate();break;
                case FootballCreationStep.Schedule:
                    foreach(var round in Format.Phases.SelectMany(p=>p.EssentialRounds))
                    {
                        if(round.VenueError.Length>0)throw new InvalidDataException(round.VenueError);
                        foreach(var tie in round.TieVenues)_=tie.Snapshot();
                        if(round.SetDrawDate)
                        {
                            if(round.DrawDate is null)throw new InvalidDataException(ATLink.Core.UiText.Get("Choose a draw date."));
                            FootballPhaseSettingsAdapter.Validate(Context,new(){DrawDate=new(FootballSettingMode.Value,(DateOnly.FromDateTime(round.DrawDate.Value).DayNumber-Context.ReferenceDate.DayNumber).ToString())});
                        }
                    }
                    _=Calendar.Plan();_=Season.Plan(Calendar.Plan());
                    var layout=Matchups.Layout();
                    if(Calendar.Automatic&&ATLink.Core.UiText.Source(Season.CalendarMode)=="Assign first-stage fixtures"&&(Participants.Count!=layout[0].Participants||layout[0].Kind==FootballPhaseKind.Knockout))throw new InvalidDataException(ATLink.Core.UiText.Get("Assigned fixtures need a league/group first phase and every club selected explicitly."));break;
                case FootballCreationStep.Rewards:
                    foreach(var round in Format.Phases.SelectMany(p=>p.EssentialRounds))if(round.PrizeError.Length>0)throw new InvalidDataException(round.PrizeError);break;
                case FootballCreationStep.Rules:
                    _=GeneralRules.Overrides();foreach(var phase in Format.Phases)_=phase.Rules.Overrides();break;
                case FootballCreationStep.Advancement:
                    Advancement.Synchronize();Advancement.Validate();break;
                case FootballCreationStep.Review:
                    if(!ValidateStep(2))return false;_=Names.Snapshot(Context,Name);Links.Validate();break;
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
        Season.RefreshPreview();var phases=Format.Snapshot();Review=ATLink.Core.UiText.Concat($"{Name}\n{Region?.Name} · {Type?.Name}\n\n", ATLink.Core.UiText.Join(" → ",phases.Select(p=>ATLink.Core.UiText.Format("{0} ({1} teams)", p.Name, p.Participants))), "\n\n", ATLink.Core.UiText.Join("\n",phases.Select(p=>$"• {p.Name}: {p.Summary}; "+(p.Output>0?ATLink.Core.UiText.Format("{0} advance", p.Output):ATLink.Core.UiText.Get("final standings")))), ATLink.Core.UiText.Format("\n\n{0} selected teams. ", Participants.Count), (Participants.Count==0?ATLink.Core.UiText.Get("Participants can be configured later."):ATLink.Core.UiText.Get("Existing domestic league memberships stay unchanged.")), ATLink.Core.UiText.Get("\n\nInternal preparation and native connections are generated automatically. Create saves a project draft; Export writes a new DB/LOC/competition project. Game installation and testing remain separate."));Review+=ATLink.Core.UiText.Concat(ATLink.Core.UiText.Get("\n\nSeason behaviour\n"), Season.Preview, "\n", Season.ReferenceLabel);Review+="\n\n"+Calendar.Summary();Review+=ATLink.Core.UiText.Concat(ATLink.Core.UiText.Get("\n\nInternational calendar\n"), Calendar.International.Summary);Review+=ATLink.Core.UiText.Concat(ATLink.Core.UiText.Get("\n\nPhase essentials\n"), ATLink.Core.UiText.Join("\n",Format.Phases.Select(p=>p.Name+"\n"+p.EssentialsSummary)));Review+=ATLink.Core.UiText.Concat(ATLink.Core.UiText.Get("\n\nMatchups\n"), Matchups.Summary);Review+=ATLink.Core.UiText.Concat(ATLink.Core.UiText.Get("\n\nAdvancement rules\n"), Advancement.Review);Review+=ATLink.Core.UiText.Concat(ATLink.Core.UiText.Get("\n\nLinked competitions\n"), ATLink.Core.UiText.Join("\n\n",Links.Drafts().Select(d=>d.Name+"\n"+d.Links.Summary)));Review+=ATLink.Core.UiText.Concat(ATLink.Core.UiText.Get("\n\nArtwork\n"), ATLink.Core.UiText.Join("\n",Links.Drafts().Select(d=>d.Name+": "+d.Format.Artwork.Summary)));Review+=ATLink.Core.UiText.Concat(ATLink.Core.UiText.Get("\n\nNames\n"), ATLink.Core.UiText.Join("\n",Links.Drafts().SelectMany(d=>d.Names.Snapshot(Context,d.Name)).Select(n=>n.Language+": "+n.FullName+" / "+n.ShortName)));Notify(nameof(Review));
    }
    public FootballCompetitionDraft SnapshotDraft()=>new(DraftKey,Type?.Kind??throw new InvalidDataException(ATLink.Core.UiText.Get("Choose a competition type.")),
        Region?.Id??throw new InvalidDataException(ATLink.Core.UiText.Get("Choose a region.")),Name,Matchups.Layout(Format.Snapshot()),Participants.Select(t=>t.Id).ToArray(),
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
