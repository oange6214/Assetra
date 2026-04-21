using Assetra.AppLayer.Portfolio.Dtos;

namespace Assetra.AppLayer.Portfolio.Contracts;

public interface ITradeMetadataWorkflowService
{
    Task<bool> UpdateAsync(
        TradeMetadataUpdateRequest request,
        CancellationToken ct = default);
}
