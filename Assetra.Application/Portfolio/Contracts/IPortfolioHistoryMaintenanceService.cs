namespace Assetra.Application.Portfolio.Contracts;

public interface IPortfolioHistoryMaintenanceService
{
    /// <summary>
    /// Record today's portfolio snapshot. <paramref name="currency"/> tags the totals'
    /// unit so downstream analysis can detect mismatches with the active base currency.
    /// </summary>
    Task<bool> TryRecordSnapshotAsync(
        decimal totalCost,
        decimal marketValue,
        decimal pnl,
        int positionCount,
        string currency = "TWD",
        CancellationToken ct = default);

    Task<int> BackfillAsync(CancellationToken ct = default);
}
