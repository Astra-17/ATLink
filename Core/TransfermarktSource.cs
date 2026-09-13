using System.Net;
using System.Net.Http;
using System.Globalization;
using System.Text.RegularExpressions;
using HtmlAgilityPack;



namespace ATLink.Core;

public sealed class TransfermarktSource : IDisposable
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    private static readonly Regex TransfermarktHost = new(@"transfermarkt\.", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ClubPath = new(@"/(startseite|kader|plan|news)/verein/", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly HashSet<string> LoanEndTokens =
    [
        "end of loan", "fin de pret", "fin de prêt", "fin du pret", "fin du prêt", "leih-ende", "retour de pret", "retour de prêt", "retour du pret", "retour du prêt",
        "leiheende", "loan end"
    ];

    private readonly HttpClient _http;
    private readonly ITransferLog _logger;
    private readonly NameNormalizer _normalizer;
    private readonly bool _ownsClient;

    public TransfermarktSource(ITransferLog logger, NameNormalizer normalizer, HttpMessageHandler? handler = null, TimeSpan? timeout = null)
    {
        _logger = logger;
        _normalizer = normalizer;
        if (handler is null)
        {
            _http = new HttpClient(new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                AllowAutoRedirect = true
            });
            _ownsClient = true;
        }
        else
        {
            _http = new HttpClient(handler, disposeHandler: false);
            _ownsClient = true;
        }

        _http.Timeout = timeout ?? DefaultTimeout;
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "fr-FR,fr;q=0.9,en-US;q=0.8,en;q=0.7");
    }

    public bool IsTransfermarktUrl(string? url)
    {
        return TryNormalizeUrl(url, out _);
    }

    public async Task<SourceFetchResult> FetchTransfersAsync(string url, CancellationToken cancellationToken = default,IProgress<TransferProgress>? progress=null)
    {
        if (!TryNormalizeUrl(url, out var normalized))
        {
            return Fail("Cette URL n'est pas une page Transfermarkt valide.", url);
        }

        try
        {
            progress?.Report(new("Downloading competition transfers…"));
            using var response = await SendWithRetryAsync(normalized, cancellationToken).ConfigureAwait(false);
            var status = (int)response.StatusCode;

            if (status == 403)
            {
                return Fail("Accès refusé par Transfermarkt. Réessayez plus tard.", normalized, status);
            }

            if (status == 404)
            {
                return Fail("Page Transfermarkt introuvable.", normalized, status);
            }

            if (status == 429)
            {
                return Fail("Trop de requêtes. Réessayez dans quelques instants.", normalized, status);
            }

            if (status >= 500)
            {
                return Fail("Transfermarkt est temporairement indisponible.", normalized, status);
            }

            if (!response.IsSuccessStatusCode)
            {
                return Fail("Impossible de récupérer les transferts depuis cette URL.", normalized, status);
            }

            var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(html) || !html.Contains("transfermarkt", StringComparison.OrdinalIgnoreCase) && !html.Contains("profil/spieler", StringComparison.OrdinalIgnoreCase))
            {
                return Fail("Le contenu de la page Transfermarkt est inattendu.", normalized, status);
            }

            var transfers = ParseHtml(html);
            if (transfers.Count == 0)
            {
                return Fail("Aucun transfert détecté sur cette page.", normalized, status);
            }

            transfers = await EnrichLoansAsync(transfers,normalized,cancellationToken,progress).ConfigureAwait(false);
            return new SourceFetchResult
            {
                Success = true,
                UserMessage = $"Source valide — {transfers.Count} transfert{(transfers.Count > 1 ? "s" : "")} détecté{(transfers.Count > 1 ? "s" : "")}",
                Transfers = transfers,
                Url = normalized
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Fail("La requête a expiré.", normalized);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            _logger.Error("Impossible de contacter Transfermarkt.", ex, new Dictionary<string, object?> { ["url"] = normalized });
            return Fail("Impossible de contacter Transfermarkt.", normalized);
        }
        catch (Exception ex)
        {
            _logger.Error("Erreur inattendue lors de la récupération Transfermarkt.", ex, new Dictionary<string, object?> { ["url"] = normalized });
            return Fail("Impossible de récupérer les transferts depuis cette URL.", normalized);
        }
    }

    public IReadOnlyList<SourceTransfer> ParseHtml(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var results = new List<SourceTransfer>();

        var boxes = doc.DocumentNode.SelectNodes("//div[contains(@class,'box')]");
        if (boxes is null)
        {
            return results;
        }

        foreach (var box in boxes)
        {
            var clubName = ExtractBoxClubName(box);
            if (string.IsNullOrWhiteSpace(clubName))
            {
                continue;
            }

            var tables = box.SelectNodes(".//div[contains(@class,'responsive-table')]//table");
            if (tables is null)
            {
                continue;
            }

            for (var i = 0; i < tables.Count; i++)
            {
                var isArrival = IsArrivalTable(tables[i], i);
                ParseTable(tables[i], clubName, isArrival, results);
            }
        }

        return results;
    }

    private void ParseTable(HtmlNode table, string boxClub, bool isArrival, List<SourceTransfer> results)
    {
        var rows = table.SelectNodes("./tbody/tr");
        if (rows is null)
        {
            return;
        }

        foreach (var row in rows)
        {
            var playerName = ExtractPlayerName(row);
            if (string.IsNullOrWhiteSpace(playerName))
            {
                continue;
            }

            var feeText = _normalizer.Normalize(ExtractFeeText(row));
            if (LoanEndTokens.Any(token => feeText.Contains(_normalizer.Normalize(token))))
            {
                continue;
            }

            var otherClub = ExtractOtherClub(row);
            if (string.IsNullOrWhiteSpace(otherClub))
            {
                otherClub = "Sans club";
            }

            var fromClub = isArrival ? otherClub : boxClub;
            var toClub = isArrival ? boxClub : otherClub;
            var normalizedFee=_normalizer.Normalize(ExtractFeeText(row));
            bool loanToBuy=normalizedFee.Contains("montant du pret",StringComparison.Ordinal)||normalizedFee.Contains("loan fee",StringComparison.Ordinal);
            bool isLoan=loanToBuy||normalizedFee=="pret"||normalizedFee.Contains(" prêt",StringComparison.Ordinal)||
                normalizedFee.Contains("loan",StringComparison.Ordinal)||normalizedFee.Contains("leihe",StringComparison.Ordinal);

            results.Add(new SourceTransfer
            {
                Sequence = results.Count + 1,
                PlayerName = _normalizer.Clean(playerName),
                FromClub = _normalizer.Clean(fromClub),
                ToClub = _normalizer.Clean(toClub),
                Phase = isArrival ? "arrival" : "departure",
                PlayerProfileUrl=ExtractPlayerProfileUrl(row),IsLoan=isLoan,IsLoanToBuy=loanToBuy
            });
        }
    }

    private static bool IsArrivalTable(HtmlNode table, int index)
    {
        var header = table.SelectSingleNode(".//th[contains(@class,'spieler-transfer-cell')]")?.InnerText ?? string.Empty;
        var compact = header.Trim().ToLowerInvariant();
        if (compact.Contains("arriv") || compact is "in" or "zugänge" or "zugange")
        {
            return true;
        }

        if (compact.Contains("départ") || compact.Contains("depart") || compact is "out" or "abgänge" or "abgange")
        {
            return false;
        }

        return index == 0;
    }

    private static string ExtractBoxClubName(HtmlNode box)
    {
        var links = box.SelectNodes("./h2//a[@href]");
        if (links is null)
        {
            return string.Empty;
        }

        foreach (var link in links)
        {
            var href = link.GetAttributeValue("href", "");
            if (href.Contains("/verein/", StringComparison.OrdinalIgnoreCase))
            {
                var title = CleanClubTitle(link.GetAttributeValue("title", ""));
                if (!string.IsNullOrWhiteSpace(title))
                {
                    return title;
                }

                var text = HtmlEntity.DeEntitize(link.InnerText ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }

        return string.Empty;
    }

    private static string ExtractPlayerName(HtmlNode row)
    {
        var preferred = row.SelectSingleNode(".//span[contains(@class,'hide-for-small')]//a[contains(@href,'/profil/spieler/')]");
        var link = preferred ?? row.SelectSingleNode(".//a[contains(@href,'/profil/spieler/')]");
        if (link is null)
        {
            return string.Empty;
        }

        var title = link.GetAttributeValue("title", "");
        if (!string.IsNullOrWhiteSpace(title))
        {
            return title.Trim();
        }

        return HtmlEntity.DeEntitize(link.InnerText ?? string.Empty).Trim();
    }

    private static string ExtractPlayerProfileUrl(HtmlNode row)=>row.SelectSingleNode(".//a[contains(@href,'/profil/spieler/')]")?.GetAttributeValue("href","")??"";

    private static string ExtractFeeText(HtmlNode row)
    {
        var cell=row.SelectSingleNode(".//td[contains(@class,'abloese') or contains(@class,'fee')]")??row.SelectNodes("./td")?.LastOrDefault();
        return HtmlEntity.DeEntitize(cell?.InnerText??string.Empty).Trim();
    }

    private async Task<IReadOnlyList<SourceTransfer>> EnrichLoansAsync(IReadOnlyList<SourceTransfer> transfers,string sourceUrl,CancellationToken token,IProgress<TransferProgress>? progress)
    {
        var output=new List<SourceTransfer>(transfers.Count);
        var cache=new Dictionary<string,(DateOnly? Date,string Error)>(StringComparer.OrdinalIgnoreCase);
        var origin=new Uri(sourceUrl); int completed=0,total=transfers.Count(t=>t.IsLoan);
        foreach(var transfer in transfers)
        {
            if(!transfer.IsLoan){output.Add(transfer);continue;}
            token.ThrowIfCancellationRequested();
            progress?.Report(new($"Downloading loan profiles: {completed}/{total} · {transfer.PlayerName}",completed++,total));
            string profile=Uri.TryCreate(origin,transfer.PlayerProfileUrl,out var uri)?uri.ToString():"";
            if(profile.Length==0)
            {
                output.Add(CopyLoan(transfer,null,"Profil Transfermarkt du joueur introuvable."));continue;
            }
            string cacheKey=profile+'\u001f'+_normalizer.NormalizeTeamName(transfer.FromClub)+'\u001f'+_normalizer.NormalizeTeamName(transfer.ToClub);
            if(!cache.TryGetValue(cacheKey,out var found))
            {
                try
                {
                    string playerId=Regex.Match(transfer.PlayerProfileUrl,@"/spieler/(\d+)",RegexOptions.IgnoreCase).Groups[1].Value;
                    if(playerId.Length==0)found=(null,"Identifiant Transfermarkt du joueur introuvable.");
                    else
                    {
                        using var historyResponse=await GetApiAsync($"https://tmapi.transfermarkt.technology/transfer/history/player/{playerId}",token).ConfigureAwait(false);
                        if(!historyResponse.IsSuccessStatusCode)found=(null,$"Historique Transfermarkt indisponible ({(int)historyResponse.StatusCode}).");
                        else
                        {
                            string historyJson=await historyResponse.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                            var clubIds=ApiClubIds(historyJson);
                            string query=string.Join('&',clubIds.Select(id=>$"ids%5B%5D={Uri.EscapeDataString(id)}"));
                            using var clubsResponse=await GetApiAsync("https://tmapi.transfermarkt.technology/clubs?"+query,token).ConfigureAwait(false);
                            found=clubsResponse.IsSuccessStatusCode
                                ?(ParseApiLoanEndDate(historyJson,await clubsResponse.Content.ReadAsStringAsync(token).ConfigureAwait(false),transfer.FromClub,transfer.ToClub),"")
                                :(null,$"Clubs Transfermarkt indisponibles ({(int)clubsResponse.StatusCode}).");
                        }
                    }
                    if(found.Date is null&&found.Error.Length==0)found=(null,"Date de fin du prêt introuvable dans l'historique Transfermarkt.");
                }
                catch(Exception ex) when(ex is not OperationCanceledException)
                {
                    _logger.Error("Impossible de lire la fin du prêt Transfermarkt.",ex,new Dictionary<string,object?>{{"url",profile}});
                    found=(null,"Impossible de lire la date de fin du prêt Transfermarkt.");
                }
                cache[cacheKey]=found;
            }
            output.Add(CopyLoan(transfer,found.Date,found.Error));
        }
        progress?.Report(new("Download complete",total,total));
        return output;
    }

    async Task<HttpResponseMessage> GetApiAsync(string url,CancellationToken token)
    {
        async Task<HttpResponseMessage> Send()
        {
            var request=new HttpRequestMessage(HttpMethod.Get,url);
            request.Headers.TryAddWithoutValidation("Accept","application/json");
            return await _http.SendAsync(request,token).ConfigureAwait(false);
        }
        var response=await Send().ConfigureAwait(false);
        if((int)response.StatusCode!=429)return response;
        response.Dispose();await Task.Delay(TimeSpan.FromSeconds(2),token).ConfigureAwait(false);
        return await Send().ConfigureAwait(false);
    }

    static string[] ApiClubIds(string json)
    {
        using var document=System.Text.Json.JsonDocument.Parse(json);
        return document.RootElement.GetProperty("data").GetProperty("clubIds").EnumerateArray()
            .Select(id=>id.GetString()??"").Where(id=>id.Length>0).Distinct(StringComparer.Ordinal).ToArray();
    }

    public DateOnly? ParseApiLoanEndDate(string historyJson,string clubsJson,string fromClub,string toClub)
    {
        using var history=System.Text.Json.JsonDocument.Parse(historyJson);
        using var clubsDocument=System.Text.Json.JsonDocument.Parse(clubsJson);
        var clubs=clubsDocument.RootElement.GetProperty("data").EnumerateArray().ToDictionary(
            c=>c.GetProperty("id").GetString()??"",c=>new[]{
                c.TryGetProperty("name",out var name)?name.GetString()??"":"",
                c.TryGetProperty("baseDetails",out var details)&&details.TryGetProperty("shortName",out var shortName)?shortName.GetString()??"":"",
                c.TryGetProperty("baseDetails",out details)&&details.TryGetProperty("abbreviation",out var abbreviation)?abbreviation.GetString()??"":""});
        var root=history.RootElement.GetProperty("data").GetProperty("history");
        var terminated=root.GetProperty("terminated").EnumerateArray().ToArray();
        var pending=root.TryGetProperty("pending",out var pendingElement)?pendingElement.EnumerateArray().ToArray():[];
        var active=terminated.Where(t=>TransferType(t)=="ACTIVE_LOAN_TRANSFER"&&ClubIdMatches(SourceId(t),fromClub)&&ClubIdMatches(DestinationId(t),toClub))
            .OrderByDescending(TransferDate).FirstOrDefault();
        if(active.ValueKind==System.Text.Json.JsonValueKind.Undefined)return null;
        string returnFrom=DestinationId(active),returnTo=SourceId(active);var start=TransferDate(active);
        return pending.Concat(terminated).Where(t=>TransferType(t)=="RETURNED_FROM_PREVIOUS_LOAN"&&SourceId(t)==returnFrom&&DestinationId(t)==returnTo&&TransferDate(t)>=start)
            .Select(TransferDate).OrderBy(date=>date).Cast<DateOnly?>().FirstOrDefault();

        bool ClubIdMatches(string id,string expected)=>clubs.TryGetValue(id,out var labels)&&labels.Any(label=>label.Length>0&&ClubMatches(label,expected));
        static string SourceId(System.Text.Json.JsonElement t)=>t.GetProperty("transferSource").GetProperty("clubId").GetString()??"";
        static string DestinationId(System.Text.Json.JsonElement t)=>t.GetProperty("transferDestination").GetProperty("clubId").GetString()??"";
        static string TransferType(System.Text.Json.JsonElement t)=>t.GetProperty("typeDetails").GetProperty("type").GetString()??"";
        static DateOnly TransferDate(System.Text.Json.JsonElement t)=>DateOnly.FromDateTime(DateTimeOffset.Parse(t.GetProperty("details").GetProperty("date").GetString()!,CultureInfo.InvariantCulture).Date);
    }

    static SourceTransfer CopyLoan(SourceTransfer value,DateOnly? date,string error)=>new()
    {
        Sequence=value.Sequence,PlayerName=value.PlayerName,FromClub=value.FromClub,ToClub=value.ToClub,Phase=value.Phase,
        PlayerProfileUrl=value.PlayerProfileUrl,IsLoan=value.IsLoan,IsLoanToBuy=value.IsLoanToBuy,LoanEndDate=date,LoanError=error
    };

    public DateOnly? ParseLoanEndDate(string html,string fromClub,string toClub)
    {
        var doc=new HtmlDocument();doc.LoadHtml(html);
        foreach(var row in doc.DocumentNode.SelectNodes("//tr")??Enumerable.Empty<HtmlNode>())
        {
            if(TryHistoryNodes(row.SelectNodes("./td")??Enumerable.Empty<HtmlNode>(),fromClub,toClub,out var date))return date;
        }
        foreach(var dateCell in doc.DocumentNode.SelectNodes("//*[contains(@class,'transfer-history-grid__date')]")??Enumerable.Empty<HtmlNode>())
        {
            var parent=dateCell.ParentNode;
            var nodes=parent.DescendantsAndSelf().ToArray();
            if((parent.SelectNodes(".//a[contains(@href,'/verein/')]")?.Count??0)<2&&parent.ParentNode is HtmlNode grid)
            {
                var siblings=grid.ChildNodes.Where(n=>n.NodeType==HtmlNodeType.Element).ToArray();
                int index=Array.IndexOf(siblings,dateCell);
                if(index<0)index=Array.IndexOf(siblings,parent);
                if(index>=0)nodes=siblings.Skip(Math.Max(0,index-1)).Take(7).ToArray();
            }
            if(TryHistoryNodes(nodes,fromClub,toClub,out var date))return date;
        }
        return null;
    }

    bool TryHistoryNodes(IEnumerable<HtmlNode> nodes,string fromClub,string toClub,out DateOnly date)
    {
        var array=nodes.ToArray();
        var normalized=array.Select(c=>_normalizer.Normalize(c.InnerText)).ToArray();
        if(!LoanEndTokens.Any(t=>normalized.Any(c=>c.Contains(_normalizer.Normalize(t),StringComparison.Ordinal)))){date=default;return false;}
        var clubNames=array.SelectMany(n=>n.SelectNodes(".//a[contains(@href,'/verein/')]")??Enumerable.Empty<HtmlNode>())
            .Distinct().Select(a=>CleanClubTitle(a.GetAttributeValue("title",a.InnerText))).Where(s=>s.Length>0).ToArray();
        bool clubsMatch=clubNames.Length<2||(ClubMatches(clubNames[0],fromClub)&&ClubMatches(clubNames[1],toClub))||
            (ClubMatches(clubNames[0],toClub)&&ClubMatches(clubNames[1],fromClub));
        if(!clubsMatch){date=default;return false;}
        foreach(var node in array)if(TryDate(HtmlEntity.DeEntitize(node.InnerText).Trim(),out date))return true;
        date=default;return false;
    }

    bool ClubMatches(string left,string right)
    {
        var leftName=_normalizer.NormalizeTeamName(left);var rightName=_normalizer.NormalizeTeamName(right);
        if(leftName==rightName)return true;
        var leftKey=_normalizer.TeamKey(left);var rightKey=_normalizer.TeamKey(right);
        return leftKey.Length>0&&leftKey==rightKey;
    }
    static bool TryDate(string text,out DateOnly date)
    {
        string[] formats=["dd/MM/yyyy","d/M/yyyy","dd.MM.yyyy","d.M.yyyy","dd/MM/yy","d/M/yy","MMM d, yyyy","d MMM yyyy"];
        foreach(var culture in new[]{CultureInfo.GetCultureInfo("fr-FR"),CultureInfo.GetCultureInfo("en-US"),CultureInfo.GetCultureInfo("de-DE")})
            if(DateOnly.TryParseExact(text,formats,culture,DateTimeStyles.AllowWhiteSpaces,out date)||DateOnly.TryParse(text,culture,DateTimeStyles.AllowWhiteSpaces,out date))return true;
        date=default;return false;
    }

    private static string ExtractOtherClub(HtmlNode row)
    {
        var cell = row.SelectSingleNode(".//td[contains(@class,'verein-flagge-transfer-cell')]");
        if (cell is not null)
        {
            var link = cell.SelectSingleNode(".//a[@title]");
            if (link is not null)
            {
                var title = CleanClubTitle(link.GetAttributeValue("title", ""));
                if (!string.IsNullOrWhiteSpace(title))
                {
                    return title;
                }
            }

            var text = HtmlEntity.DeEntitize(cell.InnerText ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        var anyClub = row.SelectNodes(".//a[contains(@href,'/verein/')]")
            ?.FirstOrDefault(a => !string.IsNullOrWhiteSpace(a.GetAttributeValue("title", "")));
        return anyClub is null ? string.Empty : CleanClubTitle(anyClub.GetAttributeValue("title", ""));
    }

    private static string CleanClubTitle(string title)
    {
        title = HtmlEntity.DeEntitize(title ?? string.Empty).Trim();
        if (title.EndsWith("Array", StringComparison.Ordinal))
        {
            title = title[..^5].Trim();
        }

        return title;
    }

    public bool TryNormalizeUrl(string? url, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        url = url.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttps || !TransfermarktHttp.IsProfile(uri))
        {
            return false;
        }

        var builder = new UriBuilder(uri)
        {
            Fragment = string.Empty
        };

        var path = ClubPath.Replace(builder.Path, "/transfers/verein/");
        builder.Path = path;
        normalized = builder.Uri.ToString();
        return true;
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(string url, CancellationToken cancellationToken)
    {
        var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if ((int)response.StatusCode != 429)
        {
            return response;
        }

        response.Dispose();
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
        return await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
    }

    private SourceFetchResult Fail(string message, string url, int? status = null)
    {
        _logger.Error(message, null, new Dictionary<string, object?>
        {
            ["url"] = url,
            ["httpStatus"] = status
        });

        return new SourceFetchResult
        {
            Success = false,
            UserMessage = message,
            Url = url
        };
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _http.Dispose();
        }
    }
}
