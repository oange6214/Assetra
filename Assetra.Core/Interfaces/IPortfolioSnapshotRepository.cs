using Assetra.Core.Models;

namespace Assetra.Core.Interfaces;

public interface IPortfolioSnapshotRepository
{
    Task<IReadOnlyList<PortfolioDailySnapshot>> GetSnapshotsAsync(
        DateOnly? from = null, DateOnly? to = null, CancellationToken ct = default);
    Task<PortfolioDailySnapshot?> GetSnapshotAsync(DateOnly date, CancellationToken ct = default);
    Task UpsertAsync(PortfolioDailySnapshot snapshot, CancellationToken ct = default);
}
