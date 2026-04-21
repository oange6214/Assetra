namespace Assetra.AppLayer.Portfolio.Dtos;

public sealed record ClosePriceLookupResult(
    bool HasPrice,
    decimal? Price,
    string Hint);

public sealed record BuyPreviewRequest(
    string Symbol,
    decimal Price,
    int Quantity,
    decimal CommissionDiscount,
    decimal? ManualFee);

public sealed record BuyPreviewResult(
    decimal GrossAmount,
    decimal Commission,
    decimal TotalCost,
    decimal CostPerShare);
