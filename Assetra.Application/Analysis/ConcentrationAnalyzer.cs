using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Analysis;
using Assetra.Core.Models.Analysis;

namespace Assetra.Application.Analysis;

/// <summary>
/// Concentration measured on cost basis (PositionSnapshot.TotalCost). Live market
/// value would require a synchronous quote service which Assetra doesn't currently
/// expose; cost basis is a stable proxy good enough for MVP alerts.
///
/// <para>
/// Cross-currency (v0.14.1): when <see cref="IMultiCurrencyValuationService"/> +
/// <see cref="IAppSettingsService"/> are wired, each entry's <c>TotalCost</c> is converted from
/// <c>PortfolioEntry.Currency</c> to <c>AppSettings.BaseCurrency</c> before bucket aggregation
/// (using today as <c>asOf</c>). Buckets with missing FX rates are skipped to avoid mixing
/// currencies in the denominator.
/// </para>
/// </summary>
public sealed class ConcentrationAnalyzer : IConcentrationAnalyzer
{
    private readonly IPortfolioRepository _portfolio;
    private readonly IPositionQueryService _positions;
    private readonly IMultiCurrencyValuationService? _fx;
    private readonly IAppSettingsService? _settings;

    public ConcentrationAnalyzer(
        IPortfolioRepository portfolio,
        IPositionQueryService positions,
        IMultiCurrencyValuationService? fx = null,
        IAppSettingsService? settings = null)
    {
        _portfolio = portfolio;
        _positions = positions;
        _fx = fx;
        _settings = settings;
    }

    public async Task<IReadOnlyList<ConcentrationBucket>> AnalyzeAsync(int topN = 5, CancellationToken ct = default)
    {
        if (topN < 1) throw new ArgumentOutOfRangeException(nameof(topN));
        var (buckets, total) = await BuildBucketsAsync(ct).ConfigureAwait(false);
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
        var (buckets, total) = await BuildBucketsAsync(ct).ConfigureAwait(false);
        if (buckets.Count == 0 || total == 0m) return null;
        var hhi = 0m;
        foreach (var b in buckets)
        {
            var w = b.MarketValue / total;
            hhi += w * w;
        }
        return hhi;
    }

    private async Task<(List<ConcentrationBucket> buckets, decimal total)> BuildBucketsAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var entries = await _portfolio.GetEntriesAsync().ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        var snapshots = await _positions.GetAllPositionSnapshotsAsync().ConfigureAwait(false);

        var baseCcy = _settings?.Current.BaseCurrency;
        var fxEnabled = _fx is not null && !string.IsNullOrWhiteSpace(baseCcy);
        var asOf = DateOnly.FromDateTime(DateTime.Today);

        var buckets = new List<ConcentrationBucket>();
        var total = 0m;
        foreach (var e in entries.Where(x => x.IsActive))
        {
            if (!snapshots.TryGetValue(e.Id, out var snap)) continue;
            if (snap.Quantity <= 0 || snap.TotalCost <= 0) continue;

            var cost = snap.TotalCost;
            if (fxEnabled
                && !string.IsNullOrWhiteSpace(e.Currency)
                && !string.Equals(e.Currency, baseCcy, StringComparison.OrdinalIgnoreCase))
            {
                var converted = await _fx!.ConvertAsync(cost, e.Currency, baseCcy!, asOf, ct).ConfigureAwait(false);
                if (converted is null) continue;
                cost = converted.Value;
            }

            var label = string.IsNullOrWhiteSpace(e.DisplayName) ? e.Symbol : $"{e.Symbol} {e.DisplayName}";
            buckets.Add(new ConcentrationBucket(label, cost, 0m));
            total += cost;
        }
        return (buckets, total);
    }
}
