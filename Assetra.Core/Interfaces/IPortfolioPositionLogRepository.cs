using Assetra.Core.Models;

namespace Assetra.Core.Interfaces;

public interface IPortfolioPositionLogRepository
{
    /// <summary>Appends a single position state entry.</summary>
    Task LogAsync(PortfolioPositionLog entry);

    /// <summary>Appends multiple entries in a single transaction (for batch seeding).</summary>
    Task LogBatchAsync(IEnumerable<PortfolioPositionLog> entries);

    /// <summary>Returns all log entries ordered by <see cref="PortfolioPositionLog.LogDate"/> ascending.</summary>
    Task<IReadOnlyList<PortfolioPositionLog>> GetAllAsync();

    /// <summary>True when at least one log entry exists (used for migration detection).</summary>
    Task<bool> HasAnyAsync();
}
