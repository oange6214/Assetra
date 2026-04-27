using Assetra.Core.Models.Analysis;

namespace Assetra.Core.Interfaces.Analysis;

public interface IPnlAttributionService
{
    Task<IReadOnlyList<AttributionBucket>> ComputeAsync(PerformancePeriod period, CancellationToken ct = default);
}
