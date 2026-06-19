using Assetra.Application.Portfolio.Contracts;
using Assetra.Application.Portfolio.Dtos;
using Assetra.Core.Interfaces;

namespace Assetra.Application.Portfolio.Services;

public sealed class TradeMetadataWorkflowService : ITradeMetadataWorkflowService
{
    private readonly ITradeRepository _tradeRepository;
    private readonly IPortfolioPositionLogRepository _positionLogRepository;

    public TradeMetadataWorkflowService(
        ITradeRepository tradeRepository,
        IPortfolioPositionLogRepository positionLogRepository)
    {
        _tradeRepository = tradeRepository;
        _positionLogRepository = positionLogRepository;
    }

    public async Task<TradeMetadataUpdateResult> UpdateAsync(
        TradeMetadataUpdateRequest request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var all = await _tradeRepository.GetAllAsync().ConfigureAwait(false);
        var original = all.FirstOrDefault(t => t.Id == request.TradeId);
        if (original is null)
            return TradeMetadataUpdateResult.NotFound;

        // Position trades (Buy / Sell / StockDividend) carry a PortfolioEntryId and have a
        // matching position-log row dated on the trade's (local) date. If the date moves we
        // must move that row too — but ONLY when it's unambiguous AND order-preserving.
        // Otherwise refuse the whole edit so we never silently desync, or scramble the
        // running-quantity history that snapshots / the return calendar are rebuilt from.
        var oldDate = DateOnly.FromDateTime(original.TradeDate.ToLocalTime());
        var newDate = DateOnly.FromDateTime(request.TradeDate.ToLocalTime());

        if (oldDate != newDate && original.PortfolioEntryId is Guid positionId)
        {
            var blocked = await TrySyncPositionLogAsync(positionId, oldDate, newDate, ct)
                .ConfigureAwait(false);
            if (blocked)
                return TradeMetadataUpdateResult.BlockedByPositionLog;
        }

        var updated = original with
        {
            TradeDate = request.TradeDate,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note,
        };
        await _tradeRepository.UpdateAsync(updated).ConfigureAwait(false);
        return TradeMetadataUpdateResult.Updated;
    }

    /// <summary>
    /// Moves the position-log row created by the edited trade from <paramref name="oldDate"/>
    /// to <paramref name="newDate"/> when that is safe. Returns <see langword="true"/> when the
    /// move is unsafe and the caller must block the edit. Safe cases:
    /// <list type="bullet">
    /// <item>No row on the old date → already desynced or non-logging trade; nothing to move.</item>
    /// <item>Exactly one row on the old date, and no other row of this position lies within the
    /// moved date span → move it.</item>
    /// </list>
    /// Unsafe (returns true): more than one row on the old date (ambiguous), or a sibling row
    /// inside the span (the move would cross another trade and reorder quantities).
    /// </summary>
    private async Task<bool> TrySyncPositionLogAsync(
        Guid positionId, DateOnly oldDate, DateOnly newDate, CancellationToken ct)
    {
        var positionLogs = (await _positionLogRepository.GetAllAsync(ct).ConfigureAwait(false))
            .Where(l => l.PositionId == positionId)
            .ToList();

        var atOldDate = positionLogs.Where(l => l.LogDate == oldDate).ToList();

        // More than one row on the old date → can't tell which this trade produced.
        if (atOldDate.Count > 1)
            return true;

        if (atOldDate.Count == 0)
            return false; // nothing to move

        var entry = atOldDate[0];
        var lo = oldDate < newDate ? oldDate : newDate;
        var hi = oldDate < newDate ? newDate : oldDate;

        // Any sibling row inside the moved span means the move would cross another trade and
        // scramble the running-quantity order.
        var wouldReorder = positionLogs.Any(l =>
            l.LogId != entry.LogId && l.LogDate >= lo && l.LogDate <= hi);
        if (wouldReorder)
            return true;

        await _positionLogRepository.UpdateLogDateAsync(entry.LogId, newDate, ct).ConfigureAwait(false);
        return false;
    }
}
