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
