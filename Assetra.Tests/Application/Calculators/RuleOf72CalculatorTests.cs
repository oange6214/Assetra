using Assetra.Application.Calculators;
using Xunit;
namespace Assetra.Tests.Application.Calculators;
public class RuleOf72CalculatorTests
{
    [Fact] public void DoublingYears_From6Percent_Is12() => Assert.Equal(12.0, new RuleOf72Calculator().DoublingYears(6.0), 3);
    [Fact] public void RequiredRate_For8Years_Is9Percent() => Assert.Equal(9.0, new RuleOf72Calculator().RequiredRatePercent(8.0), 3);
    [Fact] public void NonPositiveRate_ReturnsInfinity() => Assert.True(double.IsInfinity(new RuleOf72Calculator().DoublingYears(0)));
}
