using System.Text.Json;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.Infrastructure.Fx;

/// <summary>
/// Reads historical FX rates from Yahoo Finance's v8 chart endpoint. Yahoo
/// exposes pairs under the symbol pattern <c>{from}{to}=X</c>
/// (e.g. <c>USDTWD=X</c>); the chart endpoint returns OHLC + timestamps
/// keyed by UTC seconds.
///
/// <para>Endpoint shape:
/// <c>https://query1.finance.yahoo.com/v8/finance/chart/USDTWD=X?period1={unix}&amp;period2={unix}&amp;interval=1d</c>
/// </para>
///
/// <para>Any network / HTTP / JSON failure is swallowed and surfaced as an
/// empty list per <see cref="IFxRateHistoryFetcher"/> contract — callers
/// (typically a batch backfill loop) should NOT have a single bad pair
/// abort the entire ingest.</para>
/// </summary>
public sealed class YahooFxRateHistoryFetcher : IFxRateHistoryFetcher
{
    private readonly HttpClient _http;

    public YahooFxRateHistoryFetcher(HttpClient http)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    public string SourceName => "yahoo";

    public async Task<IReadOnlyList<FxRateHistoryEntry>> FetchAsync(
        string fromCurrency, string toCurrency,
        DateOnly from, DateOnly to,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(fromCurrency) || string.IsNullOrWhiteSpace(toCurrency))
            return Array.Empty<FxRateHistoryEntry>();

        var fromUpper = fromCurrency.Trim().ToUpperInvariant();
        var toUpper = toCurrency.Trim().ToUpperInvariant();
        if (string.Equals(fromUpper, toUpper, StringComparison.Ordinal))
            return Array.Empty<FxRateHistoryEntry>();

        // Normalize date range: swap if reversed; both inclusive.
        if (from > to) (from, to) = (to, from);

        try
        {
            // Yahoo uses unix-seconds period1/period2. Add a one-day buffer on
            // each side so the endpoint reliably returns boundary days.
            var period1 = new DateTimeOffset(from.AddDays(-1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();
            var period2 = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero).ToUnixTimeSeconds();
            var symbol = $"{fromUpper}{toUpper}=X";
            var url = $"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(symbol)}?period1={period1}&period2={period2}&interval=1d";

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("Accept", "application/json");
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return Array.Empty<FxRateHistoryEntry>();

            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

            return ParseEntries(doc.RootElement, fromUpper, toUpper);
        }
        catch
        {
            // Per contract: swallow any failure, return empty so batch callers continue.
            return Array.Empty<FxRateHistoryEntry>();
        }
    }

    /// <summary>
    /// Yahoo response shape (chart.result[0].timestamp + chart.result[0].indicators.quote[0].close):
    /// <code>
    /// {
    ///   "chart": {
    ///     "result": [{
    ///       "timestamp": [1735603200, 1735689600, ...],
    ///       "indicators": { "quote": [{ "close": [31.5, 31.7, null, ...] }] }
    ///     }]
    ///   }
    /// }
    /// </code>
    /// Some days have null close (provider gap) — we skip those entries.
    /// </summary>
    private List<FxRateHistoryEntry> ParseEntries(JsonElement root, string from, string to)
    {
        var entries = new List<FxRateHistoryEntry>();
        if (!root.TryGetProperty("chart", out var chart)) return entries;
        if (!chart.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Array) return entries;
        if (result.GetArrayLength() == 0) return entries;
        var first = result[0];

        if (!first.TryGetProperty("timestamp", out var timestamps) || timestamps.ValueKind != JsonValueKind.Array)
            return entries;
        if (!first.TryGetProperty("indicators", out var indicators)) return entries;
        if (!indicators.TryGetProperty("quote", out var quotes) || quotes.ValueKind != JsonValueKind.Array || quotes.GetArrayLength() == 0)
            return entries;
        if (!quotes[0].TryGetProperty("close", out var closes) || closes.ValueKind != JsonValueKind.Array)
            return entries;

        var count = Math.Min(timestamps.GetArrayLength(), closes.GetArrayLength());
        var now = DateTimeOffset.UtcNow;
        for (int i = 0; i < count; i++)
        {
            var tsEl = timestamps[i];
            var closeEl = closes[i];
            if (tsEl.ValueKind != JsonValueKind.Number) continue;
            if (closeEl.ValueKind != JsonValueKind.Number) continue; // null / gap day
            var unixSec = tsEl.GetInt64();
            var rate = closeEl.GetDouble();
            if (rate <= 0 || double.IsNaN(rate) || double.IsInfinity(rate)) continue;

            var date = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(unixSec).UtcDateTime);
            entries.Add(new FxRateHistoryEntry(date, from, to, (decimal)rate, SourceName, now));
        }
        return entries;
    }
}
