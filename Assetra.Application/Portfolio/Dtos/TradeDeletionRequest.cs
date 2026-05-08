using Assetra.Core.Models;

namespace Assetra.Application.Portfolio.Dtos;

/// <summary>
/// Why a trade is being deleted. Drives the <c>Action</c> column in the
/// trade_audit log so users (and forensic queries) can distinguish a manual
/// delete from the implicit delete that happens during the edit-recreate flow.
/// </summary>
public enum TradeDeletionReason
{
    /// <summary>User clicked the explicit delete button on a trade row.</summary>
    UserDelete,

    /// <summary>
    /// Internal delete during the edit-recreate flow — a new trade has just
    /// been written that supersedes the one being deleted. Audit history can
    /// reconstruct that the user "edited" rather than "deleted" by joining
    /// rows with <c>Action == "edit-replace"</c> against the new trade by
    /// timestamp.
    /// </summary>
    EditReplace,
}

/// <summary>
/// <paramref name="Reason"/> defaults to <see cref="TradeDeletionReason.UserDelete"/>
/// to keep call sites that don't care backwards-compatible.
/// </summary>
public sealed record TradeDeletionRequest(
    Guid TradeId,
    TradeType TradeType,
    string Symbol,
    int Quantity,
    Guid? PortfolioEntryId,
    TradeDeletionReason Reason = TradeDeletionReason.UserDelete);

public sealed record TradeDeletionResult(
    bool Success,
    bool BlockedBySell = false);
