using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.Infrastructure.Http;

namespace Assetra.Infrastructure.History;

/// <summary>
/// 盤中分時來源路由（績效比較頁 1D/5D）：台股個股 + 1D + 有 Fugle key → Fugle 官方盤中（<c>/intraday/candles</c>，
/// 最準）；^指數／海外、以及所有 5D（Fugle 盤中僅「今日」）一律走 Yahoo（<c>interval=1m/5m</c>）。Fugle 未設 key
/// 或回傳點數不足時自動退 Yahoo，盡量不讓圖空白。
/// </summary>
internal sealed class DynamicIntradayProvider : IIntradayHistoryProvider
{
    private readonly YahooFinanceHistoryProvider _yahoo;
    private readonly FugleClient _fugle;

    public DynamicIntradayProvider(HttpClient http, FugleClient fugleClient)
    {
        _yahoo = new YahooFinanceHistoryProvider(http);
        _fugle = fugleClient;
    }

    public async Task<IReadOnlyList<IntradayPoint>> GetIntradayAsync(
        string symbol, string exchange, IntradayRange range, CancellationToken ct = default)
    {
        var isIndexOrForeign = YahooSymbolMapper.IsForeignExchange(exchange)
            || (!string.IsNullOrEmpty(symbol) && symbol.StartsWith('^'));

        // 台股個股 ＋ 1D ＋ 有 Fugle key → Fugle 官方盤中；點數不足再退 Yahoo。
        if (!isIndexOrForeign && range == IntradayRange.OneDay && _fugle.IsConfigured)
        {
            var fugle = await _fugle.FetchIntradayCandlesAsync(symbol, range, ct).ConfigureAwait(false);
            if (fugle.Count >= 2)
                return fugle;
        }

        return await _yahoo.GetIntradayAsync(symbol, exchange, range, ct).ConfigureAwait(false);
    }
}
