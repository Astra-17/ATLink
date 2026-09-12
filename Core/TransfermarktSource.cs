using System.Net;
using System.Net.Http;
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
        "end of loan", "fin de pret", "fin de prêt", "leih-ende", "retour de pret", "retour de prêt",
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

    public async Task<SourceFetchResult> FetchTransfersAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeUrl(url, out var normalized))
        {
            return Fail("Cette URL n'est pas une page Transfermarkt valide.", url);
        }

        try
        {
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

            var feeText = _normalizer.Normalize(row.InnerText);
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

            results.Add(new SourceTransfer
            {
                Sequence = results.Count + 1,
                PlayerName = _normalizer.Clean(playerName),
                FromClub = _normalizer.Clean(fromClub),
                ToClub = _normalizer.Clean(toClub)
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
