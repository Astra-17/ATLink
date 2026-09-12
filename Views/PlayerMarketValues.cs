using System.Globalization;
using System.IO;
using System.Text.Json;

namespace ATLink.Views;

public static class PlayerMarketValues
{
    static readonly Lazy<IReadOnlyDictionary<string, decimal>> Values = new(Load);

    public static decimal? For(string playerId)
        => Values.Value.TryGetValue(playerId, out decimal value) && value > 0 ? value : null;

    public static string Format(decimal? value)
    {
        if (value is not decimal amount || amount <= 0) return "—";
        if (amount >= 1_000_000)
            return (amount / 1_000_000).ToString("0.#", CultureInfo.InvariantCulture) + " M €";
        if (amount >= 1_000)
            return (amount / 1_000).ToString("0.#", CultureInfo.InvariantCulture) + " K €";
        return amount.ToString("0", CultureInfo.InvariantCulture) + " €";
    }

    static IReadOnlyDictionary<string, decimal> Load()
    {
        var result = new Dictionary<string, decimal>(StringComparer.Ordinal);
        string path = Path.Combine(AppContext.BaseDirectory, "Data", "players_enrich.json");
        if (!File.Exists(path)) return result;
        try
        {
            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);
            if (!document.RootElement.TryGetProperty("players", out var players) || players.ValueKind != JsonValueKind.Array)
                return result;
            foreach (var player in players.EnumerateArray())
            {
                if (!player.TryGetProperty("playerid", out var idElement) ||
                    !player.TryGetProperty("transfermarkt_market_value", out var valueElement) ||
                    valueElement.ValueKind != JsonValueKind.Number ||
                    !valueElement.TryGetDecimal(out decimal value) || value <= 0)
                    continue;
                string id = idElement.ValueKind == JsonValueKind.Number
                    ? idElement.GetInt64().ToString(CultureInfo.InvariantCulture)
                    : idElement.GetString() ?? "";
                if (id.Length > 0) result[id] = value;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        return result;
    }
}