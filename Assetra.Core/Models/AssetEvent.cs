namespace Assetra.Core.Models;

/// <summary>
/// Records either a cash transaction (changes cost basis) or a
/// valuation update (market price change, no cash flow).
/// CashAccountId is only set for Transaction events.
/// </summary>
public sealed record AssetEvent(
    Guid           Id,
    Guid           AssetId,
    AssetEventType EventType,
    DateTime       EventDate,
    decimal?       Amount,
    decimal?       Quantity,
    string?        Note,
    Guid?          CashAccountId,
    DateTime       CreatedAt);
