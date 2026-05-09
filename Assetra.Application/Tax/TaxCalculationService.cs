using Assetra.Core.Models;

namespace Assetra.Application.Tax;

/// <summary>
/// 純函式：將 trade journal 聚合為年度 <see cref="TaxSummary"/>。
/// <para>規則：
///   <list type="bullet">
///     <item><see cref="TradeType.CashDividend"/> → <see cref="DividendIncomeRecord"/>，依 Exchange 的 Country 拆分國內/海外。</item>
///     <item><see cref="TradeType.Sell"/>（含 <see cref="Trade.RealizedPnl"/>）→ <see cref="CapitalGainRecord"/>，台股本國資本利得目前免稅但仍記錄；海外計入 AMT。</item>
///     <item>其他 TradeType（Buy / Deposit / Income / Loan…）不屬於本服務範圍。</item>
///   </list>
/// </para>
/// 不含 I/O，不含匯率換算（多幣別由 caller 用 v0.14 <c>IMultiCurrencyValuationService</c> 預先轉成 base currency）。
/// </summary>
public static class TaxCalculationService
{
    /// <summary>個人海外所得申報門檻（NTD）。海外股利 + 海外資本利得合計 ≥ 此值時應依最低稅負制申報。</summary>
    public const decimal AmtDeclarationThreshold = 1_000_000m;

    /// <summary>本國國別代碼（StockExchange.Country）。其他國別視為海外。</summary>
    public const string DomesticCountry = "TW";

    public static TaxSummary CalculateForYear(int year, IEnumerable<Trade> trades)
    {
        ArgumentNullException.ThrowIfNull(trades);

        var dividends = new List<DividendIncomeRecord>();
        var capitalGains = new List<CapitalGainRecord>();

        foreach (var t in trades)
        {
            if (t.TradeDate.Year != year) continue;

            if (t.Type == TradeType.CashDividend)
            {
                var country = ResolveCountry(t.Exchange);
                var isOverseas = !IsDomestic(country);
                var amount = t.CashAmount ?? (t.Price * t.Quantity);
                dividends.Add(new DividendIncomeRecord(
                    TradeId: t.Id,
                    Date: DateOnly.FromDateTime(t.TradeDate),
                    Symbol: t.Symbol,
                    Exchange: t.Exchange,
                    Country: country,
                    Amount: amount,
                    IsOverseas: isOverseas));
            }
            else if (t.Type == TradeType.Sell && t.RealizedPnl is { } pnl)
            {
                var country = ResolveCountry(t.Exchange);
                var isOverseas = !IsDomestic(country);
                capitalGains.Add(new CapitalGainRecord(
                    TradeId: t.Id,
                    Date: DateOnly.FromDateTime(t.TradeDate),
                    Symbol: t.Symbol,
                    Exchange: t.Exchange,
                    Country: country,
                    RealizedPnl: pnl,
                    IsOverseas: isOverseas));
            }
        }

        var domesticDiv = dividends.Where(d => !d.IsOverseas).Sum(d => d.Amount);
        var overseasDiv = dividends.Where(d => d.IsOverseas).Sum(d => d.Amount);
        var domesticGain = capitalGains.Where(c => !c.IsOverseas).Sum(c => c.RealizedPnl);
        var overseasGain = capitalGains.Where(c => c.IsOverseas).Sum(c => c.RealizedPnl);
        var overseasIncome = overseasDiv + overseasGain;

        return new TaxSummary(
            Year: year,
            DomesticDividendTotal: domesticDiv,
            OverseasDividendTotal: overseasDiv,
            DomesticCapitalGainTotal: domesticGain,
            OverseasCapitalGainTotal: overseasGain,
            OverseasIncomeTotal: overseasIncome,
            TriggersAmtDeclaration: overseasIncome >= AmtDeclarationThreshold,
            Dividends: dividends,
            CapitalGains: capitalGains);
    }

    /// <summary>
    /// Computes the AMT liability for a given <see cref="TaxSummary"/> using
    /// the supplied <see cref="AmtCalculationParameters"/>. Pure function — no
    /// I/O, no time dependence.
    ///
    /// <para>
    /// 規則（簡化版，僅含海外所得這條特定項目）：
    /// <list type="number">
    ///   <item>若海外所得合計 &lt; <see cref="AmtDeclarationThreshold"/>（100 萬）→ 海外所得不計入基本所得，IsApplicable=false。</item>
    ///   <item>否則 BaseTaxableIncome = RegularTaxableIncome + OverseasIncomeTotal。</item>
    ///   <item>AmtBaseTax = max(0, BaseTaxableIncome − Exemption) × Rate。</item>
    ///   <item>AmtLiability = max(0, AmtBaseTax − RegularIncomeTax)。</item>
    /// </list>
    /// </para>
    /// </summary>
    public static AmtCalculationResult ComputeAmtLiability(TaxSummary summary, AmtCalculationParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(parameters);

        var triggers = summary.OverseasIncomeTotal >= AmtDeclarationThreshold;
        var overseasIncluded = triggers ? summary.OverseasIncomeTotal : 0m;
        var baseTaxableIncome = parameters.RegularTaxableIncome + overseasIncluded;
        var amtBaseTax = Math.Max(0m, baseTaxableIncome - parameters.Exemption) * parameters.Rate;
        var liability = Math.Max(0m, amtBaseTax - parameters.RegularIncomeTax);
        var applicable = triggers && parameters.RegularTaxableIncome > 0m;

        return new AmtCalculationResult(
            OverseasIncomeIncluded: overseasIncluded,
            BaseTaxableIncome: baseTaxableIncome,
            AmtBaseTax: amtBaseTax,
            RegularIncomeTax: parameters.RegularIncomeTax,
            AmtLiability: liability,
            IsApplicable: applicable);
    }

    private static string ResolveCountry(string exchange) =>
        StockExchangeRegistry.TryGet(exchange)?.Country ?? DomesticCountry;

    private static bool IsDomestic(string country) =>
        string.Equals(country, DomesticCountry, StringComparison.OrdinalIgnoreCase);
}
