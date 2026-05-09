using Assetra.Core.Models;

namespace Assetra.Core.Services;

/// <summary>
/// Pure amortization calculator. No external dependencies.
///
/// Uses equal-payment (等額本息) method:
///   M = P × r × (1+r)^n / ((1+r)^n − 1)   where r = annualRate / 12
///
/// Rounding: monthly payment and each period's interest are rounded to
/// the nearest integer NT dollar. The last period is adjusted to exactly
/// clear the remaining balance, eliminating accumulated rounding error.
/// </summary>
public static class AmortizationService
{
    /// <summary>
    /// Recomputes a loan schedule preserving already-paid entries verbatim and
    /// regenerating only the unpaid tail according to the new <paramref name="newAnnualRate"/>
    /// and <paramref name="newTermMonths"/>.
    ///
    /// <para>
    /// Semantics: paid periods retain their exact amounts, dates, and trade
    /// references — so historical payments stay accurate. The remaining
    /// principal (= original − Σ paid principal) is re-amortised over
    /// <c>newTermMonths − paidEntries.Count</c> periods. The first new
    /// unpaid period's due date follows the last paid entry's due date by
    /// one month (or <paramref name="originalFirstPaymentDate"/> if no entries
    /// have been paid yet).
    /// </para>
    ///
    /// <para>Throws when there is nothing to regenerate (newTermMonths ≤ paidCount,
    /// or remaining principal ≤ 0).</para>
    /// </summary>
    public static IReadOnlyList<LoanScheduleEntry> RecomputeUnpaidTail(
        Guid                                    assetId,
        decimal                                 originalPrincipal,
        decimal                                 newAnnualRate,
        int                                     newTermMonths,
        DateOnly                                originalFirstPaymentDate,
        IReadOnlyList<LoanScheduleEntry>        existingEntries)
    {
        ArgumentNullException.ThrowIfNull(existingEntries);
        if (newAnnualRate < 0)  throw new ArgumentOutOfRangeException(nameof(newAnnualRate));
        if (newTermMonths <= 0) throw new ArgumentOutOfRangeException(nameof(newTermMonths));

        var paid = existingEntries.Where(e => e.IsPaid).OrderBy(e => e.Period).ToList();
        var paidCount = paid.Count;

        if (newTermMonths <= paidCount)
            throw new InvalidOperationException(
                $"新總期數 ({newTermMonths}) 必須大於已付期數 ({paidCount})。請改成「結清」流程，而非縮短期數。");

        var paidPrincipal = paid.Sum(e => e.PrincipalAmount);
        var remainingPrincipal = originalPrincipal - paidPrincipal;
        if (remainingPrincipal <= 0)
            throw new InvalidOperationException(
                "原始本金已被先前還款全部攤銷，無法重算未付期；如需保留紀錄請改成「結清」。");

        var remainingTermMonths = newTermMonths - paidCount;

        // Use first unpaid date if available; otherwise rebase off the last paid entry.
        DateOnly firstUnpaidDueDate = paid.Count > 0
            ? paid[^1].DueDate.AddMonths(1)
            : originalFirstPaymentDate;

        var regenerated = Generate(assetId, remainingPrincipal, newAnnualRate, remainingTermMonths, firstUnpaidDueDate);

        // Renumber regenerated periods to follow the paid tail (period = paidCount + i).
        var combined = new List<LoanScheduleEntry>(newTermMonths);
        combined.AddRange(paid);
        for (var i = 0; i < regenerated.Count; i++)
        {
            var r = regenerated[i];
            combined.Add(r with { Period = paidCount + i + 1 });
        }
        return combined;
    }

    public static IReadOnlyList<LoanScheduleEntry> Generate(
        Guid     assetId,
        decimal  principal,
        decimal  annualRate,
        int      termMonths,
        DateOnly firstPaymentDate)
    {
        if (principal <= 0)   throw new ArgumentOutOfRangeException(nameof(principal));
        if (annualRate < 0)   throw new ArgumentOutOfRangeException(nameof(annualRate));
        if (termMonths <= 0)  throw new ArgumentOutOfRangeException(nameof(termMonths));

        var r = annualRate / 12m;

        // Monthly payment (rounded to nearest NT dollar)
        decimal monthlyPayment;
        if (r == 0m)
        {
            monthlyPayment = Math.Round(principal / termMonths, 0, MidpointRounding.AwayFromZero);
        }
        else
        {
            var factor = (decimal)Math.Pow((double)(1m + r), termMonths);
            monthlyPayment = Math.Round(principal * r * factor / (factor - 1m), 0, MidpointRounding.AwayFromZero);
        }

        var entries  = new List<LoanScheduleEntry>(termMonths);
        var remaining = principal;

        for (var i = 1; i <= termMonths; i++)
        {
            var interest      = Math.Round(remaining * r, 0, MidpointRounding.AwayFromZero);
            bool isLast       = i == termMonths;
            var principalPart = isLast ? remaining : monthlyPayment - interest;
            var total         = principalPart + interest;
            remaining        -= principalPart;

            entries.Add(new LoanScheduleEntry(
                Id:              Guid.NewGuid(),
                AssetId:         assetId,
                Period:          i,
                DueDate:         firstPaymentDate.AddMonths(i - 1),
                TotalAmount:     total,
                PrincipalAmount: principalPart,
                InterestAmount:  interest,
                Remaining:       Math.Max(0m, remaining),
                IsPaid:          false,
                PaidAt:          null,
                TradeId:         null));
        }

        return entries;
    }
}
