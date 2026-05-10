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
/// 年度稅務彙整。所有金額以原始 trade 幣別加總，跨幣別換算交給 caller。
/// <para><see cref="TriggersAmtDeclaration"/>：海外所得（股利 + 資本利得）合計 ≥
/// 該年度 <c>TaxYearProfile.AmtOverseasThreshold</c> 時為 true。</para>
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
/// AMT 計算所需的「使用者輸入參數」— 不再含稅率/免稅額（改由 TaxYearProfile 提供）。
/// 所有欄位皆為使用者於 Settings 填寫的「年度彙整值」。
/// <para>
/// 計算流程（v2，含 AMT 4 項）：
/// <list type="number">
///   <item>基本所得 = 一般綜合所得淨額
///         + (海外所得 if 海外所得 ≥ 該年門檻 else 0)
///         + max(0, 保險給付 − AmtInsuranceDeduction)
///         + 未上市櫃股票交易所得
///         + 私募基金交易所得
///         + 非現金捐贈扣除額還原</item>
///   <item>基本稅額 = max(0, 基本所得 − 該年免稅額) × 該年稅率</item>
///   <item>應補繳 AMT = max(0, 基本稅額 − 一般所得稅額 − 海外已納稅額可扣抵)</item>
/// </list>
/// </para>
/// </summary>
/// <param name="RegularTaxableIncome">一般綜合所得淨額（NTD）。0 = 未填，AMT 不計算。</param>
/// <param name="RegularIncomeTax">一般綜合所得稅應納稅額（NTD）。</param>
/// <param name="InsuranceDeathProceeds">人壽/年金「死亡給付」總額（NTD），要保人≠受益人。超過 AmtInsuranceDeduction 部分計入。</param>
/// <param name="InsuranceNonDeathProceeds">人壽/年金「非死亡給付」總額（NTD），要保人≠受益人。**全額**計入，無扣除。</param>
/// <param name="UnlistedSecurityGains">未上市櫃股票交易所得（NTD）。</param>
/// <param name="NonCashDonation">非現金捐贈扣除額（NTD）— 須加計回基本所得。</param>
/// <param name="PrivateFundGains">私募證券投資信託基金受益憑證交易所得（NTD）。</param>
/// <param name="OverseasTaxCredit">海外已納稅額可扣抵（NTD）。</param>
/// <param name="DividendSeparateTaxed">股利選 28% 分離課稅之合計金額（NTD）— 須加回 AMT 基本所得避免雙重免稅。</param>
public sealed record AmtCalculationParameters(
    decimal RegularTaxableIncome = 0m,
    decimal RegularIncomeTax = 0m,
    decimal InsuranceDeathProceeds = 0m,
    decimal InsuranceNonDeathProceeds = 0m,
    decimal UnlistedSecurityGains = 0m,
    decimal NonCashDonation = 0m,
    decimal PrivateFundGains = 0m,
    decimal OverseasTaxCredit = 0m,
    decimal DividendSeparateTaxed = 0m);

/// <summary>
/// AMT 計算結果（v2 — 含 4 項擴充）。
/// </summary>
/// <param name="OverseasIncomeIncluded">實際計入基本所得的海外所得。</param>
/// <param name="InsuranceDeathIncluded">扣除該年度 AmtInsuranceDeduction 後計入的死亡給付。</param>
/// <param name="InsuranceNonDeathIncluded">全額計入之非死亡給付（無扣除）。</param>
/// <param name="UnlistedIncluded">計入的未上市股票交易所得。</param>
/// <param name="PrivateFundIncluded">計入的私募基金交易所得。</param>
/// <param name="NonCashDonationAdded">加計回基本所得的非現金捐贈。</param>
/// <param name="DividendSeparateAdded">加計回基本所得的股利分離課稅金額。</param>
/// <param name="BaseTaxableIncome">基本所得 = 一般所得 + 上述 6 項加總。</param>
/// <param name="AmtBaseTax">基本稅額 = max(0, 基本所得 − 免稅額) × 稅率。</param>
/// <param name="RegularIncomeTax">一般所得稅額（含使用者輸入或自動計算）。</param>
/// <param name="OverseasTaxCreditApplied">實際扣抵的海外已納稅額。</param>
/// <param name="AmtLiability">應補繳 AMT = max(0, AmtBaseTax − RegularIncomeTax − OverseasTaxCreditApplied)。</param>
/// <param name="IsApplicable">是否實際適用（一般所得 > 0 且至少一項 AMT 加項 > 0）。</param>
/// <param name="ProfileExtrapolated">該年度 TaxYearProfile 為推估而非官方公告（年度太新或太舊）。</param>
public sealed record AmtCalculationResult(
    decimal OverseasIncomeIncluded,
    decimal InsuranceDeathIncluded,
    decimal InsuranceNonDeathIncluded,
    decimal UnlistedIncluded,
    decimal PrivateFundIncluded,
    decimal NonCashDonationAdded,
    decimal DividendSeparateAdded,
    decimal BaseTaxableIncome,
    decimal AmtBaseTax,
    decimal RegularIncomeTax,
    decimal OverseasTaxCreditApplied,
    decimal AmtLiability,
    bool IsApplicable,
    bool ProfileExtrapolated);
