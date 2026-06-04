namespace Assetra.Application.Calculators;
public sealed class RuleOf72Calculator
{
    public double DoublingYears(double annualRatePercent) => annualRatePercent <= 0 ? double.PositiveInfinity : 72.0 / annualRatePercent;
    public double RequiredRatePercent(double years) => years <= 0 ? double.PositiveInfinity : 72.0 / years;
}
