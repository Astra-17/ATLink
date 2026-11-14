using System.Collections.ObjectModel;
using System.IO;
using ATLink.Core;

namespace ATLink.ViewModels;

public sealed record FootballDraftChoice(string Name,FootballCompetitionReference? Reference)
{
    public override string ToString()=>Name;
}
public sealed class FootballCreationLinksViewModel : FootballNotify,IFootballQuotaEditor
{
    readonly FootballCreationViewModel owner;
    public FootballCreationViewModel Creation=>owner;
    CompetitionProjectContext Context=>owner.Context;
    public ObservableCollection<FootballCreationViewModel> NewDrafts {get;}=[];
    public FootballCreationViewModel? SelectedDraft {get;set;}
    public IReadOnlyList<FootballDraftChoice> Choices {get;private set;}=[];
    public IReadOnlyList<FootballDraftChoice> NeighbourChoices {get;private set;}=[];
    public IReadOnlyList<FootballDraftChoice> ReceivingChoices {get;private set;}=[];
    public FootballDraftChoice? Higher {get;set;}
    public FootballDraftChoice? Lower {get;set;}
    public FootballDraftChoice? CalendarSource {get;set;}
    public FootballDraftChoice? ExcludedCompetition {get;set;}
    public IReadOnlyList<FootballChoice> PromotionChoices {get;}=[new(0,ATLink.Core.UiText.Get("First of each group")),new(1,ATLink.Core.UiText.Get("Winners of the semi-finals"))];
    FootballChoice? promotion;
    public FootballChoice? Promotion
    {
        get=>promotion;
        set
        {
            if(promotion?.Id==value?.Id)return;
            promotion=value;Notify();
            foreach(var draft in KnownDrafts().Where(d=>d!=owner))
            {
                var links=draft.Links;
                if(Same(Higher?.Reference,links.Current)&&Same(links.Lower?.Reference,Current)||
                   Same(Lower?.Reference,links.Current)&&Same(links.Higher?.Reference,Current))
                    links.Promotion=links.PromotionChoices.FirstOrDefault(p=>p.Id==value?.Id);
            }
        }
    }
    public bool UseSourceTime {get;set;}=true;
    public string CalendarKickoff {get;set;}="21:00";
    bool addQualifiers;public bool AddQualifiers {get=>addQualifiers;set{addQualifiers=value;Notify();owner.RefreshEntries();}}
    public string QualifierName {get;set;}=ATLink.Core.UiText.Get("Qualifying playoffs");
    public FootballCompetitionNamesViewModel QualifierNames {get;}=new();
    public CompetitionArtworkViewModel QualifierArtwork {get;}
    FootballDraftChoice? destination;
    public FootballDraftChoice? QualifierDestination
    {
        get=>destination;
        set{destination=value;Notify();RefreshDirectTeams();owner.RefreshEntries();}
    }
    public IReadOnlyList<FootballChoice> Paths {get;}=[new(0,ATLink.Core.UiText.Get("First qualifying path")),new(1,ATLink.Core.UiText.Get("Second qualifying path"))];
    public IReadOnlyList<FootballChoice> Leagues {get;}
    public IReadOnlyList<int> Counts {get;}=Enumerable.Range(1,32).ToArray();
    public IReadOnlyList<string> Times {get;}=Enumerable.Range(0,96).Select(n=>$"{n/4:00}:{n%4*15:00}").ToArray();
    public ObservableCollection<FootballQuotaViewModel> Quotas {get;}=[];
    public FootballQuotaViewModel? SelectedQuota {get;set;}
    public ObservableCollection<FootballLinkedDateViewModel> Dates {get;}=[];
    public ObservableCollection<FootballDirectClubViewModel> DirectClubs {get;}=[];
    public int RequiredDirect {get;private set;}
    public string DirectCount=>ATLink.Core.UiText.Format("{0}/{1} direct clubs", DirectClubs.Count(c=>c.Selected), RequiredDirect);
    public string QuotaCount=>ATLink.Core.UiText.Join(" · ",Paths.Select(p=>(p.Id==0?ATLink.Core.UiText.Get("First path"):ATLink.Core.UiText.Get("Second path"))+" "+Quotas.Where(q=>q.Path?.Id==p.Id).Sum(q=>q.Places)+"/32"));
    public string Summary {get;private set;}=ATLink.Core.UiText.Get("No linked competitions selected.");
    public string Error {get;private set;}="";

    public FootballCreationLinksViewModel(FootballCreationViewModel owner)
    {
        this.owner=owner;QualifierNames.Configure(Context);QualifierArtwork=new(owner.Context);
        Leagues=owner.Season.Leagues;
        Promotion=PromotionChoices[0];
        Quotas.CollectionChanged+=(_,e)=>
        {
            if(e.NewItems is not null)foreach(FootballQuotaViewModel q in e.NewItems)q.Changed=()=>Notify(nameof(QuotaCount));
            Notify(nameof(QuotaCount));
        };
        string[] rounds=[ATLink.Core.UiText.Get("Round of 32"),ATLink.Core.UiText.Get("Round of 16"),"Quarter-finals","Semi-finals",ATLink.Core.UiText.Get("Playoffs")];
        int[] days=[17,20,23,26,28];
        for(int i=0;i<5;i++)Dates.Add(new(){Name=rounds[i],Date=Context.SeasonStart.AddDays(days[i]).ToDateTime(TimeOnly.MinValue),Round=new(Context,rounds[i])});
        RefreshChoices();
    }
    public IEnumerable<FootballCreationViewModel> Drafts()
    {
        yield return owner;
        foreach(var child in NewDrafts)foreach(var draft in child.Links.Drafts())yield return draft;
    }
    FootballCreationViewModel Root
    {
        get{var root=owner;while(root.ParentDraft is not null)root=root.ParentDraft;return root;}
    }
    public FootballCompetitionReference Current=>new(FootballReferenceKind.Current,DraftKey:owner.DraftKey);
    public int ReservedQualifierPlaces=>KnownDrafts().Any(d=>d.Links.AddQualifiers&&d.Links.QualifierDestination?.Reference?.DraftKey==owner.DraftKey)?2:0;
    IEnumerable<FootballCreationViewModel> KnownDrafts()=>Root==owner?Drafts():Root.Links.Drafts().Append(owner).DistinctBy(d=>d.DraftKey);
    public static bool Same(FootballCompetitionReference? a,FootballCompetitionReference? b)=>
        a is null||b is null?a is null&&b is null:a.Kind==FootballReferenceKind.Existing||b.Kind==FootballReferenceKind.Existing?
        a.Kind==b.Kind&&a.ExistingId==b.ExistingId:a.DraftKey==b.DraftKey;
    FootballCreationViewModel? Model(FootballCompetitionReference? reference)=>reference?.DraftKey is Guid key?
        KnownDrafts().FirstOrDefault(d=>d.DraftKey==key):null;
    (int Region,FootballPhase? Phase) Structure(FootballCompetitionReference? reference)
    {
        if(reference?.Kind==FootballReferenceKind.Existing&&reference.ExistingId is int id)
        {
            var model=FootballCompetitionAdapter.Read(Context,id);
            return(model.Region,model.Phases.FirstOrDefault());
        }
        var draft=Model(reference);
        try{return(draft?.Region?.Id??-1,draft?.Format.StructureSnapshot().FirstOrDefault());}catch{return(-1,null);}
    }
    public void RefreshChoices()
    {
        var none=new FootballDraftChoice(ATLink.Core.UiText.Get("None"),null);
        var drafts=KnownDrafts().ToArray();
        Choices=[none,..Context.Objects.Where(o=>o.Kind==3).Select(o=>new FootballDraftChoice(o.DisplayName,new(FootballReferenceKind.Existing,o.Id))),
            ..drafts.Select(d=>new FootballDraftChoice(d.Name+(d==owner?ATLink.Core.UiText.Get(" (current draft)"):ATLink.Core.UiText.Get(" (draft)")),
                new(d==owner?FootballReferenceKind.Current:FootballReferenceKind.New,DraftKey:d.DraftKey)))];
        var own=Structure(Current);
        NeighbourChoices=[none,..Choices.Where(c=>
        {
            if(c.Reference is null||Same(c.Reference,Current))return false;
            var candidate=Structure(c.Reference);
            return candidate.Region==own.Region&&own.Phase is not null&&candidate.Phase is not null&&
                (own.Phase.Kind==FootballPhaseKind.Groups&&own.Phase.Groups==2&&candidate.Phase.Kind==FootballPhaseKind.Groups&&
                    candidate.Phase.Groups==2&&candidate.Phase.Participants==own.Phase.Participants||
                 own.Phase.Kind==FootballPhaseKind.League&&candidate.Phase.Kind==FootballPhaseKind.League);
        })];
        ReceivingChoices=[none,..Choices.Where(c=>c.Reference is not null&&Structure(c.Reference).Phase is {Kind:FootballPhaseKind.Groups,Groups:2})];
        FootballDraftChoice Keep(FootballDraftChoice? selected,IReadOnlyList<FootballDraftChoice> options)=>options.FirstOrDefault(c=>Same(c.Reference,selected?.Reference))??options[0];
        Higher=Keep(Higher,NeighbourChoices);Lower=Keep(Lower,NeighbourChoices);
        CalendarSource=Keep(CalendarSource,Choices);
        ExcludedCompetition=Keep(ExcludedCompetition,Choices);
        destination=Keep(destination,ReceivingChoices);
        if(destination.Reference is null&&ReceivingChoices.Any(c=>Same(c.Reference,Current)))
            destination=ReceivingChoices.Single(c=>Same(c.Reference,Current));
        RefreshDirectTeams();
        foreach(string key in new[]{nameof(Choices),nameof(NeighbourChoices),nameof(ReceivingChoices),nameof(Higher),nameof(Lower),
            nameof(CalendarSource),nameof(ExcludedCompetition),nameof(QualifierDestination)})Notify(key);
    }
    void RefreshDirectTeams()
    {
        var previous=DirectClubs.Where(c=>c.Selected).Select(c=>c.Club.Id).ToHashSet();
        var reference=destination?.Reference;
        var draft=Model(reference);
        var selected=draft?.Participants.Select(t=>t.Id).ToArray()??(reference?.ExistingId is int existingId?
            FootballLinkedCompetitions.DirectTeams(Context,existingId).ToArray():[]);
        RequiredDirect=Math.Max(0,(Structure(reference).Phase?.Participants??0)-2);
        bool sameRoster=DirectClubs.Select(c=>c.Club.Id).SequenceEqual(selected);
        if(!sameRoster)
        {
            DirectClubs.Clear();
            foreach(string id in selected)
            {
                var team=Context.Teams.FirstOrDefault(t=>t.Id==id);
                if(team is not null)DirectClubs.Add(new(){Club=new(team),Selected=previous.Count>0?previous.Contains(id):DirectClubs.Count<RequiredDirect,
                    Changed=()=>Notify(nameof(DirectCount))});
            }
        }
        Notify(nameof(DirectCount));
    }
    public void AcceptNew(FootballCreationViewModel child)
    {
        if(child.ParentDraft!=owner)throw new InvalidDataException(ATLink.Core.UiText.Get("This draft belongs to another creation."));
        NewDrafts.Add(child);RefreshChoices();
        Lower=NeighbourChoices.FirstOrDefault(c=>c.Reference?.DraftKey==child.DraftKey)??NeighbourChoices[0];
        if(Same(child.Links.Higher?.Reference,Current))Promotion=PromotionChoices.Single(p=>p.Id==child.Links.Promotion?.Id);
        if(ReceivingChoices.FirstOrDefault(c=>c.Reference?.DraftKey==child.DraftKey) is {} destinationChoice)
            QualifierDestination=destinationChoice;
        Notify(nameof(Lower));
    }
    public void RemoveNew()
    {
        if(SelectedDraft is null)return;
        var removed=SelectedDraft.Links.Drafts().Select(d=>d.DraftKey).ToHashSet();
        NewDrafts.Remove(SelectedDraft);SelectedDraft=null;
        foreach(var draft in Root.Links.Drafts())
        {
            var links=draft.Links;
            bool Removed(FootballDraftChoice? choice)=>choice?.Reference?.DraftKey is Guid key&&removed.Contains(key);
            if(Removed(links.Higher))links.Higher=null;
            if(Removed(links.Lower))links.Lower=null;
            if(Removed(links.CalendarSource))links.CalendarSource=null;
            if(Removed(links.ExcludedCompetition))links.ExcludedCompetition=null;
            if(Removed(links.QualifierDestination))links.QualifierDestination=null;
            links.RefreshChoices();
        }
    }
    public void AddQuota()=>Quotas.Add(new(){Path=Paths[0],League=Leagues.FirstOrDefault()});
    public void RemoveQuota(){if(SelectedQuota is not null)Quotas.Remove(SelectedQuota);}
    public void UseEuropeanQuotas()
    {
        Quotas.Clear();
        foreach(var quota in FootballLinkedCompetitions.EuropeanQuotas)
            Quotas.Add(new(){Path=Paths[quota.Path],League=Leagues.FirstOrDefault(l=>l.Id==quota.LeagueId),Places=quota.Places});
    }
    public FootballDraftNeighbours? NeighbourPlan()=>Higher?.Reference is null&&Lower?.Reference is null?null:
        new(Current,Higher?.Reference,Lower?.Reference,Promotion?.Id==1?FootballPromotionSource.SemifinalWinners:FootballPromotionSource.GroupWinners);
    public FootballDraftPlayoffs? PlayoffPlan()
    {
        if(!AddQualifiers)return null;
        var target=QualifierDestination?.Reference??throw new InvalidDataException(ATLink.Core.UiText.Get("Choose the competition receiving the playoff winners."));
        var quotas=Quotas.Select(q=>new FootballNationalQuota(q.Path?.Id??-1,q.League?.Id??-1,q.Places)).ToArray();
        FootballLinkedCompetitions.ValidateQuotas(Context,quotas);
        if(string.IsNullOrWhiteSpace(QualifierName))throw new InvalidDataException(ATLink.Core.UiText.Get("Name the qualifying competition."));
        if(DirectClubs.Count(c=>c.Selected)!=RequiredDirect||RequiredDirect<=0)
            throw new InvalidDataException(ATLink.Core.UiText.Concat(ATLink.Core.UiText.Get("Select "), RequiredDirect, ATLink.Core.UiText.Get(" direct entrants for the receiving competition.")));
        var dates=Dates.Select(d=>new FootballPlayoffDate(d.Date.HasValue?DateOnly.FromDateTime(d.Date.Value):
            throw new InvalidDataException(ATLink.Core.UiText.Get("Choose all playoff dates.")),d.FirstKickoff,d.SecondKickoff)).ToArray();
        if(dates.Zip(dates.Skip(1)).Any(p=>p.First.Date>=p.Second.Date))throw new InvalidDataException(ATLink.Core.UiText.Get("Choose five playoff dates in chronological order."));
        if(Same(ExcludedCompetition?.Reference,target))throw new InvalidDataException(ATLink.Core.UiText.Get("The receiving competition is already excluded automatically."));
        return new(QualifierName,target,ExcludedCompetition?.Reference,DirectClubs.Where(c=>c.Selected).Select(c=>c.Club.Id).ToArray(),quotas,dates){Rounds=Dates.Select(d=>d.Round!.Snapshot()).ToArray(),Names=QualifierNames.Snapshot(Context,QualifierName),Artwork=QualifierArtwork.Snapshot()};
    }
    public FootballDraftCalendar? CalendarPlan()=>CalendarSource?.Reference is {} source?
        new(Current,source,UseSourceTime?null:CalendarKickoff):null;
    public void Validate()
    {
        if(Higher?.Reference is not null&&Same(Higher.Reference,Lower?.Reference))throw new InvalidDataException(ATLink.Core.UiText.Get("Choose different higher and lower divisions."));
        if(Higher?.Reference is not null&&Lower?.Reference is not null&&Structure(Current).Phase?.Kind==FootballPhaseKind.Groups)
            throw new InvalidDataException(ATLink.Core.UiText.Get("The custom exchange currently connects two divisions. Choose one neighbouring division."));
        if(Same(CalendarSource?.Reference,Current))throw new InvalidDataException(ATLink.Core.UiText.Get("Choose another competition as the calendar source."));
        if(Structure(Current).Phase?.Kind==FootballPhaseKind.League)
            foreach(var pair in new[]{(Choice:Higher,Reverse:"info_league_releg"),(Choice:Lower,Reverse:"info_league_promo")})
                if(pair.Choice?.Reference is {Kind:FootballReferenceKind.Existing,ExistingId:int id}&&
                    int.TryParse(Context.EffectiveRules(id).FirstOrDefault(r=>r.Key==pair.Reverse).Value,out int existing)&&existing>0)
                    throw new InvalidDataException(ATLink.Core.UiText.Concat(pair.Choice.Name, ATLink.Core.UiText.Get(" already has another neighbouring division. Review that relationship before adding this one.")));
        _=PlayoffPlan();
        if(!UseSourceTime)_=TournamentBuilder.Time(CalendarKickoff);
    }
    public void Review()
    {
        try
        {
            RefreshChoices();Validate();Error="";
            var text=new List<string>();
            if(Higher?.Reference is not null)text.Add(ATLink.Core.UiText.Concat(ATLink.Core.UiText.Get("Higher division: "), Higher.Name));
            if(Lower?.Reference is not null)text.Add(ATLink.Core.UiText.Concat(ATLink.Core.UiText.Get("Lower division: "), Lower.Name));
            if(NeighbourPlan() is not null)text.Add(ATLink.Core.UiText.Concat(ATLink.Core.UiText.Get("Promoted teams: "), Promotion?.Name));
            if(PlayoffPlan() is {} playoff)
            {
                text.Add(ATLink.Core.UiText.Concat(playoff.Name, ATLink.Core.UiText.Get(": two winners join "), QualifierDestination!.Name, ATLink.Core.UiText.Get(" in the same edition.")));
                text.Add(DirectCount+"; "+QuotaCount+".");
                var removed=DirectClubs.Where(t=>!t.Selected).Select(t=>t.Club.Name).ToArray();
                if(removed.Length>0)text.Add(ATLink.Core.UiText.Concat(ATLink.Core.UiText.Get("Removed from direct entry: "), ATLink.Core.UiText.Join(", ",removed)));
            }
            if(CalendarPlan() is not null)text.Add(ATLink.Core.UiText.Concat(ATLink.Core.UiText.Get("Group matchdays from "), CalendarSource!.Name, (UseSourceTime?ATLink.Core.UiText.Get(" with the same kickoffs."):ATLink.Core.UiText.Concat(ATLink.Core.UiText.Get(" at "), CalendarKickoff, "."))));
            if(AddQualifiers)text.AddRange(QualifierNames.Snapshot(Context,QualifierName).Select(n=>n.Language+": "+n.FullName+" / "+n.ShortName));
            if(AddQualifiers)text.Add(QualifierArtwork.Summary);
            if(AddQualifiers)text.AddRange(Dates.Select(d=>d.Name+": "+d.Round!.Summary));
            text.AddRange(NewDrafts.Select(d=>ATLink.Core.UiText.Concat(ATLink.Core.UiText.Get("New linked competition: "), d.Name)));
            Summary=text.Count==0?ATLink.Core.UiText.Get("No linked competitions selected."):ATLink.Core.UiText.Join("\n",text);
        }
        catch(Exception ex){Error=ex.Message;}
        Notify(nameof(Summary));Notify(nameof(Error));
    }
    public void SynchronizeCalendar(HashSet<Guid> visited,HashSet<Guid> visiting)
    {
        if(visited.Contains(owner.DraftKey))return;
        if(!visiting.Add(owner.DraftKey))throw new InvalidDataException(ATLink.Core.UiText.Get("The linked calendars form a cycle."));
        if(CalendarPlan() is {} plan)
        {
            var sourceDraft=Model(plan.Source);
            sourceDraft?.Links.SynchronizeCalendar(visited,visiting);
            IReadOnlyList<FootballMatchdayDraft> days;
            if(sourceDraft is not null)
            {
                sourceDraft.Calendar.Synchronize();
                var sourcePlan=sourceDraft.Calendar.Plan();
                if(sourcePlan.Mode is FootballCalendarMode.ManualMatchdays or FootballCalendarMode.ExactFixtures)
                    days=sourcePlan.Matchdays.Where(d=>d.PhaseIndex==0).ToArray();
                else
                {
                    var first=sourceDraft.Calendar.Phases.First();
                    days=Enumerable.Range(1,first.Rounds).Select(n=>new FootballMatchdayDraft(0,n,sourcePlan.FirstDate.AddDays((n-1)*sourcePlan.IntervalDays),sourcePlan.Kickoff,first.Matches,first.Matches)).ToArray();
                }
            }
            else
            {
                int source=plan.Source.ExistingId??throw new InvalidDataException(ATLink.Core.UiText.Get("The calendar source is unavailable."));
                var phase=FootballLinkedCompetitions.GroupPhase(Context,source);
                days=Context.Calendar(source).Where(s=>s.ObjectId==phase.NativeId||phase.NativeGroups.Contains(s.ObjectId))
                    .Select(s=>new FootballMatchdayDraft(0,s.Round,s.Date(Context.ReferenceDate),s.Time,s.Minimum,s.Maximum)).ToArray();
            }
            owner.Calendar.Synchronize();
            var group=owner.Calendar.Phases.FirstOrDefault()??throw new InvalidDataException(ATLink.Core.UiText.Get("Add a group phase first."));
            var rounds=days.GroupBy(d=>d.Round).ToArray();
            if(rounds.Length!=group.Rounds||rounds.Any(g=>g.Select(d=>d.Date).Distinct().Count()!=1||plan.Kickoff is null&&g.Select(d=>d.Kickoff).Distinct().Count()!=1))
                throw new InvalidDataException(ATLink.Core.UiText.Get("Linked group calendars need matching matchdays with one date and kickoff per day."));
            owner.Calendar.Mode=ATLink.Core.UiText.Get("Choose matchday dates");owner.Calendar.Synchronize();
            foreach(var row in owner.Calendar.Matchdays.Where(d=>d.PhaseIndex==0).ToArray())owner.Calendar.Matchdays.Remove(row);
            foreach(var day in rounds.Select(g=>g.First()).OrderBy(d=>d.Round))
                owner.Calendar.Matchdays.Add(new(){PhaseIndex=0,PhaseKey=group.Key,PhaseName=group.Name,Round=day.Round,Date=day.Date.ToDateTime(TimeOnly.MinValue),
                    Kickoff=plan.Kickoff??day.Kickoff,Minimum=group.Matches.ToString(),Maximum=group.Matches.ToString(),Capacity=group.Matches});
        }
        visiting.Remove(owner.DraftKey);visited.Add(owner.DraftKey);
    }
}





