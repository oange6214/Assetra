using Assetra.Application.Calculators;
using Xunit;
namespace Assetra.Tests.Application.Calculators;

public class RentVsBuyCalculatorTests
{
    private static RentVsBuyCalculator New() => new(new LoanAmortizationService());

    [Fact] // WHY: 高增值且租方投資報酬不高時，買方期末淨值應可能勝出
    public void HighAppreciation_BuyerEndingNetWorthCanWin()
    {
        var r = New().Calculate(new(
            HomePrice: 10_000_000m, DownPayment: 2_000_000m, MortgageAnnualRate: 0.02m, LoanYears: 30,
            AnnualHoldingCostRate: 0.01m, AnnualAppreciation: 0.04m,
            MonthlyRent: 30_000m, AnnualRentIncrease: 0.02m, CompareYears: 30,
            AnnualInvestmentReturn: 0.02m, PurchaseCostRate: 0.01m, SellCostRate: 0.01m));
        Assert.True(r.BuyCheaper);
        Assert.NotNull(r.BreakEvenYear);
        Assert.True(r.BuyerEndingNetWorth > r.RenterEndingNetWorth);
    }

    [Fact] // WHY: 租方應投資頭期款、買入成本與每月現金流差額，否則高房價情境會錯誤偏向買房
    public void ExpensiveHome_RenterInvestsSavedCashAndCanWin()
    {
        var r = New().Calculate(new(
            HomePrice: 50_000_000m, DownPayment: 2_000_000m, MortgageAnnualRate: 0.02m, LoanYears: 30,
            AnnualHoldingCostRate: 0.01m, AnnualAppreciation: 0.02m,
            MonthlyRent: 25_000m, AnnualRentIncrease: 0.02m, CompareYears: 10,
            AnnualInvestmentReturn: 0.04m, PurchaseCostRate: 0.02m, SellCostRate: 0.02m));

        Assert.False(r.BuyCheaper);
        Assert.Null(r.BreakEvenYear);
        Assert.True(r.RenterEndingNetWorth > r.BuyerEndingNetWorth);
        Assert.True(r.RenterInvestmentBalance > 25_000m * 12m * 10m);
    }

    [Fact] // WHY: 買入與出售成本都會降低買方期末淨值，不能只看房價增值與剩餘貸款
    public void PurchaseAndSellCostsReduceBuyerEndingNetWorth()
    {
        var r = New().Calculate(new(
            HomePrice: 10_000_000m, DownPayment: 5_000_000m, MortgageAnnualRate: 0.03m, LoanYears: 30,
            AnnualHoldingCostRate: 0.02m, AnnualAppreciation: 0m,
            MonthlyRent: 5_000m, AnnualRentIncrease: 0m, CompareYears: 1,
            AnnualInvestmentReturn: 0m, PurchaseCostRate: 0.02m, SellCostRate: 0.03m));

        Assert.Equal(200_000m, r.PurchaseCost);
        Assert.Equal(300_000m, r.SellCost);
        Assert.True(r.BuyerEndingNetWorth < 5_000_000m);
    }
}
