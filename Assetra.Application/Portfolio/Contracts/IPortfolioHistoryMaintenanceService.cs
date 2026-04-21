namespace Assetra.Application.Portfolio.Contracts;

public interface IPortfolioHistoryMaintenanceService
{
    Task<bool> TryRecordSnapshotAsync(
        decimal totalCost,
        decimal marketValue,
        decimal pnl,
        int positionCount,
        CancellationToken ct = default);

    Task<int> BackfillAsync(CancellationToken ct = default);
}
