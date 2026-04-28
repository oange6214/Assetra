using Assetra.Core.Models;

namespace Assetra.Core.Interfaces;

public interface IAlertRepository
{
    Task<IReadOnlyList<AlertRule>> GetRulesAsync(CancellationToken ct = default);
    Task AddAsync(AlertRule rule, CancellationToken ct = default);
    Task RemoveAsync(Guid id, CancellationToken ct = default);
    Task UpdateAsync(AlertRule rule, CancellationToken ct = default);
}
