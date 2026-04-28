using Assetra.Core.Models;

namespace Assetra.Core.Interfaces;

public interface IPendingRecurringEntryRepository
{
    Task<IReadOnlyList<PendingRecurringEntry>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<PendingRecurringEntry>> GetByStatusAsync(PendingStatus status, CancellationToken ct = default);
    Task<PendingRecurringEntry?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(PendingRecurringEntry entry, CancellationToken ct = default);
    Task UpdateAsync(PendingRecurringEntry entry, CancellationToken ct = default);
    Task RemoveAsync(Guid id, CancellationToken ct = default);
    Task RemoveByRecurringSourceAsync(Guid recurringSourceId, CancellationToken ct = default);
}
