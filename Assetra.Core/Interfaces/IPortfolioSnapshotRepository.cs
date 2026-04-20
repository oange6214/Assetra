using Assetra.Core.Models;

namespace Assetra.Core.Interfaces;

public interface IPortfolioSnapshotRepository
{
    Task<IReadOnlyList<PortfolioDailySnapshot>> GetSnapshotsAsync(DateOnly? from = null, DateOnly? to = null);
    Task<PortfolioDailySnapshot?> GetSnapshotAsync(DateOnly date);
    Task UpsertAsync(PortfolioDailySnapshot snapshot);
}
