using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.Application.Fx;

/// <summary>
/// 從 <see cref="IFxRateRepository"/> 讀取使用者設定／批次寫入的歷史匯率。
/// 同幣別查詢回傳 1.0；雙向查詢時若直接缺資料、嘗試取反向比率倒數補齊。
/// </summary>
public sealed class StaticFxRateProvider : IFxRateProvider
{
    private readonly IFxRateRepository _repo;

    public StaticFxRateProvider(IFxRateRepository repo)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
    }

    public async Task<decimal?> GetRateAsync(string from, string to, DateOnly asOf, CancellationToken ct = default)
    {
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
            return 1m;

        var direct = await _repo.GetAsync(from, to, asOf, ct).ConfigureAwait(false);
        if (direct is not null) return direct.Rate;

        var inverse = await _repo.GetAsync(to, from, asOf, ct).ConfigureAwait(false);
        if (inverse is not null && inverse.Rate != 0m) return 1m / inverse.Rate;

        return null;
    }

    public async Task<IReadOnlyList<FxRate>> GetHistoricalSeriesAsync(
        string from, string to, DateOnly start, DateOnly end, CancellationToken ct = default)
    {
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
            return Array.Empty<FxRate>();
        return await _repo.GetRangeAsync(from, to, start, end, ct).ConfigureAwait(false);
    }
}
