using Assetra.Application.Portfolio.Dtos;

namespace Assetra.Application.Portfolio.Contracts;

public interface ISellWorkflowService
{
    Task<SellWorkflowResult> RecordAsync(
        SellWorkflowRequest request,
        CancellationToken ct = default);
}
