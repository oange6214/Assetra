using Assetra.Core.Models;

namespace Assetra.Core.Interfaces;

public interface ICategoryRepository
{
    Task<IReadOnlyList<ExpenseCategory>> GetAllAsync(CancellationToken ct = default);
    Task<ExpenseCategory?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(ExpenseCategory category, CancellationToken ct = default);
    Task UpdateAsync(ExpenseCategory category, CancellationToken ct = default);
    Task RemoveAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// 是否有任何分類存在（含已封存）。供初次啟動判斷是否需要種子資料。
    /// </summary>
    Task<bool> AnyAsync(CancellationToken ct = default);
}
