namespace Assetra.Core.Trading;

public enum TradeCostSide
{
    Buy,
    Sell,
}

public sealed record TradeCostRequest(
    string Exchange,
    string Symbol,
    TradeCostSide Side,
    decimal Price,
    decimal Quantity,
    decimal CommissionDiscount = 1m,
    bool IsEtf = false,
    bool IsBondEtf = false);

public sealed record TradeCostEstimate(
    decimal GrossAmount,
    decimal Commission,
    decimal TransactionTax,
    decimal NetAmount,
    bool IsComplete,
    string Notes = "");

public interface ITradeCostEstimator
{
    bool CanHandle(string exchange);
    TradeCostEstimate Estimate(TradeCostRequest request);
}
