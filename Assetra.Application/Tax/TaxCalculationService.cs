using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.Application.Tax;

/// <summary>
/// 純函式：將 trade journal 聚合為年度 <see cref="TaxSummary"/>，並依 <see cref="ITaxProfileProvider"/>
/// 套用該年度官方參數計算 AMT 應補繳金額。
/// <para>
/// v2 改動（vs v1）：
/// <list type="bullet">
///   <item>不再寫死 670 萬 / 20% / 100 萬門檻 — 改由 <see cref="TaxYearProfile"/> 動態提供。</item>
///   <item>AMT 計算擴充至 5 項加項（海外、保險、未上市、非現金捐贈、私募基金）+ 海外已納稅額扣抵。</item>
///   <item>仍為純函式：靜態方法 + provider 由 caller 注入；無 I/O、無時間相依。</item>
/// </list>
/// </para>
/// </summary>
public static class TaxCalculationService
{
    /// <summary>本國國別代碼（StockExchange.Country）。其他國別視為海外。</summary>
    public const string DomesticCountry = "TW";

    /// <summary>
    /// 從 trades 聚合出年度 TaxSummary。AMT 申報門檻判定使用 <paramref name="profile"/> 提供的
    /// <c>AmtOverseasThreshold</c>（歷年皆 100 萬，但仍走 profile 以利未來政策變動）。
    /// </summary>
    public static TaxSummary CalculateForYear(int year, IEnumerable<Trade> trades, TaxYearProfile profile)
    {
        ArgumentNullException.ThrowIfNull(trades);
        ArgumentNullException.ThrowIfNull(profile);

        var dividends = new List<DividendIncomeRecord>();
        var capitalGains = new List<CapitalGainRecord>();

        foreach (var t in trades)
        {
            if (t.TradeDate.Year != year)
                continue;

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
            TriggersAmtDeclaration: overseasIncome >= profile.AmtOverseasThreshold,
            Dividends: dividends,
            CapitalGains: capitalGains);
    }

    /// <summary>
    /// 計算 AMT 應補繳金額。納入 5 項基本所得加項與海外已納稅額扣抵，依
    /// <paramref name="profile"/> 提供的免稅額/稅率/海外門檻/保險扣除額執行。
    /// </summary>
    public static AmtCalculationResult ComputeAmtLiability(
        TaxSummary summary,
        AmtCalculationParameters parameters,
        TaxYearProfile profile)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(profile);

        // 1. 海外所得：≥ 該年門檻才整筆計入
        var overseasIncluded = summary.OverseasIncomeTotal >= profile.AmtOverseasThreshold
            ? summary.OverseasIncomeTotal : 0m;

        // 2a. 死亡給付：超過該年度 AmtInsuranceDeduction（2024=3,740 萬）部分計入
        var insuranceDeathIncluded = Math.Max(0m, parameters.InsuranceDeathProceeds - profile.AmtInsuranceDeduction);
        // 2b. 非死亡給付（滿期 / 解約）：全額計入無扣除
        var insuranceNonDeathIncluded = Math.Max(0m, parameters.InsuranceNonDeathProceeds);

        // 3. 未上市股票交易所得：全額計入
        var unlistedIncluded = Math.Max(0m, parameters.UnlistedSecurityGains);

        // 4. 私募基金交易所得：全額計入
        var privateFundIncluded = Math.Max(0m, parameters.PrivateFundGains);

        // 5. 非現金捐贈：加計回基本所得
        var nonCashDonationAdded = Math.Max(0m, parameters.NonCashDonation);

        // 6. 股利分離課稅 28%：須加回基本所得避免雙重免稅
        // （CFP 文件依據：股利分開計稅金額計入 AMT 基本所得額）
        var dividendSeparateAdded = Math.Max(0m, parameters.DividendSeparateTaxed);

        var baseTaxableIncome = parameters.RegularTaxableIncome
            + overseasIncluded
            + insuranceDeathIncluded
            + insuranceNonDeathIncluded
            + unlistedIncluded
            + privateFundIncluded
            + nonCashDonationAdded
            + dividendSeparateAdded;

        var amtBaseTax = Math.Max(0m, baseTaxableIncome - profile.AmtExemption) * profile.AmtRate;

        // 應補繳 = max(0, 基本稅額 − 一般稅額 − 海外抵稅)
        var creditApplied = Math.Min(parameters.OverseasTaxCredit,
            Math.Max(0m, amtBaseTax - parameters.RegularIncomeTax));
        var liability = Math.Max(0m, amtBaseTax - parameters.RegularIncomeTax - creditApplied);

        // 適用：一般所得 > 0 且至少有一項 AMT 加項
        var hasAnyAddBack = overseasIncluded > 0m
            || insuranceDeathIncluded > 0m
            || insuranceNonDeathIncluded > 0m
            || unlistedIncluded > 0m
            || privateFundIncluded > 0m
            || nonCashDonationAdded > 0m
            || dividendSeparateAdded > 0m;
        var applicable = hasAnyAddBack && parameters.RegularTaxableIncome > 0m;

        return new AmtCalculationResult(
            OverseasIncomeIncluded: overseasIncluded,
            InsuranceDeathIncluded: insuranceDeathIncluded,
            InsuranceNonDeathIncluded: insuranceNonDeathIncluded,
            UnlistedIncluded: unlistedIncluded,
            PrivateFundIncluded: privateFundIncluded,
            NonCashDonationAdded: nonCashDonationAdded,
            DividendSeparateAdded: dividendSeparateAdded,
            BaseTaxableIncome: baseTaxableIncome,
            AmtBaseTax: amtBaseTax,
            RegularIncomeTax: parameters.RegularIncomeTax,
            OverseasTaxCreditApplied: creditApplied,
            AmtLiability: liability,
            IsApplicable: applicable,
            ProfileExtrapolated: profile.IsExtrapolated);
    }

    private static string ResolveCountry(string exchange) =>
        StockExchangeRegistry.TryGet(exchange)?.Country ?? DomesticCountry;

    private static bool IsDomestic(string country) =>
        string.Equals(country, DomesticCountry, StringComparison.OrdinalIgnoreCase);
}
