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
/// <para><see cref="TriggersAmtDeclaration"/>：海外所得（股利 + 資本利得）合計 ≥ <see cref="TaxCalculationService.AmtDeclarationThreshold"/> 時為 true，
/// 屬於最低稅負制的申報門檻（個人海外所得 100 萬 NTD）。實際 AMT 稅額計算需另含一般所得、扣除額、免稅額等，超出本骨架範圍。</para>
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
