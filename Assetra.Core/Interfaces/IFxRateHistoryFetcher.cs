using Assetra.Core.Models;

namespace Assetra.Core.Interfaces;

/// <summary>
/// External source for historical FX quotes. Implementations call out to a
/// provider (Yahoo Finance, ECB, etc.) and translate the response into
/// <see cref="FxRateHistoryEntry"/> rows ready to feed
/// <see cref="IFxRateHistoryRepository.UpsertRangeAsync"/>.
///
/// <para>Contract:
/// <list type="bullet">
///   <item>Same-currency pairs (e.g. <c>USD → USD</c>) return an empty list
///     without making a network call.</item>
///   <item>Any HTTP / JSON / network failure must be caught internally and
///     returned as an empty list — never throw to the caller. This lets a
///     batch fetcher loop over multiple currency pairs without one bad pair
///     aborting the entire backfill.</item>
///   <item>The <see cref="FxRateHistoryEntry.IngestedAt"/> field is stamped
///     with <c>DateTimeOffset.UtcNow</c> when the entry is constructed.</item>
/// </list>
/// </para>
/// </summary>
public interface IFxRateHistoryFetcher
{
    /// <summary>
    /// Fetch daily FX rates for the given currency pair across an inclusive
    /// date range. Weekends / holidays are silently skipped by the upstream
    /// provider (Yahoo doesn't quote on Saturday/Sunday).
    /// </summary>
    /// <param name="fromCurrency">From-side ISO 4217 (e.g. "USD").</param>
    /// <param name="toCurrency">To-side ISO 4217 (e.g. "TWD").</param>
    /// <param name="from">Inclusive start date.</param>
    /// <param name="to">Inclusive end date.</param>
    Task<IReadOnlyList<FxRateHistoryEntry>> FetchAsync(
        string fromCurrency, string toCurrency,
        DateOnly from, DateOnly to,
        CancellationToken ct = default);

    /// <summary>Stable name of the source — used for the <c>source</c> column.</summary>
    string SourceName { get; }
}
