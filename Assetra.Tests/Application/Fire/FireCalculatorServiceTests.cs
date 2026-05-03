using Assetra.Application.Fire;
using Assetra.Core.Models.Fire;
using Xunit;

namespace Assetra.Tests.Application.Fire;

public class FireCalculatorServiceTests
{
    private readonly FireCalculatorService _svc = new();

    [Fact]
    public void Calculate_FireNumber_Is25xExpensesAtFourPercent()
    {
        var result = _svc.Calculate(new FireInputs(
            CurrentNetWorth: 0m, AnnualExpenses: 600_000m,
            AnnualSavings: 0m, ExpectedAnnualReturn: 0m,
            WithdrawalRate: 0.04m));

        Assert.Equal(15_000_000m, result.FireNumber);
    }

    [Fact]
    public void Calculate_AlreadyAtFire_ReturnsZeroYears()
    {
        var result = _svc.Calculate(new FireInputs(
            CurrentNetWorth: 20_000_000m, AnnualExpenses: 600_000m,
            AnnualSavings: 0m, ExpectedAnnualReturn: 0.05m,
            WithdrawalRate: 0.04m));

        Assert.Equal(0, result.YearsToFire);
        Assert.Equal(20_000_000m, result.ProjectedNetWorthAtFire);
    }

    [Fact]
    public void Calculate_PositiveSavings_ReachesFire()
    {
        var result = _svc.Calculate(new FireInputs(
            CurrentNetWorth: 1_000_000m, AnnualExpenses: 600_000m,
            AnnualSavings: 600_000m, ExpectedAnnualReturn: 0.05m,
            WithdrawalRate: 0.04m,
            MaxYears: 60));

        Assert.NotNull(result.YearsToFire);
        Assert.True(result.ProjectedNetWorthAtFire >= result.FireNumber);
    }

    [Fact]
    public void Calculate_NoGrowthNoSavings_NeverReaches()
    {
        var result = _svc.Calculate(new FireInputs(
            CurrentNetWorth: 100_000m, AnnualExpenses: 600_000m,
            AnnualSavings: 0m, ExpectedAnnualReturn: 0m,
            WithdrawalRate: 0.04m,
            MaxYears: 30));

        Assert.Null(result.YearsToFire);
        Assert.Equal(31, result.WealthPath.Count);
    }

    [Fact]
    public void Calculate_WealthPath_StartsWithCurrentNetWorth()
    {
        var result = _svc.Calculate(new FireInputs(
            CurrentNetWorth: 500_000m, AnnualExpenses: 200_000m,
            AnnualSavings: 100_000m, ExpectedAnnualReturn: 0.05m,
            WithdrawalRate: 0.04m, MaxYears: 5));

        Assert.Equal(500_000m, result.WealthPath[0]);
        Assert.Equal(6, result.WealthPath.Count);
    }

    [Fact]
    public void Calculate_InvalidWithdrawalRate_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _svc.Calculate(new FireInputs(0m, 100m, 0m, 0.05m, 0m)));
    }

    [Theory]
    [InlineData(0, 0, 0, 0.05, 0.04)]
    [InlineData(0, -100, 0, 0.05, 0.04)]
    [InlineData(-1, 100, 0, 0.05, 0.04)]
    [InlineData(0, 100, -1, 0.05, 0.04)]
    [InlineData(0, 100, 0, -1, 0.04)]
    [InlineData(0, 100, 0, 0.05, 1.01)]
    public void Calculate_InvalidDomainInputs_Throw(
        double currentNetWorth,
        double annualExpenses,
        double annualSavings,
        double expectedAnnualReturn,
        double withdrawalRate)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _svc.Calculate(new FireInputs(
                (decimal)currentNetWorth,
                (decimal)annualExpenses,
                (decimal)annualSavings,
                (decimal)expectedAnnualReturn,
                (decimal)withdrawalRate)));
    }
}
