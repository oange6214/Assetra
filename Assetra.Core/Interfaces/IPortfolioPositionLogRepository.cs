using Assetra.Core.Models;

namespace Assetra.Core.Interfaces;

public interface IPortfolioPositionLogRepository
{
    /// <summary>Appends a single position state entry.</summary>
    Task LogAsync(PortfolioPositionLog entry, CancellationToken ct = default);

    /// <summary>Appends multiple entries in a single transaction (for batch seeding).</summary>
    Task LogBatchAsync(IEnumerable<PortfolioPositionLog> entries, CancellationToken ct = default);

    /// <summary>Returns all log entries ordered by <see cref="PortfolioPositionLog.LogDate"/> ascending.</summary>
    Task<IReadOnlyList<PortfolioPositionLog>> GetAllAsync(CancellationToken ct = default);

    /// <summary>True when at least one log entry exists (used for migration detection).</summary>
    Task<bool> HasAnyAsync(CancellationToken ct = default);
}
