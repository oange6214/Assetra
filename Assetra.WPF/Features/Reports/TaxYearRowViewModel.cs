using Assetra.Core.Models;

namespace Assetra.WPF.Features.Reports;

/// <summary>
/// One row in the multi-year tax comparison table. Wraps a <see cref="TaxSummary"/>
/// so XAML can bind directly to per-year buckets without having to format inline.
/// All values pre-rounded to integer NTD for display density (cents are noise at
/// the year-aggregate level).
/// </summary>
public sealed record TaxYearRowViewModel(
    int Year,
    decimal DomesticDividendTotal,
    decimal OverseasDividendTotal,
    decimal DomesticCapitalGainTotal,
    decimal OverseasCapitalGainTotal,
    decimal OverseasIncomeTotal,
    bool TriggersAmtDeclaration,
    int RecordCount)
{
    public static TaxYearRowViewModel FromSummary(TaxSummary s) => new(
        Year: s.Year,
        DomesticDividendTotal: s.DomesticDividendTotal,
        OverseasDividendTotal: s.OverseasDividendTotal,
        DomesticCapitalGainTotal: s.DomesticCapitalGainTotal,
        OverseasCapitalGainTotal: s.OverseasCapitalGainTotal,
        OverseasIncomeTotal: s.OverseasIncomeTotal,
        TriggersAmtDeclaration: s.TriggersAmtDeclaration,
        RecordCount: s.Dividends.Count + s.CapitalGains.Count);

    /// <summary>Total dividend + total capital gain (signed, can be negative).</summary>
    public decimal TotalTaxableEvents =>
        DomesticDividendTotal + OverseasDividendTotal +
        DomesticCapitalGainTotal + OverseasCapitalGainTotal;
}
