using System.Text.Json;
using Assetra.Core.Models;
using Microsoft.Extensions.Logging;

namespace Assetra.Infrastructure.Http;

internal sealed class TwseClient(HttpClient http, ILogger<TwseClient> logger) : ITwseClient
{
    private const string BaseUrl = "https://mis.twse.com.tw/stock/api/getStockInfo.jsp";
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public async Task<IReadOnlyList<StockQuote>> FetchQuotesAsync(IEnumerable<string> symbols)
    {
        try
        {
            var exCh = string.Join("|", symbols.Select(s => $"tse_{s}.tw"));
            var json = await http.GetStringAsync($"{BaseUrl}?ex_ch={exCh}");
            var response = JsonSerializer.Deserialize<TwseMisResponse>(json, JsonOpts);
            var now = DateTimeOffset.Now;
            return response?.MsgArray?
                .Where(s => MisParsing.IsValidPrice(s.CurrentPrice)
                         || MisParsing.IsValidPrice(s.PrevClose))
                .Select(s => ToStockQuote(s, "TWSE", now))
                .ToList() ?? [];
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            logger.LogWarning(ex, "TWSE quote fetch failed");
            return [];
        }
    }

    private static StockQuote ToStockQuote(TwseMisStock s, string exchange, DateTimeOffset now)
    {
        var prevClose = MisParsing.ParseDecimal(s.PrevClose);
        // Use prevClose as fallback when market is closed and CurrentPrice is "-"
        var price = MisParsing.IsValidPrice(s.CurrentPrice)
            ? MisParsing.ParseDecimal(s.CurrentPrice)
            : prevClose;
        var change = Math.Round(price - prevClose, 2);
        var changePct = prevClose != 0 ? Math.Round(change / prevClose * 100, 2) : 0;
        return new StockQuote(s.Code ?? string.Empty, s.Name ?? string.Empty, exchange,
            price, change, changePct,
            MisParsing.ParseLong(s.Volume),
            MisParsing.ParseDecimal(s.Open), MisParsing.ParseDecimal(s.High),
            MisParsing.ParseDecimal(s.Low), prevClose, now);
    }
}
