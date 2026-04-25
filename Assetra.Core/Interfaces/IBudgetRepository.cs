using Assetra.Core.Models;

namespace Assetra.Core.Interfaces;

public interface IBudgetRepository
{
    Task<IReadOnlyList<Budget>> GetAllAsync(CancellationToken ct = default);
    Task<Budget?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(Budget budget, CancellationToken ct = default);
    Task UpdateAsync(Budget budget, CancellationToken ct = default);
    Task RemoveAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// 取得指定期間（月或年）對應的預算設定。
    /// Monthly: year + month；Yearly: year（month 應為 null）。
    /// </summary>
    Task<IReadOnlyList<Budget>> GetByPeriodAsync(int year, int? month, CancellationToken ct = default);
}
