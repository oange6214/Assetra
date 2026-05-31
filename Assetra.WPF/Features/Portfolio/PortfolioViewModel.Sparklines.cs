using Assetra.Core.Models;

namespace Assetra.WPF.Features.Portfolio;

/// <summary>
/// PortfolioViewModel partial — per-position sparkline 載入。
/// 走 CachedStockHistoryProvider，每符號每天最多打一次外部 API；
/// 快取於 equity_ohlc_cache 表，重啟 App 仍可命中。
/// </summary>
public partial class PortfolioViewModel
{
    private bool _sparklinesQueued;

    /// <summary>
    /// 由 RebuildTotals / LoadPositionsAsync 完成後呼叫。Fire-and-forget — 不阻塞 UI。
    /// 同時只允許一個 batch 在跑，避免快速 reload 時重複請求。
    /// </summary>
    private void QueueSparklineLoadIfNeeded()
    {
        if (_stockHistory is null)
            return;
        if (_sparklinesQueued)
            return;

        var rows = Positions
            .Where(p => p.IsStock && !string.IsNullOrEmpty(p.Symbol))
            .ToList();
        if (rows.Count == 0)
            return;

        _sparklinesQueued = true;
        _ = LoadSparklinesAsync(rows);
    }

    private async Task LoadSparklinesAsync(IReadOnlyList<PortfolioRowViewModel> rows)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        try
        {
            foreach (var row in rows)
            {
                IReadOnlyList<Assetra.Core.Models.OhlcvPoint>? history = null;
                try
                {
                    history = await _stockHistory!
                        .GetHistoryAsync(row.Symbol, row.Exchange, ChartPeriod.OneMonth)
                        .ConfigureAwait(false);
                }
                catch
                {
                    history = null; // provider 故障 → 走 Unavailable 分支
                }

                if (history is null || history.Count < 2)
                {
                    // 沒抓到資料 → 標記為 Unavailable，cell 顯示「—」
                    if (dispatcher is not null && !dispatcher.CheckAccess())
                        dispatcher.Invoke(() => row.SparklineState = 2);
                    else
                        row.SparklineState = 2;
                    continue;
                }

                var points = history.OrderBy(p => p.Date).Select(p => (double)p.Close).ToArray();

                // ObservableCollection mutation via property setter → marshal 回 UI thread
                if (dispatcher is not null && !dispatcher.CheckAccess())
                {
                    dispatcher.Invoke(() =>
                    {
                        row.SparklinePoints = points;
                        row.SparklineState = 1; // Loaded
                        RefreshSelectedPortfolioGroupDetail();
                    });
                }
                else
                {
                    row.SparklinePoints = points;
                    row.SparklineState = 1;
                    RefreshSelectedPortfolioGroupDetail();
                }
            }
        }
        finally
        {
            _sparklinesQueued = false;
        }
    }
}
