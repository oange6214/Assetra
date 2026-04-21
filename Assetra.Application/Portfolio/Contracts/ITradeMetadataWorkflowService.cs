using Assetra.Application.Portfolio.Dtos;

namespace Assetra.Application.Portfolio.Contracts;

public interface ITradeMetadataWorkflowService
{
    Task<bool> UpdateAsync(
        TradeMetadataUpdateRequest request,
        CancellationToken ct = default);
}
