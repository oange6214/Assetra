using Assetra.Core.Models.Analysis;

namespace Assetra.Application.Analysis;

/// <summary>
/// MultiCurrency-Reporting P4.5 — pure function that splits a realized P&amp;L
/// into market gain (USD if you bought AAPL) + FX gain (TWD impact of USD/TWD
/// moving between buy and sell).
///
/// <para>Zero infrastructure dependencies — all inputs are scalars so this
/// is trivially testable and the caller decides where the FX rates come
/// from (typically <c>IFxRateHistoryService.GetRateAsync</c> at the
/// buy-date and sell-date).</para>
/// </summary>
public static class RealizedPnlBreakdownCalculator
{
    /// <summary>
    /// Compute the breakdown for a closed lot. Pass weighted-average buy data
    /// when multiple lots feed one sell (e.g. FIFO matching).
    /// </summary>
    /// <param name="sellPriceNative">Sell price per share in instrument currency.</param>
    /// <param name="buyAvgPriceNative">(Weighted) average buy price per share, instrument currency.</param>
    /// <param name="quantity">Quantity sold (positive).</param>
    /// <param name="buyFxRate">Instrument→Base FX rate as-of the buy date. Null = missing data.</param>
    /// <param name="sellFxRate">Instrument→Base FX rate as-of the sell date. Null = missing data.</param>
    /// <returns>
    /// The breakdown, or null when:
    /// <list type="bullet">
    ///   <item><paramref name="quantity"/> &lt;= 0</item>
    ///   <item>Mixed-currency case (rates ≠ 1.0 expected) where either fx rate is null</item>
    /// </list>
    /// For same-currency trades (both fx rates supplied as 1.0 or both null), the
    /// calculator returns a breakdown with <see cref="RealizedPnlBreakdown.FxBase"/> = 0.
    /// </returns>
    public static RealizedPnlBreakdown? Compute(
        decimal sellPriceNative,
        decimal buyAvgPriceNative,
        int quantity,
        decimal? buyFxRate,
        decimal? sellFxRate)
    {
        if (quantity <= 0) return null;

        // Same-currency convention: caller passes 1.0 for both, or null for both.
        // Either way the fx component is 0 and total = market.
        var bothNull = buyFxRate is null && sellFxRate is null;
        var oneNull = buyFxRate is null ^ sellFxRate is null;
        if (oneNull) return null; // mixed-currency with insufficient FX data — caller renders "—"

        var buyFx = buyFxRate ?? 1m;
        var sellFx = sellFxRate ?? 1m;

        var marketNative = (sellPriceNative - buyAvgPriceNative) * quantity;
        var marketBase = marketNative * sellFx;
        var buyCostNative = buyAvgPriceNative * quantity;
        var fxBase = bothNull
            ? 0m
            : buyCostNative * (sellFx - buyFx);
        var totalBase = marketBase + fxBase;

        return new RealizedPnlBreakdown(
            MarketNative: marketNative,
            MarketBase: marketBase,
            FxBase: fxBase,
            TotalBase: totalBase);
    }
}
