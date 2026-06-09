using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using Assetra.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace Assetra.WPF.Features.FinancialOverview.Calendar;

/// <summary>
/// Stage 4 (Dashboard consolidation)：報酬日曆熱度圖的 ViewModel。
/// 由 PortfolioHistoryViewModel 持有，餵入既已載入的 daily snapshot；
/// 不重複呼叫 repository。月份切換在 VM 內過濾現有 snapshot 計算單日 Δ
/// 與週彙總，視覺色階由 cell VM 的 Tone 屬性決定。
/// </summary>
public sealed partial class ReturnCalendarViewModel : ObservableObject
{
    private IReadOnlyList<PortfolioDailySnapshot> _allSnapshots = [];
    private Dictionary<DateOnly, decimal> _portfolioCashFlowsByDate = [];

    /// <summary>
    /// 在 cstor 抓 UI thread 的 SynchronizationContext；UpdateSnapshots 可能
    /// 從 background thread 被呼叫（PortfolioHistoryViewModel.LoadAsync 內的
    /// continuation），ObservableCollection 的 Clear/Add 必須 marshal 回 UI
    /// thread，否則 WPF 綁定會 throw（被 LoadAsync 的 try-catch 吞掉，UI 看
    /// 起來像空白）。
    /// </summary>
    private readonly SynchronizationContext? _uiContext;

    /// <summary>目前月曆所對齊的「月份起始日」（day=1）。預設為當月。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MonthDisplay))]
    [NotifyPropertyChangedFor(nameof(CanGoPrev))]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    private DateOnly _currentMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    /// <summary>"2026 / 05" 格式的標題字串。</summary>
    public string MonthDisplay => $"{CurrentMonth.Year} / {CurrentMonth.Month:D2}";

    /// <summary>
    /// 下拉選單用的月份清單（從最早 snapshot 月份到目前月份，倒序）。
    /// 用 MonthOption record（含預先格式好的 Display 字串）— DateOnly 直接綁
    /// ComboBox 在 SelectionBoxItem path 上 Run / Path=. 都有奇怪的解析失敗
    /// （顯示空白），改成 ComboBox.DisplayMemberPath="Display" + SelectedValue
    /// 路徑後可靠多了。
    /// </summary>
    private readonly ObservableCollection<MonthOption> _availableMonths = [];
    public ReadOnlyObservableCollection<MonthOption> AvailableMonths { get; }

    private static MonthOption ToOption(DateOnly d) =>
        new(d, $"{d.Year:0000} / {d.Month:00}");

    /// <summary>月份彙總：當月絕對損益總和。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMonthlyPnlPositive))]
    [NotifyPropertyChangedFor(nameof(IsMonthlyPnlNegative))]
    private decimal _monthlyAbsolutePnl;

    /// <summary>月份彙總：當月報酬率（end/start − 1，naive）。</summary>
    [ObservableProperty] private decimal _monthlyReturnPct;

    /// <summary>True 當當月損益 &gt; 0；XAML 用 DataTrigger 切紅色。</summary>
    public bool IsMonthlyPnlPositive => MonthlyAbsolutePnl > 0m;

    /// <summary>True 當當月損益 &lt; 0；XAML 用 DataTrigger 切綠色。</summary>
    public bool IsMonthlyPnlNegative => MonthlyAbsolutePnl < 0m;

    /// <summary>當月是否有任何資料；false 時顯示 empty state。</summary>
    [ObservableProperty] private bool _hasData;

    /// <summary>
    /// v2：cell 色階分桶基準 — false（預設）按 |Δ%| 分；true 按 |Δ 絕對值| 分。
    /// 切換不重抓資料，只重算 cell tone。
    /// </summary>
    [ObservableProperty] private bool _useAbsoluteForTone;
    partial void OnUseAbsoluteForToneChanged(bool _)
    {
        Rebuild();
        // 年度檢視也要立即重算 tone — 否則切換後得切走再切回來才生效。
        OnPropertyChanged(nameof(YearViewCells));
        OnPropertyChanged(nameof(YearViewWeekColumns));
    }

    /// <summary>
    /// v2 #6d：年度檢視 — 把 12 個月併成一個熱度圖（GitHub contribution graph style）。
    /// 切換時只重組 view-only collection，不影響月曆主 grid 的 Cells。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(YearViewCells))]
    [NotifyPropertyChangedFor(nameof(YearViewWeekColumns))]
    private bool _isYearView;

    /// <summary>
    /// 年度熱度圖的 cell 集合 — 365 (或 366) 個 DailyCellVm，按整年順序。
    /// 純 computed，IsYearView=true 時 binding 才會看；XAML 用 UniformGrid Rows="7"
    /// 渲染（7 row × 53 col 接近一年）。
    /// </summary>
    public IReadOnlyList<DailyCellVm> YearViewCells => BuildYearCells();

    /// <summary>
    /// 年度熱度圖的「週欄」集合 — GitHub-style 嚴格 7-row 排列。
    /// 每 column = 一週（Mon..Sun），長度 ≈ 54。1/1 的 day-of-week 前面 cells
    /// 用 null 填空，12/31 後同理。
    /// </summary>
    public IReadOnlyList<YearWeekColumnVm> YearViewWeekColumns => BuildYearWeekColumns();

    /// <summary>
    /// 使用者點選的單一日 cell。null 時 popover 關閉。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCellPopoverOpen))]
    private DailyCellVm? _selectedCell;

    public bool IsCellPopoverOpen => SelectedCell is not null;

    /// <summary>
    /// 月底迷你 bar chart 的 series — 當月每日 PnL 直方圖（紅漲綠跌）。
    /// 沒資料的日子 column 為 0，HasData=true 的日子用該日 Delta 值。
    /// </summary>
    [ObservableProperty] private ISeries[] _monthlyBarSeries = [];
    [ObservableProperty] private ICartesianAxis[] _barXAxes = [new Axis { IsVisible = false }];
    [ObservableProperty] private ICartesianAxis[] _barYAxes = [new Axis { IsVisible = false }];

    /// <summary>42 個 cell（6 週 × 7 天）。非當月或無資料的 cell 用 placeholder。</summary>
    private readonly ObservableCollection<DailyCellVm> _cells = [];
    public ReadOnlyObservableCollection<DailyCellVm> Cells { get; }

    /// <summary>
    /// 6 個週列（每列 = 7 天 + 週合計）。Cells 的 grid 投影；提供右側「週損益」欄
    /// 用。Cells 仍保留為 flat 視角給單元測試與向後相容用。
    /// </summary>
    private readonly ObservableCollection<WeekRowVm> _weeks = [];
    public ReadOnlyObservableCollection<WeekRowVm> Weeks { get; }

    /// <summary>是否可以往前一個月 — 在有更早統計來源資料時為 true。</summary>
    public bool CanGoPrev => TryGetCalendarDateRange(out var earliest, out _)
        && earliest < CurrentMonth;

    /// <summary>是否可以往後一個月 — 不超過今天。</summary>
    public bool CanGoNext => CurrentMonth < new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1);

    public ReturnCalendarViewModel()
    {
        _uiContext = SynchronizationContext.Current;
        Cells = new ReadOnlyObservableCollection<DailyCellVm>(_cells);
        Weeks = new ReadOnlyObservableCollection<WeekRowVm>(_weeks);
        AvailableMonths = new ReadOnlyObservableCollection<MonthOption>(_availableMonths);
        // 初始至少含 CurrentMonth — 避免 snapshots 還沒載入時，ComboBox 因
        // ItemsSource 空但 SelectedValue 設了 CurrentMonth 而清空，造成 dropdown
        // 顯示空白。UpdateSnapshots 之後會用完整月份清單覆寫。
        _availableMonths.Add(ToOption(CurrentMonth));
        Rebuild();
    }

    /// <summary>
    /// 由 PortfolioHistoryViewModel 在每次 LoadAsync 後呼叫。報酬日曆仍以每日
    /// snapshot 市值變化為主；交易只用來調整同日買入、賣出、股利等現金流，
    /// 避免「投入本金」或「賣出本金」被誤看成當日報酬。
    /// </summary>
    public void UpdatePortfolioData(IReadOnlyList<PortfolioDailySnapshot> snapshots, IReadOnlyList<Trade> trades)
    {
        _allSnapshots = snapshots ?? [];
        _portfolioCashFlowsByDate = BuildPortfolioCashFlowsByDate(trades ?? []);

        if (_uiContext is not null && SynchronizationContext.Current != _uiContext)
        {
            _uiContext.Post(_ => ApplyCalendarDataOnUi(), null);
        }
        else
        {
            ApplyCalendarDataOnUi();
        }
    }

    /// <summary>由 PortfolioHistoryViewModel 在每次 LoadAsync 後呼叫。</summary>
    public void UpdateSnapshots(IReadOnlyList<PortfolioDailySnapshot> snapshots)
    {
        _allSnapshots = snapshots ?? [];
        _portfolioCashFlowsByDate = [];

        if (_uiContext is not null && SynchronizationContext.Current != _uiContext)
        {
            _uiContext.Post(_ => ApplySnapshotsOnUi(), null);
        }
        else
        {
            ApplySnapshotsOnUi();
        }
    }

    private void ApplySnapshotsOnUi() => ApplyCalendarDataOnUi();

    private void ApplyCalendarDataOnUi()
    {
        // 重建月份下拉選單：從最早資料月份到目前月份，倒序。
        _availableMonths.Clear();
        if (TryGetCalendarDateRange(out var earliest, out var latest))
        {
            var today = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1);
            var stop = latest > today ? new DateOnly(latest.Year, latest.Month, 1) : today;
            var cursor = new DateOnly(earliest.Year, earliest.Month, 1);
            var list = new List<DateOnly>();
            while (cursor <= stop)
            {
                list.Add(cursor);
                cursor = cursor.AddMonths(1);
            }
            list.Reverse();
            foreach (var m in list)
                _availableMonths.Add(ToOption(m));
        }
        else
        {
            _availableMonths.Add(ToOption(CurrentMonth));
        }

        // 若預設月份沒有資料，跳到最新有資料的月份。
        if (TryGetCalendarDateRange(out _, out var latestDataDate))
        {
            var target = new DateOnly(latestDataDate.Year, latestDataDate.Month, 1);
            CurrentMonth = target;
        }
        Rebuild();
        // 強制 ComboBox 重新解析 SelectedValue — Clear() 過 _availableMonths 後
        // ComboBox 的 SelectedValue 會掉成 null（找不到舊 value）；若 target 剛好
        // 等於原本 CurrentMonth（例如建構式預設 2026/05 + 最新 snapshot 也是 2026/05），
        // setter 不會 fire PropertyChanged，ComboBox 永遠停在 null 顯示空白。
        OnPropertyChanged(nameof(CurrentMonth));
        OnPropertyChanged(nameof(CanGoPrev));
        OnPropertyChanged(nameof(CanGoNext));
    }

    partial void OnCurrentMonthChanged(DateOnly value)
    {
        Rebuild();
        OnPropertyChanged(nameof(CanGoPrev));
        OnPropertyChanged(nameof(CanGoNext));
    }

    [RelayCommand]
    private void GoPrev()
    {
        if (!CanGoPrev)
            return;
        var prev = CurrentMonth.AddMonths(-1);
        CurrentMonth = new DateOnly(prev.Year, prev.Month, 1);
    }

    [RelayCommand]
    private void GoNext()
    {
        if (!CanGoNext)
            return;
        var next = CurrentMonth.AddMonths(1);
        CurrentMonth = new DateOnly(next.Year, next.Month, 1);
    }

    [RelayCommand]
    private void GoToday() =>
        CurrentMonth = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1);

    /// <summary>使用者點 cell 開 popover；HasData=false 的 cell 不開（沒內容可看）。</summary>
    [RelayCommand]
    private void SelectCell(DailyCellVm? cell)
    {
        if (cell is null || !cell.HasData || !cell.IsCurrentMonth)
            return;
        SelectedCell = cell;
    }

    [RelayCommand]
    private void CloseCellPopover() => SelectedCell = null;

    /// <summary>
    /// 從 popover 跳到 TransactionLog 並把 trade filter 的日期區間設為該日。
    /// 使用 ShellNavigationEvents.RequestTransactionsForDate 把日期一併傳遞，
    /// PortfolioViewModel 訂閱後設置 TradeFilter.TradeDateFrom/To。
    /// </summary>
    [RelayCommand]
    private void OpenDayTransactions()
    {
        if (SelectedCell is null)
            return;
        var date = SelectedCell.Date;
        SelectedCell = null;
        Assetra.WPF.Infrastructure.ShellNavigationEvents.RequestTransactionsForDate(date);
    }

    /// <summary>重算 42 個 cell。每日 Δ = 該日 marketValue − 前一交易日 marketValue。</summary>
    private void Rebuild()
    {
        _cells.Clear();

        var firstOfMonth = CurrentMonth;
        var daysInMonth = DateTime.DaysInMonth(firstOfMonth.Year, firstOfMonth.Month);

        // 計算 grid 起點：那個月第一天「往前推到該週週一」的日期。
        // DayOfWeek: Sun=0..Sat=6；台灣慣例週一為週首，所以 Monday=0..Sunday=6。
        var dow = (int)firstOfMonth.DayOfWeek;  // 0=Sun..6=Sat
        var mondayOffset = dow == 0 ? 6 : dow - 1;
        var gridStart = firstOfMonth.AddDays(-mondayOffset);

        // 按 SnapshotDate 建索引方便 O(1) 查詢。報酬日曆只呈現交易日；
        // 週末快照會折回前一個交易日，並排除新舊 snapshot schema 混用造成的
        // legacy 回補異常值。
        var tradingSnapshots = BuildReturnCalendarSnapshots(_allSnapshots);
        var snapshotByDate = tradingSnapshots
            .GroupBy(s => s.SnapshotDate)
            .ToDictionary(g => g.Key, g => g.Last());

        // 前一個有資料的 snapshot；用於算當月第一天前的 baseline。
        decimal? prevValue = null;
        var earliestInMonth = snapshotByDate.Keys
            .Where(d => d.Year == firstOfMonth.Year && d.Month == firstOfMonth.Month)
            .OrderBy(d => d)
            .FirstOrDefault();
        if (earliestInMonth != default)
        {
            var prior = tradingSnapshots
                .Where(s => s.SnapshotDate < earliestInMonth)
                .OrderByDescending(s => s.SnapshotDate)
                .FirstOrDefault();
            prevValue = prior?.MarketValue;
        }

        var monthOpenValue = prevValue;
        decimal? lastValue = null;
        var monthlyDelta = 0m;

        for (var i = 0; i < 42; i++)
        {
            var date = gridStart.AddDays(i);
            var isCurrentMonth = date.Month == firstOfMonth.Month && date.Year == firstOfMonth.Year;
            var isWeekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            PortfolioDailySnapshot? snap = null;
            var hasSnapshot = !isWeekend && snapshotByDate.TryGetValue(date, out snap);

            decimal? delta = null;
            decimal? deltaPct = null;
            if (hasSnapshot && prevValue.HasValue && prevValue.Value != 0m)
            {
                var cashFlowAdjustment = _portfolioCashFlowsByDate.GetValueOrDefault(date);
                delta = snap!.MarketValue - prevValue.Value + cashFlowAdjustment;
                deltaPct = delta / prevValue.Value;
                if (isCurrentMonth)
                {
                    monthlyDelta += delta.Value;
                    lastValue = snap.MarketValue;
                }
            }
            if (hasSnapshot)
                prevValue = snap!.MarketValue;

            _cells.Add(new DailyCellVm(
                Date: date,
                Day: date.Day,
                IsCurrentMonth: isCurrentMonth,
                IsWeekend: isWeekend,
                HasData: hasSnapshot && delta.HasValue,
                Delta: delta,
                DeltaDisplay: FormatDelta(delta),
                Tone: UseAbsoluteForTone
                    ? ResolveToneByAbsolute(delta)
                    : ResolveTone(delta, deltaPct)));
        }

        MonthlyAbsolutePnl = monthlyDelta;
        if (monthOpenValue.HasValue && monthOpenValue.Value > 0m && lastValue.HasValue)
            MonthlyReturnPct = monthlyDelta / monthOpenValue.Value;
        else
            MonthlyReturnPct = 0m;
        HasData = _cells.Any(c => c.HasData);

        // 重建 Weeks（每 7 個 cell 一列 + 週合計）
        _weeks.Clear();
        for (var w = 0; w < 6; w++)
        {
            var dayCells = new List<DailyCellVm>(7);
            decimal weekSum = 0m;
            var anyData = false;
            for (var k = 0; k < 7; k++)
            {
                var cell = _cells[w * 7 + k];
                dayCells.Add(cell);
                if (cell.IsCurrentMonth && cell.HasData && cell.Delta.HasValue)
                {
                    weekSum += cell.Delta.Value;
                    anyData = true;
                }
            }
            _weeks.Add(new WeekRowVm(
                WeekIndex: w,
                Days: dayCells,
                TotalDelta: anyData ? weekSum : (decimal?)null,
                TotalDisplay: anyData ? FormatDelta(weekSum) : "—"));
        }

        RebuildBarChart(daysInMonth);
    }

    /// <summary>
    /// 用當月 cells 的 Delta 值建立 column series。一柱 = 該日 PnL；
    /// 紅綠依台灣慣例（漲紅跌綠）。沒資料的日子值為 0，柱子隱形。
    /// </summary>
    private void RebuildBarChart(int daysInMonth)
    {
        var upColor = GetSkColor("AppUp", "#EF4444");      // 紅（漲）
        var downColor = GetSkColor("AppDown", "#22C55E");  // 綠（跌）
        var labelColor = GetSkColor("AppTextSecondary", "#787B86");

        // 一個 day 一格；正值放 ups[i]，負值放 downs[i]，另一邊填 0。
        // 用兩個 StackedColumnSeries 著色（紅漲綠跌），對齊台灣 broker app 慣例。
        // 堆疊系列同一 category 只畫一根置中、滿格的柱；每日 up/down 只有一邊非 0，
        // 所以可見柱顏色正確且不會被另一系列往左右錯位（避免 LiveCharts 自動 dodge）。
        var ups = new List<double?>(daysInMonth);
        var downs = new List<double?>(daysInMonth);
        for (var day = 1; day <= daysInMonth; day++)
        {
            var cell = _cells.FirstOrDefault(c => c.IsCurrentMonth && c.Day == day);
            var v = cell?.Delta is decimal d ? (double)d : 0d;
            ups.Add(v > 0 ? v : 0d);
            downs.Add(v < 0 ? v : 0d);
        }

        var tooltipFormatter = (LiveChartsCore.Kernel.ChartPoint chartPoint) =>
        {
            var v = chartPoint.Coordinate.PrimaryValue;
            return (v >= 0 ? "+" : "") + v.ToString("N0", CultureInfo.InvariantCulture);
        };

        MonthlyBarSeries =
        [
            new StackedColumnSeries<double?>
            {
                Values = ups,
                Padding = 1,
                MaxBarWidth = 14,
                AnimationsSpeed = TimeSpan.Zero,
                Fill = new SolidColorPaint(upColor),
                Stroke = null,
                YToolTipLabelFormatter = tooltipFormatter,
            },
            new StackedColumnSeries<double?>
            {
                Values = downs,
                Padding = 1,
                MaxBarWidth = 14,
                AnimationsSpeed = TimeSpan.Zero,
                Fill = new SolidColorPaint(downColor),
                Stroke = null,
                YToolTipLabelFormatter = tooltipFormatter,
            }
        ];

        BarXAxes =
        [
            new Axis
            {
                Labels = Enumerable.Range(1, daysInMonth).Select(d => d.ToString(CultureInfo.InvariantCulture)).ToArray(),
                MinStep = 6,
                TextSize = 9,
                LabelsPaint = new SolidColorPaint(labelColor),
                SeparatorsPaint = null,
                TicksPaint = null,
                // 釘住整月範圍：series 以 0-based index 對應 day 1..daysInMonth
                //（index 0 = day 1）。category 柱置中於整數位置，各佔 ±0.5，
                // 故 [-0.5, daysInMonth-0.5] 剛好涵蓋第一天到最後一天。否則 LiveCharts
                // 會自動縮放到有資料的範圍（稀疏月份只顯示前幾天）。
                MinLimit = -0.5,
                MaxLimit = daysInMonth - 0.5,
            }
        ];

        BarYAxes =
        [
            new Axis
            {
                TextSize = 9,
                LabelsPaint = new SolidColorPaint(labelColor),
                SeparatorsPaint = null,
                TicksPaint = null,
                Labeler = v => v == 0 ? "0" : FormatBarYLabel((decimal)v),
                MinLimit = ups.Count + downs.Count == 0 ? -1 : null,
                MaxLimit = ups.Count + downs.Count == 0 ? 1 : null,
            }
        ];

    }

    private static string FormatBarYLabel(decimal v)
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
        if (System.Windows.Application.Current?.TryFindResource(key) is SolidColorBrush brush)
        {
            var c = brush.Color;
            return new SKColor(c.R, c.G, c.B, c.A);
        }
        return SKColor.Parse(hexFallback);
    }

    /// <summary>
    /// 建立年度熱度圖 cell 集合 — CurrentMonth.Year 整年的每一天。
    /// 全部都用 IsCurrentMonth=true 以套用 tone（年度檢視沒有「非當月」概念）。
    /// </summary>
    private IReadOnlyList<DailyCellVm> BuildYearCells()
    {
        var year = CurrentMonth.Year;
        var tradingSnapshots = BuildReturnCalendarSnapshots(_allSnapshots);
        var snapshotByDate = tradingSnapshots
            .Where(s => s.SnapshotDate.Year == year)
            .GroupBy(s => s.SnapshotDate)
            .ToDictionary(g => g.Key, g => g.Last());
        var cells = new List<DailyCellVm>(366);
        var date = new DateOnly(year, 1, 1);
        var end = new DateOnly(year, 12, 31);

        // 找年初前最後一個 snapshot 作為計算 day 1 delta 的 baseline
        decimal? prev = tradingSnapshots
            .Where(s => s.SnapshotDate < date)
            .OrderByDescending(s => s.SnapshotDate)
            .FirstOrDefault()?.MarketValue;

        while (date <= end)
        {
            var has = snapshotByDate.TryGetValue(date, out var snap);
            decimal? delta = null;
            decimal? deltaPct = null;
            if (has && prev.HasValue && prev.Value != 0m)
            {
                var cashFlowAdjustment = _portfolioCashFlowsByDate.GetValueOrDefault(date);
                delta = snap!.MarketValue - prev.Value + cashFlowAdjustment;
                deltaPct = delta / prev.Value;
            }
            if (has)
                prev = snap!.MarketValue;

            cells.Add(new DailyCellVm(
                Date: date,
                Day: date.Day,
                IsCurrentMonth: true,  // 年度檢視全顯
                IsWeekend: date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday,
                HasData: has && delta.HasValue,
                Delta: delta,
                DeltaDisplay: FormatDelta(delta),
                Tone: UseAbsoluteForTone
                    ? ResolveToneByAbsolute(delta)
                    : ResolveTone(delta, deltaPct)));
            date = date.AddDays(1);
        }
        return cells;
    }

    /// <summary>
    /// 以「週欄」結構建構年度熱度圖。每 column 7 cells（Mon..Sun），column
    /// 數量 ≈ 53–54 視該年第一天的 DOW + 是否閏年而定。MonthLabel 標在每月
    /// 第一週的 column（給 XAML 上方標題列用）。
    /// </summary>
    private IReadOnlyList<YearWeekColumnVm> BuildYearWeekColumns()
    {
        var year = CurrentMonth.Year;
        var allCells = BuildYearCells();
        var byDate = allCells.ToDictionary(c => c.Date);

        // 把 1/1 推回到該週的週一作為 grid 起點。Mon=0, Sun=6（台灣慣例）。
        var jan1 = new DateOnly(year, 1, 1);
        var dow = (int)jan1.DayOfWeek;       // Sun=0..Sat=6
        var mondayOffset = dow == 0 ? 6 : dow - 1;
        var start = jan1.AddDays(-mondayOffset);
        var end = new DateOnly(year, 12, 31);
        // 把結束日推到該週週日確保最後一欄也是完整 7 cell
        var endDow = (int)end.DayOfWeek;
        var endOffset = endDow == 0 ? 0 : 7 - endDow;
        var lastDay = end.AddDays(endOffset);

        var columns = new List<YearWeekColumnVm>();
        var cursor = start;
        int? lastMonth = null;
        while (cursor <= lastDay)
        {
            var days = new DailyCellVm?[7];
            string? monthLabel = null;
            for (var k = 0; k < 7; k++)
            {
                var d = cursor.AddDays(k);
                if (d.Year != year)
                    continue;            // 跨年 cell 留 null
                if (!byDate.TryGetValue(d, out var cell))
                    continue;
                days[k] = cell;
                // 把月份標籤標在「該週包含的第一個月份首日所在 row 0」
                if (lastMonth != d.Month && k == 0)
                {
                    monthLabel = d.Month.ToString();
                    lastMonth = d.Month;
                }
                else if (lastMonth != d.Month && d.Day <= 7)
                {
                    // 月份首週但 jan-1 不在 row 0 → 標在這欄
                    monthLabel = d.Month.ToString();
                    lastMonth = d.Month;
                }
            }
            columns.Add(new YearWeekColumnVm(days, monthLabel));
            cursor = cursor.AddDays(7);
        }
        return columns;
    }

    private bool TryGetCalendarDateRange(out DateOnly earliest, out DateOnly latest)
    {
        IReadOnlyCollection<DateOnly> dates = BuildReturnCalendarSnapshots(_allSnapshots)
            .Select(s => s.SnapshotDate)
            .ToArray();

        if (dates.Count == 0)
        {
            earliest = default;
            latest = default;
            return false;
        }

        earliest = dates.Min();
        latest = dates.Max();
        return true;
    }

    private static IReadOnlyList<PortfolioDailySnapshot> BuildReturnCalendarSnapshots(
        IEnumerable<PortfolioDailySnapshot> snapshots)
    {
        var normalized = snapshots
            .Select(s => (EffectiveDate: TryGetReturnCalendarSnapshotDate(s), Snapshot: s))
            .Where(x => x.EffectiveDate.HasValue)
            .OrderBy(x => x.EffectiveDate!.Value)
            .ThenBy(x => x.Snapshot.SnapshotDate)
            .ToList();

        var firstBreakdownDate = normalized
            .Where(x => HasSnapshotBreakdown(x.Snapshot))
            .Select(x => x.EffectiveDate)
            .FirstOrDefault();

        var result = new List<PortfolioDailySnapshot>();
        foreach (var group in normalized.GroupBy(x => x.EffectiveDate!.Value).OrderBy(g => g.Key))
        {
            IEnumerable<(DateOnly? EffectiveDate, PortfolioDailySnapshot Snapshot)> candidates = group;

            // 同一 effective date 若同時有 breakdown 與無-breakdown 候選（例：週末 breakdown
            // snapshot 回填到前一交易日，與當日舊 migration residue 競爭），優先用 breakdown。
            // 但若該交易日「只有」無-breakdown 候選，那是 PortfolioBackfillService 對漏記交易日的
            // 正當回填 —— 保留它。報酬日曆的日損益用 MarketValue（與回填同口徑 Σ qty×close），
            // 因此該日報酬可正確計算，不該被當成 residue 丟棄而留白。
            if (firstBreakdownDate.HasValue && group.Key >= firstBreakdownDate.Value)
            {
                var withBreakdown = candidates.Where(x => HasSnapshotBreakdown(x.Snapshot)).ToList();
                if (withBreakdown.Count > 0)
                    candidates = withBreakdown;
            }

            var chosen = candidates
                .OrderByDescending(x => x.Snapshot.SnapshotDate)
                .Select(x => x.Snapshot)
                .FirstOrDefault();

            if (chosen is null)
                continue;

            result.Add(chosen.SnapshotDate == group.Key
                ? chosen
                : chosen with { SnapshotDate = group.Key });
        }

        return result;
    }

    private static bool HasSnapshotBreakdown(PortfolioDailySnapshot snapshot) =>
        snapshot.CashValue.HasValue || snapshot.EquityValue.HasValue || snapshot.LiabilityValue.HasValue;

    private static DateOnly? TryGetReturnCalendarSnapshotDate(PortfolioDailySnapshot snapshot)
    {
        if (IsReturnCalendarTradingDate(snapshot.SnapshotDate))
            return snapshot.SnapshotDate;

        // 週末由即時報價刷新出的 v0.17+ snapshot 通常代表前一個交易日的收盤資料；
        // 舊格式週末 snapshot 缺少 breakdown，無法判斷來源口徑，維持忽略。
        if (!HasSnapshotBreakdown(snapshot))
            return null;

        return snapshot.SnapshotDate.DayOfWeek switch
        {
            DayOfWeek.Saturday => snapshot.SnapshotDate.AddDays(-1),
            DayOfWeek.Sunday => snapshot.SnapshotDate.AddDays(-2),
            _ => snapshot.SnapshotDate,
        };
    }

    private static Dictionary<DateOnly, decimal> BuildPortfolioCashFlowsByDate(IEnumerable<Trade> trades)
    {
        var flows = new Dictionary<DateOnly, decimal>();
        foreach (var trade in trades)
        {
            var amount = ResolvePortfolioCashFlow(trade);
            if (amount == 0m)
                continue;

            var date = ToLocalDate(trade.TradeDate);
            flows[date] = flows.TryGetValue(date, out var existing)
                ? existing + amount
                : amount;
        }

        return flows;
    }

    private static decimal ResolvePortfolioCashFlow(Trade trade) =>
        trade.Type switch
        {
            // 投資人角度的 cash flow：Buy 為負、Sell / 股利為正。
            // 日報酬用 MarketValueDelta + CashFlow，才能把本金進出歸零，只留下
            // 價格變動與交易成本/股利。
            TradeType.Buy => -ResolveBuyCashOutflow(trade),
            TradeType.Sell => ResolveSellCashInflow(trade),
            TradeType.CashDividend => ResolveCashAmount(trade),
            // 股利/收入等主交易的附屬費用子記錄，用 Withdrawal 保存。
            TradeType.Withdrawal when trade.ParentTradeId.HasValue => -ResolveCashAmount(trade),
            _ => 0m,
        };

    private static decimal ResolveBuyCashOutflow(Trade trade)
    {
        if (trade.CashAmount is { } cashAmount)
            return Math.Abs(cashAmount);

        return ResolveInstrumentNotionalInFundingCurrency(trade)
            + ResolveCommissionInFundingCurrency(trade);
    }

    private static decimal ResolveSellCashInflow(Trade trade)
    {
        if (trade.CashAmount is { } cashAmount)
            return Math.Abs(cashAmount);

        return ResolveInstrumentNotionalInFundingCurrency(trade)
            - ResolveCommissionInFundingCurrency(trade);
    }

    private static decimal ResolveCashAmount(Trade trade)
    {
        if (trade.CashAmount is { } cashAmount)
            return Math.Abs(cashAmount);

        return ResolveInstrumentNotionalInFundingCurrency(trade);
    }

    private static decimal ResolveInstrumentNotionalInFundingCurrency(Trade trade)
    {
        var amount = trade.Price * trade.Quantity;
        return amount * (trade.FxRate ?? 1m);
    }

    private static decimal ResolveCommissionInFundingCurrency(Trade trade)
    {
        var commission = trade.Commission ?? 0m;
        if (commission == 0m)
            return 0m;

        // CommissionCurrency=null 代表跟標的幣別一致，需跟 notional 一樣套 FxRate。
        // 若已明確指定手續費幣別，通常代表它已是扣款帳戶幣別，不再二次轉換。
        return string.IsNullOrWhiteSpace(trade.CommissionCurrency)
            ? commission * (trade.FxRate ?? 1m)
            : commission;
    }

    private static DateOnly ToLocalDate(DateTime value)
    {
        var local = value.Kind == DateTimeKind.Utc
            ? value.ToLocalTime()
            : value;
        return DateOnly.FromDateTime(local);
    }

    private static bool IsReturnCalendarTradingDate(DateOnly date) =>
        date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday;

    private static string FormatDelta(decimal? delta)
    {
        if (delta is null)
            return string.Empty;
        var v = delta.Value;
        // 用 K / 萬 縮短顯示：台灣慣例「萬」較合適
        var abs = Math.Abs(v);
        if (abs >= 10_000m)
        {
            var w = v / 10_000m;
            return (v >= 0 ? "+" : "") + w.ToString("F1", CultureInfo.InvariantCulture) + "萬";
        }
        return (v >= 0 ? "+" : "") + v.ToString("N0", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// v2：以「絕對值」分桶。閥值用台幣慣用單位（千 / 萬 / 十萬）。
    /// </summary>
    private static CellTone ResolveToneByAbsolute(decimal? delta)
    {
        if (delta is null)
            return CellTone.None;
        var sign = Math.Sign(delta.Value);
        if (sign == 0)
            return CellTone.None;
        var abs = Math.Abs(delta.Value);
        var bucket = abs switch
        {
            < 1_000m => 1,        // < 1K
            < 10_000m => 2,       // 1K–1萬
            < 100_000m => 3,      // 1–10萬
            _ => 4,               // > 10萬
        };
        return sign > 0
            ? bucket switch { 1 => CellTone.UpWeak, 2 => CellTone.UpMedium, 3 => CellTone.UpStrong, _ => CellTone.UpStrongest, }
            : bucket switch { 1 => CellTone.DownWeak, 2 => CellTone.DownMedium, 3 => CellTone.DownStrong, _ => CellTone.DownStrongest, };
    }

    /// <summary>
    /// 顏色強度分桶：依 |deltaPct| 分為 None / Weak / Medium / Strong。
    /// 紅綠由 sign 決定（台灣慣例：紅漲綠跌）。XAML 用 DataTrigger 對應。
    /// </summary>
    private static CellTone ResolveTone(decimal? delta, decimal? pct)
    {
        if (delta is null || !pct.HasValue)
            return CellTone.None;
        var abs = Math.Abs(pct.Value);
        var sign = Math.Sign(delta.Value);
        if (sign == 0)
            return CellTone.None;

        var bucket = abs switch
        {
            < 0.005m => 1,   // < 0.5%
            < 0.015m => 2,   // 0.5–1.5%
            < 0.03m => 3,   // 1.5–3%
            _ => 4,   // ≥ 3%
        };
        return sign > 0
            ? bucket switch { 1 => CellTone.UpWeak, 2 => CellTone.UpMedium, 3 => CellTone.UpStrong, _ => CellTone.UpStrongest, }
            : bucket switch { 1 => CellTone.DownWeak, 2 => CellTone.DownMedium, 3 => CellTone.DownStrong, _ => CellTone.DownStrongest, };
    }
}

/// <summary>
/// 月曆中的一個「週列」— 7 個日 cell + 週合計。XAML 用 ItemsControl over Weeks
/// 渲染 6 列；每列模板繪製 7 個 day cell + 1 個週合計 cell。
/// </summary>
public sealed record WeekRowVm(
    int WeekIndex,
    IReadOnlyList<DailyCellVm> Days,
    decimal? TotalDelta,
    string TotalDisplay);

/// <summary>
/// 年度熱度圖的「週欄」— 7 個 day cell（Mon..Sun，nullable 表示跨年 placeholder）
/// + 可選的月份標籤（每月第一週標示）。XAML 用 ItemsControl over Columns。
/// </summary>
public sealed record YearWeekColumnVm(
    IReadOnlyList<DailyCellVm?> Days,
    string? MonthLabel);

/// <summary>單一日曆格的 immutable 投影。</summary>
public sealed record DailyCellVm(
    DateOnly Date,
    int Day,
    bool IsCurrentMonth,
    bool IsWeekend,
    bool HasData,
    decimal? Delta,
    string DeltaDisplay,
    CellTone Tone);

/// <summary>
/// ComboBox 月份下拉選項。Display 是預先格式好的「yyyy / MM」字串，
/// 讓 ComboBox 用 DisplayMemberPath 顯示；Value 用於 SelectedValue 雙向綁
/// 回 ViewModel 的 DateOnly CurrentMonth。
/// </summary>
public sealed record MonthOption(DateOnly Value, string Display);

/// <summary>cell 色階。XAML 用 DataTrigger 把 Tone 對應到 brush。</summary>
public enum CellTone
{
    None,
    UpWeak, UpMedium, UpStrong, UpStrongest,
    DownWeak, DownMedium, DownStrong, DownStrongest,
}
