using Assetra.Application.Portfolio.Dtos;

namespace Assetra.Application.Portfolio.Contracts;

public interface IPositionMetadataWorkflowService
{
    Task UpdateAsync(
        PositionMetadataUpdateRequest request,
        CancellationToken ct = default);
}
