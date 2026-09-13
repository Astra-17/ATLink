using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
namespace ATLink.Core;
public sealed record MarketTransfer(int Sequence,string Player,string OldClub,string NewClub,string Phase,bool IsLoan=false,bool IsLoanToBuy=false,DateOnly? LoanEndDate=null,string LoanError="");
public sealed class ResolvedTransfer
{
    public int Sequence {get;init;}
    public string Player {get;init;}="";
    public string OldClub {get;init;}="";
    public string NewClub {get;init;}="";
    public string PlayerId {get;set;}="";
    public string TeamId {get;set;}="";
    public string Match {get;init;}="";
}
public static class TransfermarktScraper
{
    private static string Clean(string value)=>Regex.Replace(HtmlEntity.DeEntitize(value??""),@"\s+"," ").Trim();
    private static IEnumerable<HtmlNode> Nodes(HtmlNode node,string xpath)=>node.SelectNodes(xpath)?.Cast<HtmlNode>()??[];
    public static async Task<IReadOnlyList<MarketTransfer>> Fetch(string url,CancellationToken token=default)
    {
        if(!Uri.TryCreate(url,UriKind.Absolute,out var uri)||uri.Scheme!="https"||!TransfermarktHttp.IsProfile(uri))throw new InvalidDataException("URL HTTPS Transfermarkt attendue.");
        using var client=TransfermarktHttp.Create();
        using var response=await client.GetAsync(uri,token);
        if(!response.IsSuccessStatusCode)throw new HttpRequestException($"Transfermarkt: HTTP {(int)response.StatusCode}. Vous pouvez charger un fichier HTML enregistré ou le CSV du scraper existant.");
        return ParseHtml(await response.Content.ReadAsStringAsync(token));
    }
    public static IReadOnlyList<MarketTransfer> ParseHtml(string html)
    {
        var document=new HtmlDocument();document.LoadHtml(html);
        var groups=new Dictionary<string,List<(int Phase,string Player,string Other)>>();
        foreach(var table in Nodes(document.DocumentNode,"//table"))
        {
            var headers=Nodes(table,".//thead//th").Select(h=>Clean(h.InnerText).ToLowerInvariant()).ToArray();
            int from=Array.FindIndex(headers,h=>h.Contains("venant de")||h.Contains("left"));int to=Array.FindIndex(headers,h=>h.Contains("allant à")||h.Contains("allant a")||h.Contains("joined"));
            if(from<0&&to<0)continue;int phase=from>=0?1:2,index=from>=0?from:to;
            var box=table.Ancestors("div").FirstOrDefault(n=>n.GetAttributeValue("class","").Split(' ').Contains("box"));
            var heading=box?.SelectSingleNode("./h2|./div/h2");
            var clubLink=heading?.SelectSingleNode(".//a");string club=Clean(clubLink?.GetAttributeValue("title","")??"");if(club.Length==0)club=Clean(heading?.InnerText??"");
            if(club.Length==0||club.ToLowerInvariant().StartsWith("transferts"))throw new InvalidDataException("Club du tableau non identifié : aucune importation partielle n'est effectuée.");
            if(!groups.TryGetValue(club,out var entries)){entries=[];groups.Add(club,entries);}
            foreach(var row in Nodes(table,".//tbody/tr").Where(r=>r.Ancestors("table").FirstOrDefault()==table))
            {
                if(IsLoanEnd(row.InnerText))continue;
                var cells=Nodes(row,"./td").ToArray();var playerLink=row.SelectSingleNode(".//a[contains(@href,'/profil/spieler/')]");if(playerLink is null)continue;
                string player=Clean(playerLink.InnerText);if(player.Length==0)player=Clean(playerLink.GetAttributeValue("title",""));
                if(index>=cells.Length)throw new InvalidDataException("Colonne du club manquante.");var cell=cells[index];var otherLink=cell.SelectSingleNode(".//a[contains(@href,'/verein/')]");
                string other=Clean(otherLink?.GetAttributeValue("title","")??"");if(other.Length==0)other=Clean(otherLink?.InnerText??"");
                if(other.Length==0){var img=cell.SelectSingleNode(".//img");other=Clean(img?.GetAttributeValue("title",img.GetAttributeValue("alt",""))??cell.InnerText);}
                if(other.Length%2==0&&other[..(other.Length/2)].Equals(other[(other.Length/2)..],StringComparison.OrdinalIgnoreCase))other=other[..(other.Length/2)];
                entries.Add((phase,player,other));
            }
        }
        var result=new List<MarketTransfer>();foreach(var group in groups)foreach(var row in group.Value.OrderBy(r=>r.Phase))result.Add(new(result.Count+1,row.Player,row.Phase==1?row.Other:group.Key,row.Phase==1?group.Key:row.Other,row.Phase==1?"arrival":"departure"));
        if(result.Count==0)throw new InvalidDataException("Aucun tableau de transferts reconnu.");return result;
    }
    static readonly string[] LoanEnds=["end of loan","fin de pret","fin de prêt","leih ende","retour de pret","retour de prêt","leiheende","loan end"];
    static bool IsLoanEnd(string text)
    {
        string normalized=TransferResolver.Normalize(Clean(text));
        return LoanEnds.Any(token=>normalized.Contains(TransferResolver.Normalize(token)));
    }
    public static IReadOnlyList<MarketTransfer> ParseCsv(string csv)
    {
        var rows=TableEditing.Parse(csv.TrimStart('\uFEFF'),TableEditing.Separator(csv));if(rows.Count<2)throw new InvalidDataException("CSV vide.");
        int Column(string name){int index=Array.IndexOf(rows[0],name);return index>=0?index:throw new InvalidDataException($"Colonne manquante: {name}");}
        int sequence=Column("sequence"),player=Column("player_name"),old=Column("old_club"),dest=Column("new_club");
        return rows.Skip(1).Select(r=>r.Length==rows[0].Length?new MarketTransfer(int.Parse(r[sequence]),r[player],r[old],r[dest],""):throw new InvalidDataException("Ligne CSV invalide.")).OrderBy(r=>r.Sequence).ToArray();
    }
}
public sealed class TransferResolver
{
    private readonly IReadOnlyList<EntityItem> players,teams;
    private readonly Dictionary<string,string[]> clubs;
    private readonly Dictionary<string,string[]> aliases;
    private readonly Dictionary<string,string> normalizedPlayers;
    public TransferResolver(FootballCatalog catalog)
    {
        players=catalog.Entities("players");teams=catalog.Entities("teams");
        var teamNames=teams.ToDictionary(t=>t.Id,t=>t.Name);
        clubs=catalog.Rows("teamplayerlinks").Where(r=>!catalog.NationalTeamIds.Contains(FootballCatalog.Value(r,"teamid"))).GroupBy(r=>FootballCatalog.Value(r,"playerid")).ToDictionary(g=>g.Key,g=>g.Select(r=>teamNames.GetValueOrDefault(FootballCatalog.Value(r,"teamid"),"")).ToArray());
        normalizedPlayers=players.ToDictionary(p=>p.Id,p=>Normalize(p.Name));
        using var stream=typeof(TransferResolver).Assembly.GetManifestResourceStream("ATLink.Core.Resources.TeamAliases.json")!;
        aliases=JsonSerializer.Deserialize<Dictionary<string,string[]>>(stream)!.GroupBy(p=>Normalize(p.Key)).ToDictionary(g=>g.Key,g=>g.Last().Value);
    }
    public static string Normalize(string value)
    {
        value=value.ToLowerInvariant().Replace("&"," and ").Normalize(NormalizationForm.FormKD);
        value=new string(value.Where(c=>CharUnicodeInfo.GetUnicodeCategory(c)!=UnicodeCategory.NonSpacingMark).ToArray());return Regex.Replace(Regex.Replace(value,"[^a-z0-9 ]"," "),@"\s+"," ").Trim();
    }
    private static string TeamKey(string name)=>string.Join(' ',Normalize(name).Split(' ').Where(t=>!new[]{"fc","afc","cf","ac","sc","ssc","ss","bc","club","football","futbol","fussball"}.Contains(t)));
    // Matching-block ratio, equivalent to SequenceMatcher without junk heuristics for short names.
    public static double Similarity(string a,string b)
    {
        a=Normalize(a);b=Normalize(b);if(a.Length+b.Length==0)return 1;
        int Match(int alo,int ahi,int blo,int bhi)
        {
            int best=0,ai=alo,bi=blo;var previous=new int[b.Length+1];
            for(int i=alo;i<ahi;i++){var current=new int[b.Length+1];for(int j=blo;j<bhi;j++)if(a[i]==b[j]){int length=previous[j]+1;current[j+1]=length;if(length>best){best=length;ai=i-length+1;bi=j-length+1;}}previous=current;}
            return best==0?0:best+Match(alo,ai,blo,bi)+Match(ai+best,ahi,bi+best,bhi);
        }
        return 2.0*Match(0,a.Length,0,b.Length)/(a.Length+b.Length);
    }
    private EntityItem? ChoosePlayer(EntityItem[] candidates,string oldClub)
    {
        if(candidates.Length==1)return candidates[0];
        var exact=candidates.Where(p=>clubs.GetValueOrDefault(p.Id,[]).Any(c=>Normalize(c)==Normalize(oldClub))).ToArray();if(exact.Length==1)return exact[0];
        var keyed=candidates.Where(p=>clubs.GetValueOrDefault(p.Id,[]).Any(c=>TeamKey(c)==TeamKey(oldClub)&&TeamKey(oldClub)!="")).ToArray();return keyed.Length==1?keyed[0]:null;
    }
    private (EntityItem? Player,string Reason) Player(string name,string old)
    {
        string target=Normalize(name);var tokens=target.Split(' ',StringSplitOptions.RemoveEmptyEntries);
        var exact=players.Where(p=>normalizedPlayers[p.Id]==target).ToArray();if(exact.Length>0)return(ChoosePlayer(exact,old),exact.Length==1?"unique_exact_name":"exact_disambiguation");
        if(tokens.Length>=2)
        {
            var subset=players.Where(p=>tokens.All(t=>normalizedPlayers[p.Id].Split(' ').Contains(t))).ToArray();var chosen=ChoosePlayer(subset,old);if(chosen is not null)return(chosen,"name_subset");
            var ends=players.Where(p=>{var parts=normalizedPlayers[p.Id].Split(' ');return parts.Length>=2&&parts[0]==tokens[0]&&parts[^1]==tokens[^1];}).ToArray();chosen=ChoosePlayer(ends,old);if(chosen is not null)return(chosen,"first_last");
        }
        var fuzzy=players.Where(p=>{string other=normalizedPlayers[p.Id];return 2.0*Math.Min(target.Length,other.Length)/Math.Max(1,target.Length+other.Length)>0.88&&Similarity(target,other)>0.88;}).ToArray();return(ChoosePlayer(fuzzy,old),"fuzzy > 0.88");
    }
    private EntityItem? Team(string name)
    {
        string normalized=Normalize(name);
        EntityItem? Unique(IEnumerable<EntityItem> choices)
        {
            var all=choices.DistinctBy(t=>t.Id).ToArray();if(all.Length==1)return all[0];
            // Prefer the explicit DB gender field, never guess from ID length or row order.
            var men=all.Where(t=>FootballCatalog.Value(t.Row,"iswomensteam")=="0").ToArray();return men.Length==1?men[0]:null;
        }
        if(aliases.TryGetValue(normalized,out var targets))
        {
            if(targets.Contains("__FREE_AGENT__"))return teams.SingleOrDefault(t=>t.Id=="111592");
            foreach(string alias in targets){var result=Unique(teams.Where(t=>Normalize(t.Name)==Normalize(alias)||TeamKey(t.Name)==TeamKey(alias)));if(result is not null)return result;}
        }
        var found=Unique(teams.Where(t=>Normalize(t.Name)==normalized));if(found is not null)return found;
        found=Unique(teams.Where(t=>TeamKey(t.Name)==TeamKey(name)));if(found is not null)return found;
        string parent=Regex.Replace(name,@"\s+(U21|U23|U19|U18|B|II|2)$","",RegexOptions.IgnoreCase);return parent!=name?Unique(teams.Where(t=>TeamKey(t.Name)==TeamKey(parent))):null;
    }
    public EntityItem? FindTeam(string name)=>Team(name);
    public IReadOnlyList<ResolvedTransfer> Resolve(IEnumerable<MarketTransfer> input)
    {
        return input.OrderBy(t=>t.Sequence).Select(t=>{var player=Player(t.Player,t.OldClub);var team=Team(t.NewClub);return new ResolvedTransfer{Sequence=t.Sequence,Player=t.Player,OldClub=t.OldClub,NewClub=t.NewClub,PlayerId=player.Player?.Id??"",TeamId=team?.Id??"",Match=(player.Player is null?"Player unresolved":player.Reason)+(team is null?" · Team unresolved":"")};}).ToArray();
    }
    public static string Export(IEnumerable<ResolvedTransfer> rows)
    {
        static string Q(string s)=>"\""+s.Replace("\"","\"\"")+"\"";
        return "sequence;playerid;new_teamid;player_name;old_club;new_club;match\n"+string.Join('\n',rows.Select(r=>string.Join(';',new[]{r.Sequence.ToString(),r.PlayerId,r.TeamId,r.Player,r.OldClub,r.NewClub,r.Match}.Select(Q))))+"\n";
    }
}
