using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.Infrastructure.Fx;

/// <summary>
/// MultiCurrency-Reporting P4.1e — decorator that tries the historical FX
/// store (populated daily by the Yahoo fetcher) before falling back to the
/// legacy <see cref="IFxRateProvider"/> (manual-entry / cached live rates).
///
/// <para>Priority: history first because for past dates it's authoritative
/// (the rate that actually existed on that day); legacy fallback handles
/// the cases where:
/// <list type="bullet">
///   <item>The history table is empty (new install, first 5-sec window
///     before the auto-refresh fires)</item>
///   <item>The user manually entered a rate that should override Yahoo
///     (e.g. their broker's reported rate on a complex 複委託 trade)</item>
///   <item>Yahoo doesn't quote the pair (exotic currency)</item>
/// </list>
/// </para>
///
/// <para>This is a pure decorator — no callers change. Just re-register
/// <see cref="IFxRateProvider"/> to resolve to this type.</para>
/// </summary>
public sealed class HybridFxRateProvider : IFxRateProvider
{
    private readonly IFxRateHistoryService _history;
    private readonly IFxRateProvider _legacy;

    public HybridFxRateProvider(IFxRateHistoryService history, IFxRateProvider legacy)
    {
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _legacy = legacy ?? throw new ArgumentNullException(nameof(legacy));
    }

    public async Task<decimal?> GetRateAsync(string from, string to, DateOnly asOf, CancellationToken ct = default)
    {
        // History service short-circuits same-currency to 1.0 and missing-input to null.
        var hist = await _history.GetRateAsync(asOf, from, to, ct).ConfigureAwait(false);
        if (hist is not null)
            return hist;

        // Fall back to the legacy provider (manual / cached live rates).
        return await _legacy.GetRateAsync(from, to, asOf, ct).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<FxRate>> GetHistoricalSeriesAsync(
        string from, string to, DateOnly start, DateOnly end, CancellationToken ct = default)
    {
        // Historical series remains the legacy responsibility — fx_rate_history
        // could grow a parallel implementation later, but TWR / cash-flow ranges
        // don't use this hot enough to warrant duplicating the contract today.
        return _legacy.GetHistoricalSeriesAsync(from, to, start, end, ct);
    }
}
