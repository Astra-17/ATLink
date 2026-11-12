using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using ATLink.Core;

namespace ATLink.ViewModels;

public sealed class FootballQuotaViewModel : FootballNotify
{
    public Action? Changed { get; set; }
    FootballChoice? path; public FootballChoice? Path { get=>path; set{path=value;Notify();Changed?.Invoke();} }
    FootballChoice? league; public FootballChoice? League { get=>league; set{league=value;Notify();Changed?.Invoke();} }
    int places=1; public int Places { get=>places; set{places=value;Notify();Changed?.Invoke();} }
}
public sealed class FootballLinkedDateViewModel
{
    public required string Name { get; init; }
    public DateTime? Date { get; set; }
    public string FirstKickoff { get; set; } = "20:00";
    public string SecondKickoff { get; set; } = "21:00";
    public FootballQualifyingRoundViewModel? Round {get;set;}
}
public sealed class FootballDirectClubViewModel : FootballNotify
{
    public required CompetitionTeamVm Club { get; init; }
    bool selected;
    public bool Selected { get => selected; set { selected = value; Notify(); Changed?.Invoke(); } }
    public Action? Changed { get; init; }
}
public interface IFootballQuotaEditor
{
    void AddQuota();
    void RemoveQuota();
    void UseEuropeanQuotas();
}
public sealed class FootballLinksViewModel : FootballNotify,IFootballQuotaEditor
{
    public CompetitionProjectContext Context { get; }
    public int CompetitionId { get; }
    readonly Action? changed;
    public bool Supported => Context.Game == "FC27";
    public IReadOnlyList<FootballChoice> Competitions { get; private set; } = [];
    public IReadOnlyList<FootballChoice> NeighbourChoices {get;private set;}=[];
    public bool NativeDivision=>FootballLinkedCompetitions.Neighbours(Context,CompetitionId).Native;
    public string DivisionHelp=>NativeDivision?"Edit this league's neighbouring divisions. The preview includes reciprocal links and existing places.":
        "Choose the division above or below this competition. Two clubs move between linked divisions; qualifying places are balanced automatically.";
    public string NativePlaces=>NativeDivision?FootballCompetitionAdapter.Read(Context,CompetitionId).ParticipantCount+" clubs · "+
        string.Join(" · ",FootballStandingLabels.Read(Context,CompetitionId).Where(l=>l.Category is "Promotion" or "Relegation" or "Playoff").Select(l=>"Place "+l.Position+": "+l.Text).Distinct()):"";
    public IReadOnlyList<FootballChoice> Leagues { get; }
    public IReadOnlyList<FootballChoice> Paths { get; } = [new(0, "First qualifying path"), new(1, "Second qualifying path")];
    public IReadOnlyList<FootballChoice> PromotionChoices { get; } = [new(0, "First of each group"), new(1, "Winners of the semi-finals")];
    public IReadOnlyList<int> Counts { get; } = Enumerable.Range(1, 32).ToArray();
    public IReadOnlyList<string> Times => Enumerable.Range(0, 96).Select(n => $"{n / 4:00}:{n % 4 * 15:00}")
        .Concat(Dates.SelectMany(d=>new[]{d.FirstKickoff,d.SecondKickoff})).Distinct().Order().ToArray();
    public string Title => Context.ObjectName(CompetitionId);
    public string ExistingLinks { get; private set; } = "";
    public FootballChoice? Upper { get; set; }
    public FootballChoice? Lower { get; set; }
    public FootballChoice? Promotion { get; set; }
    public FootballChoice? CalendarSource { get; set; }
    public bool UseSourceTime { get; set; } = true;
    public string CalendarKickoff { get; set; } = "21:00";
    public FootballChoice? ExcludedCompetition { get; set; }
    FootballChoice? destination;
    public FootballChoice? QualifierDestination
    {
        get => destination;
        set
        {
            if (destination == value) return;
            destination = value;
            Notify();
            LoadQualifiers();
        }
    }
    public bool CreateNewQualifiers { get; set; } = true;
    public string QualifierName { get; set; } = "Qualifying playoffs";
    public CompetitionArtworkViewModel QualifierArtwork {get;private set;}
    FootballQualifierLink? qualifierLink;
    public string QualifierStatus => qualifierLink is null ? "Create two qualifying paths with 32 clubs each." :
        "Editing " + Context.ObjectName(qualifierLink.Competition) + ". Each path supplies one club to this edition.";
    public ObservableCollection<FootballQuotaViewModel> Quotas { get; } = [];
    public FootballQuotaViewModel? SelectedQuota { get; set; }
    public ObservableCollection<FootballLinkedDateViewModel> Dates { get; } = [];
    public ObservableCollection<FootballDirectClubViewModel> DirectClubs { get; } = [];
    public string DirectCount => $"{DirectClubs.Count(t => t.Selected)}/{RequiredDirect} direct clubs";
    public int RequiredDirect
    {
        get
        {
            try { return QualifierDestination is null ? 0 : FootballLinkedCompetitions.GroupPhase(Context, QualifierDestination.Id).Participants - 2; }
            catch { return 0; }
        }
    }
    public string QuotaCount => string.Join(" · ", Paths.Select(path =>
        (path.Id == 0 ? "First path" : "Second path") + " " + Quotas.Where(q => q.Path?.Id == path.Id).Sum(q => q.Places) + "/32"));
    public string Preview { get; private set; } = "";
    public string Error { get; private set; } = "";
    public bool Confirmed { get; set; }
    public int Section { get; set; }
    string? stamp;

    public FootballLinksViewModel(CompetitionProjectContext context, int competition, Action? changed = null)
    {
        Context = context; CompetitionId = competition; this.changed = changed;QualifierArtwork=new(context);
        Leagues = context.Base is null ? [] : new FootballCatalog(context.Base).Entities("leagues")
            .Select(l => new FootballChoice(int.Parse(l.Id), l.Name)).OrderBy(l => l.Name).ToArray();
        Quotas.CollectionChanged += (_, e) =>
        {
            if(e.OldItems is not null)foreach(FootballQuotaViewModel row in e.OldItems)row.Changed=null;
            if(e.NewItems is not null)foreach(FootballQuotaViewModel row in e.NewItems)row.Changed=()=>Notify(nameof(QuotaCount));
            Notify(nameof(QuotaCount));
        };
        Reload();
    }
    public void Reload()
    {
        Competitions = Context.Objects.Where(o => o.Kind == 3).Select(o => new FootballChoice(o.Id, o.DisplayName)).ToArray();
        var divisions = FootballLinkedCompetitions.Divisions(Context);
        var link = divisions.FirstOrDefault(l => l.Upper == CompetitionId || l.Lower == CompetitionId);
        var neighbours=FootballLinkedCompetitions.Neighbours(Context,CompetitionId);
        var compatible=FootballLinkedCompetitions.CompatibleNeighbours(Context,CompetitionId);
        NeighbourChoices=[new(0,"None"),..Competitions.Where(c=>compatible.Contains(c.Id))];
        FootballChoice Choice(FootballNeighbour? neighbour)
        {
            if(neighbour is null)return NeighbourChoices[0];
            var choice=NeighbourChoices.FirstOrDefault(c=>c.Id==neighbour.CompetitionId);
            if(choice is not null)return choice;
            if(neighbour.CompetitionId is int known&&Competitions.FirstOrDefault(c=>c.Id==known) is {} named)
            {
                NeighbourChoices=[..NeighbourChoices,named];return named;
            }
            choice=new(-neighbour.DatabaseId,"Unresolved existing division (DB "+neighbour.DatabaseId+")");
            NeighbourChoices=[..NeighbourChoices,choice];return choice;
        }
        Upper=Choice(neighbours.Higher);Lower=Choice(neighbours.Lower);
        Promotion = PromotionChoices.Single(p => p.Id == (link?.Promotion == FootballPromotionSource.SemifinalWinners ? 1 : 0));
        var ownQualifier = FootballLinkedCompetitions.ReadQualifiers(Context, CompetitionId);
        bool receives=FootballCompetitionAdapter.Read(Context,CompetitionId).Phases.FirstOrDefault() is {Kind:FootballPhaseKind.Groups,Groups:2};
        destination = Competitions.FirstOrDefault(c => c.Id == (ownQualifier?.Destination ?? (receives?CompetitionId:0)));
        var calendarLink=Context.EffectiveRules(CompetitionId).FirstOrDefault(r=>r.Key=="schedule_use_dates_comp").Value;
        CalendarSource=int.TryParse(calendarLink,out int calendarId)?Competitions.FirstOrDefault(c=>c.Id==calendarId):null;
        var messages = new List<string>();
        if(neighbours.Native)messages.Add("Higher division: "+Upper.Name+" · Lower division: "+Lower.Name);
        if(neighbours.Higher is {CompetitionId:null}||neighbours.Lower is {CompetitionId:null})messages.Add("An existing division reference could not be resolved. Its value is preserved until you choose a replacement.");
        if(ownQualifier is not null)messages.Add("Two playoff winners enter "+Context.ObjectName(ownQualifier.Destination)+" in this edition.");
        foreach (var division in divisions.Where(l => l.Upper == CompetitionId || l.Lower == CompetitionId))
            messages.Add(Context.ObjectName(division.Upper) + " ↔ " + Context.ObjectName(division.Lower) + ". " +
                (division.Promotion == FootballPromotionSource.SemifinalWinners ? "Both semi-final winners are promoted." : "Both group winners are promoted.") +
                " The last club in each upper group is relegated.");
        foreach (var qualifier in FootballLinkedCompetitions.QualifiersFor(Context, CompetitionId))
            messages.Add(Context.ObjectName(qualifier.Competition) + " supplies two clubs before the group phase.");
        ExistingLinks = messages.Count == 0 ? "Choose how this competition connects to another competition." : string.Join("\n", messages);
        LoadQualifiers();
        foreach (string property in new[] { nameof(Competitions), nameof(Upper), nameof(Lower), nameof(Promotion), nameof(CalendarSource),
            nameof(QualifierDestination), nameof(ExistingLinks), nameof(Title),nameof(NeighbourChoices),nameof(NativeDivision),nameof(DivisionHelp),nameof(NativePlaces) }) Notify(property);
    }
    public void CreatedTarget(int id)
    {
        changed?.Invoke();
        Reload();
        Lower = NeighbourChoices.FirstOrDefault(c => c.Id == id)??NeighbourChoices[0];
        QualifierDestination = Competitions.FirstOrDefault(c=>c.Id==id&&FootballCompetitionAdapter.Read(Context,id).Phases.FirstOrDefault() is {Kind:FootballPhaseKind.Groups,Groups:2});
        Notify(nameof(Lower));
    }
    void LoadQualifiers()
    {
        qualifierLink = null;
        DirectClubs.Clear(); Quotas.Clear(); Dates.Clear();
        if (QualifierDestination is not { } dest)
        {
            ExcludedCompetition=null;CreateNewQualifiers=true;Preview="";Error="";Confirmed=false;stamp=null;
            foreach(string key in new[]{nameof(ExcludedCompetition),nameof(Preview),nameof(Error),nameof(Confirmed),nameof(DirectCount),nameof(QualifierStatus)})Notify(key);
            return;
        }
        var existingQualifiers=FootballLinkedCompetitions.QualifiersFor(Context,dest.Id);
        if(existingQualifiers.Count>1)
        {
            Error="This competition has several qualifying competitions. Their existing links are preserved.";
            Notify(nameof(Error));
            return;
        }
        qualifierLink = existingQualifiers.SingleOrDefault();
        QualifierArtwork=new(Context,qualifierLink?.Competition);
        Notify(nameof(QualifierArtwork));
        CreateNewQualifiers = qualifierLink is null;
        if (qualifierLink is not null) QualifierName = Context.ObjectName(qualifierLink.Competition);
        var initial = FootballLinkedCompetitions.DirectTeams(Context, dest.Id).ToArray();
        foreach (string id in initial)
        {
            var team = Context.Teams.FirstOrDefault(t => t.Id == id);
            if (team is not null) DirectClubs.Add(new() { Club = new(team), Selected = DirectClubs.Count < RequiredDirect,
                Changed = () => Notify(nameof(DirectCount)) });
        }
        if (qualifierLink is not null)
        {
            var fills = Context.Rows("tasks.txt").Where(r => r[0] == qualifierLink.Competition.ToString() && r[2] == "FillFromLeagueMaxFromCountry");
            foreach (var row in fills)
                Quotas.Add(new() { Path = Paths[Array.IndexOf(qualifierLink.EntryPools, int.Parse(row[3]))],
                    League = Leagues.FirstOrDefault(l => l.Id == int.Parse(row[4])), Places = int.Parse(row[5]) });
            var deps = Context.Rows("settings.txt").Where(r => r[0] == qualifierLink.Competition.ToString() && r[1] == "advance_teamcompdependency")
                .Select(r => int.Parse(r[2])).Where(id => id != dest.Id).ToArray();
            ExcludedCompetition = Competitions.FirstOrDefault(c => deps.Contains(c.Id));
        }
        else
        {
            var pair = FootballLinkedCompetitions.Divisions(Context).FirstOrDefault(l => l.Lower == dest.Id);
            ExcludedCompetition = Competitions.FirstOrDefault(c => c.Id == pair?.Upper);
        }
        var phases = qualifierLink is null ? [] : FootballCompetitionAdapter.Read(Context, qualifierLink.Competition).Phases;
        int[] offsets = [17, 20, 23, 26, 28];
        string[] names = ["Round of 32", "Round of 16", "Quarter-finals", "Semi-finals", "Playoffs"];
        for (int i = 0; i < 5; i++)
        {
            var date = new FootballLinkedDateViewModel { Name = names[i], Date = Context.SeasonStart.AddDays(offsets[i]).ToDateTime(TimeOnly.MinValue) };
            if (qualifierLink is not null)
            {
                var phase = phases[i];
                var slots = Context.Rows("schedule.txt").Where(r => r[0] == phase.NativeId.ToString() || phase.NativeGroups.Contains(int.Parse(r[0]))).ToArray();
                if (slots.Length > 0)
                {
                    date.Date = Context.ReferenceDate.AddDays(int.Parse(slots[0][1])).ToDateTime(TimeOnly.MinValue);
                    string Time(int group) => slots.FirstOrDefault(r => r[0] == group.ToString())?[5] ?? slots[0][5];
                    string Display(string text) => text.PadLeft(4, '0').Insert(2, ":");
                    date.FirstKickoff = Display(Time(phase.NativeGroups[0]));
                    date.SecondKickoff = Display(Time(phase.NativeGroups[phase.NativeGroups.Count / 2]));
                }
            }
            date.Round=new(Context,names[i],qualifierLink is null?null:phases[i].NativeId);
            Dates.Add(date);
        }
        Preview = ""; Error = ""; Confirmed = false; stamp = null;
        foreach (string property in new[] { nameof(CreateNewQualifiers), nameof(QualifierName), nameof(QualifierStatus), nameof(DirectCount),
            nameof(RequiredDirect), nameof(QuotaCount), nameof(ExcludedCompetition), nameof(Preview), nameof(Error), nameof(Confirmed),nameof(Times) }) Notify(property);
    }
    public void AddQuota()
    {
        Quotas.Add(new() { Path = Paths[0], League = Leagues.FirstOrDefault(), Places = 1 });
        Notify(nameof(QuotaCount));
    }
    public void RemoveQuota() { if (SelectedQuota is not null) Quotas.Remove(SelectedQuota); Notify(nameof(QuotaCount)); }
    public void UseEuropeanQuotas()
    {
        Quotas.Clear();
        foreach(var quota in FootballLinkedCompetitions.EuropeanQuotas)
            Quotas.Add(new(){Path=Paths[quota.Path],League=Leagues.FirstOrDefault(l=>l.Id==quota.LeagueId),Places=quota.Places});
        Notify(nameof(QuotaCount));
    }
    FootballNationalQuota[] QuotaPlan() => Quotas.Select(q => new FootballNationalQuota(
        q.Path?.Id ?? throw new InvalidDataException("Choose a qualifying path."),
        q.League?.Id ?? throw new InvalidDataException("Choose a national league."), q.Places)).ToArray();
    FootballPlayoffDate[] CalendarPlan() => Dates.Select(d => new FootballPlayoffDate(
        d.Date.HasValue ? DateOnly.FromDateTime(d.Date.Value) : throw new InvalidDataException("Choose all five dates."),
        d.FirstKickoff, d.SecondKickoff)).ToArray();
    string State() => JsonSerializer.Serialize(new { Section, Upper = Upper?.Id, Lower = Lower?.Id, Promotion = Promotion?.Id,
        Calendar = CalendarSource?.Id, UseSourceTime, CalendarKickoff, Destination = QualifierDestination?.Id,
        Excluded = ExcludedCompetition?.Id, QualifierName, CreateNewQualifiers, Quotas = QuotaPlan(), CalendarPlan = CalendarPlan(), Rounds=Dates.Select(d=>d.Round!.Snapshot()),
        Direct = DirectClubs.Where(t => t.Selected).Select(t => t.Club.Id),Artwork=QualifierArtwork.Snapshot()?.Fingerprint });
    public void Review()
    {
        try
        {
            Error = "";
            if (Section == 0)
            {
                if(Upper?.Id<0||Lower?.Id<0)throw new InvalidDataException("Resolve the existing division reference, or explicitly choose None.");
                if(NativeDivision)
                {
                    var changes=FootballLinkedCompetitions.PreviewNativeNeighbours(Context,CompetitionId,Upper is {Id:>0}?Upper.Id:null,Lower is {Id:>0}?Lower.Id:null);
                    Preview=NativePlaces+"\n"+(changes.Count==0?"No division changes.":string.Join("\n",changes.Select(c=>c.Description)));
                }
                else
                {
                if(Upper is {Id:>0}&&Lower is {Id:>0})throw new InvalidDataException("The guided custom exchange currently connects two divisions. Choose one neighbour.");
                int upper=Upper is {Id:>0}?Upper.Id:CompetitionId;
                int lowerId=Lower is {Id:>0}?Lower.Id:Upper is {Id:>0}?CompetitionId:throw new InvalidDataException("Choose a higher or lower division.");
                var lower = FootballLinkedCompetitions.GroupPhase(Context, lowerId);
                int qualifiers = FootballLinkedCompetitions.QualifiersFor(Context, lowerId).Count * 2;
                Preview = Context.ObjectName(upper) + " ↔ " + Context.ObjectName(lowerId) +
                    "\nTwo clubs are promoted: " + Promotion?.Name + ". The final still determines prize ranking." +
                    "\nThe last club in each upper group is relegated." +
                    $"\n{lower.Participants - 2 - qualifiers} lower-division clubs stay; {qualifiers} exit; two relegated clubs arrive; {qualifiers} qualifying winners arrive." +
                    "\nExisting entry and season table rules for both divisions will be replaced together.";
                }
            }
            else if (Section == 1)
            {
                if (QualifierDestination is null) throw new InvalidDataException("Choose the competition receiving the two winners.");
                _=FootballLinkedCompetitions.GroupPhase(Context,QualifierDestination.Id);
                FootballLinkedCompetitions.ValidateQuotas(Context, QuotaPlan());
                if (DirectClubs.Count(t => t.Selected) != RequiredDirect)
                    throw new InvalidDataException("Choose exactly " + RequiredDirect + " direct clubs.");
                Preview = (CreateNewQualifiers ? "Create " + QualifierName : "Update " + QualifierStatus) +
                    "\n32 clubs per path. Two independent winners enter " + QualifierDestination.Name + " in the same edition." +
                    "\nExclude clubs already in " + QualifierDestination.Name + (ExcludedCompetition is null ? "." : " and " + ExcludedCompetition.Name + ".") +
                    "\n" + string.Join("\n", Quotas.Select(q => q.Path?.Name + ": " + q.Places + " from " + q.League?.Name)) +
                    "\n" + string.Join("\n", Dates.Select(d => d.Name + " · " + d.Date?.ToString("dd MMM yyyy") + " · " + d.FirstKickoff + " / " + d.SecondKickoff+" · "+d.Round!.Summary));
                var removed = DirectClubs.Where(t => !t.Selected).Select(t => t.Club.Name).ToArray();
                _=QualifierArtwork.Snapshot();Preview+="\n"+QualifierArtwork.Summary;
                if(qualifierLink is not null&&QualifierArtwork.HasPending)Preview+="\nShared artwork: "+string.Join(", ",Context.ArtworkSharing(qualifierLink.Competition).Select(o=>o.DisplayName));
                if (CreateNewQualifiers && removed.Length > 0) Preview += "\nClubs removed from direct entry: " + string.Join(", ", removed);
            }
            else
            {
                if (CalendarSource is null || CalendarSource.Id == CompetitionId) throw new InvalidDataException("Choose another competition's group calendar.");
                Preview = "Use group matchday dates from " + CalendarSource.Name + ". " +
                    (UseSourceTime ? "Use its kickoff times." : "Kickoff at " + CalendarKickoff + ".") +
                    "\nThe current group matchday dates will be replaced.";
            }
            stamp = State() + FootballCompetitionCompiler.Fingerprint(Context, CompetitionId);
            Confirmed = false;
        }
        catch (Exception ex) { Error = ex.Message; stamp = null; }
        foreach (string key in new[] { nameof(Preview), nameof(Error), nameof(Confirmed), nameof(QuotaCount) }) Notify(key);
    }
    public void Apply()
    {
        try
        {
            if (!Confirmed || stamp != State() + FootballCompetitionCompiler.Fingerprint(Context, CompetitionId))
                throw new InvalidDataException("Review and confirm the current choices first.");
            if (Section == 0)
            {
                if(NativeDivision)FootballLinkedCompetitions.ApplyNativeNeighbours(Context,CompetitionId,Upper is {Id:>0}?Upper.Id:null,Lower is {Id:>0}?Lower.Id:null);
                else FootballLinkedCompetitions.ConfigureDivisions(Context,Upper is {Id:>0}?Upper.Id:CompetitionId,
                    Lower is {Id:>0}?Lower.Id:CompetitionId,Promotion?.Id == 1?FootballPromotionSource.SemifinalWinners:FootballPromotionSource.GroupWinners);
            }
            else if (Section == 1)
            {
                Context.Execute("Configure qualifying competition",()=>
                {
                int id;
                if (CreateNewQualifiers)
                    id=FootballLinkedCompetitions.CreateQualifiers(Context, QualifierName, QualifierDestination!.Id, ExcludedCompetition?.Id,
                        DirectClubs.Where(t => t.Selected).Select(t => t.Club.Id).ToArray(), QuotaPlan(), CalendarPlan(),Dates.Select(d=>d.Round!.Snapshot()).ToArray());
                else
                {
                    id=qualifierLink?.Competition??throw new InvalidDataException("Select existing qualifying playoffs.");
                    FootballLinkedCompetitions.UpdateQualifiers(Context, qualifierLink!,
                    ExcludedCompetition?.Id, QuotaPlan(), CalendarPlan(), DirectClubs.Where(t=>t.Selected).Select(t=>t.Club.Id).ToArray(),QualifierName,Dates.Select(d=>d.Round!.Snapshot()).ToArray());
                }
                if(QualifierArtwork.HasPending||CreateNewQualifiers&&QualifierArtwork.Snapshot() is not null)Context.SetArtwork(id,QualifierArtwork.Snapshot(),Confirmed);
                });
            }
            else FootballLinkedCompetitions.ShareGroupDates(Context, CompetitionId, CalendarSource!.Id, UseSourceTime ? null : CalendarKickoff);
            changed?.Invoke(); Reload();
            Preview = "Linked competition changes applied to the project draft. Undo is available. Export and test the season transition in FC27.";
            Notify(nameof(Preview));
        }
        catch (Exception ex) { Error = ex.Message; Notify(nameof(Error)); }
    }
}

