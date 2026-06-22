using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.Infrastructure.History;

/// <summary>
/// 盤中分時來源（績效比較頁 1D/5D）：一律走 Yahoo（<c>interval=1m/5m &amp; range=1d/5d</c>）——它對台股個股(.TW)、
/// 指數(^TWII)、海外都穩定，且 1D/5D 都支援。Fugle 盤中經實測對 0050 等 ETF 回點不足（畫成 2 點直線），
/// 且多一次失敗的 round-trip 會拖慢切換，故盤中不採用 Fugle（Fugle 仍用於即時報價與日線）。
/// </summary>
internal sealed class DynamicIntradayProvider : IIntradayHistoryProvider
{
    private readonly YahooFinanceHistoryProvider _yahoo;

    public DynamicIntradayProvider(HttpClient http)
    {
        _yahoo = new YahooFinanceHistoryProvider(http);
    }

    public Task<IReadOnlyList<IntradayPoint>> GetIntradayAsync(
        string symbol, string exchange, IntradayRange range, CancellationToken ct = default)
        => _yahoo.GetIntradayAsync(symbol, exchange, range, ct);
}
