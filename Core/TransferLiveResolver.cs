namespace ATLink.Core;

public static class UnresolvedReasons
{
    public const string PlayerNotFound="player_not_found";
    public const string AmbiguousPlayer="ambiguous_player";
    public const string DestinationTeamNotFound="destination_team_not_found";
    public const string AmbiguousDestinationTeam="ambiguous_destination_team";
    public const string InvalidTransferData="invalid_transfer_data";
    public const string Retirement="retirement";
}

public sealed record LiveResolvedTransfer(int Sequence,string PlayerName,string PlayerId,string FromClub,string ToClub,string ToTeamId,string ToTeamName,string ShirtNumber,string PlayerMatchMethod,string TeamMatchMethod);
public sealed record LiveUnresolvedTransfer(int Sequence,string PlayerName,string FromClub,string ToClub,string Reason,string Details);
public sealed record LiveResolveResult(IReadOnlyList<LiveResolvedTransfer> Resolved,IReadOnlyList<LiveUnresolvedTransfer> Unresolved);

public sealed class TransferLiveResolver
{
    readonly EnrichedPlayers enrich;
    readonly NameNormalizer normalizer;
    readonly ClubNameResolver clubs;

    public TransferLiveResolver(FootballCatalog catalog,EnrichedPlayers enrich,NameNormalizer? normalizer=null)
        :this(enrich,ClubNameResolver.FromCatalog(catalog,normalizer??new NameNormalizer()),normalizer){}

    public TransferLiveResolver(EnrichedPlayers enrich,ClubNameResolver clubs,NameNormalizer? normalizer=null)
    {
        this.enrich=enrich;
        this.clubs=clubs;
        this.normalizer=normalizer??new NameNormalizer();
    }

    public LiveResolveResult Resolve(IEnumerable<MarketTransfer> transfers)
    {
        var resolved=new List<LiveResolvedTransfer>();
        var unresolved=new List<LiveUnresolvedTransfer>();
        var sequence=0;
        foreach(var transfer in transfers)
        {
            sequence++;
            var numbered=transfer.Sequence>0?transfer:transfer with{Sequence=sequence};
            var result=ResolveOne(numbered);
            if(result.Resolved is not null)resolved.Add(result.Resolved);
            else if(result.Unresolved is not null)unresolved.Add(result.Unresolved);
        }
        return new LiveResolveResult(resolved.OrderBy(row=>row.Sequence).ToArray(),unresolved.OrderBy(row=>row.Sequence).ToArray());
    }

    public (LiveResolvedTransfer? Resolved,LiveUnresolvedTransfer? Unresolved) ResolveOne(MarketTransfer transfer)
    {
        var playerName=normalizer.Clean(transfer.Player);
        var fromClub=normalizer.Clean(transfer.OldClub);
        var toClub=normalizer.Clean(transfer.NewClub);
        var sequence=transfer.Sequence;

        if(playerName.Length==0)
            return (null,new LiveUnresolvedTransfer(sequence,playerName,fromClub,toClub,UnresolvedReasons.InvalidTransferData,"Nom du joueur manquant"));
        if(normalizer.IsRetirementName(toClub))
            return (null,new LiveUnresolvedTransfer(sequence,playerName,fromClub,toClub,UnresolvedReasons.Retirement,"Fin de carrière"));
        if(toClub.Length==0)
            return (null,new LiveUnresolvedTransfer(sequence,playerName,fromClub,toClub,UnresolvedReasons.InvalidTransferData,"Club de destination manquant"));

        var player=ResolvePlayer(playerName,fromClub,out var playerMethod,out var playerDetails);
        if(player is null)
            return (null,new LiveUnresolvedTransfer(sequence,playerName,fromClub,toClub,playerMethod,playerDetails));

        var clubResult=clubs.ResolveClub(toClub);
        if(clubResult.Team is null)
        {
            var reason=clubResult.MatchMethod=="ambiguous"?UnresolvedReasons.AmbiguousDestinationTeam
                :clubResult.MatchMethod==UnresolvedReasons.Retirement?UnresolvedReasons.Retirement
                :UnresolvedReasons.DestinationTeamNotFound;
            var details=reason==UnresolvedReasons.Retirement?"Fin de carrière"
                :reason==UnresolvedReasons.AmbiguousDestinationTeam?"Plusieurs clubs correspondent à ce nom."
                :"Club de destination introuvable dans la DB.";
            return (null,new LiveUnresolvedTransfer(sequence,playerName,fromClub,toClub,reason,details));
        }

        return (new LiveResolvedTransfer(sequence,playerName,player.PlayerId,fromClub,toClub,clubResult.Team.TeamId.ToString(),clubResult.Team.TeamName,player.ShirtNumber,playerMethod,clubResult.MatchMethod),null);
    }

    public EnrichedPlayerInfo? ResolvePlayer(string playerName,string fromClub,out string reason,out string details)
    {
        reason=UnresolvedReasons.PlayerNotFound;
        details=string.Empty;
        var normalized=normalizer.NormalizePersonName(playerName);
        if(normalized.Length==0)
        {
            reason=UnresolvedReasons.InvalidTransferData;
            details="Nom du joueur vide";
            return null;
        }

        var matches=enrich.FindByPersonName(playerName);
        if(matches.Count==1)
        {
            reason="transfermarkt_exact";
            return matches[0];
        }
        if(matches.Count>1)
        {
            var source=normalizer.NormalizeTeamName(fromClub);
            var byClub=source.Length==0?[]:matches.Where(p=>normalizer.NormalizeTeamName(p.TransfermarktClub)==source).ToArray();
            var chosen=byClub.Length==1?byClub[0]:DisambiguatePlayers(matches,fromClub);
            if(chosen is not null)
            {
                reason="transfermarkt_club_disambiguation";
                return chosen;
            }
            reason=UnresolvedReasons.AmbiguousPlayer;
            details=$"{matches.Count} joueurs partagent ce nom Transfermarkt.";
            return null;
        }

        details="Nom absent des correspondances Transfermarkt de players_enrich.json.";
        return null;
    }

    EnrichedPlayerInfo? DisambiguatePlayers(IReadOnlyList<EnrichedPlayerInfo> candidates,string fromClub)
    {
        if(string.IsNullOrWhiteSpace(fromClub)||candidates.Count==0)return null;
        var fromNorm=normalizer.NormalizeTeamName(fromClub);
        var fromKey=normalizer.TeamKey(fromClub);

        var exact=candidates.Where(p=>normalizer.NormalizeTeamName(p.CurrentTeamName)==fromNorm).ToList();
        if(exact.Count==1)return exact[0];

        if(fromKey.Length>0)
        {
            var keyMatches=candidates.Where(p=>normalizer.TeamKey(p.CurrentTeamName)==fromKey).ToList();
            if(keyMatches.Count==1)return keyMatches[0];
        }

        var sourceTeam=clubs.ResolveClub(fromClub).Team;
        if(sourceTeam is not null)
        {
            var teamId=sourceTeam.TeamId.ToString();
            var byId=candidates.Where(p=>p.CurrentTeamId==teamId).ToList();
            if(byId.Count==1)return byId[0];
        }
        return null;
    }
}
