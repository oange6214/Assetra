using Assetra.Application.Portfolio.Contracts;
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
        string currency = "TWD",
        decimal? cashValue = null,
        decimal? liabilityValue = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return _snapshotService.TryRecordAsync(
            totalCost, marketValue, pnl, positionCount, currency,
            cashValue: cashValue,
            equityValue: marketValue,  // 投資組合的 equity = MarketValue
            liabilityValue: liabilityValue);
    }

    public Task<int> BackfillAsync(CancellationToken ct = default)
        => _backfillService.BackfillAsync(ct);

    public Task<bool> RepairSnapshotAsync(DateOnly date, CancellationToken ct = default)
        => _backfillService.RepairSnapshotAsync(date, ct);
}
