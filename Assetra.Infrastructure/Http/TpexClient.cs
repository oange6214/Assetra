using System.Text.Json;
using System.Text.Json.Serialization;
using Assetra.Core.Models;
using Microsoft.Extensions.Logging;

namespace Assetra.Infrastructure.Http;

internal sealed class TpexClient(HttpClient http, ILogger<TpexClient> logger) : ITpexClient
{
    // TPEX OpenAPI — returns all mainboard stocks in one shot
    private const string OpenApiUrl = "https://www.tpex.org.tw/openapi/v1/tpex_mainboard_quotes";
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public async Task<IReadOnlyList<StockQuote>> FetchQuotesAsync(IEnumerable<string> symbols)
    {
        var symbolSet = symbols.ToHashSet();
        if (symbolSet.Count == 0)
            return [];
        try
        {
            var json = await http.GetStringAsync(OpenApiUrl);
            var items = JsonSerializer.Deserialize<IReadOnlyList<TpexOpenApiQuote>>(json, JsonOpts) ?? [];
            var now = DateTimeOffset.Now;
            return items
                .Where(q => symbolSet.Contains(q.SecuritiesCompanyCode ?? ""))
                .Select(q => ToStockQuote(q, now))
                .ToList();
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            logger.LogWarning(ex, "TPEX quote fetch failed");
            return [];
        }
    }

    private static StockQuote ToStockQuote(TpexOpenApiQuote q, DateTimeOffset now)
    {
        var price = ParseDecimal(q.Close);
        var open = ParseDecimal(q.Open);
        var high = ParseDecimal(q.High);
        var low = ParseDecimal(q.Low);
        var change = ParseDecimal(q.Change);
        var prevClose = price - change;
        var changePct = prevClose != 0 ? Math.Round(change / prevClose * 100, 2) : 0m;
        var volume = ParseLong(q.TradingShares);
        return new StockQuote(
            q.SecuritiesCompanyCode ?? "",
            q.CompanyName ?? "",
            "TPEX",
            price, change, changePct, volume,
            open, high, low, prevClose, now);
    }

    private static decimal ParseDecimal(string? s) =>
        decimal.TryParse(s?.Replace(",", "").Trim(), out var v) ? v : 0m;

    private static long ParseLong(string? s) =>
        long.TryParse(s?.Replace(",", "").Trim(), out var v) ? v : 0L;
}

internal record TpexOpenApiQuote(
    [property: JsonPropertyName("SecuritiesCompanyCode")] string? SecuritiesCompanyCode,
    [property: JsonPropertyName("CompanyName")] string? CompanyName,
    [property: JsonPropertyName("Close")] string? Close,
    [property: JsonPropertyName("Change")] string? Change,
    [property: JsonPropertyName("Open")] string? Open,
    [property: JsonPropertyName("High")] string? High,
    [property: JsonPropertyName("Low")] string? Low,
    [property: JsonPropertyName("TradingShares")] string? TradingShares
);
