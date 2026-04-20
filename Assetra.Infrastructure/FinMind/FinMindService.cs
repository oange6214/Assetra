using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Assetra.Core.Interfaces;

namespace Assetra.Infrastructure.FinMind;

/// <summary>
/// Implements <see cref="IFinMindService"/> — fetches daily close prices from FinMind API.
/// Only the TaiwanStockPrice dataset is used (close-price only, no institutional/margin data).
///
/// NOTE: The API token must be supplied at construction time. Until Assetra.WPF wires an
/// AppSettings-sourced token, pass <see cref="string.Empty"/> and the service will operate
/// anonymously (subject to FinMind's anonymous rate limits).
/// </summary>
public sealed class FinMindService : IFinMindService
{
    private readonly HttpClient _http;
    private readonly ILogger<FinMindService> _logger;

    // volatile so UpdateToken() is visible to all threads without a lock.
    private volatile string _token;

    // Cache keyed by (symbol, date). Daily close data does not change once the trading day ends.
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan EmptyCacheTtl = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<(string Symbol, DateOnly Date), (DateTime Expiry, decimal? Close)>
        _closeCache = new();

    private readonly FinMindApiStatus _status;
    public bool IsAvailable => _status.IsAvailable;

    public FinMindService(HttpClient http, string token, FinMindApiStatus status,
        ILogger<FinMindService>? logger = null)
    {
        _http = http;
        _token = token;
        _status = status;
        _logger = logger ?? NullLogger<FinMindService>.Instance;
    }

    /// <summary>
    /// Hot-swaps the API token without restarting the app.
    /// Flushes the close-price cache so the next request uses the new token immediately.
    /// </summary>
    public void UpdateToken(string newToken)
    {
        _token = newToken;
        _closeCache.Clear();
        _status.Reset();
    }

    /// <inheritdoc/>
    public async Task<decimal?> GetDailyCloseAsync(
        string symbol, DateOnly date, CancellationToken ct = default)
    {
        var key = (symbol, date);
        if (_closeCache.TryGetValue(key, out var cached) && DateTime.UtcNow < cached.Expiry)
            return cached.Close;

        var startDate = date.ToString("yyyy-MM-dd");
        var endDate = date.ToString("yyyy-MM-dd");
        var tokenSuffix = string.IsNullOrWhiteSpace(_token) ? "" : $"&token={_token}";
        var url = $"https://api.finmindtrade.com/api/v4/data" +
                  $"?dataset=TaiwanStockPrice&data_id={symbol}" +
                  $"&start_date={startDate}&end_date={endDate}{tokenSuffix}";

        try
        {
            using var resp = await _http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _status.MarkUnavailable();
                _logger.LogWarning("FinMind API returned HTTP {StatusCode} for {LogSafeUrl}",
                    (int)resp.StatusCode, MaskTokenInUrl(url));
                return null;
            }

            var json = await resp.Content.ReadAsStringAsync(ct);
            if (!IsFinMindResponseOk(json))
            {
                _status.MarkUnavailable();
                _logger.LogWarning("FinMind API returned non-200 status for {LogSafeUrl}",
                    MaskTokenInUrl(url));
                return null;
            }

            var close = ParseDailyClose(json, date);
            var ttl = close.HasValue ? CacheTtl : EmptyCacheTtl;
            _closeCache[key] = (DateTime.UtcNow.Add(ttl), close);
            return close;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FinMind daily-close fetch failed for {LogSafeUrl}",
                MaskTokenInUrl(url));
            return null;
        }
    }

    /// <summary>FinMind wraps errors as HTTP 200 with a JSON <c>status</c> field != 200.</summary>
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

    internal static decimal? ParseDailyClose(string json, DateOnly date)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var dataEl))
                return null;
            foreach (var row in dataEl.EnumerateArray())
            {
                if (!row.TryGetProperty("date", out var dateEl))
                    continue;
                if (!DateOnly.TryParse(dateEl.GetString(), out var rowDate) || rowDate != date)
                    continue;
                if (row.TryGetProperty("close", out var closeEl))
                    return closeEl.GetDecimal();
            }
            return null;
        }
        catch { return null; }
    }

    private static string MaskTokenInUrl(string url)
    {
        var idx = url.IndexOf("&token=", StringComparison.Ordinal);
        return idx < 0 ? url : string.Concat(url.AsSpan(0, idx), "&token=***");
    }
}
