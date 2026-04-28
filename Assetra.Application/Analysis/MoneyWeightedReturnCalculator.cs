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
/// before XIRR. Snapshots' <c>MarketValue</c> are assumed to be already in base currency.
/// If any rate is missing, the method returns null rather than producing a misleading IRR.
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

        var withSynthetic = new List<CashFlow>(flows);
        if (startSnap is { MarketValue: > 0 })
            withSynthetic.Insert(0, new CashFlow(period.Start, -startSnap.MarketValue));
        if (endSnap is { MarketValue: > 0 })
            withSynthetic.Add(new CashFlow(period.End, endSnap.MarketValue));

        return _xirr.Compute(withSynthetic);
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
