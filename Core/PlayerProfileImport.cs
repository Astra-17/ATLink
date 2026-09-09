using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
namespace ATLink.Core;
public sealed record PlayerProfile(string Name,DateOnly? BirthDate,int? Age,int? Height,string? Foot,string? Position,double? MarketValue,string? Club=null,string? League=null,double? ClubMean=null,double? LeagueMean=null,double? WeightedTrophies=null,string? ClubUrl=null,string? LeagueUrl=null);
public static class PlayerProfileImport
{
    public static readonly string[] PositionCodes=["GK","SW","RWB","RB","RCB","CB","LCB","LB","LWB","RDM","CDM","LDM","RM","RCM","CM","LCM","LM","RAM","CAM","LAM","RF","CF","LF","RW","RS","ST","LS","LW"];
    private static string Clean(string s)=>Regex.Replace(HtmlEntity.DeEntitize(s),@"\s+"," ").Trim();
    public static PlayerProfile Parse(string text)
    {
        if(text.TrimStart().StartsWith('{'))return JsonSerializer.Deserialize<PlayerProfile>(text,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new InvalidDataException("Profil JSON vide.");
        var html=new HtmlDocument();html.LoadHtml(text);var root=html.DocumentNode;
        string? Item(string name){var n=root.SelectSingleNode($"//*[@itemprop='{name}']");return n is null?null:Clean(n.GetAttributeValue("content",n.InnerText));}
        string? Info(params string[] keys)
        {
            foreach(var label in root.SelectNodes("//span[contains(@class,'info-table__content--regular')]")??Enumerable.Empty<HtmlNode>())
                if(keys.Any(k=>Clean(label.InnerText).TrimEnd(':').Trim().Equals(k,StringComparison.OrdinalIgnoreCase)))return Clean(label.SelectSingleNode("following-sibling::span[1]")?.InnerText??"");
            return null;
        }
        var heading=root.SelectSingleNode("//h1");
        if(heading is not null)foreach(var n in heading.SelectNodes(".//*[contains(@class,'shirt-number')]")?.ToArray()??[])n.Remove();
        string name=Item("name")??Clean(heading?.InnerText??"");if(name.Length==0)throw new InvalidDataException("Nom du joueur absent du profil HTML.");
        string birth=Item("birthDate")??Info("Date of birth/Age","Date de naissance/âge","Geburtsdatum/Alter")??"";
        DateOnly? dob=null;
        string date=Regex.Replace(birth,@"\s*\(.*$","").Trim();
        foreach(string culture in new[]{"en-US","fr-FR","de-DE"})if(DateOnly.TryParse(date,CultureInfo.GetCultureInfo(culture),DateTimeStyles.None,out var d)){dob=d;break;}
        int? age=null;var ageMatch=Regex.Match(birth,@"\((\d{2})\)");if(ageMatch.Success)age=int.Parse(ageMatch.Groups[1].Value);
        string height=Item("height")??Info("Height","Taille","Größe")??"";int? cm=null;
        var number=Regex.Match(height,@"\d+(?:[.,]\d+)?");if(number.Success&&double.TryParse(number.Value.Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out double h))cm=(int)Math.Round(h<3?h*100:h);
        string foot=Info("Foot","Pied","Fuß")??"";string? normalizedFoot=TransferResolver.Normalize(foot) switch{"right" or "droit" or "rechts"=>"Right","left" or "gauche" or "links"=>"Left",_=>null};
        string? pos=MapPosition(Info("Position","Poste")??"");
        string market=Clean(root.SelectSingleNode("//*[contains(@class,'data-header__market-value-wrapper')]")?.InnerText??"");
        var clubNode=root.SelectSingleNode("//span[contains(@class,'data-header__club')]//a")??root.SelectSingleNode("//span[contains(@class,'data-header__club')]");
        string club=Clean(clubNode?.GetAttributeValue("title","")??clubNode?.InnerText??"");
        string? clubUrl=Absolute(clubNode?.GetAttributeValue("href",""));
        var leagueNode=root.SelectSingleNode("//span[@itemprop='affiliation']//a")??root.SelectSingleNode("//div[contains(@class,'data-header__club-info')]//a[contains(@href,'/wettbewerb/') or contains(@href,'/competition/')]");
        string league=Clean(leagueNode?.InnerText??"");
        string? leagueUrl=Absolute(leagueNode?.GetAttributeValue("href",""));
        return new(name,dob,age,cm,normalizedFoot,pos,ParseMoney(market),club.Length==0?null:club,league.Length==0?null:league,null,null,TrophyScore(root),clubUrl,leagueUrl);
    }
    public static PlayerProfile Merge(PlayerProfile profile,string relatedHtml)
    {
        var html=new HtmlDocument();html.LoadHtml(relatedHtml);var root=html.DocumentNode;
        var (clubMean,leagueMean)=ParseMeans(root);
        double? trophies=profile.WeightedTrophies??TrophyScore(root);
        string league=profile.League??Clean(root.SelectSingleNode("//span[@itemprop='affiliation']//a")?.InnerText??"");
        return profile with{ClubMean=profile.ClubMean??clubMean,LeagueMean=profile.LeagueMean??leagueMean,WeightedTrophies=trophies,League=string.IsNullOrWhiteSpace(league)?profile.League:league};
    }
    public static string? MapPosition(string text)
    {
        string key=TransferResolver.Normalize(text);
        var mappings=new (string Position,string[] Aliases)[]{("GK",["goalkeeper","gardien","torwart"]),("CB",["centre back","center back","defenseur central","innenverteidiger"]),("RB",["right back","arriere droit"]),("LB",["left back","arriere gauche"]),("CDM",["defensive midfield","milieu defensif"]),("CAM",["attacking midfield","milieu offensif"]),("RM",["right midfield","milieu droit"]),("LM",["left midfield","milieu gauche"]),("CM",["central midfield","milieu central"]),("RW",["right winger","ailier droit"]),("LW",["left winger","ailier gauche"]),("ST",["centre forward","center forward","avant centre","striker"]),("CF",["second striker","deuxieme attaquant"])};
        return mappings.FirstOrDefault(m=>m.Aliases.Any(key.Contains)).Position;
    }
    public static double? ParseMoney(string text)
    {
        string key=text.ToLowerInvariant().Replace("\u00a0"," ");
        var match=Regex.Match(key,@"(?<amount>\d[\d., ]*)\s*(?<suffix>mio\.?|millions?|m|th\.?|k|tsd\.?)?");
        if(!match.Success||(!key.Contains('€')&&!key.Contains("eur")&&!match.Groups["suffix"].Success))return null;
        string amount=match.Groups["amount"].Value.Trim().Replace(" ","");string suffix=match.Groups["suffix"].Value;
        if(suffix.Length==0)amount=Regex.Replace(amount,@"[.,](?=\d{3}(?:[.,]|$))","");
        amount=amount.Replace(',','.');
        if(!double.TryParse(amount,NumberStyles.Float,CultureInfo.InvariantCulture,out double value))return null;
        double factor=suffix.StartsWith('m')?1000000:suffix.Length>0?1000:1;
        return value*factor;
    }
    public static double TrophyWeight(string title)
    {
        string key=TransferResolver.Normalize(title);
        if(key.Contains("world cup")||key.Contains("coupe du monde")||key.Contains("weltmeisterschaft"))return 8;
        if(key.Contains("champions league")||key.Contains("ligue des champions")||key.Contains("libertadores"))return 5;
        if(key.Contains("europa league")||key.Contains("uefa cup")||key.Contains("conference league")||key.Contains("sudamericana"))return 2.5;
        if(key.Contains("premier league")||key.Contains("la liga")||key.Contains("bundesliga")||key.Contains("serie a")||key.Contains("ligue 1")||key.Contains("eredivisie")||key.Contains("liga portugal")||key.Contains("championship"))return 2;
        if(key.Contains("fa cup")||key.Contains("dfb-pokal")||key.Contains("coppa")||key.Contains("copa del rey")||key.Contains("coupe de france")||key.Contains("cup"))return 1;
        if(key.Contains("super cup")||key.Contains("supercup")||key.Contains("supercoupe")||key.Contains("community shield"))return 0.5;
        return 0.4;
    }
    public static async Task<PlayerProfile> Fetch(string url,CancellationToken cancellation=default)
    {
        if(!Uri.TryCreate(url,UriKind.Absolute,out var uri)||!TransfermarktHttp.IsProfile(uri)||!uri.AbsolutePath.Contains("/spieler/"))throw new InvalidDataException("URL de profil Transfermarkt HTTPS requise.");
        using var client=TransfermarktHttp.Create();
        string html=await Get(client,uri,cancellation);
        var profile=Parse(html);
        async Task<PlayerProfile> Related(string? href)
        {
            if(!Uri.TryCreate(uri,href??"",out var related)||!TransfermarktHttp.IsProfile(related))return profile;
            try{return Merge(profile,await Get(client,related,cancellation));}catch(HttpRequestException){return profile;}
        }
        profile=await Related(profile.ClubUrl);
        return await Related(profile.LeagueUrl);
    }
    private static async Task<string> Get(HttpClient client,Uri uri,CancellationToken cancellation)
    {
        using var response=await client.GetAsync(uri,cancellation);
        if(!response.IsSuccessStatusCode)throw new HttpRequestException($"HTTP {(int)response.StatusCode}. Charger un profil HTML/JSON local si l'accès est refusé.");
        return await response.Content.ReadAsStringAsync(cancellation);
    }
    private static string? Absolute(string? href)
    {
        if(string.IsNullOrWhiteSpace(href))return null;
        if(Uri.TryCreate(href,UriKind.Absolute,out var uri))return uri.ToString();
        return "https://www.transfermarkt.com"+href;
    }
    private static (double? Club,double? League) ParseMeans(HtmlNode root)
    {
        double? total=ParseMoney(Clean(root.SelectSingleNode("//*[contains(@class,'data-header__market-value-wrapper')]")?.InnerText??""));
        int? squad=Fact(root,"Squad size","Taille de l'effectif","Kadergröße","Kader");
        int? clubs=Fact(root,"Clubs","Clubs","Vereine");
        double? club=total is double value&&squad is >0?value/squad:null;
        double? league=total is double leagueTotal&&clubs is >0?leagueTotal/clubs:null;
        var meanText=root.SelectSingleNode("//td[contains(@class,'zentriert')][4]")?.InnerText;
        league??=ParseMoney(Clean(meanText??""));
        return (club,league);
    }
    private static int? Fact(HtmlNode root,params string[] keys)
    {
        foreach(var item in root.SelectNodes("//li|//th")??Enumerable.Empty<HtmlNode>())
        {
            string text=Clean(item.InnerText);
            if(!keys.Any(k=>text.Contains(k,StringComparison.OrdinalIgnoreCase)))continue;
            var match=Regex.Match(text,@"(\d+)");
            if(match.Success)return int.Parse(match.Value);
        }
        return null;
    }
    private static double? TrophyScore(HtmlNode root)
    {
        double score=0;int count=0;
        foreach(var box in root.SelectNodes("//div[contains(@class,'box')][.//table[contains(@class,'auflistung')]]")??Enumerable.Empty<HtmlNode>())
        {
            string title=Clean(box.SelectSingleNode(".//h2")?.InnerText??"");
            int trophies=box.SelectNodes(".//table[contains(@class,'auflistung')]//tr")?.Count??0;
            if(title.Length==0||trophies==0)continue;
            score+=TrophyWeight(title)*trophies;count+=trophies;
        }
        foreach(var item in root.SelectNodes("//div[contains(@class,'data-header__success-data')]|//span[contains(@class,'data-header__success')]")??Enumerable.Empty<HtmlNode>())
        {
            string title=Clean(item.GetAttributeValue("title",item.InnerText));
            if(title.Length==0)continue;
            var match=Regex.Match(title,@"(\d+)");
            score+=TrophyWeight(title)*(match.Success?int.Parse(match.Value):1);count++;
        }
        return count==0?null:score;
    }
}
