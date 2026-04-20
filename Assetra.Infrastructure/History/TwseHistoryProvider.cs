using System.Collections.Concurrent;
using System.Text.Json;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.Infrastructure.History;

internal sealed class TwseHistoryProvider : IStockHistoryProvider
{
    private readonly HttpClient _http;

    // key: (symbol, exchange, period)  value: (expiry, data)
    private readonly ConcurrentDictionary<(string, string, ChartPeriod), (DateTime Expiry, IReadOnlyList<OhlcvPoint> Data)>
        _cache = new();

    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    public TwseHistoryProvider(HttpClient http) => _http = http;

    public async Task<IReadOnlyList<OhlcvPoint>> GetHistoryAsync(
        string symbol, string exchange, ChartPeriod period, CancellationToken ct = default)
    {
        var key = (symbol, exchange, period);
        if (_cache.TryGetValue(key, out var cached) && DateTime.UtcNow < cached.Expiry)
            return cached.Data;

        var months = period switch
        {
            ChartPeriod.OneMonth => 1,
            ChartPeriod.ThreeMonths => 3,
            ChartPeriod.OneYear => 12,
            ChartPeriod.TwoYears => 24,
            _ => 3
        };

        var today = DateTime.Today;

        // Fetch all months in parallel
        var tasks = Enumerable.Range(0, months)
            .Select(i => today.AddMonths(-(months - 1 - i)))
            .Select(target => FetchMonthSafeAsync(symbol, exchange, target, ct))
            .ToList();

        var results = await Task.WhenAll(tasks);

        var data = results
            .SelectMany(r => r)
            .DistinctBy(p => p.Date)
            .OrderBy(p => p.Date)
            .ToList();

        _cache[key] = (DateTime.UtcNow.Add(CacheTtl), data);
        return data;
    }

    private async Task<IReadOnlyList<OhlcvPoint>> FetchMonthSafeAsync(
        string symbol, string exchange, DateTime month, CancellationToken ct)
    {
        try
        {
            return exchange == "TPEX"
                ? await FetchTpexMonthAsync(symbol, month, ct)
                : await FetchTwseMonthAsync(symbol, month, ct);
        }
        catch
        {
            return [];
        }
    }

    private async Task<IReadOnlyList<OhlcvPoint>> FetchTwseMonthAsync(
        string symbol, DateTime month, CancellationToken ct)
    {
        var dateStr = month.ToString("yyyyMMdd");
        var url = $"https://www.twse.com.tw/exchangeReport/STOCK_DAY?response=json&date={dateStr}&stockNo={symbol}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Referrer = new Uri("https://www.twse.com.tw/zh/");
        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode)
            return [];
        var json = await resp.Content.ReadAsStringAsync(ct);
        return ParseTwseResponse(json);
    }

    private async Task<IReadOnlyList<OhlcvPoint>> FetchTpexMonthAsync(
        string symbol, DateTime month, CancellationToken ct)
    {
        var rocYear = month.Year - 1911;
        var rocDate = $"{rocYear}/{month.Month:D2}";
        var url = $"https://www.tpex.org.tw/web/stock/aftertrading/daily_trading_info/st43.php" +
                  $"?l=zh-tw&d={Uri.EscapeDataString(rocDate)}&stkno={symbol}&s=0,asc,0&o=json";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Referrer = new Uri("https://www.tpex.org.tw/");
        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode)
            return [];
        var json = await resp.Content.ReadAsStringAsync(ct);
        return ParseTpexResponse(json);
    }

    internal static IReadOnlyList<OhlcvPoint> ParseTwseResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);

        // TWSE returns {"stat":"UNAUTHORIZED"} or {"stat":"查詢日期小於..."} when blocked / out of range
        if (doc.RootElement.TryGetProperty("stat", out var stat)
            && stat.GetString() is string s
            && !s.Equals("OK", StringComparison.OrdinalIgnoreCase))
            return [];

        if (!doc.RootElement.TryGetProperty("data", out var data))
            return [];

        var points = new List<OhlcvPoint>();
        foreach (var row in data.EnumerateArray())
        {
            var cols = row.EnumerateArray().Select(e => e.GetString() ?? "").ToArray();
            if (cols.Length < 9)
                continue;
            if (!TryParseRocDate(cols[0], out var date))
                continue;
            if (!TryParseDecimal(cols[6], out var close))
                continue;
            TryParseDecimal(cols[3], out var open);
            TryParseDecimal(cols[4], out var high);
            TryParseDecimal(cols[5], out var low);
            TryParseLong(cols[1], out var volume);
            points.Add(new OhlcvPoint(date, open, high, low, close, volume));
        }
        return points;
    }

    internal static IReadOnlyList<OhlcvPoint> ParseTpexResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("aaData", out var data))
            return [];

        var points = new List<OhlcvPoint>();
        foreach (var row in data.EnumerateArray())
        {
            var cols = row.EnumerateArray().Select(e => e.GetString() ?? "").ToArray();
            if (cols.Length < 9)
                continue;
            if (!TryParseRocDate(cols[0], out var date))
                continue;
            if (!TryParseDecimal(cols[6], out var close))
                continue;
            TryParseDecimal(cols[3], out var open);
            TryParseDecimal(cols[4], out var high);
            TryParseDecimal(cols[5], out var low);
            TryParseLong(cols[1], out var volume);
            points.Add(new OhlcvPoint(date, open, high, low, close, volume));
        }
        return points;
    }

    private static bool TryParseRocDate(string roc, out DateOnly date)
    {
        date = default;
        var parts = roc.Trim().Split('/');
        if (parts.Length != 3)
            return false;
        if (!int.TryParse(parts[0], out int year))
            return false;
        if (!int.TryParse(parts[1], out int month))
            return false;
        if (!int.TryParse(parts[2], out int day))
            return false;
        date = new DateOnly(year + 1911, month, day);
        return true;
    }

    private static bool TryParseDecimal(string s, out decimal value) =>
        decimal.TryParse(s.Replace(",", ""), out value);

    private static bool TryParseLong(string s, out long value) =>
        long.TryParse(s.Replace(",", ""), out value);
}
