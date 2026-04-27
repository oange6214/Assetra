using Assetra.Core.Models.Import;

namespace Assetra.Core.Interfaces.Import;

public interface IImportRuleRepository
{
    Task<IReadOnlyList<ImportRule>> GetAllAsync(CancellationToken ct = default);
    Task<ImportRule?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(ImportRule rule, CancellationToken ct = default);
    Task UpdateAsync(ImportRule rule, CancellationToken ct = default);
    Task RemoveAsync(Guid id, CancellationToken ct = default);
}
