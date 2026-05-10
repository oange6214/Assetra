using Assetra.Application.Tax;
using Assetra.Core.Models;

namespace Assetra.WPF.Features.Reports;

/// <summary>
/// One row in the multi-year tax comparison table. Wraps a full
/// <see cref="AnnualTaxComputation"/> so XAML can bind directly to per-year
/// 應納稅總額（IncomeTax + AMT）without inline formatting.
/// </summary>
/// <param name="Year">稅務年度。</param>
/// <param name="DomesticDividendTotal">本國股利合計。</param>
/// <param name="OverseasDividendTotal">海外股利合計。</param>
/// <param name="DomesticCapitalGainTotal">本國資本利得（目前免稅，仍記錄）。</param>
/// <param name="OverseasCapitalGainTotal">海外資本利得 — 計入 AMT。</param>
/// <param name="OverseasIncomeTotal">海外所得合計（股利 + 資本利得）。</param>
/// <param name="TriggersAmtDeclaration">海外所得 ≥ 該年門檻 → 應依 AMT 申報。</param>
/// <param name="RecordCount">該年度交易筆數（dividend + sell-with-pnl）。</param>
/// <param name="IncomeTaxLiability">綜所稅應納稅額（含雙軌取捨後）。</param>
/// <param name="AmtLiability">AMT 應補繳金額。</param>
/// <param name="TotalTaxLiability">當年總稅負 = 綜所稅 + AMT。</param>
/// <param name="AmtExemption">該年度 AMT 免稅額（顯示用）。</param>
/// <param name="ProfileExtrapolated">true = 該年度 profile 為推估，UI 應顯示警示。</param>
public sealed record TaxYearRowViewModel(
    int Year,
    decimal DomesticDividendTotal,
    decimal OverseasDividendTotal,
    decimal DomesticCapitalGainTotal,
    decimal OverseasCapitalGainTotal,
    decimal OverseasIncomeTotal,
    bool TriggersAmtDeclaration,
    int RecordCount,
    decimal IncomeTaxLiability,
    decimal AmtLiability,
    decimal TotalTaxLiability,
    decimal AmtExemption,
    bool ProfileExtrapolated)
{
    /// <summary>從完整 AnnualTaxComputation 建 row。</summary>
    public static TaxYearRowViewModel FromComputation(AnnualTaxComputation c) => new(
        Year: c.Year,
        DomesticDividendTotal: c.Summary.DomesticDividendTotal,
        OverseasDividendTotal: c.Summary.OverseasDividendTotal,
        DomesticCapitalGainTotal: c.Summary.DomesticCapitalGainTotal,
        OverseasCapitalGainTotal: c.Summary.OverseasCapitalGainTotal,
        OverseasIncomeTotal: c.Summary.OverseasIncomeTotal,
        TriggersAmtDeclaration: c.Summary.TriggersAmtDeclaration,
        RecordCount: c.Summary.Dividends.Count + c.Summary.CapitalGains.Count,
        IncomeTaxLiability: c.IncomeTax.FinalIncomeTax,
        AmtLiability: c.Amt.AmtLiability,
        TotalTaxLiability: c.TotalTaxLiability,
        AmtExemption: c.Profile.AmtExemption,
        ProfileExtrapolated: c.Profile.IsExtrapolated);

    /// <summary>Backward-compat：僅用 TaxSummary 建 row（無稅額試算）。</summary>
    public static TaxYearRowViewModel FromSummary(TaxSummary s) => new(
        Year: s.Year,
        DomesticDividendTotal: s.DomesticDividendTotal,
        OverseasDividendTotal: s.OverseasDividendTotal,
        DomesticCapitalGainTotal: s.DomesticCapitalGainTotal,
        OverseasCapitalGainTotal: s.OverseasCapitalGainTotal,
        OverseasIncomeTotal: s.OverseasIncomeTotal,
        TriggersAmtDeclaration: s.TriggersAmtDeclaration,
        RecordCount: s.Dividends.Count + s.CapitalGains.Count,
        IncomeTaxLiability: 0m,
        AmtLiability: 0m,
        TotalTaxLiability: 0m,
        AmtExemption: 0m,
        ProfileExtrapolated: false);

    public decimal TotalTaxableEvents =>
        DomesticDividendTotal + OverseasDividendTotal +
        DomesticCapitalGainTotal + OverseasCapitalGainTotal;
}
