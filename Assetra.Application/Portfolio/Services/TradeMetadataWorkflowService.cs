using Assetra.Application.Portfolio.Contracts;
using Assetra.Application.Portfolio.Dtos;
using Assetra.Core.Interfaces;

namespace Assetra.Application.Portfolio.Services;

public sealed class TradeMetadataWorkflowService : ITradeMetadataWorkflowService
{
    private readonly ITradeRepository _tradeRepository;

    public TradeMetadataWorkflowService(ITradeRepository tradeRepository)
    {
        _tradeRepository = tradeRepository;
    }

    public async Task<bool> UpdateAsync(
        TradeMetadataUpdateRequest request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var all = await _tradeRepository.GetAllAsync().ConfigureAwait(false);
        var original = all.FirstOrDefault(t => t.Id == request.TradeId);
        if (original is null)
            return false;

        var updated = original with
        {
            TradeDate = request.TradeDate,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note,
        };
        await _tradeRepository.UpdateAsync(updated).ConfigureAwait(false);
        return true;
    }
}
