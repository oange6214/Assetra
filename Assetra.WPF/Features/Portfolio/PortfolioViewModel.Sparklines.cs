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
            .Where(IsSparklineEligible)
            .ToList();
        if (rows.Count == 0)
            return;

        _sparklinesQueued = true;
        _ = LoadSparklinesAsync(rows);
    }

    /// <summary>
    /// 哪些部位要載入 sparkline —— 進而決定「投資組合走勢圖」會不會把它算進去。
    /// 用 <see cref="PortfolioRowViewModel.IsTradeableSecurity"/>（所有可交易標的：股票／ETF／
    /// 基金／債券／貴金屬／加密貨幣），不是只有 <c>IsStock</c>（AssetType.Stock）。
    /// 原本用 IsStock 會漏掉 ETF 等——例如台股 ETF 00988A（AssetType.Etf）就沒 sparkline，
    /// 選群組看走勢時整檔被漏算、金額嚴重短計（使用者實例：柏翰只畫出 DRAM、漏了 00988A）。
    /// </summary>
    internal static bool IsSparklineEligible(PortfolioRowViewModel row) =>
        row.IsTradeableSecurity && !string.IsNullOrEmpty(row.Symbol);

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
