using Assetra.Application.Fx;
using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Analysis;
using Assetra.Core.Models;
using Assetra.Core.Models.Analysis;

namespace Assetra.Application.Analysis;

/// <summary>
/// MWR for portfolio: external investment flows (Buy outflow, Sell inflow, CashDividend inflow)
/// + synthetic starting position outflow + synthetic terminal market-value inflow → XIRR.
///
/// <para>
/// Cross-currency (v0.14.1): when <see cref="IPortfolioRepository"/>,
/// <see cref="IMultiCurrencyValuationService"/>, and <see cref="IAppSettingsService"/> are all
/// available, each trade's flow is tagged with the owning <c>PortfolioEntry.Currency</c> and
/// converted to <c>AppSettings.BaseCurrency</c> via FX (using the trade date as <c>asOf</c>)
/// before XIRR. If any rate is missing, the method returns null rather than producing a
/// misleading IRR.
/// </para>
/// <para>
/// v0.14.2: snapshots are also currency-checked. <see cref="PortfolioDailySnapshot.Currency"/>
/// is matched against <c>BaseCurrency</c>; mismatched snapshots are converted via FX using the
/// snapshot's date as <c>asOf</c>. Missing rate → returns null (same conservative policy as flows).
/// </para>
/// </summary>
public sealed class MoneyWeightedReturnCalculator : IMoneyWeightedReturnCalculator
{
    private readonly ITradeRepository _trades;
    private readonly IPortfolioSnapshotRepository _snapshots;
    private readonly IXirrCalculator _xirr;
    private readonly IPortfolioRepository? _portfolio;
    private readonly IMultiCurrencyValuationService? _fx;
    private readonly IAppSettingsService? _settings;

    public MoneyWeightedReturnCalculator(
        ITradeRepository trades,
        IPortfolioSnapshotRepository snapshots,
        IXirrCalculator xirr,
        IPortfolioRepository? portfolio = null,
        IMultiCurrencyValuationService? fx = null,
        IAppSettingsService? settings = null)
    {
        _trades = trades;
        _snapshots = snapshots;
        _xirr = xirr;
        _portfolio = portfolio;
        _fx = fx;
        _settings = settings;
    }

    public async Task<decimal?> ComputeAsync(PerformancePeriod period, CancellationToken ct = default)
    {
        var all = await _trades.GetAllAsync().ConfigureAwait(false);
        var entryCurrencyMap = await BuildEntryCurrencyMapAsync().ConfigureAwait(false);
        var flows = BuildFlows(all, period, entryCurrencyMap);

        flows = await ConvertToBaseAsync(flows, ct).ConfigureAwait(false);
        if (flows is null) return null;

        var startSnap = await _snapshots.GetSnapshotAsync(period.Start).ConfigureAwait(false);
        var endSnap = await _snapshots.GetSnapshotAsync(period.End).ConfigureAwait(false);

        var startMv = await ConvertSnapshotMarketValueAsync(startSnap, ct).ConfigureAwait(false);
        if (startSnap is not null && startMv is null) return null;
        var endMv = await ConvertSnapshotMarketValueAsync(endSnap, ct).ConfigureAwait(false);
        if (endSnap is not null && endMv is null) return null;

        var withSynthetic = new List<CashFlow>(flows);
        if (startMv is > 0m)
            withSynthetic.Insert(0, new CashFlow(period.Start, -startMv.Value));
        if (endMv is > 0m)
            withSynthetic.Add(new CashFlow(period.End, endMv.Value));

        return _xirr.Compute(withSynthetic);
    }

    /// <summary>
    /// Returns snapshot.MarketValue converted to AppSettings.BaseCurrency.
    /// Returns null when a conversion is required but the FX rate is unavailable;
    /// caller treats this as "abandon — would produce misleading IRR".
    /// When no conversion is possible (no FX or no settings), passes through native value.
    /// </summary>
    private async Task<decimal?> ConvertSnapshotMarketValueAsync(
        PortfolioDailySnapshot? snap, CancellationToken ct)
    {
        if (snap is null) return null;
        var baseCcy = _settings?.Current.BaseCurrency;
        if (_fx is null || string.IsNullOrWhiteSpace(baseCcy)) return snap.MarketValue;
        var snapCcy = string.IsNullOrWhiteSpace(snap.Currency) ? "TWD" : snap.Currency;
        if (string.Equals(snapCcy, baseCcy, StringComparison.OrdinalIgnoreCase))
            return snap.MarketValue;
        return await _fx.ConvertAsync(snap.MarketValue, snapCcy, baseCcy, snap.SnapshotDate, ct)
            .ConfigureAwait(false);
    }

    public async Task<decimal?> ComputeForEntryAsync(Guid portfolioEntryId, PerformancePeriod period, CancellationToken ct = default)
    {
        var all = await _trades.GetAllAsync().ConfigureAwait(false);
        var entryCurrencyMap = await BuildEntryCurrencyMapAsync().ConfigureAwait(false);
        var entryTrades = all.Where(t => t.PortfolioEntryId == portfolioEntryId).ToList();
        var flows = BuildFlows(entryTrades, period, entryCurrencyMap);

        flows = await ConvertToBaseAsync(flows, ct).ConfigureAwait(false);
        if (flows is null) return null;
        if (flows.Count < 2) return null;
        return _xirr.Compute(flows);
    }

    private async Task<IReadOnlyDictionary<Guid, string>> BuildEntryCurrencyMapAsync()
    {
        if (_portfolio is null) return new Dictionary<Guid, string>();
        var entries = await _portfolio.GetEntriesAsync().ConfigureAwait(false);
        return entries.ToDictionary(e => e.Id, e => string.IsNullOrWhiteSpace(e.Currency) ? string.Empty : e.Currency);
    }

    private async Task<List<CashFlow>?> ConvertToBaseAsync(List<CashFlow> flows, CancellationToken ct)
    {
        var baseCcy = _settings?.Current.BaseCurrency;
        if (_fx is null || string.IsNullOrWhiteSpace(baseCcy)) return flows;
        var converted = await MultiCurrencyCashFlowConverter.ConvertAllAsync(flows, baseCcy, _fx, ct).ConfigureAwait(false);
        return converted is null ? null : converted.ToList();
    }

    private static List<CashFlow> BuildFlows(
        IReadOnlyList<Trade> trades,
        PerformancePeriod period,
        IReadOnlyDictionary<Guid, string> entryCurrency)
    {
        var flows = new List<CashFlow>();
        foreach (var t in trades)
        {
            var d = DateOnly.FromDateTime(t.TradeDate);
            if (d < period.Start || d > period.End) continue;

            var amt = t.Type switch
            {
                TradeType.Buy => -((decimal)t.Quantity * t.Price + (t.Commission ?? 0m)),
                TradeType.Sell => (decimal)t.Quantity * t.Price - (t.Commission ?? 0m),
                TradeType.CashDividend => t.CashAmount ?? (decimal)t.Quantity * t.Price,
                _ => 0m,
            };
            if (amt == 0m) continue;

            string? ccy = null;
            if (t.PortfolioEntryId is { } eid && entryCurrency.TryGetValue(eid, out var c) && !string.IsNullOrWhiteSpace(c))
                ccy = c;

            flows.Add(new CashFlow(d, amt, ccy));
        }
        return flows;
    }
}
