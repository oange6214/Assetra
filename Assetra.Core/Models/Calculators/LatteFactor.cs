namespace Assetra.Core.Models.Calculators;
public enum LatteFrequency { Daily, Weekly, Monthly }
public sealed record LatteFactorInputs(decimal AmountPerSpend, LatteFrequency Frequency, decimal AnnualReturn, int Years);
public sealed record LatteFactorResult(decimal TotalContributed, decimal FutureValue, decimal Gain);
