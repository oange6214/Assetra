using Assetra.Core.Models;

namespace Assetra.Application.Tax;

/// <summary>
/// 個人稅務檔案（年度），由 AppSettings 投影而成。純資料容器，無行為。
/// </summary>
/// <param name="IsMarried">是否夫妻合併申報。</param>
/// <param name="DependentCount">扶養親屬數（不含本人 / 配偶）。</param>
/// <param name="PreschoolCount">6 歲以下幼兒數。</param>
/// <param name="CollegeStudentCount">大專就學子女數。</param>
/// <param name="LongCareCount">長照對象數。</param>
/// <param name="DisabilityCount">身心障礙人數。</param>
/// <param name="SalaryIncome">本人薪資所得（NTD）。</param>
/// <param name="DividendIncome">股利所得（NTD，已從 TaxSummary 帶入）。</param>
/// <param name="InterestIncome">利息所得（NTD）。</param>
/// <param name="RentalExpense">房屋租金支出（NTD / 年）。</param>
/// <param name="UseItemized">是否採列舉扣除。</param>
/// <param name="ItemizedAmount">列舉扣除金額（NTD）。</param>
/// <param name="DividendSeparate">股利選 28% 分離課稅 = true；合併計稅 = false。</param>
public sealed record TaxFilingProfile(
    bool IsMarried,
    int DependentCount,
    int PreschoolCount,
    int CollegeStudentCount,
    int LongCareCount,
    int DisabilityCount,
    decimal SalaryIncome,
    decimal DividendIncome,
    decimal InterestIncome,
    decimal RentalExpense,
    bool UseItemized,
    decimal ItemizedAmount,
    bool DividendSeparate);

/// <summary>
/// 一般綜所稅試算結果。
/// </summary>
/// <param name="TotalIncome">綜合所得總額（薪資 + 股利合併 + 利息 …）。</param>
/// <param name="ExemptionTotal">免稅額合計（本人 + 配偶 + 扶養）。</param>
/// <param name="DeductionTotal">扣除額合計（標準/列舉 + 薪資特別 + 儲蓄 + 長照 + 幼兒 + 教育 + 身障 + 租金）。</param>
/// <param name="TaxableIncome">課稅所得淨額 = max(0, TotalIncome − ExemptionTotal − DeductionTotal)。</param>
/// <param name="MergedTrackTax">合併計稅軌：依級距計算的稅額（已扣抵股利 8.5% 抵減上限 8 萬）。</param>
/// <param name="SeparateTrackTax">分離課稅軌：依級距計算的稅額（股利不入級距、單獨 28%）+ 股利稅。</param>
/// <param name="DividendCredit">合併計稅軌的股利抵減稅額（僅供顯示）。</param>
/// <param name="ChosenTrack">使用者選擇的軌道："merged" 或 "separate"。</param>
/// <param name="FinalIncomeTax">最終一般所得稅額（依使用者選擇的軌道）。</param>
public sealed record IncomeTaxResult(
    decimal TotalIncome,
    decimal ExemptionTotal,
    decimal DeductionTotal,
    decimal TaxableIncome,
    decimal MergedTrackTax,
    decimal SeparateTrackTax,
    decimal DividendCredit,
    string ChosenTrack,
    decimal FinalIncomeTax);

/// <summary>
/// 純函式：依 <see cref="TaxYearProfile"/>（年度官方參數）+ <see cref="TaxFilingProfile"/>
/// （使用者個人狀況）計算當年度一般綜所稅應納稅額。
/// 採台灣現行雙軌制（股利合併 vs 28% 分離），自動算雙軌結果供 caller 比較。
/// </summary>
public static class IncomeTaxCalculator
{
    public static IncomeTaxResult Calculate(TaxFilingProfile filing, TaxYearProfile profile)
    {
        ArgumentNullException.ThrowIfNull(filing);
        ArgumentNullException.ThrowIfNull(profile);

        // ── 1. 免稅額（本人 + 配偶 + 扶養）─────────────────────────────
        var personCount = 1
            + (filing.IsMarried ? 1 : 0)
            + Math.Max(0, filing.DependentCount);
        var exemptionTotal = profile.PersonalExemption * personCount;

        // ── 2. 一般扣除額（標準 vs 列舉擇大）──────────────────────────
        var standardDeduction = filing.IsMarried
            ? profile.StandardDeductionMarried
            : profile.StandardDeductionSingle;
        var generalDeduction = filing.UseItemized
            ? Math.Max(filing.ItemizedAmount, standardDeduction)   // 取大（列舉若小於標準仍可改採標準）
            : standardDeduction;

        // ── 3. 特別扣除額 ─────────────────────────────────────────────
        var salarySpecial = filing.SalaryIncome > 0m
            ? Math.Min(filing.SalaryIncome, profile.SalarySpecialDeduction)
            : 0m;
        var savingsSpecial = Math.Min(filing.InterestIncome, profile.SavingsInvestmentDeductionCap);
        var preschoolSpecial = profile.PreschoolDeduction * Math.Max(0, filing.PreschoolCount);
        var educationSpecial = profile.EducationDeduction * Math.Max(0, filing.CollegeStudentCount);
        var longCareSpecial = profile.LongCareDeduction * Math.Max(0, filing.LongCareCount);
        var disabilitySpecial = profile.DisabilityDeduction * Math.Max(0, filing.DisabilityCount);
        var rentalSpecial = Math.Min(filing.RentalExpense, profile.RentalDeduction);

        var specialDeductionTotal = salarySpecial + savingsSpecial + preschoolSpecial
            + educationSpecial + longCareSpecial + disabilitySpecial + rentalSpecial;

        var deductionTotal = generalDeduction + specialDeductionTotal;

        // ── 4. 雙軌試算 ──────────────────────────────────────────────
        // 軌道 A：合併計稅 — 股利 + 薪資 + 利息一起進級距，享 8.5% 抵減
        var mergedTotalIncome = filing.SalaryIncome + filing.DividendIncome + filing.InterestIncome;
        var mergedTaxable = Math.Max(0m, mergedTotalIncome - exemptionTotal - deductionTotal);
        var mergedBracketTax = ApplyBrackets(mergedTaxable, profile.IncomeTaxBrackets);
        var dividendCredit = Math.Min(filing.DividendIncome * profile.DividendCreditRate,
            profile.DividendCreditCap);
        var mergedTrackTax = Math.Max(0m, mergedBracketTax - dividendCredit);

        // 軌道 B：分離課稅 — 股利不進級距，單獨 28%；其他所得進級距
        var separateNonDividendIncome = filing.SalaryIncome + filing.InterestIncome;
        var separateTaxable = Math.Max(0m, separateNonDividendIncome - exemptionTotal - deductionTotal);
        var separateBracketTax = ApplyBrackets(separateTaxable, profile.IncomeTaxBrackets);
        var separateDividendTax = filing.DividendIncome * profile.DividendSeparateRate;
        var separateTrackTax = separateBracketTax + separateDividendTax;

        // ── 5. 取捨 ─────────────────────────────────────────────────
        // 使用者顯式選擇 → 用使用者偏好；否則自動取低
        string chosen;
        decimal finalTax;
        if (filing.DividendSeparate)
        {
            chosen = "separate";
            finalTax = separateTrackTax;
        }
        else
        {
            // 合併計稅勾選預設取「自動最低」 — 但若使用者明確要合併（settings 預設 false），尊重之
            chosen = "merged";
            finalTax = mergedTrackTax;
        }

        // 顯示用 totalIncome 取合併軌（涵蓋全部）
        var totalIncomeForDisplay = mergedTotalIncome;

        return new IncomeTaxResult(
            TotalIncome: totalIncomeForDisplay,
            ExemptionTotal: exemptionTotal,
            DeductionTotal: deductionTotal,
            TaxableIncome: chosen == "merged" ? mergedTaxable : separateTaxable,
            MergedTrackTax: mergedTrackTax,
            SeparateTrackTax: separateTrackTax,
            DividendCredit: dividendCredit,
            ChosenTrack: chosen,
            FinalIncomeTax: finalTax);
    }

    /// <summary>
    /// 套累進級距速算公式。Brackets 須依 UpTo 由小到大排序；UpTo=null 為最高級距。
    /// 公式：本級距稅率 × 課稅所得 − 速算扣除額。
    /// </summary>
    private static decimal ApplyBrackets(decimal taxable, IReadOnlyList<TaxBracket> brackets)
    {
        if (taxable <= 0m || brackets.Count == 0)
            return 0m;

        foreach (var b in brackets)
        {
            if (b.UpTo is null || taxable <= b.UpTo.Value)
                return Math.Max(0m, taxable * b.Rate - b.Subtract);
        }
        // 防呆：若沒有 UpTo=null 的 fallback bracket，套用最後一個
        var last = brackets[^1];
        return Math.Max(0m, taxable * last.Rate - last.Subtract);
    }
}
