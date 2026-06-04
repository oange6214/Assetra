using Assetra.Core.Models.Calculators;
namespace Assetra.Application.Calculators;
public sealed class RentVsBuyCalculator
{
    private readonly LoanAmortizationService _loan;
    public RentVsBuyCalculator(LoanAmortizationService loan) => _loan = loan;

    public RentVsBuyResult Calculate(RentVsBuyInputs i)
    {
        if (i.HomePrice <= 0) throw new ArgumentOutOfRangeException(nameof(i.HomePrice));
        if (i.CompareYears <= 0) throw new ArgumentOutOfRangeException(nameof(i.CompareYears));
        var loanAmount = i.HomePrice - i.DownPayment;
        var loanInputs = new LoanAmortizationInputs(loanAmount <= 0 ? 1m : loanAmount, i.MortgageAnnualRate, i.LoanYears * 12);
        var monthlyPayment = loanAmount <= 0 ? 0m : _loan.Calculate(loanInputs).MonthlyPayment;

        int? breakEven = null;
        decimal buyAtN = 0m, rentAtN = 0m;
        for (int year = 1; year <= i.CompareYears; year++)
        {
            var monthsPaid = Math.Min(year, i.LoanYears) * 12;
            var mortgagePaid = monthlyPayment * monthsPaid;
            var holding = i.HomePrice * i.AnnualHoldingCostRate * year;
            var cashOut = i.DownPayment + mortgagePaid + holding;
            var homeValue = i.HomePrice * LoanAmortizationService.Pow(1m + i.AnnualAppreciation, year);
            var remaining = loanAmount <= 0 ? 0m : _loan.RemainingBalanceAtMonth(loanInputs, year * 12);
            var equity = homeValue - remaining;
            var buyNet = cashOut - equity;
            decimal rentNet = 0m;
            for (int k = 0; k < year; k++)
                rentNet += i.MonthlyRent * 12m * LoanAmortizationService.Pow(1m + i.AnnualRentIncrease, k);

            if (breakEven is null && buyNet <= rentNet) breakEven = year;
            if (year == i.CompareYears) { buyAtN = buyNet; rentAtN = rentNet; }
        }
        return new(decimal.Round(buyAtN, 0), decimal.Round(rentAtN, 0), breakEven, buyAtN <= rentAtN);
    }
}
