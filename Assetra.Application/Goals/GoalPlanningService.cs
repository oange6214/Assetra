namespace Assetra.Application.Goals;

/// <summary>
/// 目標規劃計算器 — 純函式，無 I/O。
/// 給定目前資產、目標金額、預期年化報酬率、目標日期，計算建議「每月撥款」。
///
/// 公式（年金未來值，期初 / 期末 contribution 視 <paramref name="contributionAtBeginningOfPeriod"/>）：
///   FV = PV * (1+r)^n + PMT * [((1+r)^n - 1) / r]   (期末)
///   FV = PV * (1+r)^n + PMT * [((1+r)^n - 1) / r] * (1+r)   (期初)
/// 解 PMT。
/// </summary>
public static class GoalPlanningService
{
    /// <summary>
    /// 給定目標，回傳達成所需的每月固定撥款金額（≥ 0）。
    /// 若 <paramref name="currentAmount"/> 已含複利後超過 <paramref name="targetAmount"/>，回 0（已不需再撥款）。
    /// 若 <paramref name="months"/> ≤ 0 或目標日期已過、且尚未達標，回 null（無法在期限內達成）。
    /// </summary>
    /// <param name="currentAmount">目前已累積金額（base currency）。</param>
    /// <param name="targetAmount">目標金額（base currency）。</param>
    /// <param name="annualReturnRate">預期年化報酬率，例 0.05m = 5%。允許 0；不允許負值。</param>
    /// <param name="months">距目標日期的月數（含本月）。</param>
    /// <param name="contributionAtBeginningOfPeriod">true：期初撥款（撥款後再計息）；false：期末撥款。</param>
    public static decimal? RequiredMonthlyContribution(
        decimal currentAmount,
        decimal targetAmount,
        decimal annualReturnRate,
        int months,
        bool contributionAtBeginningOfPeriod = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(annualReturnRate);

        if (targetAmount <= 0m) return 0m;
        if (months <= 0)
        {
            return currentAmount >= targetAmount ? 0m : null;
        }

        // Use double internally for the compounded math, then snap back to decimal at the end.
        var r = (double)annualReturnRate / 12.0;
        var n = months;
        var pv = (double)currentAmount;
        var fv = (double)targetAmount;

        var growthFactor = Math.Pow(1.0 + r, n);
        var pvCompounded = pv * growthFactor;

        if (pvCompounded >= fv)
            return 0m;

        var shortfall = fv - pvCompounded;

        double pmt;
        if (r == 0.0)
        {
            pmt = shortfall / n;
        }
        else
        {
            var annuityFactor = (growthFactor - 1.0) / r;
            if (contributionAtBeginningOfPeriod)
                annuityFactor *= (1.0 + r);
            pmt = shortfall / annuityFactor;
        }

        if (double.IsNaN(pmt) || double.IsInfinity(pmt) || pmt < 0)
            return null;

        return Math.Round((decimal)pmt, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// 給定每月固定撥款，回傳預計達成目標的月數；若永遠無法達成（例：報酬率 0 且 PMT ≤ 0）回 null。
    /// </summary>
    public static int? MonthsToReachTarget(
        decimal currentAmount,
        decimal targetAmount,
        decimal annualReturnRate,
        decimal monthlyContribution,
        bool contributionAtBeginningOfPeriod = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(annualReturnRate);

        if (targetAmount <= 0m || currentAmount >= targetAmount) return 0;
        if (monthlyContribution <= 0m && annualReturnRate == 0m) return null;

        var r = (double)annualReturnRate / 12.0;
        var pv = (double)currentAmount;
        var fv = (double)targetAmount;
        var pmt = (double)monthlyContribution;

        // Iterative simulation — closed-form exists but iteration handles edge cases (negative interim balance, etc.) cleanly.
        // Cap at 1200 months (100 years) to keep the bound finite.
        var balance = pv;
        for (int month = 1; month <= 1200; month++)
        {
            if (contributionAtBeginningOfPeriod)
            {
                balance += pmt;
                balance *= (1.0 + r);
            }
            else
            {
                balance *= (1.0 + r);
                balance += pmt;
            }
            if (balance >= fv) return month;
        }
        return null;
    }
}
