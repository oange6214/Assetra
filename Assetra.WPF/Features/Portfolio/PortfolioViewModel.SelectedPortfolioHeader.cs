using Assetra.Core.Models;
using Assetra.WPF.Features.Portfolio.SubViewModels;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.Kernel.Sketches;

namespace Assetra.WPF.Features.Portfolio;

/// <summary>
/// PortfolioViewModel partial — per-portfolio detail header aggregates and trend
/// for the Google-Finance-style redesign (Task 1.4).
///
/// Design decisions:
///   - 全部 tab (SelectedGroupId == null): reuses existing whole-portfolio totals
///     (TotalMarketValue / TotalCost / TotalPnl / DayPnl) already computed by
///     RebuildTotals(), and exposes History (PortfolioHistoryViewModel) for the
///     snapshot-based chart with period selector.
///   - Portfolio tab (SelectedGroupId != null): aggregates the filtered positions
///     using the same DisplayAmount logic as PortfolioGroupDetailViewModel, and
///     builds a fixed-window trend from SparklinePoints (period selector hidden,
///     membership note shown).
///
/// Recompute triggers: SelectedGroupId handler (Task 1.3 TODO) and RebuildTotals.
/// </summary>
public partial class PortfolioViewModel
{
    // ── 投資組合焦點 (Task 1.5) — stock vs ETF composition ──────────────────────────
    public PortfolioCompositionViewModel Composition { get; } = new();

    // ── Selected-portfolio display values ────────────────────────────────────────────

    [ObservableProperty] private string _selectedPortfolioName = string.Empty;
    [ObservableProperty] private decimal _selectedPortfolioMarketValue;
    [ObservableProperty] private decimal _selectedPortfolioCost;
    [ObservableProperty] private decimal _selectedPortfolioPnl;
    [ObservableProperty] private bool _isSelectedPortfolioPnlPositive;
    [ObservableProperty] private decimal _selectedPortfolioDayPnl;
    [ObservableProperty] private bool _isSelectedPortfolioDayPnlPositive;

    /// <summary>
    /// 上方概覽（走勢圖＋投資組合焦點卡＋insights chip）展開狀態。預設展開；
    /// 收合後只留頭條（市值／損益・成本／今日漲跌），讓下方持股表格成為主角。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHeaderSparklineVisible))]
    private bool _isOverviewExpanded = true;

    // ── Trend: portfolio tab — fixed-window sparkline-based trend ────────────────────
    // Null when there is no data (fewer than 2 points across all holdings in the group).

    [ObservableProperty] private ISeries[]? _selectedPortfolioTrendSeries;
    [ObservableProperty] private ICartesianAxis[]? _selectedPortfolioTrendXAxes;
    [ObservableProperty] private ICartesianAxis[]? _selectedPortfolioTrendYAxes;

    // ── Header mini sparkline — always-available short-window trend for the top card ──
    // 用獨立 series 實例（不與 SelectedPortfolioTrendSeries / History 共用），避免 LiveCharts
    // 同一 series 被兩個 chart 綁定的狀態衝突。只在「概覽」收合時顯示（展開時下方已有大圖）。
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHeaderSparklineVisible))]
    private ISeries[]? _headerSparklineSeries;
    [ObservableProperty] private ICartesianAxis[]? _headerSparklineXAxes;
    [ObservableProperty] private ICartesianAxis[]? _headerSparklineYAxes;

    /// <summary>頂部卡片 mini 走勢線：有資料（≥2 點）且「概覽」收合時才顯示。</summary>
    public bool IsHeaderSparklineVisible =>
        _headerSparklineSeries is { Length: > 0 } && !IsOverviewExpanded;

    /// <summary>
    /// True only when a specific portfolio tab is selected AND the trend has ≥ 2 points.
    /// XAML uses this to switch between the fixed-window trend (portfolio tab)
    /// and the History chart (全部 tab).
    /// </summary>
    public bool IsSelectedPortfolioTrendVisible =>
        PortfolioTabs.SelectedGroupId.HasValue &&
        _selectedPortfolioTrendSeries is { Length: > 0 };

    /// <summary>
    /// True when the 全部 tab is selected, i.e. the History chart + period selector
    /// should be shown instead of the sparkline trend.
    /// </summary>
    public bool IsSelectedPortfolioHistoryVisible => !PortfolioTabs.SelectedGroupId.HasValue;

    // ── Command: ＋投資 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Opens the transaction dialog pre-scoped to the selected portfolio tab's group.
    /// On the 全部 tab (SelectedGroupId == null) opens with default behaviour.
    /// <para>
    /// Group preselection is cleanly supported: OpenTxDialog(Guid?) already exists
    /// on TransactionDialogViewModel and wires through EnsureGroupsLoadedAsync with
    /// the preferredGroupId. OpenTxDialogForPosition uses the same path.
    /// </para>
    /// </summary>
    [RelayCommand]
    private void AddInvestmentForSelectedPortfolio()
    {
        Transaction.OpenTxDialog(PortfolioTabs.SelectedGroupId);
    }

    /// <summary>
    /// 切換上方概覽區的展開／收合。收合時一併關閉任何已展開的 KPI 下鑽面板，
    /// 避免收合後仍殘留一塊孤立的圖表面板。
    /// </summary>
    [RelayCommand]
    private void ToggleOverview()
    {
        IsOverviewExpanded = !IsOverviewExpanded;
        if (!IsOverviewExpanded)
            ExpandedKpiPanel = null;
        _ = PersistUiPreferenceAsync(s => s with { PortfolioOverviewExpanded = IsOverviewExpanded });
    }

    /// <summary>
    /// 統一持久化單一 UI 偏好（概覽展開、顯示已平倉…）。raiseChanged: false —— 純 UI 記帳，
    /// 不該觸發 IAppSettingsService.Changed 的全 App reload（見 settings-changed 回饋迴圈）。
    /// </summary>
    private async Task PersistUiPreferenceAsync(Func<AppSettings, AppSettings> mutate)
    {
        if (_settingsService is null)
            return;
        try
        {
            await _settingsService.SaveAsync(mutate(_settingsService.Current), raiseChanged: false);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "[Portfolio] 持久化 UI 偏好失敗");
        }
    }

    // ── Recompute ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Recomputes all SelectedPortfolio* properties from the current tab selection
    /// and the current Positions collection. Safe to call from any code path that
    /// changes positions or the selected tab.
    /// </summary>
    internal void RecomputeSelectedPortfolioHeader()
    {
        var groupId = PortfolioTabs.SelectedGroupId;

        if (groupId is null)
        {
            // 全部 tab: reuse whole-portfolio totals already computed by RebuildTotals.
            SelectedPortfolioName = L("Common.All", "全部");
            SelectedPortfolioMarketValue = TotalMarketValue;
            SelectedPortfolioCost = TotalCost;
            SelectedPortfolioPnl = TotalPnl;
            IsSelectedPortfolioPnlPositive = TotalPnl >= 0m;
            SelectedPortfolioDayPnl = DayPnl;
            IsSelectedPortfolioDayPnlPositive = IsDayPnlPositive;

            // 全部 tab: clear the fixed-window trend (History chart is used instead).
            SelectedPortfolioTrendSeries = null;
            SelectedPortfolioTrendXAxes = null;
            SelectedPortfolioTrendYAxes = null;

            // 全部 tab: header mini sparkline = whole-portfolio short-window trend.
            SetHeaderSparkline(PortfolioGroupDetailViewModel.BuildMarketValueTrendPublic(Positions.ToList()));

            // 全部 tab: composition over every position.
            Composition.Apply(Positions
                .Select(row => (row.AssetType == AssetType.Etf || row.IsEtf, row.MarketValueBase))
                .ToList());
        }
        else
        {
            // Portfolio tab: aggregate the filtered rows.
            var filtered = Positions
                .Where(row => !row.HasPortfolioGroupConflict &&
                              (row.PortfolioGroupId ?? PortfolioGroup.DefaultId) == groupId.Value)
                .ToList();

            var marketValue = filtered.Sum(row => DisplayAmount(row.MarketValue, row.MarketValueBase));
            var cost = filtered.Sum(row => DisplayAmount(row.Cost, row.CostBase));
            var pnl = filtered.Sum(row => DisplayAmount(row.Pnl, row.PnlBase));
            var dayPnl = filtered.Sum(row => DisplayAmount(row.DayChange, 0m));

            SelectedPortfolioMarketValue = marketValue;
            SelectedPortfolioCost = cost;
            SelectedPortfolioPnl = pnl;
            IsSelectedPortfolioPnlPositive = pnl >= 0m;
            SelectedPortfolioDayPnl = dayPnl;
            IsSelectedPortfolioDayPnlPositive = dayPnl >= 0m;

            // Name: look up the group catalog first, fall back to tab label.
            SelectedPortfolioName =
                GroupCatalog?.FindById(groupId.Value)?.Name
                ?? PortfolioTabs.SelectedTab?.Name
                ?? L("Portfolio.Group.Ungrouped", "未指定組合");

            // Fixed-window trend from sparkline points (same math as PortfolioGroupDetailViewModel).
            var trendValues = PortfolioGroupDetailViewModel.BuildMarketValueTrendPublic(filtered);
            var isPositive = trendValues.Count < 2 || trendValues[^1] >= trendValues[0];
            var (series, xAxes, yAxes) =
                PortfolioGroupDetailViewModel.BuildMarketValueTrendChartPublic(trendValues, isPositive);

            SelectedPortfolioTrendSeries = series.Length > 0 ? series : null;
            SelectedPortfolioTrendXAxes = xAxes.Length > 0 ? xAxes : null;
            SelectedPortfolioTrendYAxes = yAxes.Length > 0 ? yAxes : null;

            // Header mini sparkline = same trend values, separate series instance.
            SetHeaderSparkline(trendValues);

            // Portfolio tab: composition over the same filtered rows.
            // Effective ETF flag: row.AssetType == AssetType.Etf || row.IsEtf
            // (TW ETFs are stored as AssetType.Stock + IsEtf=true by the buy flow).
            Composition.Apply(filtered
                .Select(row => (row.AssetType == AssetType.Etf || row.IsEtf, row.MarketValueBase))
                .ToList());
        }

        OnPropertyChanged(nameof(IsSelectedPortfolioTrendVisible));
        OnPropertyChanged(nameof(IsSelectedPortfolioHistoryVisible));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────

    private static decimal DisplayAmount(decimal nativeAmount, decimal baseAmount) =>
        baseAmount != 0m ? baseAmount : nativeAmount;

    /// <summary>建頂部 mini 走勢線的獨立 series（&lt;2 點時清空）。漲跌色沿用既有 builder。</summary>
    private void SetHeaderSparkline(IReadOnlyList<double> values)
    {
        if (values.Count < 2)
        {
            HeaderSparklineSeries = null;
            HeaderSparklineXAxes = null;
            HeaderSparklineYAxes = null;
            return;
        }

        var isPositive = values[^1] >= values[0];
        var (series, xAxes, yAxes) =
            PortfolioGroupDetailViewModel.BuildMarketValueTrendChartPublic(values, isPositive);
        HeaderSparklineSeries = series.Length > 0 ? series : null;
        HeaderSparklineXAxes = xAxes.Length > 0 ? xAxes : null;
        HeaderSparklineYAxes = yAxes.Length > 0 ? yAxes : null;
    }
}
