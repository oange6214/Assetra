using Assetra.Core.Models.Analysis;

namespace Assetra.Core.Interfaces.Analysis;

public interface IMoneyWeightedReturnCalculator
{
    Task<decimal?> ComputeAsync(PerformancePeriod period, CancellationToken ct = default);
    Task<decimal?> ComputeForEntryAsync(Guid portfolioEntryId, PerformancePeriod period, CancellationToken ct = default);
}
