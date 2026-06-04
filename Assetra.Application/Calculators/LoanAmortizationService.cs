using Assetra.Core.Models.Calculators;
namespace Assetra.Application.Calculators;
public sealed class LoanAmortizationService
{
    public LoanAmortizationSchedule Calculate(LoanAmortizationInputs i)
    {
        if (i.Principal <= 0) throw new ArgumentOutOfRangeException(nameof(i.Principal));
        if (i.AnnualRate < 0) throw new ArgumentOutOfRangeException(nameof(i.AnnualRate));
        if (i.Months <= 0) throw new ArgumentOutOfRangeException(nameof(i.Months));

        var r = i.AnnualRate / 12m;
        var n = i.Months;
        decimal pmt = r == 0m
            ? decimal.Round(i.Principal / n, 2)
            : decimal.Round(i.Principal * (r * Pow(1m + r, n)) / (Pow(1m + r, n) - 1m), 2);

        var rows = new List<LoanPaymentRow>(n);
        var balance = i.Principal;
        for (int m = 1; m <= n; m++)
        {
            var interest = decimal.Round(balance * r, 2);
            var principalPart = pmt - interest;
            var begin = balance;
            balance -= principalPart;
            if (m == n) { principalPart += balance; balance = 0m; }
            rows.Add(new(m, begin, pmt, principalPart, interest, balance < 0 ? 0m : balance));
        }
        var totalPayment = decimal.Round(pmt * n, 0);
        return new(pmt, totalPayment, decimal.Round(totalPayment - i.Principal, 0), rows);
    }

    internal static decimal Pow(decimal baseValue, int exp)
    {
        decimal result = 1m;
        for (int k = 0; k < exp; k++) result *= baseValue;
        return result;
    }

    public decimal RemainingBalanceAtMonth(LoanAmortizationInputs i, int month)
    {
        var s = Calculate(i);
        if (month <= 0) return i.Principal;
        if (month > s.Rows.Count) return 0m;
        return s.Rows[month - 1].EndBalance;
    }
}
