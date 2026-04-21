using Assetra.AppLayer.Portfolio.Contracts;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.AppLayer.Portfolio.Services;

public sealed class PortfolioHistoryQueryService : IPortfolioHistoryQueryService
{
    private readonly IPortfolioSnapshotRepository _snapshotRepository;

    public PortfolioHistoryQueryService(IPortfolioSnapshotRepository snapshotRepository)
    {
        _snapshotRepository = snapshotRepository;
    }

    public Task<IReadOnlyList<PortfolioDailySnapshot>> GetSnapshotsAsync(
        DateOnly? from = null,
        DateOnly? to = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return _snapshotRepository.GetSnapshotsAsync(from, to);
    }
}
