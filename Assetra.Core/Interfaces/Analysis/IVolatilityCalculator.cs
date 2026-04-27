namespace Assetra.Core.Interfaces.Analysis;

public interface IVolatilityCalculator
{
    /// <summary>
    /// Annualized volatility = sample std-dev of daily returns × √252.
    /// Returns null when fewer than 2 valid observations exist.
    /// </summary>
    decimal? ComputeAnnualized(IReadOnlyList<(DateOnly Date, decimal Value)> values);
}
