using Assetra.Core.Models;

namespace Assetra.Core.Interfaces;

public interface IRecurringTransactionRepository
{
    Task<IReadOnlyList<RecurringTransaction>> GetAllAsync(CancellationToken ct = default);
    Task<RecurringTransaction?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<RecurringTransaction>> GetActiveAsync(CancellationToken ct = default);
    Task AddAsync(RecurringTransaction recurring, CancellationToken ct = default);
    Task UpdateAsync(RecurringTransaction recurring, CancellationToken ct = default);
    Task RemoveAsync(Guid id, CancellationToken ct = default);
}
