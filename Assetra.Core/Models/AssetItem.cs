namespace Assetra.Core.Models;

/// <summary>
/// A concrete managed item — a bank account, a property, a loan, etc.
/// Replaces CashAccount (Type=Asset) and LiabilityAccount (Type=Liability).
/// UUIDs from the old tables are preserved so all Trade FK columns remain valid.
/// </summary>
public sealed record AssetItem(
    Guid          Id,
    string        Name,
    FinancialType Type,
    Guid?         GroupId,
    string        Currency,
    DateOnly      CreatedDate,
    bool          IsActive = true,
    DateTime?     UpdatedAt = null);
