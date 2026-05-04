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

    /// <summary>
    /// Count recurring transactions referencing a specific category. Default
    /// fallback is in-memory count over <see cref="GetAllAsync" />;
    /// SQLite-backed implementations should override with SQL <c>COUNT(*)</c>.
    /// </summary>
    async Task<int> CountByCategoryAsync(Guid categoryId, CancellationToken ct = default)
    {
        var all = await GetAllAsync(ct).ConfigureAwait(false);
        return all.Count(r => r.CategoryId == categoryId);
    }
}
