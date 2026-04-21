using Assetra.AppLayer.Portfolio.Dtos;

namespace Assetra.AppLayer.Portfolio.Contracts;

public interface IPositionDeletionWorkflowService
{
    Task DeleteAsync(
        PositionDeletionRequest request,
        CancellationToken ct = default);
}
