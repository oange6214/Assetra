using Assetra.AppLayer.Portfolio.Dtos;

namespace Assetra.AppLayer.Portfolio.Contracts;

public interface IAccountMutationWorkflowService
{
    Task ArchiveAsync(Guid accountId, CancellationToken ct = default);

    Task<AccountDeletionResult> DeleteAsync(Guid accountId, CancellationToken ct = default);
}
