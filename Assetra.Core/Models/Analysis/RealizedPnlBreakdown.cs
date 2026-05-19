namespace Assetra.Core.Models.Analysis;

/// <summary>
/// Decomposition of a realized P&amp;L into the two independently-interesting
/// components: market move (was your stock pick right?) vs FX move (did the
/// currency drift work in your favor?). Both are interesting separately —
/// investors want to know which contribution is investing skill vs FX timing.
///
/// <para>Formula (see <c>docs/planning/MultiCurrency-Reporting.md</c> P4.5):
/// <code>
/// market_native = (sell_price - buy_avg_price) × qty                   [instrument ccy]
/// market_base   = market_native × sell_fx_rate                         [base ccy]
/// fx_base       = buy_cost_native × (sell_fx_rate - buy_fx_rate)       [base ccy]
/// total_base    = market_base + fx_base
/// </code>
/// </para>
///
/// <para>Same-currency trades produce <see cref="FxBase"/> = 0 because both
/// fx rates are 1.0. Mixed-currency trades with missing FX history produce
/// a null <see cref="RealizedPnlBreakdown"/> (caller renders "—").</para>
/// </summary>
/// <param name="MarketNative">Market gain in instrument currency.</param>
/// <param name="MarketBase">Market gain projected to base currency at sell-date rate.</param>
/// <param name="FxBase">Pure FX gain in base currency (zero for same-ccy trades).</param>
/// <param name="TotalBase">MarketBase + FxBase — should equal the trade's
/// realized PnL stored in <c>Trade.RealizedPnl</c> ± rounding noise.</param>
public sealed record RealizedPnlBreakdown(
    decimal MarketNative,
    decimal MarketBase,
    decimal FxBase,
    decimal TotalBase);
