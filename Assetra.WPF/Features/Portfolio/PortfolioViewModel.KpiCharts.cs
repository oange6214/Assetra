using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace Assetra.WPF.Features.Portfolio;

/// <summary>
/// PortfolioViewModel partial — 「KPI 卡展開圖表」狀態與資料。
///
/// Broker app pattern：4 張 KPI 卡（總市值 / 總付出成本 / 損益試算 / 今日漲跌）右上各
/// 有一個小圖示按鈕，點擊在 KPI row 下方展開對應的圖表分析面板。同一時間只展開一個
/// （避免堆疊），用 <see cref="ExpandedKpiPanel"/> 字串作為單選 state。
///
/// 對應內容：
/// - "marketvalue"  → 持股佔比 donut（per-position breakdown）
/// - "cost"         → 付出成本分布 donut（per-position by Cost）
/// - "pnl"          → 盈虧檔數 pie（賺/賠/平 三色）
/// - "daychange"    → 7/30/90 天每日損益柱狀圖（可切換週期）
/// </summary>
public partial class PortfolioViewModel
{
    /// <summary>目前展開的 KPI 圖表面板鍵；null = 全部摺疊。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAnyKpiPanelExpanded))]
    [NotifyPropertyChangedFor(nameof(IsKpiMarketValueExpanded))]
    [NotifyPropertyChangedFor(nameof(IsKpiCostExpanded))]
    [NotifyPropertyChangedFor(nameof(IsKpiPnlExpanded))]
    [NotifyPropertyChangedFor(nameof(IsKpiDayChangeExpanded))]
    private string? _expandedKpiPanel;

    public bool IsAnyKpiPanelExpanded => !string.IsNullOrEmpty(ExpandedKpiPanel);
    public bool IsKpiMarketValueExpanded => ExpandedKpiPanel == "marketvalue";
    public bool IsKpiCostExpanded => ExpandedKpiPanel == "cost";
    public bool IsKpiPnlExpanded => ExpandedKpiPanel == "pnl";
    public bool IsKpiDayChangeExpanded => ExpandedKpiPanel == "daychange";

    // ── Per-position pies（持股佔比 / 付出成本分布）─────────────────────────

    /// <summary>持股佔比 — 每檔市值佔總市值的 slice 集合，給 legend 用。</summary>
    public System.Collections.ObjectModel.ObservableCollection<KpiSlice> PositionAllocationSlices { get; } = [];
    [ObservableProperty] private ISeries[] _positionAllocationSeries = [];

    /// <summary>付出成本分布 — 每檔成本佔總成本的 slice 集合。</summary>
    public System.Collections.ObjectModel.ObservableCollection<KpiSlice> PositionCostSlices { get; } = [];
    [ObservableProperty] private ISeries[] _positionCostSeries = [];

    /// <summary>盈虧檔數分布 — 賺 / 賠 / 平 三 slice。Value 用市值加總。</summary>
    public System.Collections.ObjectModel.ObservableCollection<KpiSlice> PnlBreakdownSlices { get; } = [];
    [ObservableProperty] private ISeries[] _pnlBreakdownSeries = [];

    // ── Daily PnL 柱狀圖 + 週期 toggle ────────────────────────────────────

    /// <summary>每日 PnL 柱狀圖 series（紅漲綠跌，台灣慣例）。</summary>
    [ObservableProperty] private ISeries[] _dailyPnlSeries = [];
    [ObservableProperty] private ICartesianAxis[] _dailyPnlXAxes = [new Axis { IsVisible = false }];
    [ObservableProperty] private ICartesianAxis[] _dailyPnlYAxes = [new Axis { IsVisible = false }];

    /// <summary>柱狀圖檢視週期：7 / 30 / 90 天（預設 30）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPeriod7))]
    [NotifyPropertyChangedFor(nameof(IsPeriod30))]
    [NotifyPropertyChangedFor(nameof(IsPeriod90))]
    private int _dailyPnlPeriodDays = 30;

    public bool IsPeriod7 => DailyPnlPeriodDays == 7;
    public bool IsPeriod30 => DailyPnlPeriodDays == 30;
    public bool IsPeriod90 => DailyPnlPeriodDays == 90;

    [RelayCommand]
    private void SetDailyPnlPeriod(string daysRaw)
    {
        if (int.TryParse(daysRaw, out var days) && days is 7 or 30 or 90)
        {
            DailyPnlPeriodDays = days;
            if (IsKpiDayChangeExpanded)
                RebuildDailyPnlChart();
        }
    }

    /// <summary>
    /// 點 KPI 卡的小圖示時呼叫。相同鍵 → 摺疊；不同鍵 → 切換到新面板。
    /// 展開「daychange」時即時用最新 snapshots 重算柱狀圖（cheap）。
    /// </summary>
    [RelayCommand]
    private void ToggleKpiPanel(string? key)
    {
        if (string.IsNullOrEmpty(key))
        { ExpandedKpiPanel = null; return; }
        ExpandedKpiPanel = ExpandedKpiPanel == key ? null : key;

        if (ExpandedKpiPanel == "daychange")
            RebuildDailyPnlChart();
        else if (ExpandedKpiPanel is "marketvalue" or "cost" or "pnl")
            RebuildPositionPieCharts();
    }

    // ── Per-position 圓餅計算 ────────────────────────────────────────────

    /// <summary>調色盤：依持倉排序順序循環指派。</summary>
    private static readonly string[] PiePalette =
    [
        "#3B82F6", "#10B981", "#F59E0B", "#8B5CF6", "#EF4444",
        "#06B6D4", "#84CC16", "#EC4899", "#F97316", "#14B8A6",
        "#6366F1", "#A855F7",
    ];

    /// <summary>
    /// 重建三個 per-position 圓餅 — 持股佔比 / 成本分布 / 盈虧檔數。
    /// 由 RebuildTotals 觸發；只在面板展開時才會被使用，但 collection
    /// 還是先建好以避免展開瞬間空白閃爍。
    /// </summary>
    /// <remarks>
    /// Threading：RebuildTotals 可能從 background thread 被呼叫（async quote
    /// continuation 等），但 ObservableCollection 已綁到 ItemsControl/PieChart，
    /// 必須 marshal 回 UI thread 才能 Clear/Add，否則 throw
    /// NotSupportedException「CollectionView does not support changes from
    /// thread different from Dispatcher thread」。
    /// </remarks>
    private void RebuildPositionPieCharts()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            // 非 UI thread → marshal 回去再執行
            dispatcher.Invoke(RebuildPositionPieChartsCore);
            return;
        }
        RebuildPositionPieChartsCore();
    }

    private void RebuildPositionPieChartsCore()
    {
        // P5.12 — Cross-currency fix. Previously used Cost / MarketValue / Pnl
        // (each in 原幣) directly, so a USD position's value (e.g. 12,806 USD ≈
        // 410K TWD ≈ 5.8% of portfolio) was treated as 12,806 in a sum that
        // included 4M+ TWD values, making the USD slice round to ~0.2% and
        // visually disappear. Use the *Base properties (CostBase / MarketValueBase
        // / PnlBase, populated by ApplyPositionBaseValuations earlier in
        // RebuildTotals) so all rows compare in the same base currency.

        // ── 1. 持股佔比（依 MarketValue 排序，由大到小）  ──
        var byMv = Positions
            .Where(p => p.MarketValueBase > 0m)
            .OrderByDescending(p => p.MarketValueBase)
            .ToList();
        var totalMv = byMv.Sum(p => p.MarketValueBase);
        PopulatePieSlices(PositionAllocationSlices, byMv,
            p => p.MarketValueBase,
            p => $"{p.Symbol} {p.Name}",
            totalMv,
            out var mvSeries);
        PositionAllocationSeries = mvSeries;

        // ── 2. 付出成本分布（依 Cost 排序）  ──
        var byCost = Positions
            .Where(p => p.CostBase > 0m)
            .OrderByDescending(p => p.CostBase)
            .ToList();
        var totalCost = byCost.Sum(p => p.CostBase);
        PopulatePieSlices(PositionCostSlices, byCost,
            p => p.CostBase,
            p => $"{p.Symbol} {p.Name}",
            totalCost,
            out var costSeries);
        PositionCostSeries = costSeries;

        // ── 3. 盈虧檔數（賺 / 賠 / 平 三色，size 用市值加總）  ──
        // Pnl > / < / == 0 sign comparison is currency-agnostic, but the magnitude
        // sum used for slice size needs MarketValueBase (cross-currency comparable).
        var winners = Positions.Where(p => p.PnlBase > 0m).ToList();
        var losers = Positions.Where(p => p.PnlBase < 0m).ToList();
        var flats = Positions.Where(p => p.PnlBase == 0m && p.MarketValueBase > 0m).ToList();

        var winSum = winners.Sum(p => p.MarketValueBase);
        var lossSum = losers.Sum(p => p.MarketValueBase);
        var flatSum = flats.Sum(p => p.MarketValueBase);
        var grandTotal = winSum + lossSum + flatSum;

        PnlBreakdownSlices.Clear();
        if (grandTotal > 0m)
        {
            if (winners.Count > 0)
                PnlBreakdownSlices.Add(new KpiSlice(
                    $"獲利 {winners.Count} 檔", winSum, winSum / grandTotal * 100m, "#EF4444"));
            if (losers.Count > 0)
                PnlBreakdownSlices.Add(new KpiSlice(
                    $"虧損 {losers.Count} 檔", lossSum, lossSum / grandTotal * 100m, "#22C55E"));
            if (flats.Count > 0)
                PnlBreakdownSlices.Add(new KpiSlice(
                    $"持平 {flats.Count} 檔", flatSum, flatSum / grandTotal * 100m, "#94A3B8"));
        }
        PnlBreakdownSeries = BuildPieSeries(PnlBreakdownSlices);
    }

    private static void PopulatePieSlices(
        System.Collections.ObjectModel.ObservableCollection<KpiSlice> target,
        IList<PortfolioRowViewModel> rows,
        Func<PortfolioRowViewModel, decimal> valueSelector,
        Func<PortfolioRowViewModel, string> labelSelector,
        decimal total,
        out ISeries[] series)
    {
        target.Clear();
        if (total <= 0m || rows.Count == 0)
        {
            series = [];
            return;
        }
        // 前 8 大個別顯示；其餘併成「其他」
        const int maxIndividual = 8;
        var head = rows.Take(maxIndividual).ToList();
        var tail = rows.Skip(maxIndividual).ToList();
        for (var i = 0; i < head.Count; i++)
        {
            var v = valueSelector(head[i]);
            target.Add(new KpiSlice(
                labelSelector(head[i]),
                v,
                v / total * 100m,
                PiePalette[i % PiePalette.Length]));
        }
        if (tail.Count > 0)
        {
            var v = tail.Sum(valueSelector);
            target.Add(new KpiSlice(
                $"其他 {tail.Count} 檔",
                v,
                v / total * 100m,
                "#94A3B8"));
        }
        series = BuildPieSeries(target);
    }

    private static ISeries[] BuildPieSeries(
        System.Collections.Generic.IEnumerable<KpiSlice> slices)
    {
        return slices.Select(s =>
        {
            var paint = new SolidColorPaint(SKColor.Parse(s.ColorHex));
            return (ISeries)new PieSeries<double>
            {
                Values = [(double)s.Value],
                Name = $"{s.Label}  NT${s.Value:N0}  ({s.Percent:F1}%)",
                InnerRadius = 40,
                Fill = paint,
                Stroke = null,
                DataLabelsSize = 0,
                HoverPushout = 6,
                AnimationsSpeed = TimeSpan.Zero,
            };
        }).ToArray();
    }

    /// <summary>
    /// 從 <see cref="History"/>.Snapshots 取最近 30 天計算每日「市場 PnL 變動」，
    /// 紅柱 = 漲、綠柱 = 跌（台灣慣例）。
    /// 關鍵：用 <c>Pnl</c>（累計未實現損益）的日差，**不是** MarketValue 的日差，
    /// 才能排除買賣 / 現金流 / 投組首次建立等非市場波動的干擾。
    /// 例：建倉日 MV 從 0 → $10M 但 Pnl 還是 0；用 Pnl 差就不會出現 $10M 巨柱。
    /// 額外用 5× median 做 outlier filter，防止偶發資料異常吃掉整圖。
    /// </summary>
    private void RebuildDailyPnlChart()
    {
        var snapshots = History.Snapshots;
        if (snapshots is null || snapshots.Count == 0)
        {
            DailyPnlSeries = [];
            return;
        }

        // 依「日期區間」（不是 snapshot 個數）取最近 N 個日曆天。
        // 用個數的話：snapshot 數量不夠時 30 / 90 兩種模式取得的資料一樣，UI 完全沒差別；
        // 用日期區間：7 天大約 5 根（週末沒交易日無 snapshot）、30 天約 22、90 天約 65。
        // 再額外取一筆「界線前最後一筆」當基準，用來算窗內第一天的 delta。
        var ordered = snapshots.OrderBy(s => s.SnapshotDate).ToList();
        var n = DailyPnlPeriodDays;
        var today = DateOnly.FromDateTime(DateTime.Today);
        var since = today.AddDays(-n);
        var window = ordered.Where(s => s.SnapshotDate >= since).ToList();
        var prior = ordered.LastOrDefault(s => s.SnapshotDate < since);
        if (prior is not null && window.Count > 0)
            window.Insert(0, prior);
        // 至少要 2 個點才算得出 delta；少於則跳過。
        if (window.Count < 2)
        {
            DailyPnlSeries = [];
            return;
        }

        // 先算所有原始 delta — 用 Pnl 而非 MarketValue
        var rawDeltas = new double[window.Count - 1];
        for (var i = 1; i < window.Count; i++)
            rawDeltas[i - 1] = (double)(window[i].Pnl - window[i - 1].Pnl);

        // Outlier 上限：取 |delta| 的 median × 5；若 median 為 0 則退而用 mean
        var abs = rawDeltas.Select(Math.Abs).Where(v => v > 0).OrderBy(v => v).ToArray();
        double cap = double.MaxValue;
        if (abs.Length > 0)
        {
            var median = abs[abs.Length / 2];
            var mean = abs.Average();
            cap = (median > 0 ? median : mean) * 5;
        }

        var ups = new List<double?>(rawDeltas.Length);
        var downs = new List<double?>(rawDeltas.Length);
        var labels = new List<string>(rawDeltas.Length);
        for (var i = 0; i < rawDeltas.Length; i++)
        {
            var delta = rawDeltas[i];
            labels.Add(window[i + 1].SnapshotDate.ToString("MM/dd", CultureInfo.InvariantCulture));
            // 異常值直接視為「無資料」（柱子隱形）— 避免一根極端柱壓平其他天
            if (Math.Abs(delta) > cap)
            {
                ups.Add(null);
                downs.Add(null);
                continue;
            }
            if (delta > 0)
            { ups.Add(delta); downs.Add(null); }
            else if (delta < 0)
            { ups.Add(null); downs.Add(delta); }
            else
            { ups.Add(null); downs.Add(null); }
        }

        var upColor = GetSkColor("AppUp", "#EF4444");    // 紅（漲）
        var downColor = GetSkColor("AppDown", "#22C55E"); // 綠（跌）
        var labelColor = GetSkColor("AppTextSecondary", "#787B86");

        var tooltip = (LiveChartsCore.Kernel.ChartPoint cp) =>
        {
            var v = cp.Coordinate.PrimaryValue;
            return (v >= 0 ? "+" : "") + v.ToString("N0", CultureInfo.InvariantCulture);
        };

        DailyPnlSeries =
        [
            new ColumnSeries<double?>
            {
                Values = ups,
                Padding = 1,
                MaxBarWidth = 14,
                AnimationsSpeed = TimeSpan.Zero,
                Fill = new SolidColorPaint(upColor),
                Stroke = null,
                YToolTipLabelFormatter = tooltip,
            },
            new ColumnSeries<double?>
            {
                Values = downs,
                Padding = 1,
                MaxBarWidth = 14,
                AnimationsSpeed = TimeSpan.Zero,
                Fill = new SolidColorPaint(downColor),
                Stroke = null,
                YToolTipLabelFormatter = tooltip,
            }
        ];

        DailyPnlXAxes =
        [
            new Axis
            {
                Labels = labels,
                MinStep = 5,
                TextSize = 9,
                LabelsPaint = new SolidColorPaint(labelColor),
                SeparatorsPaint = null,
                TicksPaint = null,
            }
        ];

        DailyPnlYAxes =
        [
            new Axis
            {
                TextSize = 9,
                LabelsPaint = new SolidColorPaint(labelColor),
                SeparatorsPaint = null,
                TicksPaint = null,
                Labeler = v => v == 0 ? "0" : FormatYLabel((decimal)v),
            }
        ];
    }

    private static string FormatYLabel(decimal v)
    {
        var abs = Math.Abs(v);
        if (abs >= 10_000m)
        {
            var w = v / 10_000m;
            return (v >= 0 ? "+" : "") + w.ToString("F1", CultureInfo.InvariantCulture) + "萬";
        }
        return (v >= 0 ? "+" : "") + v.ToString("N0", CultureInfo.InvariantCulture);
    }

    private static SKColor GetSkColor(string key, string hexFallback)
    {
        if (System.Windows.Application.Current?.TryFindResource(key)
            is System.Windows.Media.SolidColorBrush brush)
        {
            var c = brush.Color;
            return new SKColor(c.R, c.G, c.B, c.A);
        }
        return SKColor.Parse(hexFallback);
    }
}

/// <summary>
/// KPI 圓餅圖的單一切片資料 — Label / Value / Percent / 顏色。
/// 用於 PositionAllocationSlices / PositionCostSlices / PnlBreakdownSlices 三個 legend。
/// </summary>
public sealed record KpiSlice(string Label, decimal Value, decimal Percent, string ColorHex);
