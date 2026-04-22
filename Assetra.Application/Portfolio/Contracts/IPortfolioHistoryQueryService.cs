using Assetra.Core.Models;

namespace Assetra.Application.Portfolio.Contracts;

public interface IPortfolioHistoryQueryService
{
    Task<IReadOnlyList<PortfolioDailySnapshot>> GetSnapshotsAsync(
        DateOnly? from = null,
        DateOnly? to = null,
        CancellationToken ct = default);
}
