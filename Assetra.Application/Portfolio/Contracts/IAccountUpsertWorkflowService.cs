using Assetra.AppLayer.Portfolio.Dtos;

namespace Assetra.AppLayer.Portfolio.Contracts;

public interface IAccountUpsertWorkflowService
{
    Task<AccountUpsertResult> CreateAsync(CreateAccountRequest request, CancellationToken ct = default);

    Task<AccountUpsertResult> UpdateAsync(UpdateAccountRequest request, CancellationToken ct = default);
}
