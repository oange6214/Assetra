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
    Guid? PortfolioGroupId = null)
{
    /// <summary>
    /// True when this position has been archived — the sell flow archives an entry
    /// (sets <see cref="IsActive"/> = false) once it is fully sold. Inverse of
    /// <see cref="IsActive"/>, named to match the domain's Archive/Unarchive vocabulary so
    /// "all lots archived" filters read intent-first instead of as a double-negative on IsActive.
    /// </summary>
    public bool IsArchived => !IsActive;
}
