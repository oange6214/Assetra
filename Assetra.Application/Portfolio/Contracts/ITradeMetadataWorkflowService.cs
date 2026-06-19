using Assetra.Application.Portfolio.Dtos;

namespace Assetra.Application.Portfolio.Contracts;

public interface ITradeMetadataWorkflowService
{
    Task<TradeMetadataUpdateResult> UpdateAsync(
        TradeMetadataUpdateRequest request,
        CancellationToken ct = default);
}
