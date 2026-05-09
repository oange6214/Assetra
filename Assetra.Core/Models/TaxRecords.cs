namespace Assetra.Core.Models;

/// <summary>
/// 單筆股利所得記錄 — 從 <see cref="TradeType.CashDividend"/> 抽取。
/// <para><see cref="IsOverseas"/> 由 <see cref="StockExchangeRegistry"/> 的 Country 欄位判定（非 "TW" 即為海外）。</para>
/// 海外股利屬「最低稅負制」(AMT) 中的海外所得項目。
/// </summary>
public sealed record DividendIncomeRecord(
    Guid TradeId,
    DateOnly Date,
    string Symbol,
    string Exchange,
    string Country,
    decimal Amount,
    bool IsOverseas);

/// <summary>
/// 單筆已實現資本利得記錄 — 從 <see cref="TradeType.Sell"/> 的 <see cref="Trade.RealizedPnl"/> 抽取。
/// <para>台股（TWSE/TPEX）個人資本利得目前免稅；海外資本利得計入 AMT 海外所得。</para>
/// </summary>
public sealed record CapitalGainRecord(
    Guid TradeId,
    DateOnly Date,
    string Symbol,
    string Exchange,
    string Country,
    decimal RealizedPnl,
    bool IsOverseas);

/// <summary>
/// 年度稅務彙整。所有金額以原始 trade 幣別加總，跨幣別換算交給 caller（v0.14 多幣別）。
/// <para><see cref="TriggersAmtDeclaration"/>：海外所得（股利 + 資本利得）合計 ≥
/// <c>TaxCalculationService.AmtDeclarationThreshold</c> 時為 true，屬於最低稅負制的申報門檻
/// （個人海外所得 100 萬 NTD）。實際 AMT 稅額計算見 <see cref="AmtCalculationResult"/>。</para>
/// </summary>
public sealed record TaxSummary(
    int Year,
    decimal DomesticDividendTotal,
    decimal OverseasDividendTotal,
    decimal DomesticCapitalGainTotal,
    decimal OverseasCapitalGainTotal,
    decimal OverseasIncomeTotal,
    bool TriggersAmtDeclaration,
    IReadOnlyList<DividendIncomeRecord> Dividends,
    IReadOnlyList<CapitalGainRecord> CapitalGains);

/// <summary>
/// AMT 計算所需的使用者輸入參數。預設值依台灣 2024 年公告。
/// <para>
/// 計算流程（簡化版，只考慮海外所得項目；保險給付、私募基金等其他特定項目暫不支援）：
/// <list type="number">
///   <item>基本所得 = 一般綜合所得淨額 + (海外所得合計 if 海外所得 ≥ 100 萬 else 0)</item>
///   <item>基本稅額 = max(0, 基本所得 − 免稅額) × 稅率</item>
///   <item>AMT 應補繳 = max(0, 基本稅額 − 一般綜所稅應納稅額)</item>
/// </list>
/// </para>
/// </summary>
public sealed record AmtCalculationParameters(
    decimal Exemption = 6_700_000m,
    decimal Rate = 0.20m,
    decimal RegularTaxableIncome = 0m,
    decimal RegularIncomeTax = 0m);

/// <summary>
/// AMT 計算結果。
/// </summary>
/// <param name="OverseasIncomeIncluded">實際計入基本所得的海外所得（≥ 門檻則整筆計入，否則 0）。</param>
/// <param name="BaseTaxableIncome">基本所得 = 一般所得淨額 + OverseasIncomeIncluded。</param>
/// <param name="AmtBaseTax">基本稅額 = max(0, 基本所得 − 免稅額) × 稅率。</param>
/// <param name="RegularIncomeTax">使用者填的一般綜所稅應納稅額。</param>
/// <param name="AmtLiability">AMT 應補繳 = max(0, AmtBaseTax − RegularIncomeTax)。</param>
/// <param name="IsApplicable">是否實際適用（需要使用者填一般所得，且海外所得達申報門檻）。</param>
public sealed record AmtCalculationResult(
    decimal OverseasIncomeIncluded,
    decimal BaseTaxableIncome,
    decimal AmtBaseTax,
    decimal RegularIncomeTax,
    decimal AmtLiability,
    bool IsApplicable);
