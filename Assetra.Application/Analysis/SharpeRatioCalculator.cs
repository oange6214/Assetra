using Assetra.Core.Interfaces.Analysis;

namespace Assetra.Application.Analysis;

public sealed class SharpeRatioCalculator : ISharpeRatioCalculator
{
    public decimal? Compute(decimal? annualizedReturn, decimal? annualizedVolatility, decimal riskFreeRate)
    {
        if (annualizedReturn is null || annualizedVolatility is null) return null;
        if (annualizedVolatility.Value == 0m) return null;
        return (annualizedReturn.Value - riskFreeRate) / annualizedVolatility.Value;
    }
}
