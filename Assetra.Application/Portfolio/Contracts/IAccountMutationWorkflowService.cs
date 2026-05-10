using Assetra.Application.Portfolio.Dtos;

namespace Assetra.Application.Portfolio.Contracts;

public interface IAccountMutationWorkflowService
{
    Task ArchiveAsync(Guid accountId, CancellationToken ct = default);

    /// <summary>恢復先前封存的帳戶（IsActive 設為 true）。</summary>
    Task UnarchiveAsync(Guid accountId, CancellationToken ct = default);

    Task<AccountDeletionResult> DeleteAsync(Guid accountId, CancellationToken ct = default);
}
