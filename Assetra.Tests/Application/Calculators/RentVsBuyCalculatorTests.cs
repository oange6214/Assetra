using Assetra.Application.Calculators;
using Assetra.Core.Models.Calculators;
using Xunit;
namespace Assetra.Tests.Application.Calculators;
public class RentVsBuyCalculatorTests
{
    private static RentVsBuyCalculator New() => new(new LoanAmortizationService());

    [Fact] // WHY: 高增值時買應較划算且有損益兩平年
    public void HighAppreciation_BuyBecomesCheaper()
    {
        var r = New().Calculate(new(
            HomePrice: 10_000_000m, DownPayment: 2_000_000m, MortgageAnnualRate: 0.02m, LoanYears: 30,
            AnnualHoldingCostRate: 0.01m, AnnualAppreciation: 0.04m,
            MonthlyRent: 30_000m, AnnualRentIncrease: 0.02m, CompareYears: 30));
        Assert.True(r.BuyCheaper);
        Assert.NotNull(r.BreakEvenYear);
    }
    [Fact] // WHY: 租方 N 年淨成本 = 逐年遞增租金加總（漲幅 0 時 = 月租×12×N）
    public void ZeroRentIncrease_RentCostIsFlatSum()
    {
        var r = New().Calculate(new(8_000_000m, 1_600_000m, 0.02m, 30, 0.01m, 0m, 25_000m, 0m, 10));
        Assert.Equal(25_000m * 12 * 10, r.RentNetCost);
    }
    [Fact] // WHY: 若買方淨成本在比較期間內從未低於租方，損益兩平年應為 null
    public void HighRentAndLowAppreciation_NoBreakEvenYear()
    {
        // Very high rent relative to buy costs, but zero appreciation — buy never becomes cheaper
        var r = New().Calculate(new(
            HomePrice: 10_000_000m, DownPayment: 5_000_000m, MortgageAnnualRate: 0.03m, LoanYears: 30,
            AnnualHoldingCostRate: 0.02m, AnnualAppreciation: 0m,
            MonthlyRent: 5_000m, AnnualRentIncrease: 0m, CompareYears: 30));
        // Very cheap rent vs expensive buy with zero appreciation → buy never wins
        Assert.Null(r.BreakEvenYear);
        Assert.False(r.BuyCheaper);
    }
}
