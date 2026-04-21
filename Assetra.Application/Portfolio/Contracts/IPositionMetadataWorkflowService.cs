using Assetra.AppLayer.Portfolio.Dtos;

namespace Assetra.AppLayer.Portfolio.Contracts;

public interface IPositionMetadataWorkflowService
{
    Task UpdateAsync(
        PositionMetadataUpdateRequest request,
        CancellationToken ct = default);
}
