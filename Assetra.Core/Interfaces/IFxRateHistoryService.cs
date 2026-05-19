namespace Assetra.Core.Interfaces;

/// <summary>
/// Application-layer FX history accessor. Wraps <see cref="IFxRateHistoryRepository"/>
/// with caching + nearest-date fallback semantics so callers (reports / snapshot
/// projection / P&amp;L decomposition) don't have to manage either concern themselves.
///
/// <para>If <paramref name="date"/> doesn't have an exact-match row, the service
/// silently falls back to the most recent rate within a 7-day lookback. Use the
/// repository directly if you need exact-match semantics.</para>
/// </summary>
public interface IFxRateHistoryService
{
    /// <summary>
    /// Get the FX rate as-of <paramref name="date"/>. Same-currency lookups
    /// short-circuit to 1.0. Returns null if no rate is available within the
    /// fallback window.
    /// </summary>
    Task<decimal?> GetRateAsync(
        DateOnly date, string fromCurrency, string toCurrency, CancellationToken ct = default);
}
