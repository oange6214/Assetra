using Assetra.Application.Portfolio.Dtos;

namespace Assetra.Application.Portfolio.Contracts;

public interface IAccountUpsertWorkflowService
{
    Task<AccountUpsertResult> CreateAsync(CreateAccountRequest request, CancellationToken ct = default);

    Task<AccountUpsertResult> UpdateAsync(UpdateAccountRequest request, CancellationToken ct = default);

    Task<Guid> FindOrCreateAccountAsync(string name, string currency, CancellationToken ct = default);
}
