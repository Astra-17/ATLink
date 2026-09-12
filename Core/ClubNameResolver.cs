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

public sealed class ClubNameResolver
{
    public const int FreeAgents=111592;
    public const double FuzzyThreshold=0.92;
    public const double FuzzyMargin=0.05;
    readonly NameNormalizer _normalizer;
    readonly List<Entry> _teams;
    readonly IReadOnlyDictionary<string,AliasTarget> _aliases;
    readonly Dictionary<string,string[]> _logicalAliases=new(StringComparer.Ordinal);
    readonly Dictionary<string,ClubResolution> _cache=new(StringComparer.Ordinal);
    static readonly HashSet<string> FootballTokens=new(StringComparer.Ordinal)
    {
        "fc","cf","sc","sv","sk","fk","as","ac","us","rc","afc","ssc","ss","cd","ud","ca","bc","sfc","acf",
        "club","clube","sportclub","calcio","football","futbol","fussball"
    };
    static readonly Regex ParentSuffix=new(@"\s+(U21|U23|U19|U18|B|II|2)$",RegexOptions.IgnoreCase);
    static readonly Regex SquadSuffix=new(@"\s+(u\d{1,2}|b|ii|2|castilla|futuro|women|feminin)$");

    public ClubNameResolver(IEnumerable<LiveTeam> teams,NameNormalizer? normalizer=null,IReadOnlyDictionary<string,AliasTarget>? aliases=null)
    {
        _normalizer=normalizer??new NameNormalizer();
        _aliases=aliases??new Dictionary<string,AliasTarget>();
        _teams=teams.Where(t=>t.TeamId>0&&!string.IsNullOrWhiteSpace(t.TeamName)).DistinctBy(t=>t.TeamId)
            .Select(t=>new Entry(t,NormalizeClubName(t.TeamName),NormalizeClubCoreName(t.TeamName))).ToList();
        string[][] groups=
        [
            ["PSV","PSV Eindhoven"],["PSG","Paris Saint-Germain"],
            ["OM","Olympique de Marseille"],["OL","Olympique Lyonnais"],
            ["LOSC","LOSC Lille","Lille OSC"],
            ["RB Salzburg","Red Bull Salzburg","Red Bull Salzbourg","FC Red Bull Salzburg"],
            ["Man Utd","Man United","Manchester United"],["Spurs","Tottenham","Tottenham Hotspur"],
            ["SC Fribourg","SC Freiburg","Sport-Club Freiburg"],
            ["R Charleroi SC","Sporting Charleroi"],
            ["K. Saint-Trond VV","Sint-Truidense VV"],
            ["SK Slavia Prague","SK Slavia Praha"],
            ["Glasgow Rangers","Rangers"],
            ["Club Brugge KV","Club Brugge"],
            ["Basaksehir FK","RAMS Başakşehir"],
            ["SK Beveren","Waasland-Beveren"],
            ["AZ Alkmaar","AZ"],
            ["FC Twente Enschede","FC Twente"],
            ["Aarhus GF","AGF"],
            ["FC Copenhague","F.C. København"],
            ["Real Salt Lake City","Real Salt Lake"],
            ["Celtic Glasgow","Celtic"],
            ["Dynamo Kyiv","Dynamo Kiev"],
            ["Bayern Munich","FC Bayern München"],
            ["Wolfsberger AC","RZ Pellets Wolfsberger AC"],
            ["Parma Calcio 1913","Parma"],
            ["Vålerenga Fotball Elite","Vålerenga Fotball"],
            ["CA Talleres","Club Atlético Talleres"],
            ["SV 07 Elversberg","SV Elversberg"],
            ["Athletic Bilbao","Athletic Club"],
            ["Los Angeles FC","LAFC"],
            ["Los Angeles Galaxy","LA Galaxy"],
            ["Sporting Gijón","Real Sporting"],
            ["FC Bâle 1893","FC Basel 1893"],
            ["FC Lucerne","FC Luzern"],
            ["FC Thoune","FC Thun"],
            ["SV Ried","SV Oberbank Ried"],
            ["Eyüpspor","İKAS Eyüpspor"],
            ["kas mpasa sk","Kasımpaşa SK"],
            ["Odense Boldklub","Odense BK"],
            ["Bohemian Football Club","Bohemians"],
            ["US Avellino 1912","Avellino"],
            ["CA Independiente","Club Atlético Independiente"],
            ["CA Aldosivi","Club Atlético Aldosivi"],
            ["CA Banfield","Club Atlético Banfield"],
            ["CA Lanús","Club Atlético Lanús"],
            ["Alanyaspor","Corendon Alanyaspor"],
            ["Kayserispor","Kayserispor Futbol A.Ş"],
            ["APOEL Nikosia","APOEL FC"],
            ["Legia Varsovie","Legia Warszawa"],
            ["Austria de Vienne","FK Austria Wien"],
            ["SCR Altach","SC Rheindorf Altach"],
            ["Red Bull New York","New York Red Bulls"],
            ["CS Universitatea Craiova","U Craiova 1948 Club Sportiv"]
        ];
        foreach(var group in groups)
            foreach(var name in group)
                _logicalAliases[NormalizeClubName(name)]=group;
    }

    public static ClubNameResolver FromCatalog(FootballCatalog catalog,NameNormalizer? normalizer=null)
    {
        var names=normalizer??new NameNormalizer();
        var teams=catalog.Entities("teams").Select(t=>new LiveTeam(
            int.TryParse(FootballCatalog.Value(t.Row,"teamid"),out var id)?id:0,
            FootballCatalog.Value(t.Row,"teamname"),
            FootballCatalog.Value(t.Row,"iswomensteam")!="0")).Where(t=>t.TeamId>0);
        return new ClubNameResolver(teams,names,LiveClubAliases.Load(names));
    }

    public string NormalizeClubName(string? value)=>_normalizer.NormalizeTeamName(
        value?.Replace('ı','i').Replace('ł','l').Replace('Ł','L').Replace('ø','o').Replace('Ø','O').Replace("æ","ae").Replace("Æ","AE"));

    public string NormalizeClubCoreName(string? value)
    {
        var tokens=NormalizeClubName(value).Split(' ',StringSplitOptions.RemoveEmptyEntries);
        var start=0;var end=tokens.Length;
        if(end>=2&&tokens[0]=="sporting"&&tokens[1]=="club")start=2;
        if(end-start>=2&&tokens[end-2]=="sporting"&&tokens[end-1]=="club")end-=2;
        while(start<end&&FootballTokens.Contains(tokens[start]))start++;
        while(end>start&&FootballTokens.Contains(tokens[end-1]))end--;
        return string.Join(' ',tokens[start..end]);
    }

    public ClubResolution ResolveClub(string query)
    {
        if(_cache.TryGetValue(query,out var cached))return cached;
        var result=ResolveCore(query);
        _cache[query]=result;
        return result;
    }

    ClubResolution ResolveCore(string query)
    {
        var normalized=NormalizeClubName(query);
        var core=NormalizeClubCoreName(query);
        if(_normalizer.IsRetirementName(query))return Failure("retirement");
        if(_normalizer.IsFreeAgentName(query))
            return Success(_teams.FirstOrDefault(t=>t.Team.TeamId==FreeAgents)?.Team??new LiveTeam(FreeAgents,"Agents libres",false),"free_agents");
        if(normalized.Length==0)return Failure("not_found");

        var match=Select(_teams.Where(t=>t.Normalized==normalized),"exact");
        if(match is not null)return match;

        if(_aliases.TryGetValue(normalized,out var alias))
        {
            if(alias.ExplicitId is int id&&_teams.FirstOrDefault(t=>t.Team.TeamId==id) is {} explicitTeam)
                return Success(explicitTeam.Team,"alias");
            foreach(var target in alias.Names)
            {
                match=MatchAliasTarget(target);
                if(match is not null)return match;
            }
        }
        if(_logicalAliases.TryGetValue(normalized,out var names))
        {
            var normalizedNames=names.Select(NormalizeClubName).ToHashSet(StringComparer.Ordinal);
            var coreNames=names.Select(NormalizeClubCoreName).Where(n=>n.Length>0).ToHashSet(StringComparer.Ordinal);
            match=Select(_teams.Where(t=>normalizedNames.Contains(t.Normalized)),"alias");
            match??=Select(_teams.Where(t=>coreNames.Contains(t.Core)),"alias");
            if(match is not null)return match;
        }

        if(core.Length>0)
        {
            match=Select(_teams.Where(t=>t.Core==core),"core_name_exact");
            if(match is not null)return match;
        }
        var tokens=normalized.Split(' ').ToHashSet(StringComparer.Ordinal);
        if(tokens.Count>=2)
        {
            match=Select(_teams.Where(t=>tokens.SetEquals(t.Normalized.Split(' '))),"token_set_exact");
            if(match is not null)return match;
        }

        var namedParent=normalized switch
        {
            "juventus next gen"=>"Juventus",
            "rc celta fortuna"=>"Real Club Celta de Vigo",
            _=>null
        };
        if(namedParent is not null)
        {
            var parentMatch=MatchAliasTarget(namedParent);
            return parentMatch?.Team is {} parentTeam?Success(parentTeam,"parent_team"):Failure(parentMatch?.MatchMethod??"not_found");
        }

        var parent=ParentSuffix.Replace(_normalizer.Clean(query),"");
        if(parent!=_normalizer.Clean(query))
        {
            match=Select(_teams.Where(t=>t.Normalized==NormalizeClubName(parent)),"parent_team");
            var parentCore=NormalizeClubCoreName(parent);
            if(parentCore.Length>0)match??=Select(_teams.Where(t=>t.Core==parentCore),"parent_team");
            if(match is not null)return match;
            var parentResult=ResolveCore(parent);
            if(parentResult.Team is not null&&parentResult.MatchMethod is("exact" or "first_duplicate" or "alias" or "core_name_exact" or "token_set_exact"))
                return Success(parentResult.Team,"parent_team",parentResult.Confidence);
            return Failure("not_found");
        }

        if(Regex.IsMatch(_normalizer.Clean(query),"^[A-Z]{2,5}$")&&!FootballTokens.Contains(normalized)&&normalized is not("real" or "city" or "royal"))
        {
            match=Select(_teams.Where(t=>!IsSquad(t)&&t.Normalized.StartsWith(normalized+" ",StringComparison.Ordinal)),"acronym_prefix_unique");
            if(match is not null)return match;
        }
        var coreTokens=normalized.Split(' ',StringSplitOptions.RemoveEmptyEntries).Where(t=>!FootballTokens.Contains(t)).ToHashSet(StringComparer.Ordinal);
        if(coreTokens.Count>=2)
        {
            match=Select(_teams.Where(t=>!IsSquad(t)&&coreTokens.IsSubsetOf(t.Normalized.Split(' ').Where(token=>!FootballTokens.Contains(token)))),"token_subset");
            if(match is not null)return match;
        }

        if(core.Length>=6&&!FootballTokens.Contains(normalized))
        {
            var scores=_teams.Where(t=>!IsSquad(t)).GroupBy(t=>t.Normalized)
                .Select(g=>new{Entries=g,Score=Similarity(normalized,g.Key)}).OrderByDescending(x=>x.Score).ToList();
            if(scores.Count>0&&scores[0].Score>FuzzyThreshold)
            {
                var second=scores.Count>1?scores[1].Score:0;
                if(scores[0].Score-second<FuzzyMargin)return Failure("ambiguous");
                return Select(scores[0].Entries,"fuzzy",scores[0].Score)!;
            }
        }
        return Failure("not_found");
    }

    ClubResolution? MatchAliasTarget(string target)
    {
        var normalized=NormalizeClubName(target);
        var exact=Select(_teams.Where(t=>t.Normalized==normalized),"alias");
        if(exact is not null)return exact;
        var core=NormalizeClubCoreName(target);
        return core.Length==0?null:Select(_teams.Where(t=>t.Core==core),"alias");
    }

    ClubResolution? Select(IEnumerable<Entry> candidates,string method,double score=1)
    {
        var matches=candidates.ToList();
        if(matches.Count==0)return null;
        if(matches.Count==1)return Success(matches[0].Team,method,score);
        if(matches.Select(t=>t.Normalized).Distinct().Count()!=1)return Failure("ambiguous");
        var men=matches.Where(t=>!t.Team.IsWomens).ToList();
        if(men.Count==1)return Success(men[0].Team,method=="exact"?"first_duplicate":method,score);
        var pool=men.Count>0?men:matches;
        var nonSix=pool.Where(t=>t.Team.TeamId<100000||t.Team.TeamId>999999).ToList();
        var selected=nonSix.Count==1?nonSix[0]:nonSix.Count==0?pool[0]:null;
        if(selected is null)return Failure("ambiguous");
        return Success(selected.Team,method=="exact"?"first_duplicate":method,score);
    }

    double Similarity(string a,string b)=>_normalizer.Similarity(a,b);
    static bool IsSquad(Entry entry)=>SquadSuffix.IsMatch(entry.Normalized);
    static ClubResolution Success(LiveTeam team,string method,double score=1)=>new(){Team=team,MatchMethod=method,Confidence=score};
    static ClubResolution Failure(string status)=>new(){MatchMethod=status};
    sealed record Entry(LiveTeam Team,string Normalized,string Core);
}
