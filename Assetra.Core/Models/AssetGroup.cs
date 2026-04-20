namespace Assetra.Core.Models;

/// <summary>
/// A user-managed (or system-default) category within a FinancialType.
/// Examples: "🏦 銀行帳戶" under Asset; "🏦 銀行貸款" under Liability.
/// IsSystem groups cannot be deleted.
/// </summary>
public sealed record AssetGroup(
    Guid          Id,
    string        Name,
    FinancialType Type,
    string?       Icon,
    int           SortOrder,
    bool          IsSystem,
    DateOnly      CreatedDate);
