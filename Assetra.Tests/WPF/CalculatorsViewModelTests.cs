using Assetra.Application.Calculators;
using Assetra.WPF.Features.Calculators;
using Xunit;

namespace Assetra.Tests.WPF;

public class LoanCalcViewModelTests
{
    private static LoanCalcViewModel Create() => new(new LoanAmortizationService());

    [Fact] // WHY: 壞輸入不應產生結果，且必須有錯誤訊息供 UI 顯示
    public void BadInput_NonNumericPrincipal_SetsErrorAndNoResult()
    {
        var vm = Create();
        vm.Principal = "abc";
        vm.CalculateCommand.Execute(null);
        Assert.NotNull(vm.ErrorMessage);
        Assert.False(vm.HasResult);
    }

    [Fact] // WHY: 合法輸入必須產生非零月付與攤還明細
    public void ValidInput_SetsMonthlyPaymentAndSchedule()
    {
        var vm = Create();
        vm.Principal = "300000";
        vm.AnnualRatePercent = "6";
        vm.Months = "12";
        vm.CalculateCommand.Execute(null);
        Assert.True(vm.HasResult);
        Assert.Null(vm.ErrorMessage);
        Assert.Equal(12, vm.Schedule.Count);
        Assert.NotEmpty(vm.MonthlyPayment);
    }
}

public class RentVsBuyCalcViewModelTests
{
    private static RentVsBuyCalcViewModel Create() =>
        new(new RentVsBuyCalculator(new LoanAmortizationService()));

    [Fact] // WHY: 壞輸入（非數字）不應產生結果
    public void BadInput_NonNumericHomePrice_SetsError()
    {
        var vm = Create();
        vm.HomePrice = "xyz";
        vm.CalculateCommand.Execute(null);
        Assert.NotNull(vm.ErrorMessage);
        Assert.False(vm.HasResult);
    }

    [Fact] // WHY: 合法輸入必須產生租/買淨成本，且能判斷誰划算
    public void ValidInput_ProducesBuyAndRentCosts()
    {
        var vm = Create();
        vm.HomePrice = "10000000";
        vm.DownPayment = "2000000";
        vm.MortgageRatePercent = "2";
        vm.LoanYears = "30";
        vm.HoldingCostRatePercent = "1";
        vm.AppreciationRatePercent = "4";
        vm.MonthlyRent = "30000";
        vm.RentIncreasePercent = "2";
        vm.CompareYears = "30";
        vm.CalculateCommand.Execute(null);
        Assert.True(vm.HasResult);
        Assert.Null(vm.ErrorMessage);
        Assert.NotEmpty(vm.BuyNetCost);
        Assert.NotEmpty(vm.RentNetCost);
        Assert.NotEmpty(vm.WinnerLabel);
    }
}
