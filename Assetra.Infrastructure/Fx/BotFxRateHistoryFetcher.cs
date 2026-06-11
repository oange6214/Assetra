using System.Globalization;
using System.Text;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.Infrastructure.Fx;

/// <summary>
/// Reads historical FX rates from Bank of Taiwan (台灣銀行) monthly CSV exports,
/// using the 即期買入 (spot-buy) column. BOT quotes every foreign currency
/// directly per 1 unit in TWD, so this fetcher only serves <c>foreign → TWD</c>
/// pairs.
///
/// <para>Endpoint shape (one month, one currency):
/// <c>https://rate.bot.com.tw/xrt/flcsv/0/{yyyy-MM}/{CUR}</c>
/// (e.g. <c>2026-06/USD</c>). One line per trading day, newest first.</para>
///
/// <para>Monthly CSV columns (0-indexed, note the extra leading 資料日期 column
/// vs. the daily export):
/// <c>[0]=資料日期(yyyyMMdd), [1]=幣別, [2]="本行買入", [3]=現金買入,
/// [4]=即期買入, …</c> → date = col[0], 即期買入 = col[4].</para>
///
/// <para>Any per-month network / HTTP / parse failure is swallowed and the
/// loop continues to the next month (a 404 for a future / very old month must
/// not abort the whole range). Overall the method never throws and surfaces
/// what it collected as an empty-or-partial list per
/// <see cref="IFxRateHistoryFetcher"/> contract.</para>
/// </summary>
public sealed class BotFxRateHistoryFetcher : IFxRateHistoryFetcher
{
    private readonly HttpClient _http;

    public BotFxRateHistoryFetcher(HttpClient http)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    public string SourceName => "bot";

    public async Task<IReadOnlyList<FxRateHistoryEntry>> FetchAsync(
        string fromCurrency, string toCurrency,
        DateOnly from, DateOnly to,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(fromCurrency) || string.IsNullOrWhiteSpace(toCurrency))
            return Array.Empty<FxRateHistoryEntry>();

        var fromUpper = fromCurrency.Trim().ToUpperInvariant();
        var toUpper = toCurrency.Trim().ToUpperInvariant();

        // BOT only quotes foreign → TWD; anything else (incl. same-currency) is empty.
        if (!string.Equals(toUpper, "TWD", StringComparison.Ordinal))
            return Array.Empty<FxRateHistoryEntry>();
        if (string.Equals(fromUpper, toUpper, StringComparison.Ordinal))
            return Array.Empty<FxRateHistoryEntry>();

        // Normalize date range: swap if reversed; both inclusive.
        if (from > to)
            (from, to) = (to, from);

        try
        {
            var entries = new List<FxRateHistoryEntry>();
            var now = DateTimeOffset.UtcNow;

            foreach (var (year, month) in EnumerateMonths(from, to))
            {
                if (ct.IsCancellationRequested)
                    break;

                var url = $"https://rate.bot.com.tw/xrt/flcsv/0/{year:D4}-{month:D2}/{fromUpper}";
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, url);
                    using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode)
                        continue; // 404 for a future/old month etc. — skip, keep going.

                    var bytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
                    var csv = Encoding.UTF8.GetString(bytes);
                    ParseMonthlyCsvInto(entries, csv, fromUpper, from, to, now);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    // Per-month failure is non-fatal — continue to the next month.
                }
            }

            return entries;
        }
        catch
        {
            // Per contract: swallow any failure, return empty so batch callers continue.
            return Array.Empty<FxRateHistoryEntry>();
        }
    }

    /// <summary>
    /// Distinct (year, month) pairs spanning the inclusive [from, to] range.
    /// </summary>
    private static IEnumerable<(int Year, int Month)> EnumerateMonths(DateOnly from, DateOnly to)
    {
        var cursor = new DateOnly(from.Year, from.Month, 1);
        var end = new DateOnly(to.Year, to.Month, 1);
        while (cursor <= end)
        {
            yield return (cursor.Year, cursor.Month);
            cursor = cursor.AddMonths(1);
        }
    }

    /// <summary>
    /// Parse a BOT monthly CSV (<c>/xrt/flcsv/0/{yyyy-MM}/{CUR}</c>) and append
    /// in-range rows to <paramref name="entries"/>.
    /// <para>Columns: [0]=資料日期(yyyyMMdd), [1]=幣別, [2]="本行買入",
    /// [3]=現金買入, [4]=即期買入, …. First line is a header (leading BOM) and
    /// is skipped. Rows whose 即期買入 is non-numeric / ≤ 0, or whose date is
    /// unparseable / outside [from, to], are skipped.</para>
    /// </summary>
    private void ParseMonthlyCsvInto(
        List<FxRateHistoryEntry> entries, string csv, string fromUpper,
        DateOnly from, DateOnly to, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(csv))
            return;

        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        // Line 0 is the header row (資料日期,幣別,匯率,…); skip it.
        for (int i = 1; i < lines.Length; i++)
        {
            var cols = lines[i].Split(',');
            if (cols.Length <= 4)
                continue;

            if (!DateOnly.TryParseExact(cols[0].Trim(), "yyyyMMdd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var date))
                continue;
            if (date < from || date > to)
                continue;

            if (!decimal.TryParse(cols[4].Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var spotBuy)
                || spotBuy <= 0m)
                continue;

            entries.Add(new FxRateHistoryEntry(date, fromUpper, "TWD", spotBuy, SourceName, now));
        }
    }
}
