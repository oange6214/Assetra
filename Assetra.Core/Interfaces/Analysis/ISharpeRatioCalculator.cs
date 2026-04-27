namespace Assetra.Core.Interfaces.Analysis;

public interface ISharpeRatioCalculator
{
    /// <summary>
    /// Sharpe = (annualized return − risk-free rate) / annualized volatility.
    /// </summary>
    decimal? Compute(decimal? annualizedReturn, decimal? annualizedVolatility, decimal riskFreeRate);
}
