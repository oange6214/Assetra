using Assetra.Core.Models;

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

public sealed record EnsureStockEntryRequest(
    string Symbol,
    string? Exchange = null,
    string? Name = null);

public sealed record StockBuyRequest(
    string Symbol,
    decimal Price,
    int Quantity,
    DateOnly BuyDate,
    Guid? CashAccountId,
    decimal CommissionDiscount,
    decimal? ManualFee = null,
    string? Exchange = null,
    string? Name = null);

public sealed record StockBuyResult(
    PortfolioEntry Entry,
    decimal Commission,
    decimal? CommissionDiscountUsed,
    decimal CostPerShare);

public sealed record ManualAssetCreateRequest(
    string Symbol,
    string Exchange,
    string Name,
    AssetType AssetType,
    decimal Quantity,
    decimal TotalCost,
    decimal UnitPrice,
    DateOnly AcquiredOn);

public sealed record ManualAssetCreateResult(
    PortfolioEntry Entry,
    PositionSnapshot Snapshot);
