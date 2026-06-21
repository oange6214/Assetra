using Assetra.Core.Models;

namespace Assetra.Core.Interfaces;

/// <summary>
/// 盤中分時 K 線抓取（給績效比較頁 1D/5D 盤中曲線）。與 <see cref="IStockHistoryProvider"/>（日線）分開，
/// 因為盤中點帶時分（<see cref="IntradayPoint"/> 的 <c>DateTime</c>）、日線是 <c>DateOnly</c>。
/// 無資料／來源未設定時回空集合（呼叫端自行退日線、不讓圖空白）。
/// </summary>
public interface IIntradayHistoryProvider
{
    Task<IReadOnlyList<IntradayPoint>> GetIntradayAsync(
        string symbol, string exchange, IntradayRange range, CancellationToken ct = default);
}
