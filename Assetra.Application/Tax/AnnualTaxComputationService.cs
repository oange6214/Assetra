using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.Application.Tax;

/// <summary>
/// 年度稅務「總成本」計算結果 — 整合一般綜所稅 + AMT 雙軌取大者。
/// </summary>
/// <param name="Year">稅務年度。</param>
/// <param name="Profile">該年使用的官方參數（IsExtrapolated=true 表示推估）。</param>
/// <param name="Summary">trade-derived 年度彙整（dividend / capital gain）。</param>
/// <param name="IncomeTax">綜所稅試算（雙軌結果）。</param>
/// <param name="Amt">AMT 試算結果。</param>
/// <param name="TotalTaxLiability">最終應納稅額 = IncomeTax.FinalIncomeTax + Amt.AmtLiability。</param>
public sealed record AnnualTaxComputation(
    int Year,
    TaxYearProfile Profile,
    TaxSummary Summary,
    IncomeTaxResult IncomeTax,
    AmtCalculationResult Amt,
    decimal TotalTaxLiability);

/// <summary>
/// Orchestrates 一個年度的完整稅務試算：抓 profile → 算 IncomeTax → 算 AMT → 加總。
/// 純函式（依賴注入 ITaxProfileProvider 後即無 I/O）。
/// </summary>
public sealed class AnnualTaxComputationService
{
    private readonly ITaxProfileProvider _profiles;

    public AnnualTaxComputationService(ITaxProfileProvider profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        _profiles = profiles;
    }

    public AnnualTaxComputation Compute(
        int year,
        IEnumerable<Trade> trades,
        TaxFilingProfile filing,
        AmtCalculationParameters amtInputs)
    {
        ArgumentNullException.ThrowIfNull(trades);
        ArgumentNullException.ThrowIfNull(filing);
        ArgumentNullException.ThrowIfNull(amtInputs);

        var profile = _profiles.Get(year);
        var summary = TaxCalculationService.CalculateForYear(year, trades, profile);

        // 將 trade-derived 股利 + filing 設定組成完整 filing（使用者可覆寫）
        // 若使用者未填薪資/股利，仍以 trade 推導值帶入
        var effectiveFiling = filing with
        {
            DividendIncome = filing.DividendIncome > 0m
                ? filing.DividendIncome
                : summary.DomesticDividendTotal,   // 海外股利不入合併計稅
        };

        var incomeTax = IncomeTaxCalculator.Calculate(effectiveFiling, profile);

        // AMT inputs 補上：若使用者未填 RegularTaxableIncome / RegularIncomeTax，
        // 就用 IncomeTaxCalculator 算出的值代入 — 確保 AMT 比較有意義。
        // 同時：若使用者選 28% 分離課稅，自動把股利金額帶入 DividendSeparateTaxed
        // （CFP 文件規定：分離課稅股利須加回 AMT 基本所得，避免雙重免稅）。
        var dividendSeparateForAmt = filing.DividendSeparate
            ? Math.Max(amtInputs.DividendSeparateTaxed, effectiveFiling.DividendIncome)
            : amtInputs.DividendSeparateTaxed;

        var effectiveAmtInputs = amtInputs with
        {
            RegularTaxableIncome = amtInputs.RegularTaxableIncome > 0m
                ? amtInputs.RegularTaxableIncome
                : incomeTax.TaxableIncome,
            RegularIncomeTax = amtInputs.RegularIncomeTax > 0m
                ? amtInputs.RegularIncomeTax
                : incomeTax.FinalIncomeTax,
            DividendSeparateTaxed = dividendSeparateForAmt,
        };

        var amt = TaxCalculationService.ComputeAmtLiability(summary, effectiveAmtInputs, profile);

        var total = incomeTax.FinalIncomeTax + amt.AmtLiability;

        return new AnnualTaxComputation(
            Year: year,
            Profile: profile,
            Summary: summary,
            IncomeTax: incomeTax,
            Amt: amt,
            TotalTaxLiability: total);
    }
}
