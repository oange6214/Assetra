using Assetra.Application.Portfolio.Dtos;

namespace Assetra.Application.Portfolio.Contracts;

public interface ITradeDeletionWorkflowService
{
    Task<TradeDeletionResult> DeleteAsync(
        TradeDeletionRequest request,
        CancellationToken ct = default);
}
