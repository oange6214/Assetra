using Assetra.Application.Portfolio.Dtos;

namespace Assetra.Application.Portfolio.Contracts;

public interface IPositionDeletionWorkflowService
{
    Task DeleteAsync(
        PositionDeletionRequest request,
        CancellationToken ct = default);
}
