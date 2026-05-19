namespace Assetra.Core.Models;

public sealed record PortfolioEntry(
    Guid Id,
    string Symbol,
    string Exchange,
    AssetType AssetType = AssetType.Stock,
    string DisplayName = "",
    string Currency = "TWD",
    bool IsActive = true,
    bool IsEtf = false,
    // Portfolio-Groups-Refactor P3 — 所屬群組 (bucket)。null = legacy 未指派；
    // repo 在持久化時 fallback 到 PortfolioGroup.DefaultId（同 TradeSqliteRepository 慣例）。
    Guid? PortfolioGroupId = null);
