namespace Assetra.Core.Models;

public sealed record PortfolioEntry(
    Guid Id,
    string Symbol,
    string Exchange,
    AssetType AssetType = AssetType.Stock,
    string DisplayName = "",
    string Currency = "TWD",
    bool IsActive = true);
