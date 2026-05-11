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
    /// XAML ComboBox 綁這個，SelectedItem 雙向到 CurrentMonth。
    /// </summary>
    private readonly ObservableCollection<DateOnly> _availableMonths = [];
    public ReadOnlyObservableCollection<DateOnly> AvailableMonths { get; }

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

    /// <summary>是否可以往前一個月 — 在有更早 snapshot 時為 true。</summary>
    public bool CanGoPrev => _allSnapshots.Count > 0
        && _allSnapshots.Min(s => s.SnapshotDate) < CurrentMonth;

    /// <summary>是否可以往後一個月 — 不超過今天。</summary>
    public bool CanGoNext => CurrentMonth < new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1);

    public ReturnCalendarViewModel()
    {
        _uiContext = SynchronizationContext.Current;
        Cells = new ReadOnlyObservableCollection<DailyCellVm>(_cells);
        Weeks = new ReadOnlyObservableCollection<WeekRowVm>(_weeks);
        AvailableMonths = new ReadOnlyObservableCollection<DateOnly>(_availableMonths);
        Rebuild();
    }

    /// <summary>由 PortfolioHistoryViewModel 在每次 LoadAsync 後呼叫。</summary>
    public void UpdateSnapshots(IReadOnlyList<PortfolioDailySnapshot> snapshots)
    {
        _allSnapshots = snapshots ?? [];

        if (_uiContext is not null && SynchronizationContext.Current != _uiContext)
        {
            _uiContext.Post(_ => ApplySnapshotsOnUi(), null);
        }
        else
        {
            ApplySnapshotsOnUi();
        }
    }

    private void ApplySnapshotsOnUi()
    {
        // 重建月份下拉選單：從最早 snapshot 月份到目前月份，倒序。
        _availableMonths.Clear();
        if (_allSnapshots.Count > 0)
        {
            var earliest = _allSnapshots.Min(s => s.SnapshotDate);
            var latest = _allSnapshots.Max(s => s.SnapshotDate);
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
            foreach (var m in list) _availableMonths.Add(m);
        }

        // 若預設月份沒有資料，跳到最新有資料的月份。
        if (_allSnapshots.Count > 0)
        {
            var latest = _allSnapshots.Max(s => s.SnapshotDate);
            var target = new DateOnly(latest.Year, latest.Month, 1);
            CurrentMonth = target;
        }
        Rebuild();
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
        var prev = CurrentMonth.AddMonths(-1);
        CurrentMonth = new DateOnly(prev.Year, prev.Month, 1);
    }

    [RelayCommand]
    private void GoNext()
    {
        if (!CanGoNext) return;
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
        if (cell is null || !cell.HasData || !cell.IsCurrentMonth) return;
        SelectedCell = cell;
    }

    [RelayCommand]
    private void CloseCellPopover() => SelectedCell = null;

    /// <summary>從 popover 跳到當日明細：navigate 到 TransactionLog（後續可加日期 filter）。</summary>
    [RelayCommand]
    private void OpenDayTransactions()
    {
        if (SelectedCell is null) return;
        SelectedCell = null;
        Assetra.WPF.Infrastructure.ShellNavigationEvents.RequestNavigateTo("TransactionLog");
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

        // 按 SnapshotDate 建索引方便 O(1) 查詢
        var snapshotByDate = _allSnapshots.ToDictionary(s => s.SnapshotDate);

        // 前一個有資料的 snapshot；用於算當月第一天前的 baseline。
        decimal? prevValue = null;
        var earliestInMonth = snapshotByDate.Keys
            .Where(d => d.Year == firstOfMonth.Year && d.Month == firstOfMonth.Month)
            .OrderBy(d => d)
            .FirstOrDefault();
        if (earliestInMonth != default)
        {
            var prior = _allSnapshots
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
            var hasSnapshot = snapshotByDate.TryGetValue(date, out var snap);
            var isWeekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

            decimal? delta = null;
            decimal? deltaPct = null;
            if (hasSnapshot && prevValue.HasValue && prevValue.Value != 0m)
            {
                delta = snap!.MarketValue - prevValue.Value;
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
                Tone: ResolveTone(delta, deltaPct)));
        }

        MonthlyAbsolutePnl = monthlyDelta;
        if (monthOpenValue.HasValue && monthOpenValue.Value > 0m && lastValue.HasValue)
            MonthlyReturnPct = (lastValue.Value - monthOpenValue.Value) / monthOpenValue.Value;
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

        // 一個 day 一格；正值放 ups[i]，負值放 downs[i]，其餘為 null。
        // 用兩個 ColumnSeries 著色（紅漲綠跌），對齊台灣 broker app 慣例。
        var ups = new List<double?>(daysInMonth);
        var downs = new List<double?>(daysInMonth);
        for (var day = 1; day <= daysInMonth; day++)
        {
            var cell = _cells.FirstOrDefault(c => c.IsCurrentMonth && c.Day == day);
            var v = cell?.Delta is decimal d ? (double)d : 0d;
            if (v > 0)        { ups.Add(v); downs.Add(null); }
            else if (v < 0)   { ups.Add(null); downs.Add(v); }
            else              { ups.Add(null); downs.Add(null); }
        }

        var tooltipFormatter = (LiveChartsCore.Kernel.ChartPoint chartPoint) =>
        {
            var v = chartPoint.Coordinate.PrimaryValue;
            return (v >= 0 ? "+" : "") + v.ToString("N0", CultureInfo.InvariantCulture);
        };

        MonthlyBarSeries =
        [
            new ColumnSeries<double?>
            {
                Values = ups,
                Padding = 1,
                MaxBarWidth = 14,
                AnimationsSpeed = TimeSpan.Zero,
                Fill = new SolidColorPaint(upColor),
                Stroke = null,
                YToolTipLabelFormatter = tooltipFormatter,
            },
            new ColumnSeries<double?>
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

    private static string FormatDelta(decimal? delta)
    {
        if (delta is null) return string.Empty;
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
    /// 顏色強度分桶：依 |deltaPct| 分為 None / Weak / Medium / Strong。
    /// 紅綠由 sign 決定（台灣慣例：紅漲綠跌）。XAML 用 DataTrigger 對應。
    /// </summary>
    private static CellTone ResolveTone(decimal? delta, decimal? pct)
    {
        if (delta is null || !pct.HasValue) return CellTone.None;
        var abs = Math.Abs(pct.Value);
        var sign = Math.Sign(delta.Value);
        if (sign == 0) return CellTone.None;

        var bucket = abs switch
        {
            < 0.005m => 1,   // < 0.5%
            < 0.015m => 2,   // 0.5–1.5%
            < 0.03m  => 3,   // 1.5–3%
            _        => 4,   // ≥ 3%
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

/// <summary>cell 色階。XAML 用 DataTrigger 把 Tone 對應到 brush。</summary>
public enum CellTone
{
    None,
    UpWeak, UpMedium, UpStrong, UpStrongest,
    DownWeak, DownMedium, DownStrong, DownStrongest,
}
