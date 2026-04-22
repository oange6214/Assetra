using Assetra.Application.Portfolio.Dtos;

namespace Assetra.Application.Portfolio.Contracts;

public interface IAccountMutationWorkflowService
{
    Task ArchiveAsync(Guid accountId, CancellationToken ct = default);

    Task<AccountDeletionResult> DeleteAsync(Guid accountId, CancellationToken ct = default);
}
