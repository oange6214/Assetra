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
        if (i.DownPayment < 0 || i.DownPayment > i.HomePrice) throw new ArgumentOutOfRangeException(nameof(i.DownPayment));

        var loanAmount = i.HomePrice - i.DownPayment;
        var loanMonths = Math.Max(1, i.LoanYears * 12);
        var loanInputs = new LoanAmortizationInputs(loanAmount <= 0 ? 1m : loanAmount, i.MortgageAnnualRate, loanMonths);
        var monthlyPayment = loanAmount <= 0 ? 0m : _loan.Calculate(loanInputs).MonthlyPayment;
        var purchaseCost = i.HomePrice * i.PurchaseCostRate;
        var monthlyInvestmentRate = i.AnnualInvestmentReturn / 12m;
        var renterInvestment = i.DownPayment + purchaseCost;
        var totalRentPaid = 0m;
        var totalBuyerCashOut = i.DownPayment + purchaseCost;

        int? breakEven = null;
        decimal buyerEnding = 0m;
        decimal renterEnding = renterInvestment;
        decimal homeValue = i.HomePrice;
        decimal remaining = loanAmount;
        decimal sellCost = 0m;

        for (int month = 1; month <= i.CompareYears * 12; month++)
        {
            var yearIndex = (month - 1) / 12;
            var rent = i.MonthlyRent * LoanAmortizationService.Pow(1m + i.AnnualRentIncrease, yearIndex);
            var valueForHolding = i.HomePrice * LoanAmortizationService.Pow(1m + i.AnnualAppreciation, yearIndex);
            var holding = valueForHolding * i.AnnualHoldingCostRate / 12m;
            var mortgage = month <= loanMonths ? monthlyPayment : 0m;
            var buyerMonthlyOutflow = mortgage + holding;

            renterInvestment *= 1m + monthlyInvestmentRate;
            renterInvestment += buyerMonthlyOutflow - rent;
            totalRentPaid += rent;
            totalBuyerCashOut += buyerMonthlyOutflow;

            if (month % 12 == 0)
            {
                var year = month / 12;
                homeValue = i.HomePrice * LoanAmortizationService.Pow(1m + i.AnnualAppreciation, year);
                remaining = loanAmount <= 0 ? 0m : _loan.RemainingBalanceAtMonth(loanInputs, month);
                sellCost = homeValue * i.SellCostRate;
                buyerEnding = homeValue - remaining - sellCost;
                renterEnding = renterInvestment;

                if (breakEven is null && buyerEnding >= renterEnding)
                {
                    breakEven = year;
                }
            }
        }

        var difference = buyerEnding - renterEnding;
        return new(
            decimal.Round(buyerEnding, 0),
            decimal.Round(renterEnding, 0),
            decimal.Round(difference, 0),
            breakEven,
            difference >= 0m,
            decimal.Round(homeValue, 0),
            decimal.Round(remaining, 0),
            decimal.Round(renterInvestment, 0),
            decimal.Round(totalRentPaid, 0),
            decimal.Round(totalBuyerCashOut, 0),
            decimal.Round(purchaseCost, 0),
            decimal.Round(sellCost, 0));
    }
}
