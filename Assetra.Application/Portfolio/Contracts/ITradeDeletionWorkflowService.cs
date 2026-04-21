using Assetra.AppLayer.Portfolio.Dtos;

namespace Assetra.AppLayer.Portfolio.Contracts;

public interface ITradeDeletionWorkflowService
{
    Task<TradeDeletionResult> DeleteAsync(
        TradeDeletionRequest request,
        CancellationToken ct = default);
}
