using Assetra.Core.Models;

namespace Assetra.Core.Interfaces;

public interface IPortfolioEventRepository
{
    Task<IReadOnlyList<PortfolioEvent>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<PortfolioEvent>> GetRangeAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
    Task AddAsync(PortfolioEvent evt, CancellationToken ct = default);
    Task UpdateAsync(PortfolioEvent evt, CancellationToken ct = default);
    Task RemoveAsync(Guid id, CancellationToken ct = default);
}
