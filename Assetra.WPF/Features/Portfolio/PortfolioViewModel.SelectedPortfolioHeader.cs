using Assetra.Core.Models;
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
    // ── Selected-portfolio display values ────────────────────────────────────────────

    [ObservableProperty] private string _selectedPortfolioName = string.Empty;
    [ObservableProperty] private decimal _selectedPortfolioMarketValue;
    [ObservableProperty] private decimal _selectedPortfolioCost;
    [ObservableProperty] private decimal _selectedPortfolioPnl;
    [ObservableProperty] private bool _isSelectedPortfolioPnlPositive;
    [ObservableProperty] private decimal _selectedPortfolioDayPnl;
    [ObservableProperty] private bool _isSelectedPortfolioDayPnlPositive;

    // ── Trend: portfolio tab — fixed-window sparkline-based trend ────────────────────
    // Null when there is no data (fewer than 2 points across all holdings in the group).

    [ObservableProperty] private ISeries[]? _selectedPortfolioTrendSeries;
    [ObservableProperty] private ICartesianAxis[]? _selectedPortfolioTrendXAxes;
    [ObservableProperty] private ICartesianAxis[]? _selectedPortfolioTrendYAxes;

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
        }

        OnPropertyChanged(nameof(IsSelectedPortfolioTrendVisible));
        OnPropertyChanged(nameof(IsSelectedPortfolioHistoryVisible));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────

    private static decimal DisplayAmount(decimal nativeAmount, decimal baseAmount) =>
        baseAmount != 0m ? baseAmount : nativeAmount;
}
