namespace Assetra.Core.Trading;

/// <summary>
/// Calculates Taiwan stock exchange transaction fees.
///
/// Rate reference (2024):
///   手續費 (Brokerage Commission) — both buy and sell
///     Standard rate : 0.1425 % of transaction value
///     Minimum       : NT$20 per transaction
///     Discount      : brokers typically offer 6折 (60 %), yielding 0.0855 %
///
///   證券交易稅 (Securities Transaction Tax) — sell only
///     普通股 (Stock)  : 0.30 % (千分之三)
///     ETF             : 0.10 % (千分之一)   ← ETF listed on TWSE/TPEX
///
///   Capital gains tax : none in Taiwan (suspended indefinitely as of 2024)
/// </summary>
public static class TaiwanTradeFeeCalculator
{
    /// <summary>Standard brokerage rate: 0.1425 %.</summary>
    public const decimal StandardCommissionRate = 0.001425m;

    /// <summary>Minimum brokerage fee per transaction: NT$20.</summary>
    public const decimal MinCommission = 20m;

    /// <summary>Securities transaction tax rate for ordinary stocks: 0.3 %.</summary>
    public const decimal StockTaxRate = 0.003m;

    /// <summary>Securities transaction tax rate for ETFs: 0.1 %.</summary>
    public const decimal EtfTaxRate = 0.001m;

    /// <summary>Securities transaction tax rate for **bond ETFs**: 0 %（免徵，政府政策延長至 2026 底）。</summary>
    public const decimal BondEtfTaxRate = 0m;

    // Public API

    /// <summary>
    /// Calculates the fee breakdown for a <b>buy</b> order.
    /// Net cost = gross + commission (no transaction tax on buy).
    /// </summary>
    /// <param name="price">成交價 per share.</param>
    /// <param name="quantity">Shares purchased.</param>
    /// <param name="commissionDiscount">
    ///   Discount multiplier applied to the standard rate (e.g. 0.6 = 6折).
    ///   Defaults to 1.0 (no discount) if not supplied.
    /// </param>
    /// <param name="isEtf">Whether the security is an ETF (affects tax display only; buy tax is 0).</param>
    public static TaiwanTradeFee CalcBuy(
        decimal price,
        int quantity,
        decimal commissionDiscount = 1m,
        bool isEtf = false)
    {
        var gross = price * quantity;
        var commission = CalcCommission(gross, commissionDiscount);
        return new TaiwanTradeFee(
            GrossAmount: gross,
            Commission: commission,
            TransactionTax: 0m,
            NetAmount: gross + commission,
            IsEtf: isEtf);
    }

    /// <summary>
    /// Calculates the fee breakdown for a <b>sell</b> order.
    /// Net proceeds = gross − commission − transaction tax.
    /// </summary>
    /// <param name="price">成交價 per share.</param>
    /// <param name="quantity">Shares sold.</param>
    /// <param name="commissionDiscount">Discount multiplier (e.g. 0.6 = 6折).</param>
    /// <param name="isEtf">True → ETF 稅率 (0.1%)；false → 股票稅率 (0.3%)。</param>
    /// <param name="isBondEtf">True → 債券 ETF，免徵證交稅（優先於 <paramref name="isEtf"/>）。</param>
    public static TaiwanTradeFee CalcSell(
        decimal price,
        int quantity,
        decimal commissionDiscount = 1m,
        bool isEtf = false,
        bool isBondEtf = false)
    {
        var gross = price * quantity;
        var commission = CalcCommission(gross, commissionDiscount);
        var tax = CalcTax(gross, isEtf, isBondEtf);
        return new TaiwanTradeFee(
            GrossAmount: gross,
            Commission: commission,
            TransactionTax: tax,
            NetAmount: gross - commission - tax,
            IsEtf: isEtf);
    }

    // Helpers

    /// <summary>
    /// Commission = max(gross × standardRate × discount, minFee)，**無條件捨去**到 NT$1。
    /// 台股券商普遍以無條件捨去（floor）結算到整數元；與 CalcTax 一致。
    /// </summary>
    private static decimal CalcCommission(decimal gross, decimal discount)
    {
        var raw = gross * StandardCommissionRate * Math.Clamp(discount, 0.1m, 1m);
        return Math.Max(Math.Floor(raw), MinCommission);
    }

    /// <summary>
    /// Transaction tax = gross × taxRate, truncated to NT$1 (market convention).
    /// 優先順序：債券 ETF (0%) > ETF (0.1%) > 一般股票 (0.3%)。
    /// </summary>
    private static decimal CalcTax(decimal gross, bool isEtf, bool isBondEtf = false)
    {
        if (isBondEtf)
            return 0m;
        var rate = isEtf ? EtfTaxRate : StockTaxRate;
        return Math.Floor(gross * rate);     // 交易稅無條件捨去
    }
}
