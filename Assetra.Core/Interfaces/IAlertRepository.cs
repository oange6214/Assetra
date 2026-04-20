using Assetra.Core.Models;

namespace Assetra.Core.Interfaces;

public interface IAlertRepository
{
    Task<IReadOnlyList<AlertRule>> GetRulesAsync();
    Task AddAsync(AlertRule rule);
    Task RemoveAsync(Guid id);
    Task UpdateAsync(AlertRule rule);
}
