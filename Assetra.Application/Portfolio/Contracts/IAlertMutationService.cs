using Assetra.Core.Models;

namespace Assetra.Application.Portfolio.Contracts;

public interface IAlertMutationService
{
    Task AddAsync(AlertRule rule, CancellationToken ct = default);
    Task UpdateAsync(AlertRule rule, CancellationToken ct = default);
    Task RemoveAsync(Guid id, CancellationToken ct = default);
}
