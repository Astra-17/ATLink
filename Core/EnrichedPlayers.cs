using System.Globalization;
using System.Text.Json;

namespace ATLink.Core;

public sealed record EnrichedPlayerInfo(string PlayerId,string TransfermarktName,string TransfermarktClub,string ShirtNumber,string CurrentTeamId,string CurrentTeamName);

public sealed class EnrichedPlayers
{
    static readonly NameNormalizer names=new();
    readonly ILookup<string,EnrichedPlayerInfo> byName;
    readonly Dictionary<string,EnrichedPlayerInfo> byId=new(StringComparer.Ordinal);
    public int Count {get;}
    EnrichedPlayers(IReadOnlyList<EnrichedPlayerInfo> players)
    {
        Count=players.Count;
        byName=players.ToLookup(p=>names.NormalizePersonName(p.TransfermarktName),StringComparer.Ordinal);
        foreach(var player in players)byId[player.PlayerId]=player;
    }

    static EnrichedPlayers? cached;
    static string? cachedPath;
    static long cachedLength;
    static DateTime cachedWrite;
    public static EnrichedPlayers FromPlayers(IReadOnlyList<EnrichedPlayerInfo> players)=>new(players);
    public static EnrichedPlayers Load(string? path=null)
    {
        string? file=path;
        if(string.IsNullOrWhiteSpace(file)||!File.Exists(file))
            file=Path.Combine(AppContext.BaseDirectory,"Data","players_enrich.json");
        if(!File.Exists(file))
            file=Path.Combine(Directory.GetCurrentDirectory(),"files","players_enrich.json");
        if(!File.Exists(file))throw new FileNotFoundException("players_enrich.json is required to match Transfermarkt names.");
        var stamp=new FileInfo(file);
        if(cached is not null&&cachedPath==file&&cachedLength==stamp.Length&&cachedWrite==stamp.LastWriteTimeUtc)return cached;
        using var stream=File.OpenRead(file);
        using var document=JsonDocument.Parse(stream);
        if(!document.RootElement.TryGetProperty("players",out var array)||array.ValueKind!=JsonValueKind.Array)
            throw new InvalidDataException("players_enrich.json has no players array.");
        var players=new List<EnrichedPlayerInfo>();
        foreach(var player in array.EnumerateArray())
        {
            string id=ReadId(player,"playerid");
            string name=ReadString(player,"transfermarkt_name");
            if(id.Length==0||name.Length==0)continue;
            string shirt=ReadNumber(player,"shirt_number");
            if(shirt.Length==0)shirt=ReadNumber(player,"transfermarkt_shirt_number");
            players.Add(new EnrichedPlayerInfo(id,name,ReadString(player,"transfermarkt_club"),shirt,ReadId(player,"current_teamid"),ReadString(player,"current_teamname")));
        }
        cached=new EnrichedPlayers(players);cachedPath=file;cachedLength=stamp.Length;cachedWrite=stamp.LastWriteTimeUtc;return cached;
    }

    public IReadOnlyList<EnrichedPlayerInfo> FindByPersonName(string transfermarktName)
    {
        string key=names.NormalizePersonName(transfermarktName);
        return key.Length==0?[]:byName[key].ToArray();
    }

    public EnrichedPlayerInfo? Match(string transfermarktName,string fromClub)
    {
        var (player,_,_)=TryMatch(transfermarktName,fromClub);
        return player;
    }

    public (EnrichedPlayerInfo? Player,string Reason,string Details) TryMatch(string transfermarktName,string fromClub)
    {
        if(names.NormalizePersonName(transfermarktName).Length==0)return (null,"invalid_transfer_data","Nom du joueur manquant.");
        var matches=FindByPersonName(transfermarktName);
        if(matches.Count==1)return (matches[0],"transfermarkt_exact","");
        if(matches.Count==0)return (null,"player_not_found","Nom absent des correspondances Transfermarkt de players_enrich.json.");
        string club=names.NormalizeTeamName(fromClub);
        var byClub=club.Length==0?[]:matches.Where(p=>names.NormalizeTeamName(p.TransfermarktClub)==club).ToArray();
        if(byClub.Length==1)return (byClub[0],"transfermarkt_club_disambiguation","");
        return (null,"ambiguous_player",$"{matches.Count} joueurs partagent ce nom Transfermarkt.");
    }
    public EnrichedPlayerInfo? ById(string playerId)=>byId.GetValueOrDefault(playerId);

    static string ReadId(JsonElement player,string name)
    {
        if(!player.TryGetProperty(name,out var value))return "";
        return value.ValueKind==JsonValueKind.Number?value.GetInt64().ToString(CultureInfo.InvariantCulture):value.GetString()?.Trim()??"";
    }
    static string ReadString(JsonElement player,string name)=>player.TryGetProperty(name,out var value)?value.GetString()?.Trim()??"":"";
    static string ReadNumber(JsonElement player,string name)
    {
        if(!player.TryGetProperty(name,out var value)||value.ValueKind==JsonValueKind.Null)return "";
        if(value.ValueKind==JsonValueKind.Number)return value.GetInt32().ToString(CultureInfo.InvariantCulture);
        return value.GetString()?.Trim()??"";
    }
}
