using System.Collections.Concurrent;
using System.Text.Json;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.Infrastructure.FinMind;

namespace Assetra.Infrastructure.History;

internal sealed class FinMindHistoryProvider : IStockHistoryProvider
{
    private readonly HttpClient _http;
    private readonly FinMindService _finMind;
    private readonly FinMindApiStatus _status;

    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan EmptyCacheTtl = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<(string, ChartPeriod), (DateTime Expiry, IReadOnlyList<OhlcvPoint> Data)>
        _cache = new();

    public FinMindHistoryProvider(HttpClient http, FinMindService finMind, FinMindApiStatus status)
    {
        _http = http;
        _finMind = finMind;
        _status = status;
    }

    public async Task<IReadOnlyList<OhlcvPoint>> GetHistoryAsync(
        string symbol, string exchange, ChartPeriod period, CancellationToken ct = default)
    {
        var key = (symbol, period);
        if (_cache.TryGetValue(key, out var cached) && DateTime.UtcNow < cached.Expiry)
            return cached.Data;

        try
        {
            // Token is managed by FinMindService; use empty string for anonymous access
            // until WPF wires a token source via FinMindService.UpdateToken().
            var token = string.Empty;
            var months = period switch
            {
                ChartPeriod.OneMonth => 1,
                ChartPeriod.ThreeMonths => 3,
                ChartPeriod.OneYear => 12,
                ChartPeriod.TwoYears => 24,
                _ => 3
            };
            var startDate = DateTime.Today.AddMonths(-months).ToString("yyyy-MM-dd");
            var endDate = DateTime.Today.ToString("yyyy-MM-dd");

            var url = $"https://api.finmindtrade.com/api/v4/data" +
                      $"?dataset=TaiwanStockPrice&data_id={symbol}" +
                      $"&start_date={startDate}&end_date={endDate}" +
                      (string.IsNullOrWhiteSpace(token) ? "" : $"&token={token}");

            using var resp = await _http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _status.MarkUnavailable();   // HTTP 4xx/5xx
                return [];
            }

            var json = await resp.Content.ReadAsStringAsync(ct);
            if (!IsFinMindResponseOk(json))
            {
                _status.MarkUnavailable();   // JSON status != 200 — quota exceeded
                return [];
            }

            var data = ParseResponse(json);
            var ttl = data.Count > 0 ? CacheTtl : EmptyCacheTtl;
            _cache[key] = (DateTime.UtcNow.Add(ttl), data);
            return data;
        }
        catch
        {
            return [];
        }
    }

    private static bool IsFinMindResponseOk(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("status", out var s))
                return s.GetInt32() == 200;
            return true;
        }
        catch { return true; }
    }

    internal static IReadOnlyList<OhlcvPoint> ParseResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var dataEl))
            return [];

        var points = new List<OhlcvPoint>();
        foreach (var row in dataEl.EnumerateArray())
        {
            if (!row.TryGetProperty("date", out var dateEl))
                continue;
            if (!row.TryGetProperty("close", out var closeEl))
                continue;

            if (!DateOnly.TryParse(dateEl.GetString(), out var date))
                continue;

            var close = closeEl.GetDecimal();
            var open = row.TryGetProperty("open", out var o) ? o.GetDecimal() : close;
            var high = row.TryGetProperty("max", out var h) ? h.GetDecimal() : close;
            var low = row.TryGetProperty("min", out var l) ? l.GetDecimal() : close;
            var volume = row.TryGetProperty("Trading_Volume", out var v) ? v.GetInt64() : 0L;

            points.Add(new OhlcvPoint(date, open, high, low, close, volume));
        }

        return points.OrderBy(p => p.Date).ToList();
    }
}
