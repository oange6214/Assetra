using System.Text.Json;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Microsoft.Extensions.Logging;

namespace Assetra.Infrastructure.Http;

internal sealed class FugleClient(
    HttpClient http,
    IAppSettingsService settings,
    ILogger<FugleClient> logger)
{
    private const string BaseUrl = "https://api.fugle.tw/marketdata/v1.0/stock";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(settings.Current.FugleApiKey);

    public async Task<StockQuote?> FetchQuoteAsync(string symbol, CancellationToken ct = default)
    {
        var apiKey = settings.Current.FugleApiKey?.Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
            return null;

        using var req = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/intraday/quote/{symbol}");
        req.Headers.TryAddWithoutValidation("X-API-KEY", apiKey);

        try
        {
            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode)
                return null;

            var json = await resp.Content.ReadAsStringAsync(ct);
            return ParseQuote(json);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Fugle quote fetch failed for {Symbol}", symbol);
            return null;
        }
    }

    public async Task<IReadOnlyList<OhlcvPoint>> FetchDailyHistoryAsync(
        string symbol,
        ChartPeriod period,
        CancellationToken ct = default)
    {
        var apiKey = settings.Current.FugleApiKey?.Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
            return [];

        var from = DateTime.Today.AddMonths(period switch
        {
            ChartPeriod.OneMonth => -1,
            ChartPeriod.ThreeMonths => -3,
            ChartPeriod.OneYear => -12,
            ChartPeriod.TwoYears => -24,
            _ => -3,
        }).ToString("yyyy-MM-dd");
        var to = DateTime.Today.ToString("yyyy-MM-dd");

        var url = $"{BaseUrl}/historical/candles/{symbol}" +
                  $"?from={from}&to={to}&timeframe=D&sort=asc&fields=open,high,low,close,volume,change";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("X-API-KEY", apiKey);

        try
        {
            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode)
                return [];

            var json = await resp.Content.ReadAsStringAsync(ct);
            return ParseCandles(json);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Fugle candle fetch failed for {Symbol}", symbol);
            return [];
        }
    }

    internal static StockQuote? ParseQuote(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("symbol", out var symbolEl))
            return null;

        var symbol = symbolEl.GetString() ?? string.Empty;
        var name = root.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? string.Empty : string.Empty;
        var exchange = root.TryGetProperty("exchange", out var exchangeEl) ? exchangeEl.GetString() ?? string.Empty : string.Empty;
        var prevClose = GetDecimal(root, "previousClose");
        var reference = GetDecimal(root, "referencePrice");
        var price = FirstNonZero(
            GetDecimal(root, "lastPrice"),
            GetDecimal(root, "closePrice"),
            prevClose,
            reference);
        var change = root.TryGetProperty("change", out var changeEl) && changeEl.ValueKind != JsonValueKind.Null
            ? changeEl.GetDecimal()
            : price - (prevClose != 0 ? prevClose : reference);
        var changePct = root.TryGetProperty("changePercent", out var cpEl) && cpEl.ValueKind != JsonValueKind.Null
            ? cpEl.GetDecimal()
            : (prevClose != 0 ? Math.Round(change / prevClose * 100m, 2) : 0m);

        long volume = 0L;
        if (root.TryGetProperty("total", out var totalEl)
            && totalEl.TryGetProperty("tradeVolume", out var volEl)
            && volEl.ValueKind != JsonValueKind.Null)
            volume = volEl.GetInt64();

        var updatedAt = DateTimeOffset.Now;
        if (root.TryGetProperty("lastUpdated", out var updatedEl) && updatedEl.ValueKind != JsonValueKind.Null)
        {
            var micros = updatedEl.GetInt64();
            updatedAt = DateTimeOffset.FromUnixTimeMilliseconds(micros / 1000);
        }

        return new StockQuote(
            symbol,
            name,
            exchange,
            price,
            change,
            changePct,
            volume,
            GetDecimal(root, "openPrice"),
            GetDecimal(root, "highPrice"),
            GetDecimal(root, "lowPrice"),
            prevClose != 0 ? prevClose : reference,
            updatedAt);
    }

    internal static IReadOnlyList<OhlcvPoint> ParseCandles(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var dataEl) || dataEl.ValueKind != JsonValueKind.Array)
            return [];

        var list = new List<OhlcvPoint>();
        foreach (var item in dataEl.EnumerateArray())
        {
            if (!item.TryGetProperty("date", out var dateEl)
                || !DateOnly.TryParse(dateEl.GetString(), out var date))
                continue;

            var open = GetDecimal(item, "open");
            var high = GetDecimal(item, "high");
            var low = GetDecimal(item, "low");
            var close = GetDecimal(item, "close");
            long volume = 0;
            if (item.TryGetProperty("volume", out var volEl) && volEl.ValueKind != JsonValueKind.Null)
                volume = volEl.GetInt64();

            list.Add(new OhlcvPoint(date, open, high, low, close, volume));
        }

        return list;
    }

    private static decimal GetDecimal(JsonElement obj, string propertyName)
    {
        if (!obj.TryGetProperty(propertyName, out var el) || el.ValueKind == JsonValueKind.Null)
            return 0m;
        return el.GetDecimal();
    }

    private static decimal FirstNonZero(params decimal[] values) =>
        values.FirstOrDefault(v => v != 0m);
}
