using Assetra.Application.Calculators;
using Assetra.Core.Models.Calculators;
using Xunit;
namespace Assetra.Tests.Application.Calculators;
public class LoanAmortizationServiceTests
{
    [Fact] // WHY: 攤還表的本金加總必須等於本金、末期餘額必須歸零，否則攤還邏輯錯誤
    public void Calculate_AmortizesToZero_PrincipalSumsToLoan()
    {
        var s = new LoanAmortizationService().Calculate(new(300_000m, 0.06m, 12));
        Assert.Equal(12, s.Rows.Count);
        Assert.Equal(0m, s.Rows[^1].EndBalance);
        Assert.Equal(300_000m, decimal.Round(s.Rows.Sum(r => r.Principal), 0));
        Assert.Equal(s.TotalPayment - 300_000m, decimal.Round(s.TotalInterest, 0));
    }
    [Fact] // WHY: 零利率必須退化為平均攤還、零利息
    public void Calculate_ZeroRate_SplitsEvenly()
    {
        var s = new LoanAmortizationService().Calculate(new(120_000m, 0m, 12));
        Assert.Equal(10_000m, s.MonthlyPayment);
        Assert.Equal(0m, s.TotalInterest);
    }
}
