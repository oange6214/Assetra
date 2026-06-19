namespace Assetra.Application.Portfolio.Dtos;

/// <summary>
/// Outcome of <see cref="Contracts.ITradeMetadataWorkflowService.UpdateAsync"/>.
/// Distinguishes a clean update from the two non-update outcomes so the UI can show the
/// right message: a missing/stale trade vs. a position-log sync that would be ambiguous
/// or reorder the quantity history — which is refused rather than silently applied.
/// </summary>
public enum TradeMetadataUpdateResult
{
    /// <summary>Trade (and, when needed, its position-log row) updated.</summary>
    Updated,

    /// <summary>No trade matched the requested id.</summary>
    NotFound,

    /// <summary>
    /// The date change would desync the position log: more than one log row sits on the old
    /// date (can't tell which this trade produced), or moving the row would cross another of
    /// the position's trades and scramble the running-quantity order. The whole edit is
    /// refused so nothing is silently corrupted — re-enter the trade with the correct date.
    /// </summary>
    BlockedByPositionLog,
}
