using Assetra.Core.Models.Analysis;

namespace Assetra.Core.Interfaces.Analysis;

public interface IConcentrationAnalyzer
{
    Task<IReadOnlyList<ConcentrationBucket>> AnalyzeAsync(int topN = 5, CancellationToken ct = default);
    Task<decimal?> ComputeHhiAsync(CancellationToken ct = default);
}
