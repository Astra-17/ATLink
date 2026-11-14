namespace ATLink.Core;

public enum FootballReferenceKind { Current, Existing, New }
public sealed record FootballCompetitionReference(FootballReferenceKind Kind, int? ExistingId = null, Guid? DraftKey = null);
public sealed record FootballCompetitionDraft(Guid Key, FootballCompetitionKind Kind, int Region, string Name,
    IReadOnlyList<FootballPhase> Phases, IReadOnlyList<string> Teams, IReadOnlyDictionary<string,string[]> Rules, FootballBehaviourPlan Behaviour)
{
    public IReadOnlyList<FootballCompetitionName> Names {get;init;}=[];
    public bool GroupStageObjective {get;init;}=true;
    public bool RegionalRegistration {get;init;}
    public CompetitionArtwork? Artwork {get;init;}
}
public sealed record FootballDraftNeighbours(FootballCompetitionReference Current, FootballCompetitionReference? Higher,
    FootballCompetitionReference? Lower, FootballPromotionSource Promotion);
public sealed record FootballDraftPlayoffs(string Name, FootballCompetitionReference Destination,
    FootballCompetitionReference? Excluded, IReadOnlyList<string> DirectTeams, IReadOnlyList<FootballNationalQuota> Quotas,
    IReadOnlyList<FootballPlayoffDate> Dates)
{
    public IReadOnlyList<FootballQualifierRound> Rounds { get; init; } = [];
    public IReadOnlyList<FootballCompetitionName> Names { get; init; } = [];
    public CompetitionArtwork? Artwork {get;init;}
}
public sealed record FootballDraftCalendar(FootballCompetitionReference Destination, FootballCompetitionReference Source, string? Kickoff);
public sealed record FootballCreationGraph(IReadOnlyList<FootballCompetitionDraft> Competitions,
    IReadOnlyList<FootballDraftNeighbours> Neighbours, IReadOnlyList<FootballDraftPlayoffs> Playoffs,
    IReadOnlyList<FootballDraftCalendar> Calendars)
{
    public IReadOnlyList<FootballInternationalMove> InternationalMoves { get; init; } = [];
}

public static class FootballCompetitionGraph
{
    public static IReadOnlyDictionary<Guid,int> Create(CompetitionProjectContext c,FootballCreationGraph graph)
    {
        if(graph.Competitions.Count==0||graph.Competitions.Select(n=>n.Key).Distinct().Count()!=graph.Competitions.Count)
            throw new InvalidDataException(ATLink.Core.UiText.Get("Choose distinct competition drafts."));
        foreach(var node in graph.Competitions)FootballCompetitionCompiler.Validate(node.Phases);
        var ids=new Dictionary<Guid,int>();
        int Resolve(FootballCompetitionReference reference)
        {
            if(reference.Kind==FootballReferenceKind.Existing)
                return c.Objects.SingleOrDefault(o=>o.Id==reference.ExistingId&&o.Kind==3)?.Id
                    ??throw new InvalidDataException(ATLink.Core.UiText.Get("A linked competition no longer exists."));
            return reference.DraftKey is Guid key&&ids.TryGetValue(key,out int id)?id:
                throw new InvalidDataException(ATLink.Core.UiText.Get("A linked competition draft is unavailable."));
        }
        c.Execute("Create linked competitions",()=>
        {
            FootballInternationalCalendar.Apply(c, graph.InternationalMoves);
            foreach(var node in graph.Competitions)
            {
                bool reserved=graph.Playoffs.Any(p=>p.Destination.DraftKey==node.Key&&p.DirectTeams.Count+2==node.Phases[0].Participants);
                // A linked qualifying tournament supplies the two missing clubs later in
                // this transaction. Do not require fictitious temporary direct entrants.
                var initial=reserved&&node.Teams.Count==node.Phases[0].Participants-2?Array.Empty<string>():node.Teams;
                // Entry tables can be supplied/rebuilt by linked competitions below.
                // Apply a user-defined first-group allocation only after those sources exist.
                var phases=node.Phases.Select((p,i)=>reserved&&i==0&&p.Kind!=FootballPhaseKind.Knockout?p with{Matchups=[]}:p).ToArray();
                ids.Add(node.Key,FootballCompetitionCompiler.Create(c,node.Kind,node.Region,node.Name,phases,initial,node.Rules,node.Behaviour));
                FootballCompetitionNames.Apply(c,ids[node.Key],node.Names);
                if(node.Artwork is not null)c.SetArtwork(ids[node.Key],node.Artwork);
                if(node.RegionalRegistration)c.SetRule(ids[node.Key],"nation_id",["0"]);
                if(!node.GroupStageObjective)
                {
                    var groups=FootballCompetitionAdapter.Read(c,ids[node.Key]).Phases.Where(p=>p.Kind==FootballPhaseKind.Groups).SelectMany(p=>p.NativeGroups).Select(g=>g.ToString()).ToHashSet();
                    c.Edit("objectives.txt",t=>{foreach(var row in t.Data.Rows.Cast<System.Data.DataRow>().Where(r=>groups.Contains((string)r[0])&&(string)r[1]=="REACH_GROUPS").ToArray())row.Delete();});
                }
            }
            foreach(var playoffs in graph.Playoffs)
            {
                int id=FootballLinkedCompetitions.CreateQualifiers(c,playoffs.Name,Resolve(playoffs.Destination),
                    playoffs.Excluded is null?null:Resolve(playoffs.Excluded),playoffs.DirectTeams,playoffs.Quotas,playoffs.Dates,playoffs.Rounds);
                FootballCompetitionNames.Apply(c,id,playoffs.Names);
                if(playoffs.Artwork is not null)c.SetArtwork(id,playoffs.Artwork);
            }
            var exchanges=new Dictionary<(int Upper,int Lower),FootballPromotionSource>();
            foreach(var relationship in graph.Neighbours)
            {
                int current=Resolve(relationship.Current);
                int? higher=relationship.Higher is null?null:Resolve(relationship.Higher);
                int? lower=relationship.Lower is null?null:Resolve(relationship.Lower);
                if(FootballLinkedCompetitions.IsDomesticDivision(c,current))
                    FootballLinkedCompetitions.ApplyNativeNeighbours(c,current,higher,lower);
                else
                {
                    foreach(var edge in new[]{higher.HasValue?(higher.Value,current):((int,int)?)null,
                        lower.HasValue?(current,lower.Value):((int,int)?)null}.Where(e=>e.HasValue).Select(e=>e!.Value))
                    {
                        if(exchanges.TryGetValue(edge,out var policy)&&policy!=relationship.Promotion)
                            throw new InvalidDataException(ATLink.Core.UiText.Get("Choose the same promotion rule on both sides of this relationship."));
                        exchanges[edge]=relationship.Promotion;
                    }
                }
            }
            foreach(var exchange in exchanges)
                FootballLinkedCompetitions.ConfigureDivisions(c,exchange.Key.Upper,exchange.Key.Lower,exchange.Value);
            foreach(var calendar in graph.Calendars)
                FootballLinkedCompetitions.ShareGroupDates(c,Resolve(calendar.Destination),Resolve(calendar.Source),calendar.Kickoff);
            foreach(var node in graph.Competitions.Where(n=>n.Phases[0].Kind!=FootballPhaseKind.Knockout&&n.Phases[0].Matchups.Any(m=>m.Explicit)))
            {
                int id=ids[node.Key];var native=FootballCompetitionAdapter.Read(c,id);
                var phases=node.Phases.Select((p,i)=>i==0?p:p with{Matchups=[]}).ToArray();
                var participants=graph.Playoffs.FirstOrDefault(p=>p.Destination.DraftKey==node.Key)?.DirectTeams??node.Teams;
                int pool=FootballQualifications.EntryPools(c,id).Single();
                FootballMatchups.Apply(c,id,phases,native.Phases.Select(p=>p.NativeGroups.ToArray()).ToArray(),native.Phases.Select(p=>p.NativeId!.Value).ToArray(),participants,pool,true);
            }
        });
        return ids;
    }
}
