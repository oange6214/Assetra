using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Analysis;
using Assetra.Core.Models.Analysis;

namespace Assetra.Application.Analysis;

/// <summary>
/// Concentration measured on cost basis (PositionSnapshot.TotalCost). Live market
/// value would require a synchronous quote service which Assetra doesn't currently
/// expose; cost basis is a stable proxy good enough for MVP alerts.
/// </summary>
public sealed class ConcentrationAnalyzer : IConcentrationAnalyzer
{
    private readonly IPortfolioRepository _portfolio;
    private readonly IPositionQueryService _positions;

    public ConcentrationAnalyzer(IPortfolioRepository portfolio, IPositionQueryService positions)
    {
        _portfolio = portfolio;
        _positions = positions;
    }

    public async Task<IReadOnlyList<ConcentrationBucket>> AnalyzeAsync(int topN = 5, CancellationToken ct = default)
    {
        if (topN < 1) throw new ArgumentOutOfRangeException(nameof(topN));
        var (buckets, total) = await BuildBucketsAsync().ConfigureAwait(false);
        if (buckets.Count == 0 || total == 0m) return Array.Empty<ConcentrationBucket>();

        var ordered = buckets.OrderByDescending(b => b.MarketValue).ToList();
        var top = ordered.Take(topN).ToList();
        var others = ordered.Skip(topN).ToList();
        var result = top.Select(b => new ConcentrationBucket(b.Label, b.MarketValue, b.MarketValue / total)).ToList();
        if (others.Count > 0)
        {
            var sumOther = others.Sum(b => b.MarketValue);
            result.Add(new ConcentrationBucket("Others", sumOther, sumOther / total));
        }
        return result;
    }

    public async Task<decimal?> ComputeHhiAsync(CancellationToken ct = default)
    {
        var (buckets, total) = await BuildBucketsAsync().ConfigureAwait(false);
        if (buckets.Count == 0 || total == 0m) return null;
        var hhi = 0m;
        foreach (var b in buckets)
        {
            var w = b.MarketValue / total;
            hhi += w * w;
        }
        return hhi;
    }

    private async Task<(List<ConcentrationBucket> buckets, decimal total)> BuildBucketsAsync()
    {
        var entries = await _portfolio.GetEntriesAsync().ConfigureAwait(false);
        var snapshots = await _positions.GetAllPositionSnapshotsAsync().ConfigureAwait(false);

        var buckets = new List<ConcentrationBucket>();
        var total = 0m;
        foreach (var e in entries.Where(x => x.IsActive))
        {
            if (!snapshots.TryGetValue(e.Id, out var snap)) continue;
            if (snap.Quantity <= 0 || snap.TotalCost <= 0) continue;
            var label = string.IsNullOrWhiteSpace(e.DisplayName) ? e.Symbol : $"{e.Symbol} {e.DisplayName}";
            buckets.Add(new ConcentrationBucket(label, snap.TotalCost, 0m));
            total += snap.TotalCost;
        }
        return (buckets, total);
    }
}
