using Assetra.Core.Models;

namespace Assetra.Core.Interfaces;

public interface IStockService : IDisposable
{
    IObservable<IReadOnlyList<StockQuote>> QuoteStream { get; }
    void Start();
    void Stop();

    /// <summary>
    /// 立即抓取一次所有持倉 / 警示標的的報價，結果照常推送到 <see cref="QuoteStream"/>，
    /// 不必等下一輪輪詢。供「報價來源 / API 金鑰」等設定變更後即時刷新使用：
    /// 會略過盤別 / 冷啟動 gate，強制重抓（使用者剛換來源就是要看到結果）。
    /// 失敗時靜默（與輪詢同樣行為），不拋出例外。
    /// </summary>
    Task RefreshNowAsync(CancellationToken ct = default);
}
