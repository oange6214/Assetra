using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.Infrastructure.Persistence;

/// <summary>
/// Records one portfolio snapshot per calendar day.
/// Called from PortfolioViewModel after totals are rebuilt with live prices.
/// Idempotent — the in-memory date guard prevents repeated DB writes,
/// and INSERT OR REPLACE handles app restarts within the same day.
///
/// v0.14.2: snapshots are tagged with the currency they were computed in
/// (typically <c>AppSettings.BaseCurrency</c>) so downstream analysis (MWR, trends)
/// can detect mismatches and convert via FX rather than silently mixing units.
/// </summary>
public sealed class PortfolioSnapshotService
{
    private readonly IPortfolioSnapshotRepository _repo;
    private DateOnly? _lastSnapshotDate;

    public PortfolioSnapshotService(IPortfolioSnapshotRepository repo)
    {
        _repo = repo;
    }

    /// <summary>
    /// Persist today's snapshot if it hasn't been recorded yet this session.
    /// Skips when <paramref name="marketValue"/> is zero (prices not yet loaded).
    /// </summary>
    /// <param name="currency">
    /// The currency the totals are computed in. Defaults to "TWD" for compatibility with
    /// pre-v0.14.2 callers; new callers should pass <c>AppSettings.BaseCurrency</c>.
    /// </param>
    public async Task<bool> TryRecordAsync(
        decimal totalCost, decimal marketValue, decimal pnl, int positionCount,
        string currency = "TWD")
    {
        if (marketValue <= 0)
            return false;
        if (positionCount == 0)
            return false;

        var today = DateOnly.FromDateTime(DateTime.Today);
        if (_lastSnapshotDate == today)
            return false;

        var snapshot = new PortfolioDailySnapshot(
            today, totalCost, marketValue, pnl, positionCount,
            string.IsNullOrWhiteSpace(currency) ? "TWD" : currency);
        await _repo.UpsertAsync(snapshot).ConfigureAwait(false);
        _lastSnapshotDate = today;
        return true;
    }
}
