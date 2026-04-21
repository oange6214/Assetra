using Assetra.AppLayer.Portfolio.Contracts;
using Assetra.Infrastructure.Persistence;

namespace Assetra.WPF.Infrastructure;

public sealed class PortfolioHistoryMaintenanceService : IPortfolioHistoryMaintenanceService
{
    private readonly PortfolioSnapshotService _snapshotService;
    private readonly PortfolioBackfillService _backfillService;

    public PortfolioHistoryMaintenanceService(
        PortfolioSnapshotService snapshotService,
        PortfolioBackfillService backfillService)
    {
        _snapshotService = snapshotService;
        _backfillService = backfillService;
    }

    public Task<bool> TryRecordSnapshotAsync(
        decimal totalCost,
        decimal marketValue,
        decimal pnl,
        int positionCount,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return _snapshotService.TryRecordAsync(totalCost, marketValue, pnl, positionCount);
    }

    public Task<int> BackfillAsync(CancellationToken ct = default)
        => _backfillService.BackfillAsync(ct);
}
