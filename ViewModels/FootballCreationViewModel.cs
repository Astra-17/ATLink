using System.IO;
using System.Collections.ObjectModel;
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
    public string Name{get;set;}="New competition";
    public FootballFormatViewModel Format {get;}
    public FootballSeasonViewModel Season {get;}
    public CreationCalendarViewModel Calendar {get;}
    public FootballRulesViewModel GeneralRules {get;private set;}
    public string Error {get;private set;}="";public string Review {get;private set;}="";
    string search="";public string Search{get=>search;set{search=value;Notify();Notify(nameof(Teams));}}
    public IReadOnlyList<CompetitionTeamVm> Teams=>Context.Teams.Where(t=>t.Name.Contains(Search,StringComparison.CurrentCultureIgnoreCase)).Select(t=>new CompetitionTeamVm(t)).ToArray();
    public ObservableCollection<CompetitionTeamVm> Participants {get;}=[];
    public CompetitionTeamVm? SelectedAvailable {get;set;}public CompetitionTeamVm? SelectedParticipant {get;set;}
    public string ParticipantSummary=>$"{Participants.Count} teams chosen. Leave this list empty to configure qualifications later.";
    public string ParticipantCounter=>$"{Participants.Count}/{(int.TryParse(Format.Phases.FirstOrDefault()?.Participants,out int total)?total:0)}";
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
    public FootballCreationViewModel(CompetitionProjectContext c,int? preferredRegion,Action<int> created)
    {
        Context=c;this.created=created;profiles=FootballCompetitionAdapter.Profiles(c);Format=new(c,false);Format.Phases.CollectionChanged+=(_,_)=>RefreshParticipantCounter();Participants.CollectionChanged+=(_,_)=>RefreshParticipantCounter();Season=new(c);Calendar=new(c,Season,()=>Format.Snapshot());GeneralRules=new(null,new Dictionary<string,string[]>());Type=Types[0];
        Calendar.Board.Participants=SimulationParticipants;
        Calendar.Board.FixtureAssignmentAllowed=()=>Season.Tasks.All(t=>!t.Task.Action.StartsWith("Fill")||t.Task.Action=="FillWithTeam");
        if(Regions.FirstOrDefault(r=>r.Id==preferredRegion) is {} preferred)Region=preferred;
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
        Participants.Clear();Error="";Notify(nameof(Error));Notify(nameof(ParticipantSummary));
    }
    public void AddTeam(){if(SelectedAvailable is not null&&!Participants.Any(t=>t.Id==SelectedAvailable.Id))Participants.Add(SelectedAvailable);Notify(nameof(ParticipantSummary));}
    public void RemoveTeam(){if(SelectedParticipant is not null)Participants.Remove(SelectedParticipant);Notify(nameof(ParticipantSummary));}
    public bool ValidateStep(int step)
    {
        try
        {
            if(Profile is null)throw new InvalidDataException("Choose a supported competition type and region.");
            if(step>=1){if(string.IsNullOrWhiteSpace(Name)||Name.IndexOfAny([',','\n','\r'])>=0)throw new InvalidDataException("Enter a competition name without commas or line breaks.");var layout=Format.Snapshot();Season.Capacity=layout[0].Participants;Season.FinalCapacity=layout[^1].Kind==FootballPhaseKind.Knockout?2:layout[^1].Participants/layout[^1].Groups;FootballCompetitionCompiler.Validate(layout);if(Type?.Kind is FootballCompetitionKind.Cup or FootballCompetitionKind.GroupsAndKnockout&&layout[^1].Output!=1)throw new InvalidDataException("Add enough knockout rounds to reach the final and one winner.");}
            if(step>=2){var phases=Format.Snapshot();GeneralRules.ConfigureEndings(phases.Any(p=>p.Kind==FootballPhaseKind.Knockout&&(p.Legs==1||p.FinalLegs==1)),phases.Any(p=>p.Kind==FootballPhaseKind.Knockout&&(p.Legs==2||p.FinalLegs==2)));FootballBehaviour.ValidateEntries(Context,phases[0].Participants,Season.Tasks.Select(t=>t.Task).ToArray(),Participants.Select(t=>t.Id).ToArray());if(Season.AutomaticResults&&(phases[^1].Kind==FootballPhaseKind.Knockout?phases[^1].Output!=1:phases[^1].Groups!=1))throw new InvalidDataException("Automatic season results need a final or one final table. Add a final phase, or disable automatic results and configure the classification explicitly.");_ =Season.Plan(Calendar.Plan());if(Calendar.Automatic&&Season.CalendarMode=="Assign first-stage fixtures"&&(Participants.Count!=phases[0].Participants||phases[0].Kind==FootballPhaseKind.Knockout))throw new InvalidDataException("Assigned fixtures need a league/group first phase and every club selected explicitly.");_ =GeneralRules.Overrides();}
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
            if(step>=1)FootballBehaviour.ValidateEntries(Context,phases[0].Participants,Season.Tasks.Select(t=>t.Task).ToArray(),Participants.Select(t=>t.Id).ToArray());
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
        if(!ValidateStep(2))return;
        Season.RefreshPreview();var phases=Format.Snapshot();Review=$"{Name}\n{Region?.Name} · {Type?.Name}\n\n"+string.Join(" → ",phases.Select(p=>$"{p.Name} ({p.Participants} teams)"))+"\n\n"+string.Join("\n",phases.Select(p=>$"• {p.Name}: {p.Summary}; "+(p.Output>0?$"{p.Output} advance":"final standings")))+$"\n\n{Participants.Count} selected teams. "+(Participants.Count==0?"Participants can be configured later.":"Existing domestic league memberships stay unchanged.")+"\n\nInternal preparation and native connections are generated automatically. Create saves a project draft; Export writes a new DB/LOC/competition project. Game installation and testing remain separate.";Review+="\n\nSeason behaviour\n"+Season.Preview+"\n"+Season.ReferenceLabel;Review+="\n\n"+Calendar.Summary();Review+="\n\nPhase essentials\n"+string.Join("\n",Format.Phases.Select(p=>p.Name+"\n"+p.EssentialsSummary));Notify(nameof(Review));
    }
    public void Create()
    {
        if(Completed||!ValidateStep(2)||Type is null||Region is null)return;
        try{int id=0;var phases=Format.Snapshot();var defaults=GeneralRules.Overrides();Context.Execute("Create football competition",()=>id=FootballCompetitionCompiler.Create(Context,Type.Kind,Region.Id,Name,phases,Participants.Select(t=>t.Id).ToArray(),defaults,Season.Plan(Calendar.Plan())));Completed=true;created(id);}
        catch(Exception ex){Error=ex.Message;Notify(nameof(Error));}
    }
}
