using Assetra.AppLayer.Portfolio.Dtos;

namespace Assetra.AppLayer.Portfolio.Contracts;

public interface ISellWorkflowService
{
    Task<SellWorkflowResult> RecordAsync(
        SellWorkflowRequest request,
        CancellationToken ct = default);
}
