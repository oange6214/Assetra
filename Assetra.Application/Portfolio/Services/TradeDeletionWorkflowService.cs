using System.Text.Json;
using Assetra.Application.Portfolio.Contracts;
using Assetra.Application.Portfolio.Dtos;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.Application.Portfolio.Services;

public sealed class TradeDeletionWorkflowService : ITradeDeletionWorkflowService
{
    private readonly ITradeRepository _tradeRepository;
    private readonly IPortfolioRepository _portfolioRepository;
    private readonly IPositionQueryService _positionQueryService;

    /// <summary>
    /// Optional. When supplied, every successful deletion writes a JSON snapshot
    /// of the deleted trade so the user has a recovery trail. Null in test/null-
    /// service contexts where audit is irrelevant.
    /// </summary>
    private readonly ITradeAuditRepository? _auditRepository;

    public TradeDeletionWorkflowService(
        ITradeRepository tradeRepository,
        IPortfolioRepository portfolioRepository,
        IPositionQueryService positionQueryService,
        ITradeAuditRepository? auditRepository = null)
    {
        _tradeRepository = tradeRepository;
        _portfolioRepository = portfolioRepository;
        _positionQueryService = positionQueryService;
        _auditRepository = auditRepository;
    }

    public async Task<TradeDeletionResult> DeleteAsync(
        TradeDeletionRequest request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (await WouldRemovalCauseNegativeQtyAsync(request).ConfigureAwait(false))
            return new TradeDeletionResult(Success: false, BlockedBySell: true);

        // Capture pre-deletion snapshot for the audit log. Best-effort —
        // a missing trade or audit-write failure must not block the deletion.
        await TryWriteAuditAsync(request.TradeId, ct).ConfigureAwait(false);

        await ApplyTradeRemovalOnPositionAsync(request, ct).ConfigureAwait(false);
        await _tradeRepository.RemoveChildrenAsync(request.TradeId).ConfigureAwait(false);
        await _tradeRepository.RemoveAsync(request.TradeId).ConfigureAwait(false);

        return new TradeDeletionResult(Success: true);
    }

    private async Task TryWriteAuditAsync(Guid tradeId, CancellationToken ct)
    {
        if (_auditRepository is null)
            return;

        try
        {
            var trade = await _tradeRepository.GetByIdAsync(tradeId, ct).ConfigureAwait(false);
            if (trade is null)
                return;

            var json = JsonSerializer.Serialize(trade);
            var entry = new TradeAuditEntry(
                Id: Guid.NewGuid(),
                TradeId: tradeId,
                Action: "delete",
                TradeJson: json,
                RecordedAt: DateTime.UtcNow,
                Note: null);
            await _auditRepository.AppendAsync(entry, ct).ConfigureAwait(false);
        }
        catch
        {
            // Audit write must never abort a deletion — swallow any failure.
            // A persistent failure mode would surface as a missing audit row,
            // which is preferable to losing the user's intended delete.
        }
    }

    private async Task<bool> WouldRemovalCauseNegativeQtyAsync(TradeDeletionRequest request)
    {
        if (request.TradeType is not (TradeType.Buy or TradeType.StockDividend))
            return false;
        if (request.PortfolioEntryId is not { } entryId)
            return false;

        var snapshot = await _positionQueryService.GetPositionAsync(entryId).ConfigureAwait(false);
        return snapshot is not null && snapshot.Quantity - request.Quantity < 0;
    }

    private async Task ApplyTradeRemovalOnPositionAsync(
        TradeDeletionRequest request,
        CancellationToken ct)
    {
        if (request.PortfolioEntryId is not { } entryId)
            return;

        switch (request.TradeType)
        {
            case TradeType.Buy:
            {
                var refs = await _portfolioRepository
                    .HasTradeReferencesAsync(entryId, ct)
                    .ConfigureAwait(false);
                if (refs <= 1)
                    await _portfolioRepository.RemoveAsync(entryId).ConfigureAwait(false);
                break;
            }
            case TradeType.StockDividend:
                break;
            case TradeType.Sell:
            {
                var allEntries = await _portfolioRepository.GetEntriesAsync().ConfigureAwait(false);
                foreach (var entry in allEntries)
                {
                    if (string.Equals(entry.Symbol, request.Symbol, StringComparison.OrdinalIgnoreCase)
                        && !entry.IsActive)
                    {
                        await _portfolioRepository.UnarchiveAsync(entry.Id).ConfigureAwait(false);
                    }
                }
                break;
            }
        }
    }
}
