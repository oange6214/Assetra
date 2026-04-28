using System.Text.Json;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.Infrastructure.History;

internal sealed class YahooFinanceHistoryProvider : IStockHistoryProvider
{
    private readonly HttpClient _http;
    private static readonly TimeZoneInfo TaipeiTz = ResolveTimeZoneOrTaipei("Asia/Taipei");

    public YahooFinanceHistoryProvider(HttpClient http) => _http = http;

    /// <summary>
    /// Resolves an IANA tz id to a <see cref="TimeZoneInfo"/>. .NET 6+ accepts IANA on all platforms via ICU,
    /// but older Windows hosts may map only the Windows ids ("Taipei Standard Time"). Try the IANA id, then
    /// the legacy Windows id, then fall back to UTC so we never throw at runtime.
    /// </summary>
    internal static TimeZoneInfo ResolveTimeZoneOrTaipei(string ianaOrWindowsId)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(ianaOrWindowsId); }
        catch (TimeZoneNotFoundException) { }
        catch (InvalidTimeZoneException) { }
        if (ianaOrWindowsId == "Asia/Taipei")
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("Taipei Standard Time"); }
            catch { return TimeZoneInfo.Utc; }
        }
        return TimeZoneInfo.Utc;
    }

    private static TimeZoneInfo ResolveExchangeTimeZone(string? exchange)
    {
        var ex = StockExchangeRegistry.TryGet(exchange);
        return ex is null ? TaipeiTz : ResolveTimeZoneOrTaipei(ex.TimeZone);
    }

    public async Task<IReadOnlyList<OhlcvPoint>> GetHistoryAsync(
        string symbol, string exchange, ChartPeriod period, CancellationToken ct = default)
    {
        try
        {
            var yahooSymbol = YahooSymbolMapper.ToYahooSymbol(symbol, exchange);
            var range = period switch
            {
                ChartPeriod.OneMonth => "1mo",
                ChartPeriod.ThreeMonths => "3mo",
                ChartPeriod.OneYear => "1y",
                ChartPeriod.TwoYears => "2y",
                _ => "3mo"
            };
            var url = $"https://query1.finance.yahoo.com/v8/finance/chart/{yahooSymbol}?interval=1d&range={range}";

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
            req.Headers.TryAddWithoutValidation("Accept-Language", "zh-TW,zh;q=0.9,en-US;q=0.8");
            using var response = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
                return [];

            var json = await response.Content.ReadAsStringAsync(ct);
            return ParseResponse(json, exchange);
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Parses a Yahoo Finance v8 chart response. The session-close timestamp returned by Yahoo is interpreted in
    /// the exchange's local timezone (resolved via <see cref="StockExchangeRegistry"/>). For an unknown exchange
    /// — or the legacy single-arg overload — Taipei is used to preserve v0.15.1 behaviour.
    /// </summary>
    internal static IReadOnlyList<OhlcvPoint> ParseResponse(string json, string? exchange = null)
    {
        using var doc = JsonDocument.Parse(json);
        var chart = doc.RootElement.GetProperty("chart");
        var resultEl = chart.GetProperty("result");

        // Yahoo returns result:null when the symbol is not found or API is restricted
        if (resultEl.ValueKind == JsonValueKind.Null || resultEl.GetArrayLength() == 0)
            return [];

        var result = resultEl[0];
        var tz = ResolveExchangeTimeZone(exchange);

        var timestamps = result.GetProperty("timestamp").EnumerateArray().ToList();
        var quote = result.GetProperty("indicators").GetProperty("quote")[0];
        var opens = quote.GetProperty("open").EnumerateArray().ToList();
        var highs = quote.GetProperty("high").EnumerateArray().ToList();
        var lows = quote.GetProperty("low").EnumerateArray().ToList();
        var closes = quote.GetProperty("close").EnumerateArray().ToList();
        var volumes = quote.GetProperty("volume").EnumerateArray().ToList();

        var points = new List<OhlcvPoint>();
        for (int i = 0; i < timestamps.Count; i++)
        {
            if (closes[i].ValueKind == JsonValueKind.Null)
                continue;
            var dt = DateTimeOffset.FromUnixTimeSeconds(timestamps[i].GetInt64());
            var local = TimeZoneInfo.ConvertTime(dt, tz);
            points.Add(new OhlcvPoint(
                DateOnly.FromDateTime(local.Date),
                opens[i].ValueKind == JsonValueKind.Null ? 0m : opens[i].GetDecimal(),
                highs[i].ValueKind == JsonValueKind.Null ? 0m : highs[i].GetDecimal(),
                lows[i].ValueKind == JsonValueKind.Null ? 0m : lows[i].GetDecimal(),
                closes[i].GetDecimal(),
                volumes[i].ValueKind == JsonValueKind.Null ? 0L : volumes[i].GetInt64()));
        }
        return points;
    }
}
