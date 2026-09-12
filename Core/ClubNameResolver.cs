using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ATLink.Core;

public sealed record LiveTeam(int TeamId,string TeamName,bool IsWomens);
public sealed class AliasTarget
{
    public int? ExplicitId{get;init;}
    public IReadOnlyList<string> Names{get;init;}=[];
}
public sealed class ClubResolution
{
    public LiveTeam? Team{get;init;}
    public int? TeamId=>Team?.TeamId;
    public string? TeamName=>Team?.TeamName;
    public string MatchMethod{get;init;}="not_found";
    public double Confidence{get;init;}
    public ClubDiagnostic? Diagnostic {get;init;}
}
public static class LiveClubAliases
{
    public static IReadOnlyDictionary<string,AliasTarget> Load(NameNormalizer normalizer)
    {
        using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("ATLink.Core.Resources.LiveClubAliases.json");
        if(stream is null)return new Dictionary<string,AliasTarget>();
        using var document=JsonDocument.Parse(stream);
        var map=new Dictionary<string,AliasTarget>(StringComparer.Ordinal);
        if(!document.RootElement.TryGetProperty("clubs",out var clubs)||clubs.ValueKind!=JsonValueKind.Object)return map;
        foreach(var property in clubs.EnumerateObject())
        {
            var key=normalizer.NormalizeTeamName(property.Name);
            if(key.Length==0)continue;
            var alias=Parse(property.Value,normalizer);
            if(alias is not null)map[key]=alias;
        }
        return map;
    }

    static AliasTarget? Parse(JsonElement value,NameNormalizer normalizer)
    {
        switch(value.ValueKind)
        {
            case JsonValueKind.Number when value.TryGetInt32(out var id):return new AliasTarget{ExplicitId=id};
            case JsonValueKind.String:return new AliasTarget{Names=[normalizer.Clean(value.GetString())]};
            case JsonValueKind.Array:
                return new AliasTarget{Names=value.EnumerateArray().Where(x=>x.ValueKind==JsonValueKind.String).Select(x=>normalizer.Clean(x.GetString())).Where(x=>x.Length>0).ToArray()};
            case JsonValueKind.Object:
                int? explicitId=null;var names=new List<string>();
                if(value.TryGetProperty("teamId",out var teamId)&&teamId.TryGetInt32(out var tid))explicitId=tid;
                if(value.TryGetProperty("names",out var namesEl)&&namesEl.ValueKind==JsonValueKind.Array)
                    names.AddRange(namesEl.EnumerateArray().Where(x=>x.ValueKind==JsonValueKind.String).Select(x=>normalizer.Clean(x.GetString())).Where(x=>x.Length>0));
                if(value.TryGetProperty("name",out var nameEl)&&nameEl.ValueKind==JsonValueKind.String)names.Add(normalizer.Clean(nameEl.GetString()));
                return new AliasTarget{ExplicitId=explicitId,Names=names};
            default:return null;
        }
    }
}

/// <summary>Club identity resolution. Input order is retained for identical database names.</summary>
public sealed class ClubNameResolver
{
    public const int FreeAgents = 111592;
    ClubNameResolver? referenceNames;
    Dictionary<int,LiveTeam> nativeTeams = [];
    public static ClubNameResolver FromCatalog(FootballCatalog catalog, NameNormalizer? normalizer = null)
    {
        var names = normalizer ?? new NameNormalizer();
        // Entities sorts by ID for UI purposes. Resolution must retain physical DB order.
        var teams = catalog.Rows("teams").Select(row => new LiveTeam(
            int.TryParse(FootballCatalog.Value(row,"teamid"), out var id) ? id : 0,
            FootballCatalog.Value(row,"teamname"), FootballCatalog.Value(row,"iswomensteam") == "1"));
        var native=teams.ToArray();
        var aliases=LiveClubAliases.Load(names);
        var resolver=new ClubNameResolver(native,names,aliases);
        resolver.nativeTeams=native.DistinctBy(t=>t.TeamId).ToDictionary(t=>t.TeamId);
        using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("ATLink.Core.Resources.TTLiveTeamNames.json");
        if(stream is not null)
        {
            using var document=JsonDocument.Parse(stream);
            var byId=document.RootElement.GetProperty("teams").EnumerateArray()
                .ToDictionary(t=>t.GetProperty("teamId").GetInt32(),t=>t.GetProperty("teamName").GetString()!);
            // Localized FC26 names are alternate labels for existing IDs, never new teams.
            var reference=native.Where(t=>byId.ContainsKey(t.TeamId))
                .Select(t=>new LiveTeam(t.TeamId,byId[t.TeamId],t.IsWomens));
            resolver.referenceNames=new ClubNameResolver(reference,names,aliases);
        }
        return resolver;
    }

    public const double FuzzyThreshold = 0.92;
    public const double FuzzyMargin = 0.05;
    private readonly NameNormalizer _normalizer;
    private readonly List<Entry> _teams;
    private readonly IReadOnlyDictionary<string, AliasTarget> _aliases;
    private readonly Dictionary<string, string[]> _logicalAliases = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ClubResolution> _cache = new(StringComparer.Ordinal);
    private static readonly HashSet<string> FootballTokens = new(StringComparer.Ordinal)
    {
        "fc", "cf", "sc", "sv", "sk", "fk", "as", "ac", "us", "rc", "afc", "ssc", "ss",
        "cd", "ud", "ca", "bc", "sfc", "acf", "club", "clube", "sportclub", "calcio", "football", "futbol", "fussball"
    };
    private static readonly Regex ParentSuffix = new(@"\s+(U21|U23|U19|U18|B|II|2)$", RegexOptions.IgnoreCase);
    private static readonly Regex SquadSuffix = new(@"\s+(u\d{1,2}|b|ii|2|castilla|futuro|women|feminin)$");

    public ClubNameResolver(IEnumerable<LiveTeam> teams, NameNormalizer? normalizer = null,
        IReadOnlyDictionary<string, AliasTarget>? aliases = null)
    {
        _normalizer = normalizer ?? new NameNormalizer();
        _aliases = aliases ?? new Dictionary<string, AliasTarget>();
        _teams = teams.Where(t => t.TeamId > 0 && !string.IsNullOrWhiteSpace(t.TeamName))
            .DistinctBy(t => t.TeamId).Select(t => new Entry(t, NormalizeClubName(t.TeamName), NormalizeClubCoreName(t.TeamName))).ToList();
        // Small semantic groups, deliberately bidirectional; not a generated acronym dictionary.
        string[][] groups = [
            ["PSV", "PSV Eindhoven"], ["PSG", "Paris Saint-Germain"],
            ["OM", "Olympique de Marseille"], ["OL", "Olympique Lyonnais"],
            ["LOSC", "LOSC Lille", "Lille OSC"],
            ["RB Salzburg", "Red Bull Salzburg", "Red Bull Salzbourg", "FC Red Bull Salzburg"],
            ["Man Utd", "Man United", "Manchester United"], ["Spurs", "Tottenham", "Tottenham Hotspur"],
            ["SC Fribourg", "SC Freiburg", "Sport-Club Freiburg"],
            ["R Charleroi SC", "Sporting Charleroi"],
            ["K. Saint-Trond VV", "Sint-Truidense VV"],
            ["SK Slavia Prague", "SK Slavia Praha"],
            ["Glasgow Rangers", "Rangers"],
            ["Club Brugge KV", "Club Brugge"],
            ["Basaksehir FK", "RAMS Başakşehir"],
            ["SK Beveren", "Waasland-Beveren"],
            ["AZ Alkmaar", "AZ"],
            ["FC Twente Enschede", "FC Twente"],
            ["Aarhus GF", "AGF"],
            ["FC Copenhague", "F.C. København"],
            ["Real Salt Lake City", "Real Salt Lake"],
            ["Celtic Glasgow", "Celtic"],
            ["Dynamo Kyiv", "Dynamo Kiev"],
            ["Bayern Munich", "FC Bayern München"],
            ["Wolfsberger AC", "RZ Pellets Wolfsberger AC"],
            ["Parma Calcio 1913", "Parma"],
            ["Vålerenga Fotball Elite", "Vålerenga Fotball"],
            ["CA Talleres", "Club Atlético Talleres"],
            ["SV 07 Elversberg", "SV Elversberg"],
            ["Athletic Bilbao", "Athletic Club"],
            ["Los Angeles FC", "LAFC"],
            ["Los Angeles Galaxy", "LA Galaxy"],
            ["Sporting Gijón", "Real Sporting"],
            ["FC Bâle 1893", "FC Basel 1893"],
            ["FC Lucerne", "FC Luzern"],
            ["FC Thoune", "FC Thun"],
            ["SV Ried", "SV Oberbank Ried"],
            ["Eyüpspor", "İKAS Eyüpspor"],
            // Historical V9 normalization replaced the dotless i with a space.
            ["kas mpasa sk", "Kasımpaşa SK"],
            ["Odense Boldklub", "Odense BK"],
            ["Bohemian Football Club", "Bohemians"],
            ["US Avellino 1912", "Avellino"],
            ["CA Independiente", "Club Atlético Independiente"],
            ["CA Aldosivi", "Club Atlético Aldosivi"],
            ["CA Banfield", "Club Atlético Banfield"],
            ["CA Lanús", "Club Atlético Lanús"],
            ["Alanyaspor", "Corendon Alanyaspor"],
            ["Kayserispor", "Kayserispor Futbol A.Ş"],
            ["APOEL Nikosia", "APOEL FC"],
            ["Legia Varsovie", "Legia Warszawa"],
            ["Austria de Vienne", "FK Austria Wien"],
            ["SCR Altach", "SC Rheindorf Altach"],
            ["Red Bull New York", "New York Red Bulls"],
            ["CS Universitatea Craiova", "U Craiova 1948 Club Sportiv"]
        ];
        foreach (var group in groups)
            foreach (var name in group)
                _logicalAliases[NormalizeClubName(name)] = group;
    }

    public string NormalizeClubName(string? value) => _normalizer.NormalizeTeamName(
        value?.Replace('ı', 'i').Replace('ł', 'l').Replace('Ł', 'L').Replace('ø', 'o').Replace('Ø', 'O').Replace("æ", "ae").Replace("Æ", "AE"));

    public string NormalizeClubCoreName(string? value)
    {
        var tokens = NormalizeClubName(value).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var start = 0;
        var end = tokens.Length;
        // Sporting is only generic in the explicit phrase "Sporting Club".
        if (end >= 2 && tokens[0] == "sporting" && tokens[1] == "club") start = 2;
        if (end - start >= 2 && tokens[end - 2] == "sporting" && tokens[end - 1] == "club") end -= 2;
        while (start < end && FootballTokens.Contains(tokens[start])) start++;
        while (end > start && FootballTokens.Contains(tokens[end - 1])) end--;
        return string.Join(' ', tokens[start..end]);
    }

    public ClubResolution ResolveClub(string query)
    {
        if (_cache.TryGetValue(query, out var cached)) return cached;
        var result = ResolveCore(query);
        _cache[query] = result;
        return result;
    }

    public bool RefersToTeam(string query,int teamId,string teamName)
    {
        if(string.IsNullOrWhiteSpace(query)||teamId<=0)return false;
        var resolved=ResolveClub(query);
        if(resolved.Team?.TeamId==teamId)return true;
        var qn=NormalizeClubName(query);var tn=NormalizeClubName(teamName);
        if(qn.Length>0&&qn==tn)return true;
        var qk=_normalizer.TeamKey(query);var tk=_normalizer.TeamKey(teamName);
        if(qk.Length>0&&qk==tk)return true;
        var qc=NormalizeClubCoreName(query);var tc=NormalizeClubCoreName(teamName);
        if(qc.Length>0&&qc==tc)return true;
        if(_logicalAliases.TryGetValue(qn,out var group)&&group.Select(NormalizeClubName).Contains(tn))return true;
        if(_aliases.TryGetValue(qn,out var fromAlias)&&(fromAlias.ExplicitId==teamId||fromAlias.Names.Select(NormalizeClubName).Contains(tn)))return true;
        if(_aliases.TryGetValue(tn,out var dbAlias)&&(dbAlias.ExplicitId==teamId||dbAlias.Names.Select(NormalizeClubName).Contains(qn)))return true;
        return false;
    }

    private ClubResolution ResolveCore(string query)
    {
        var normalized = NormalizeClubName(query);
        var core = NormalizeClubCoreName(query);
        if (_normalizer.IsRetirementName(query)) return Failure(query, "retirement");
        if (_normalizer.IsFreeAgentName(query))
            return Success(_teams.FirstOrDefault(t => t.Team.TeamId == FreeAgents)?.Team ??
                new LiveTeam(FreeAgents, "Agents libres", false), "free_agents");
        if (normalized.Length == 0) return Failure(query, "not_found");

        var match = Select(_teams.Where(t => t.Normalized == normalized), query, "exact");
        if (match is not null) return match;
        if(referenceNames is not null)
        {
            var known=referenceNames.ResolveClub(query);
            if(known.Team is not null && known.MatchMethod is not ("fuzzy" or "not_found" or "ambiguous") &&
                nativeTeams.TryGetValue(known.Team.TeamId,out var native))
                return Success(native,known.MatchMethod,known.Confidence);
        }

        // Preserve the ordered licence/mod aliases already supplied by the application.
        if (_aliases.TryGetValue(normalized, out var alias))
        {
            if (alias.ExplicitId is int id && _teams.FirstOrDefault(t => t.Team.TeamId == id) is { } explicitTeam)
                return Success(explicitTeam.Team, "alias");
            foreach (var target in alias.Names)
            {
                match = MatchAliasTarget(target, query);
                if (match is not null) return match;
            }
        }
        if (_logicalAliases.TryGetValue(normalized, out var names))
        {
            var normalizedNames = names.Select(NormalizeClubName).ToHashSet(StringComparer.Ordinal);
            var coreNames = names.Select(NormalizeClubCoreName).Where(n => n.Length > 0).ToHashSet(StringComparer.Ordinal);
            match = Select(_teams.Where(t => normalizedNames.Contains(t.Normalized)), query, "alias");
            match ??= Select(_teams.Where(t => coreNames.Contains(t.Core)), query, "alias");
            if (match is not null) return match;
        }

        if (core.Length > 0)
        {
            match = Select(_teams.Where(t => t.Core == core), query, "core_name_exact");
            if (match is not null) return match;
        }
        var tokens = normalized.Split(' ').ToHashSet(StringComparer.Ordinal);
        if (tokens.Count >= 2)
        {
            match = Select(_teams.Where(t => tokens.SetEquals(t.Normalized.Split(' '))), query, "token_set_exact");
            if (match is not null) return match;
        }

        // Prefer an actual reserve entry above; otherwise use the existing parent fallback.
        var namedParent = normalized switch
        {
            "juventus next gen" => "Juventus",
            "rc celta fortuna" => "Real Club Celta de Vigo",
            _ => null
        };
        if (namedParent is not null)
        {
            var parentMatch = MatchAliasTarget(namedParent, query);
            return parentMatch?.Team is { } parentTeam
                ? Success(parentTeam, "parent_team")
                : Failure(query, parentMatch?.MatchMethod ?? "not_found");
        }

        // Keep the established reserve/youth -> first-team fallback ahead of approximations.
        var parent = ParentSuffix.Replace(_normalizer.Clean(query), "");
        if (parent != _normalizer.Clean(query))
        {
            match = Select(_teams.Where(t => t.Normalized == NormalizeClubName(parent)), query, "parent_team");
            var parentCore = NormalizeClubCoreName(parent);
            if (parentCore.Length > 0)
                match ??= Select(_teams.Where(t => t.Core == parentCore), query, "parent_team");
            if (match is not null) return match;
            // Reuse explicit identities for the parent, without accepting a fuzzy parent.
            var parentResult = ResolveCore(parent);
            if (parentResult.Team is not null && parentResult.MatchMethod is
                ("exact" or "first_duplicate" or "alias" or "core_name_exact" or "token_set_exact"))
                return Success(parentResult.Team, "parent_team", parentResult.Confidence);
            return Failure(query, "not_found");
        }

        if (Regex.IsMatch(_normalizer.Clean(query), "^[A-Z]{2,5}$") && !FootballTokens.Contains(normalized)
            && normalized is not ("real" or "city" or "royal"))
        {
            // The complete first token must match; never derive acronyms from initials.
            match = Select(_teams.Where(t => !IsSquad(t) && t.Normalized.StartsWith(normalized + " ", StringComparison.Ordinal)), query, "acronym_prefix_unique");
            if (match is not null) return match;
        }
        var coreTokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => !FootballTokens.Contains(t)).ToHashSet(StringComparer.Ordinal);
        if (coreTokens.Count >= 2)
        {
            match = Select(_teams.Where(t => !IsSquad(t) && coreTokens.IsSubsetOf(t.Normalized.Split(' ').Where(token => !FootballTokens.Contains(token)))), query, "token_subset");
            if (match is not null) return match;
        }

        if (core.Length >= 6 && !FootballTokens.Contains(normalized))
        {
            // Scores are compared across distinct names, not duplicate male/female records.
            var scores = _teams.Where(t => !IsSquad(t)).GroupBy(t => t.Normalized)
                .Select(g => new { Entries = g, Score = Similarity(normalized, g.Key) })
                .OrderByDescending(x => x.Score).ToList();
            if (scores.Count > 0 && scores[0].Score > FuzzyThreshold)
            {
                var second = scores.Count > 1 ? scores[1].Score : 0;
                if (scores[0].Score - second < FuzzyMargin) return Failure(query, "ambiguous");
                return Select(scores[0].Entries, query, "fuzzy", scores[0].Score)!;
            }
        }
        return Failure(query, "not_found");
    }

    private ClubResolution? MatchAliasTarget(string target, string query)
    {
        var normalized = NormalizeClubName(target);
        var exact = Select(_teams.Where(t => t.Normalized == normalized), query, "alias");
        if (exact is not null) return exact;
        var core = NormalizeClubCoreName(target);
        return core.Length == 0 ? null : Select(_teams.Where(t => t.Core == core), query, "alias");
    }

    private ClubResolution? Select(IEnumerable<Entry> candidates, string query, string method, double score = 1)
    {
        var matches = candidates.ToList();
        if (matches.Count == 0) return null;
        if (matches.Count == 1) return Success(matches[0].Team, method, score);
        // Different database names collapsing to a core/subset are a real ambiguity.
        if (matches.Select(t => t.Normalized).Distinct().Count() != 1) return Failure(query, "ambiguous");
        var nonSix = matches.Where(t => t.Team.TeamId < 100000 || t.Team.TeamId > 999999).ToList();
        var selected = nonSix.Count == 1 ? nonSix[0] : nonSix.Count == 0 ? matches[0] : null;
        if (selected is null) return Failure(query, "ambiguous");
        return Success(selected.Team, method == "exact" ? "first_duplicate" : method, score);
    }

    private double Similarity(string a, string b) => _normalizer.Similarity(a, b);
    private static bool IsSquad(Entry entry) => SquadSuffix.IsMatch(entry.Normalized);
    private static ClubResolution Success(LiveTeam team, string method, double score = 1) =>
        new() { Team = team, MatchMethod = method, Confidence = score };

    private ClubResolution Failure(string query, string status) => new()
    {
        MatchMethod = status,
        Diagnostic = new ClubDiagnostic
        {
            Query = query, Normalized = NormalizeClubName(query), CoreNormalized = NormalizeClubCoreName(query),
            BestCandidates = _teams.Select(t => new ClubCandidate
            {
                TeamId = t.Team.TeamId, TeamName = t.Team.TeamName,
                Method = t.Normalized == NormalizeClubName(query) ? "exact" :
                    t.Core.Length > 0 && t.Core == NormalizeClubCoreName(query) ? "core_name_exact" : "fuzzy",
                Score = Similarity(NormalizeClubName(query), t.Normalized)
            }).OrderByDescending(t => t.Score).Take(5).ToArray()
        }
    };
    private sealed record Entry(LiveTeam Team, string Normalized, string Core);
}

public sealed class ClubDiagnostic
{
    public string Query { get; init; } = "";
    public string Normalized { get; init; } = "";
    public string CoreNormalized { get; init; } = "";
    public IReadOnlyList<ClubCandidate> BestCandidates { get; init; } = [];
}
public sealed class ClubCandidate
{
    public int TeamId { get; init; }
    public string TeamName { get; init; } = "";
    public string Method { get; init; } = "";
    public double Score { get; init; }
}
