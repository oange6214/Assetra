using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using SkiaSharp;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.Infrastructure.Persistence;
using Serilog;
using Assetra.WPF.Infrastructure;
using Wpf.Ui.Appearance;

namespace Assetra.WPF.Features.Portfolio;

public enum PortfolioTab { Dashboard, Positions, AllocationAnalysis, Accounts, Liability, Trades }

public partial class PortfolioViewModel : ObservableObject, IDisposable
{
    private readonly IPortfolioRepository _repo;
    private readonly IStockSearchService _search;
    private readonly PortfolioSnapshotService _snapshotService;
    private readonly IPortfolioPositionLogRepository _logRepo;
    private readonly PortfolioBackfillService _backfill;
    private readonly ITradeRepository _tradeRepo;
    private readonly IAppSettingsService? _settingsService;
    private readonly ISnackbarService? _snackbar;
    private readonly IThemeService? _themeService;
    private Action<ApplicationTheme>? _onThemeChanged;
    private readonly ICurrencyService? _currencyService;
    private readonly IAssetRepository? _assetRepo;
    private readonly ICryptoService? _cryptoService;
    private readonly IStockHistoryProvider? _historyProvider;
    private readonly ITransactionService _txService;
    private readonly IBalanceQueryService _balanceQuery;
    private readonly IPositionQueryService _positionQuery;
    private readonly CompositeDisposable _disposables = new();

    public ObservableCollection<PortfolioRowViewModel> Positions { get; } = [];
    public ObservableCollection<TradeRowViewModel> Trades { get; } = [];
    public ObservableCollection<CashAccountRowViewModel> CashAccounts { get; } = [];
    public ObservableCollection<LiabilityRowViewModel> Liabilities { get; } = [];

    // Asset allocation (pie chart)
    public ObservableCollection<AssetAllocationSlice> AllocationSlices { get; } = [];
    [ObservableProperty] private ISeries[] _allocationPieSeries = [];
    [ObservableProperty] private bool _isAllocationVisible = true;
    [ObservableProperty] private bool _isMetricCardsExpanded = true;
    [ObservableProperty] private bool _isPositionsSummaryExpanded = true;
    [ObservableProperty] private bool _isCashSummaryExpanded = true;
    [ObservableProperty] private bool _isLiabilitySummaryExpanded = true;
    [ObservableProperty] private bool _isDivCalendarExpanded = true;

    public bool HasAllocationData => AllocationSlices.Count > 0;

    // Totals
    [ObservableProperty] private decimal _totalCost;
    [ObservableProperty] private decimal _totalMarketValue;
    [ObservableProperty] private decimal _totalPnl;
    [ObservableProperty] private decimal _totalPnlPercent;
    [ObservableProperty] private bool _isTotalPositive;
    [ObservableProperty] private bool _hasNoPositions = true;
    [ObservableProperty] private bool _showWelcomeBanner;

    // 現金 / 負債 / 總資產 / 淨資產
    [ObservableProperty] private decimal _totalCash;
    [ObservableProperty] private decimal _totalLiabilities;
    [ObservableProperty] private decimal _totalAssets;
    [ObservableProperty] private decimal _netWorth;
    [ObservableProperty] private bool _hasNoCashAccounts = true;
    [ObservableProperty] private bool _hasNoLiabilities = true;

    // 本日盈虧（基於 PrevClose，報價載入後才有效）
    [ObservableProperty] private decimal _dayPnl;
    [ObservableProperty] private bool _isDayPnlPositive;
    [ObservableProperty] private bool _hasDayPnl;

    private decimal _dayPnlPercent;
    /// <summary>本日盈虧百分比，格式如 (+1.23%) 或 (-0.45%)，直接綁定到 TextBlock.Text。</summary>
    public string DayPnlPercentDisplay =>
        _dayPnlPercent >= 0
            ? $"(+{_dayPnlPercent:F2}%)"
            : $"({_dayPnlPercent:F2}%)";

    // 實現損益統計（所有已平倉交易）
    [ObservableProperty] private decimal _totalRealizedPnl;
    [ObservableProperty] private bool _isTotalRealizedPositive;
    [ObservableProperty] private bool _hasRealizedPnl;

    // 收入 & 股利統計
    [ObservableProperty] private decimal _totalIncome;
    [ObservableProperty] private decimal _totalDividends;
    [ObservableProperty] private bool _hasIncome;
    [ObservableProperty] private bool _hasDividends;

    // Confirm dialog
    [ObservableProperty] private bool _isConfirmDialogOpen;
    [ObservableProperty] private string _confirmDialogMessage = string.Empty;
    private Func<Task>? _confirmDialogAction;

    [RelayCommand]
    private async Task ConfirmDialogYes()
    {
        IsConfirmDialogOpen = false;
        if (_confirmDialogAction is not null)
            await _confirmDialogAction();
        _confirmDialogAction = null;
    }

    [RelayCommand]
    private void ConfirmDialogNo()
    {
        IsConfirmDialogOpen = false;
        _confirmDialogAction = null;
    }

    private void AskConfirm(string message, Func<Task> action)
    {
        ConfirmDialogMessage = message;
        _confirmDialogAction = action;
        IsConfirmDialogOpen = true;
    }

    [ObservableProperty] private string _filterText = string.Empty;
    [ObservableProperty] private bool _hasNoTrades = true;
    [ObservableProperty] private bool _hasAnyDividendTrades;

    // Tab state
    [ObservableProperty] private PortfolioTab _selectedTab = PortfolioTab.Positions;

    public bool IsDashboardTab
    {
        get => SelectedTab == PortfolioTab.Dashboard;
        set { if (value) SelectedTab = PortfolioTab.Dashboard; }
    }

    public bool IsPositionsTab
    {
        get => SelectedTab == PortfolioTab.Positions;
        set { if (value) SelectedTab = PortfolioTab.Positions; }
    }

    public bool IsAllocationAnalysisTab
    {
        get => SelectedTab == PortfolioTab.AllocationAnalysis;
        set { if (value) SelectedTab = PortfolioTab.AllocationAnalysis; }
    }

    public bool IsAccountsTab
    {
        get => SelectedTab == PortfolioTab.Accounts;
        set { if (value) SelectedTab = PortfolioTab.Accounts; }
    }

    public bool IsLiabilityTab
    {
        get => SelectedTab == PortfolioTab.Liability;
        set { if (value) SelectedTab = PortfolioTab.Liability; }
    }

    public bool IsTradesTab
    {
        get => SelectedTab == PortfolioTab.Trades;
        set { if (value) SelectedTab = PortfolioTab.Trades; }
    }

    partial void OnSelectedTabChanged(PortfolioTab value)
    {
        OnPropertyChanged(nameof(IsDashboardTab));
        OnPropertyChanged(nameof(IsPositionsTab));
        OnPropertyChanged(nameof(IsAllocationAnalysisTab));
        OnPropertyChanged(nameof(IsAccountsTab));
        OnPropertyChanged(nameof(IsLiabilityTab));
        OnPropertyChanged(nameof(IsTradesTab));
    }

    // 負債健康度（供 Liability tab 摘要卡片使用）
    /// <summary>負債 / 總資產，0–100 範圍，供 ProgressBar 直接綁定。</summary>
    public decimal DebtRatioValue => TotalAssets > 0
        ? Math.Min(TotalLiabilities / TotalAssets * 100m, 100m)
        : 0m;
    public string DebtRatioDisplay => TotalAssets > 0 ? $"{DebtRatioValue:F1}%" : "—";
    public string LeverageRatioDisplay => NetWorth > 0 ? $"{TotalAssets / NetWorth:F2}" : "—";
    public bool IsDebtHealthy => DebtRatioValue < 30m;
    public bool IsDebtWarning => DebtRatioValue is >= 30m and < 50m;
    public bool IsDebtDanger => DebtRatioValue >= 50m;

    /// <summary>"healthy" | "warning" | "danger" | "none" — single binding for XAML color triggers.</summary>
    public string DebtStatusTag => IsDebtDanger ? "danger" : IsDebtWarning ? "warning" : IsDebtHealthy ? "healthy" : "none";

    /// <summary>所有負債的原始借款總額（OriginalAmount = 0 的條目以 Balance 補位）。</summary>
    public decimal TotalOriginalLiabilities => Liabilities.Sum(l =>
        l.OriginalAmount > 0 ? l.OriginalAmount : l.Balance);

    /// <summary>已繳百分比（0–100），供 ProgressBar 直接綁定。</summary>
    public decimal PaidPercentValue
    {
        get
        {
            var orig = TotalOriginalLiabilities;
            if (orig <= 0)
                return 0m;
            var paid = orig - TotalLiabilities;
            return Math.Clamp(paid / orig * 100m, 0m, 100m);
        }
    }

    public string PaidPercentDisplay => $"{PaidPercentValue:F1}%";

    public string TotalOriginalDisplay =>
        $"NT${TotalOriginalLiabilities:N0}";

    // 緊急預備金（供 Cash tab 摘要卡片使用）
    /// <summary>每月預估開銷（從 AppSettings 讀取，可由 UI 修改）。</summary>
    [ObservableProperty] private decimal _monthlyExpense;

    partial void OnMonthlyExpenseChanged(decimal value)
    {
        OnPropertyChanged(nameof(EmergencyFundMonths));
        OnPropertyChanged(nameof(EmergencyFundMonthsDisplay));
        OnPropertyChanged(nameof(EmergencyFundBarValue));
        OnPropertyChanged(nameof(IsEmergencySafe));
        OnPropertyChanged(nameof(IsEmergencyWarning));
        OnPropertyChanged(nameof(IsEmergencyDanger));
        OnPropertyChanged(nameof(EmergencyStatusTag));
        OnPropertyChanged(nameof(IsMonthlyExpenseSet));
        _ = SaveMonthlyExpenseAsync();
    }

    public bool IsMonthlyExpenseSet => MonthlyExpense > 0;

    /// <summary>可撐幾個月（無上限）。</summary>
    public decimal EmergencyFundMonths =>
        MonthlyExpense > 0 ? TotalCash / MonthlyExpense : 0m;

    public string EmergencyFundMonthsDisplay =>
        MonthlyExpense > 0 ? $"{EmergencyFundMonths:F1}" : "—";

    /// <summary>0–100，超過 12 個月視為滿格。</summary>
    public decimal EmergencyFundBarValue =>
        MonthlyExpense > 0 ? Math.Min(EmergencyFundMonths / 12m * 100m, 100m) : 0m;

    public bool IsEmergencySafe => EmergencyFundMonths >= 6m;
    public bool IsEmergencyWarning => EmergencyFundMonths is >= 3m and < 6m;
    public bool IsEmergencyDanger => MonthlyExpense > 0 && EmergencyFundMonths < 3m;

    /// <summary>"safe" | "warning" | "danger" | "none" — single binding for XAML color triggers.</summary>
    public string EmergencyStatusTag => IsEmergencyDanger ? "danger" : IsEmergencyWarning ? "warning" : IsEmergencySafe ? "safe" : "none";

    private async Task SaveMonthlyExpenseAsync()
    {
        if (_settingsService is null)
            return;
        try
        {
            var updated = _settingsService.Current with { MonthlyExpense = MonthlyExpense };
            await _settingsService.SaveAsync(updated);
        }
        catch (Exception ex)
        {
            // 儲存偏好設定失敗不影響主要功能
            System.Diagnostics.Debug.WriteLine($"[Portfolio] SaveMonthlyExpense failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task DismissWelcomeBannerAsync()
    {
        ShowWelcomeBanner = false;
        if (_settingsService is null) return;
        try
        {
            await _settingsService.SaveAsync(_settingsService.Current with { HasShownWelcomeBanner = true });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Portfolio] DismissWelcomeBanner failed: {ex.Message}");
        }
    }

    // Trade filters
    // Trade type filter — 改為多選 collection（取代舊的單一 string "All"/"Buy"/…）
    // 每個項目有 IsChecked 狀態；全部未勾 = 不篩選（等同舊的 "All"）。
    public ObservableCollection<TradeTypeFilterItem> TradeTypeFilters { get; } = [];

    [ObservableProperty] private string _tradeTypeFiltersSearch = string.Empty;

    partial void OnTradeTypeFiltersSearchChanged(string _) => TradeTypeFiltersView?.Refresh();

    /// <summary>供 popup ListBox 綁定，跟隨 search text 過濾。</summary>
    public ICollectionView? TradeTypeFiltersView { get; private set; }

    /// <summary>摘要顯示：0 勾 → 所有類型；1 勾 → 該標籤；多勾 → "N 個類型"。</summary>
    public string TradeTypeFiltersDisplay
    {
        get
        {
            var checkedItems = TradeTypeFilters.Where(f => f.IsChecked).ToList();
            if (checkedItems.Count == 0)
                return System.Windows.Application.Current?.TryFindResource("Portfolio.Filter.AllTypes") as string ?? "所有類型";
            if (checkedItems.Count == 1)
                return checkedItems[0].Label;
            var unit = System.Windows.Application.Current?.TryFindResource("Portfolio.Filter.TypesCountUnit") as string ?? "個類型";
            return $"{checkedItems.Count} {unit}";
        }
    }
    [ObservableProperty] private string _tradeSearchText = string.Empty;
    [ObservableProperty] private DateTime? _tradeDateFrom;
    [ObservableProperty] private DateTime? _tradeDateTo;

    // Trade asset filter — 多選 popup（跟 type filter 對稱）；items 依「投資/現金/負債」分組
    public ObservableCollection<TradeAssetFilterItem> TradeAssetFilters { get; } = [];

    [ObservableProperty] private string _tradeAssetFiltersSearch = string.Empty;

    partial void OnTradeAssetFiltersSearchChanged(string _) => TradeAssetFiltersView?.Refresh();

    public ICollectionView? TradeAssetFiltersView { get; private set; }

    public string TradeAssetFiltersDisplay
    {
        get
        {
            var checkedItems = TradeAssetFilters.Where(f => f.IsChecked).ToList();
            if (checkedItems.Count == 0)
                return System.Windows.Application.Current?.TryFindResource("Portfolio.Filter.AllAssets") as string ?? "所有資產";
            if (checkedItems.Count == 1)
                return checkedItems[0].Symbol;
            var unit = System.Windows.Application.Current?.TryFindResource("Portfolio.Filter.AssetsCountUnit") as string ?? "項資產";
            return $"{checkedItems.Count} {unit}";
        }
    }

    // Trade pagination
    [ObservableProperty] private int _tradePageSize = 25;
    [ObservableProperty] private int _tradeCurrentPage = 1;
    [ObservableProperty] private int _tradeTotalPages = 1;
    [ObservableProperty] private int _tradeTotalCount;
    [ObservableProperty] private string _tradePageDisplay = string.Empty;

    public static IReadOnlyList<int> PageSizeOptions { get; } = [25, 50, 100];

    private ICollectionView? _positionsView;
    public ICollectionView PositionsView
    {
        get
        {
            if (_positionsView is null)
            {
                _positionsView = CollectionViewSource.GetDefaultView(Positions);
                _positionsView.Filter = FilterPosition;
            }
            return _positionsView;
        }
    }

    partial void OnFilterTextChanged(string value) => PositionsView.Refresh();

    private bool FilterPosition(object obj)
    {
        if (obj is not PortfolioRowViewModel row)
            return false;

        if (string.IsNullOrWhiteSpace(FilterText))
            return true;

        return row.Symbol.Contains(FilterText, StringComparison.OrdinalIgnoreCase)
            || row.Name.Contains(FilterText, StringComparison.OrdinalIgnoreCase);
    }

    // Cash / Liability filters
    [ObservableProperty] private string _cashFilterText = string.Empty;
    [ObservableProperty] private string _liabilityFilterText = string.Empty;

    private ICollectionView? _cashAccountsView;
    public ICollectionView CashAccountsView
    {
        get
        {
            if (_cashAccountsView is null)
            {
                _cashAccountsView = CollectionViewSource.GetDefaultView(CashAccounts);
                _cashAccountsView.Filter = FilterCashAccount;
            }
            return _cashAccountsView;
        }
    }

    partial void OnCashFilterTextChanged(string value) => CashAccountsView.Refresh();

    private bool FilterCashAccount(object obj)
        => obj is CashAccountRowViewModel row
           && (string.IsNullOrEmpty(CashFilterText)
               || row.Name.Contains(CashFilterText, StringComparison.OrdinalIgnoreCase));

    private ICollectionView? _liabilitiesView;
    public ICollectionView LiabilitiesView
    {
        get
        {
            if (_liabilitiesView is null)
            {
                _liabilitiesView = CollectionViewSource.GetDefaultView(Liabilities);
                _liabilitiesView.Filter = FilterLiability;
            }
            return _liabilitiesView;
        }
    }

    partial void OnLiabilityFilterTextChanged(string value) => LiabilitiesView.Refresh();

    private bool FilterLiability(object obj)
        => obj is LiabilityRowViewModel row
           && (string.IsNullOrEmpty(LiabilityFilterText)
               || row.Name.Contains(LiabilityFilterText, StringComparison.OrdinalIgnoreCase));

    private ICollectionView? _tradesView;
    public ICollectionView TradesView
    {
        get
        {
            if (_tradesView is null)
            {
                _tradesView = CollectionViewSource.GetDefaultView(Trades);
                _tradesView.Filter = FilterTrade;
            }
            return _tradesView;
        }
    }

    /// <summary>ICollectionView filter — criteria + pagination.</summary>
    private bool FilterTrade(object obj)
    {
        if (obj is not TradeRowViewModel t)
            return false;
        return _visibleTradeIds.Contains(t.Id);
    }

    /// <summary>Pure criteria match (no pagination).</summary>
    private bool MatchesTradeFilter(TradeRowViewModel t)
    {
        // Type filter — 多選模式：勾了任意項時，只顯示被勾選的那幾類
        var checkedKeys = TradeTypeFilters.Where(f => f.IsChecked)
                                           .Select(f => f.Key)
                                           .ToHashSet();
        if (checkedKeys.Count > 0 && !TradeMatchesAnyTypeKey(t, checkedKeys))
            return false;

        // Text search (symbol, name, note)
        if (!string.IsNullOrWhiteSpace(TradeSearchText))
        {
            var q = TradeSearchText;
            if (!t.Symbol.Contains(q, StringComparison.OrdinalIgnoreCase) &&
                !t.Name.Contains(q, StringComparison.OrdinalIgnoreCase) &&
                !(t.Note?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false))
                return false;
        }

        // Date range
        if (TradeDateFrom.HasValue && t.TradeDate < TradeDateFrom.Value.Date.ToUniversalTime())
            return false;
        if (TradeDateTo.HasValue && t.TradeDate > TradeDateTo.Value.Date.AddDays(1).ToUniversalTime())
            return false;

        // Asset filter — 多選模式：勾了任意項時，只顯示被勾 Symbol
        var checkedSymbols = TradeAssetFilters.Where(f => f.IsChecked)
                                              .Select(f => f.Symbol)
                                              .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (checkedSymbols.Count > 0 && !checkedSymbols.Contains(t.Symbol))
            return false;

        return true;
    }

    /// <summary>
    /// Stores the indices of trades visible on the current page,
    /// so FilterTrade can implement Skip/Take pagination.
    /// </summary>
    private HashSet<Guid> _visibleTradeIds = [];

    private void RefreshTradesView()
    {
        // 1. Compute filtered list (ignoring pagination)
        var filtered = Trades.Where(t => MatchesTradeFilter(t)).ToList();
        TradeTotalCount = filtered.Count;
        TradeTotalPages = Math.Max(1, (int)Math.Ceiling((double)TradeTotalCount / TradePageSize));
        if (TradeCurrentPage > TradeTotalPages)
            TradeCurrentPage = TradeTotalPages;
        var unit = System.Windows.Application.Current?.TryFindResource("Portfolio.TradeCount.Unit") as string ?? "筆";
        TradePageDisplay = $"{TradeTotalCount} {unit}";

        // 2. Compute page slice
        var pageItems = filtered
            .Skip((TradeCurrentPage - 1) * TradePageSize)
            .Take(TradePageSize)
            .Select(t => t.Id)
            .ToHashSet();
        _visibleTradeIds = pageItems;

        // 3. Refresh the view (FilterTrade will now check _visibleTradeIds)
        TradesView.Refresh();

        // 4. 更新頁碼按鈕清單（當前頁、總頁數變動時都要重建）
        RebuildPageNumbers();
    }

    partial void OnTradePageSizeChanged(int _) { TradeCurrentPage = 1; RefreshTradesView(); }

    /// <summary>
    /// 根據勾選的 type keys 判斷該交易是否符合。keys 可能是 "Buy"/"Sell"/"Income"/
    /// "CashDividend"/"StockDividend"/"Deposit"/"Withdrawal"/"Transfer"/"LoanBorrow"/"LoanRepay"。
    /// </summary>
    private static bool TradeMatchesAnyTypeKey(TradeRowViewModel t, HashSet<string> keys)
    {
        if (t.IsBuy && keys.Contains("Buy")) return true;
        if (t.IsSell && keys.Contains("Sell")) return true;
        if (t.IsIncome && keys.Contains("Income")) return true;
        if (t.IsCashDividend && keys.Contains("CashDividend")) return true;
        if (t.Type == TradeType.StockDividend && keys.Contains("StockDividend")) return true;
        if (t.IsDeposit && keys.Contains("Deposit")) return true;
        if (t.IsWithdrawal && keys.Contains("Withdrawal")) return true;
        if (t.IsTransfer && keys.Contains("Transfer")) return true;
        if (t.IsLoanBorrow && keys.Contains("LoanBorrow")) return true;
        if (t.IsLoanRepay && keys.Contains("LoanRepay")) return true;
        return false;
    }

    /// <summary>當任一 type filter item 的 IsChecked 變動時觸發。</summary>
    private void OnTradeTypeFilterItemChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TradeTypeFilterItem.IsChecked)) return;
        TradeCurrentPage = 1;
        RefreshTradesView();
        OnPropertyChanged(nameof(HasActiveTradeFilter));
        OnPropertyChanged(nameof(TradeTypeFiltersDisplay));
    }

    /// <summary>初始化 Trade type filter 清單與 collection view（供 search 過濾）。</summary>
    private void InitTradeTypeFilters()
    {
        if (TradeTypeFilters.Count > 0) return;  // 只做一次
        string Label(string key) =>
            System.Windows.Application.Current?.TryFindResource($"Portfolio.Filter.{key}") as string ?? key;
        string[] keys = ["Buy", "Sell", "Income", "CashDividend", "StockDividend",
                         "Deposit", "Withdrawal", "Transfer", "LoanBorrow", "LoanRepay"];
        foreach (var k in keys)
        {
            var item = new TradeTypeFilterItem(k, Label(k));
            item.PropertyChanged += OnTradeTypeFilterItemChanged;
            TradeTypeFilters.Add(item);
        }
        TradeTypeFiltersView = CollectionViewSource.GetDefaultView(TradeTypeFilters);
        TradeTypeFiltersView.Filter = o =>
        {
            if (string.IsNullOrWhiteSpace(TradeTypeFiltersSearch)) return true;
            return o is TradeTypeFilterItem i &&
                   i.Label.Contains(TradeTypeFiltersSearch, StringComparison.OrdinalIgnoreCase);
        };
        OnPropertyChanged(nameof(TradeTypeFiltersView));
    }

    /// <summary>清除所有 type filter 勾選（popup 裡的 Clear filters 按鈕）。</summary>
    [RelayCommand]
    private void ClearTradeTypeFilters()
    {
        foreach (var item in TradeTypeFilters)
            item.IsChecked = false;
        TradeTypeFiltersSearch = string.Empty;
    }

    /// <summary>
    /// 從 Trades 重建資產篩選清單，依交易類型分到投資/現金/負債三個群組。
    /// 會保留原本勾選狀態（若該 symbol 仍存在）。
    /// </summary>
    private void RebuildTradeAssetFilters()
    {
        // 記下當前勾選，重建後還原
        var previouslyChecked = TradeAssetFilters.Where(f => f.IsChecked)
                                                  .Select(f => f.Symbol)
                                                  .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 解除舊訂閱
        foreach (var old in TradeAssetFilters)
            old.PropertyChanged -= OnTradeAssetFilterItemChanged;
        TradeAssetFilters.Clear();

        string LabelOf(string key) =>
            System.Windows.Application.Current?.TryFindResource($"Portfolio.Filter.Category.{key}") as string ?? key;
        string InvestmentLabel = LabelOf("Investment");
        string CashLabel       = LabelOf("Cash");
        string LiabilityLabel  = LabelOf("Liability");

        // 根據第一筆出現該 symbol 的交易類型來歸類
        var symbolCategory = new Dictionary<string, (string Key, int Order, string Label)>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in Trades)
        {
            var sym = t.Symbol;
            if (string.IsNullOrWhiteSpace(sym)) continue;
            if (symbolCategory.ContainsKey(sym)) continue;

            (string, int, string) cat = t.Type switch
            {
                TradeType.Buy or TradeType.Sell or TradeType.CashDividend or TradeType.StockDividend
                    => ("Investment", 0, InvestmentLabel),
                TradeType.LoanBorrow or TradeType.LoanRepay
                    => ("Liability",  2, LiabilityLabel),
                _ => ("Cash", 1, CashLabel),
            };
            symbolCategory[sym] = cat;
        }

        // 排序：先按 category order, 再按 symbol 字母
        foreach (var (sym, (catKey, catOrder, catLabel)) in symbolCategory
            .OrderBy(kv => kv.Value.Order)
            .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            var item = new TradeAssetFilterItem(sym, catKey, catOrder, catLabel)
            {
                IsChecked = previouslyChecked.Contains(sym),
            };
            item.PropertyChanged += OnTradeAssetFilterItemChanged;
            TradeAssetFilters.Add(item);
        }

        // Setup view (once)：依 CategoryLabel 分組 + search 過濾
        if (TradeAssetFiltersView is null)
        {
            TradeAssetFiltersView = CollectionViewSource.GetDefaultView(TradeAssetFilters);
            TradeAssetFiltersView.Filter = o =>
            {
                if (string.IsNullOrWhiteSpace(TradeAssetFiltersSearch)) return true;
                return o is TradeAssetFilterItem i &&
                       i.Symbol.Contains(TradeAssetFiltersSearch, StringComparison.OrdinalIgnoreCase);
            };
            TradeAssetFiltersView.GroupDescriptions.Add(
                new PropertyGroupDescription(nameof(TradeAssetFilterItem.CategoryLabel)));
            // Items 已按 CategoryOrder/Symbol 排入，CollectionView 不另外 sort 以免覆蓋群組順序
            OnPropertyChanged(nameof(TradeAssetFiltersView));
        }
        else
        {
            TradeAssetFiltersView.Refresh();
        }
        OnPropertyChanged(nameof(TradeAssetFiltersDisplay));
    }
    partial void OnTradeSearchTextChanged(string _) { TradeCurrentPage = 1; RefreshTradesView(); OnPropertyChanged(nameof(HasActiveTradeFilter)); }
    partial void OnTradeDateFromChanged(DateTime? _) { TradeCurrentPage = 1; RefreshTradesView(); OnPropertyChanged(nameof(HasActiveTradeFilter)); }
    partial void OnTradeDateToChanged(DateTime? _) { TradeCurrentPage = 1; RefreshTradesView(); OnPropertyChanged(nameof(HasActiveTradeFilter)); }

    /// <summary>任一 asset filter item 的 IsChecked 變動時觸發。</summary>
    private void OnTradeAssetFilterItemChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TradeAssetFilterItem.IsChecked)) return;
        TradeCurrentPage = 1;
        RefreshTradesView();
        OnPropertyChanged(nameof(HasActiveTradeFilter));
        OnPropertyChanged(nameof(TradeAssetFiltersDisplay));
    }

    /// <summary>清除所有資產勾選（popup 裡的 Clear filters 按鈕）。</summary>
    [RelayCommand]
    private void ClearTradeAssetFilters()
    {
        foreach (var item in TradeAssetFilters)
            item.IsChecked = false;
        TradeAssetFiltersSearch = string.Empty;
    }

    [RelayCommand]
    private void ClearTradeFilters()
    {
        TradeSearchText = string.Empty;
        TradeDateFrom = null;
        TradeDateTo = null;
        foreach (var item in TradeTypeFilters)
            item.IsChecked = false;
        foreach (var item in TradeAssetFilters)
            item.IsChecked = false;
        TradeCurrentPage = 1;
        RefreshTradesView();
    }

    /// <summary>Called from DividendCalendarPanel when a month cell is clicked.</summary>
    [RelayCommand]
    private void FilterByDividendMonth(int month)
    {
        // 勾選現金股息 + 股票股利；清掉 asset 勾選
        foreach (var item in TradeTypeFilters)
            item.IsChecked = item.Key is "CashDividend" or "StockDividend";
        foreach (var item in TradeAssetFilters)
            item.IsChecked = false;
        TradeDateFrom = new DateTime(DivCalendarYear, month, 1);
        TradeDateTo = new DateTime(DivCalendarYear, month, DateTime.DaysInMonth(DivCalendarYear, month));
        TradeSearchText = string.Empty;
    }

    public bool HasActiveTradeFilter =>
        !string.IsNullOrEmpty(TradeSearchText) ||
        TradeDateFrom.HasValue || TradeDateTo.HasValue ||
        TradeTypeFilters.Any(f => f.IsChecked) ||
        TradeAssetFilters.Any(f => f.IsChecked);

    [RelayCommand]
    private void TradePagePrev()
    {
        if (TradeCurrentPage > 1)
        { TradeCurrentPage--; RefreshTradesView(); }
    }

    [RelayCommand]
    private void TradePageNext()
    {
        if (TradeCurrentPage < TradeTotalPages)
        { TradeCurrentPage++; RefreshTradesView(); }
    }

    /// <summary>跳到指定頁碼（供頁碼按鈕呼叫）。</summary>
    [RelayCommand]
    private void GoToPage(int page)
    {
        if (page < 1 || page > TradeTotalPages || page == TradeCurrentPage)
            return;
        TradeCurrentPage = page;
        RefreshTradesView();
    }

    /// <summary>
    /// 「頁碼」跳頁輸入框內容。始終同步當前頁碼（<see cref="TradeCurrentPage"/>）
    /// 作為可視提示，使用者編輯後按 Enter 跳頁，跳成功則會透過 OnTradeCurrentPageChanged
    /// 自動更新回新頁碼。
    /// </summary>
    [ObservableProperty] private string _tradeJumpPageInput = "1";

    partial void OnTradeCurrentPageChanged(int value) => TradeJumpPageInput = value.ToString();

    /// <summary>跳頁輸入按 Enter 觸發 — 解析並跳頁；若解析失敗則回復為當前頁。</summary>
    [RelayCommand]
    private void JumpToPage()
    {
        if (int.TryParse(TradeJumpPageInput, out var p))
            GoToPage(p);
        // 不論成功或失敗，都同步回當前頁（避免無效輸入停留在 input）
        TradeJumpPageInput = TradeCurrentPage.ToString();
    }

    /// <summary>
    /// 頁碼按鈕列表（含省略號）。總頁數 ≤7 時全部顯示；否則顯示首頁 + 當前附近 ±2
    /// + 末頁，中間以 ellipsis 帶過。
    /// </summary>
    public IReadOnlyList<TradePageItem> TradePageNumbers { get; private set; } = [];

    private void RebuildPageNumbers()
    {
        var total = TradeTotalPages;
        var current = TradeCurrentPage;
        var items = new List<TradePageItem>();

        if (total <= 7)
        {
            for (var i = 1; i <= total; i++)
                items.Add(new TradePageItem(i, i == current, false));
        }
        else
        {
            // 視當前位置決定 window
            int windowStart, windowEnd;
            if (current <= 4)           { windowStart = 2; windowEnd = 5; }
            else if (current >= total - 3) { windowStart = total - 4; windowEnd = total - 1; }
            else                         { windowStart = current - 1; windowEnd = current + 1; }

            items.Add(new TradePageItem(1, current == 1, false));
            if (windowStart > 2)
                items.Add(new TradePageItem(0, false, true));
            for (var i = windowStart; i <= windowEnd; i++)
                items.Add(new TradePageItem(i, i == current, false));
            if (windowEnd < total - 1)
                items.Add(new TradePageItem(0, false, true));
            items.Add(new TradePageItem(total, current == total, false));
        }

        TradePageNumbers = items;
        OnPropertyChanged(nameof(TradePageNumbers));
    }

    [RelayCommand]
    private void RemoveTrade(TradeRowViewModel row)
    {
        var msg = Application.Current?.TryFindResource("Portfolio.Confirm.DeleteTrade") as string ?? "確定刪除此交易紀錄？";
        AskConfirm(msg, async () =>
        {
            try
            {
                // Under the single-truth model the balance is a pure projection over
                // remaining trades — no reversal bookkeeping needed. Just drop the row
                // and re-project. Remove fee sub-records first, then the parent.
                await _tradeRepo.RemoveChildrenAsync(row.Id);
                await _tradeRepo.RemoveAsync(row.Id);
                Trades.Remove(row);
                HasNoTrades = Trades.Count == 0;
                HasAnyDividendTrades = Trades.Any(t => t.IsCashDividend);
                RebuildRealizedPnl();
                RefreshTradesView();
                await ReloadAccountBalancesAsync();
                RebuildTotals();
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "RemoveTrade failed");
                _snackbar?.Error(Application.Current?.TryFindResource("Portfolio.Trade.DeleteFailed") as string
                    ?? "刪除交易記錄失敗，請稍後再試");
            }
        });
    }


    /// <summary>Child ViewModel for portfolio value history chart.</summary>
    public PortfolioHistoryViewModel History { get; }

    // Dividend calendar
    [ObservableProperty] private int _divCalendarYear = DateTime.Today.Year;

    [RelayCommand]
    private void DivCalendarPrevYear() => DivCalendarYear--;

    [RelayCommand]
    private void DivCalendarNextYear() => DivCalendarYear++;

    public IReadOnlyDictionary<int, decimal> GetDividendsByMonth(int year)
    {
        return Trades
            .Where(t => t.IsCashDividend && t.TradeDate.Year == year)
            .GroupBy(t => t.TradeDate.Month)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.CashAmount ?? 0));
    }

    public PortfolioViewModel(
        PortfolioRepositories repositories,
        PortfolioServices services,
        PortfolioUiServices ui)
    {
        ArgumentNullException.ThrowIfNull(repositories);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(ui);

        _repo = repositories.Portfolio;
        _search = services.Search;
        _snackbar = ui.Snackbar;
        _snapshotService = services.Snapshot;
        _logRepo = repositories.PositionLog;
        _backfill = services.Backfill;
        _tradeRepo = repositories.Trade ?? new NullTradeRepository();
        _settingsService = ui.Settings;
        _currencyService = services.Currency;
        _assetRepo = repositories.Asset;
        _cryptoService = services.Crypto;
        _historyProvider = services.History;
        _txService = services.Transaction ?? new NullTransactionService();
        _balanceQuery = services.BalanceQuery ?? new NullBalanceQueryService();
        _positionQuery = services.PositionQuery ?? new NullPositionQueryService();
        History = new PortfolioHistoryViewModel(repositories.Snapshot);

        // Rebuild chart colours whenever the user switches theme
        if (ui.Theme is not null)
        {
            _onThemeChanged = _ => History.OnThemeChanged();
            ui.Theme.ThemeChanged += _onThemeChanged;
            _themeService = ui.Theme;
        }

        // Refresh all displayed amounts when currency changes
        if (services.Currency is not null)
            services.Currency.CurrencyChanged += OnCurrencyChanged;

        services.Stock.QuoteStream
            .ObserveOn(ui.Scheduler)
            .Subscribe(UpdatePrices)
            .DisposeWith(_disposables);
    }

    /// <summary>
    /// 把每個持倉列的 <c>CommissionDiscount</c> 設為該 Symbol 最新一筆 Buy 的折扣，
    /// 作為未來賣出時的估算假設。交易沒存折扣（legacy）或沒有任何 Buy 時維持 1m。
    /// 由 <see cref="LoadTradesAsync"/> 與 <c>AddPosition</c> 完成後呼叫。
    /// </summary>
    private void ApplyLatestTradeDiscounts()
    {
        // 以 Symbol 分組，取最新 Buy 的折扣
        var latestDiscount = Trades
            .Where(t => t.Type == TradeType.Buy && t.CommissionDiscount is > 0)
            .GroupBy(t => t.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(t => t.TradeDate).First().CommissionDiscount!.Value,
                StringComparer.OrdinalIgnoreCase);

        foreach (var row in Positions)
        {
            if (!latestDiscount.TryGetValue(row.Symbol, out var disc))
                continue;
            if (row.CommissionDiscount == disc)
                continue;
            row.CommissionDiscount = disc;
            row.Refresh();
        }
    }

    private async Task LoadPositionsAsync()
    {
        var entries = await _repo.GetEntriesAsync();
        var snapshots = await _positionQuery.GetAllPositionSnapshotsAsync();

        Positions.Clear();
        foreach (var g in entries.GroupBy(e => (e.Symbol, e.AssetType)))
        {
            var lots = g.ToList();
            var primary = lots[0];

            // Archive filter: hide when all lots are archived and ShowArchivedPositions is off.
            // Aggregating by symbol means one "archived" lot shouldn't hide an active symbol.
            if (!ShowArchivedPositions && lots.All(l => !l.IsActive)) continue;

            if (lots.Count == 1)
            {
                snapshots.TryGetValue(primary.Id, out var snap);

                // Hide-empty filter: projection says qty == 0 (position closed out).
                if (HideEmptyPositions && (snap?.Quantity ?? 0m) == 0m) continue;

                var row = ToRow(primary, snap);
                row.IsActive = primary.IsActive;
                Positions.Add(row);
                continue;
            }

            // Multiple lots: aggregate into one display row (weighted average cost from projection)
            var totalQty  = lots.Sum(e => snapshots.TryGetValue(e.Id, out var s) ? s.Quantity : 0m);
            var totalCost = lots.Sum(e => snapshots.TryGetValue(e.Id, out var s) ? s.TotalCost : 0m);
            var firstBuyDate = lots
                .Select(e => snapshots.TryGetValue(e.Id, out var s) ? s.FirstBuyDate : null)
                .Where(d => d.HasValue)
                .Select(d => d!.Value)
                .OrderBy(d => d)
                .FirstOrDefault();
            snapshots.TryGetValue(primary.Id, out var primarySnap);
            var aggregatedSnap = new PositionSnapshot(
                primary.Id,
                totalQty,
                totalCost,
                totalQty > 0 ? totalCost / totalQty : 0m,
                lots.Sum(e => snapshots.TryGetValue(e.Id, out var s) ? s.RealizedPnl : 0m),
                firstBuyDate == default ? null : firstBuyDate);

            if (HideEmptyPositions && totalQty == 0m) continue;

            var aggRow = ToRow(primary, aggregatedSnap);
            // Row is active if ANY lot is active (matches the archive-filter semantics above).
            aggRow.IsActive = lots.Any(l => l.IsActive);
            foreach (var extra in lots.Skip(1))
                aggRow.AllEntryIds.Add(extra.Id);
            Positions.Add(aggRow);
        }
        HasNoPositions = Positions.Count == 0;

        // Refresh Lazy-Upsert suggestion list for TX form editable ComboBox (Task 19).
        // Only active entries — one row per (Symbol, Exchange).
        PositionSuggestions.Clear();
        foreach (var e in entries.Where(x => x.IsActive)
                                  .GroupBy(x => (x.Symbol, x.Exchange))
                                  .Select(g => g.First())
                                  .OrderBy(x => x.Symbol))
        {
            PositionSuggestions.Add(new PositionSuggestion(e.Id, e.Symbol, e.Exchange, e.DisplayName));
        }
    }

    /// <summary>
    /// When true, archived (soft-deleted) positions are shown in the Positions list.
    /// Plan Task 15 + Task 20 (XAML toggle to be wired later).
    /// </summary>
    [ObservableProperty] private bool _showArchivedPositions;

    /// <summary>
    /// When true, positions with zero projected quantity (fully sold-out or 'watchlist')
    /// are hidden from the Positions list.
    /// </summary>
    [ObservableProperty] private bool _hideEmptyPositions;

    partial void OnShowArchivedPositionsChanged(bool value) => _ = LoadPositionsAsync();
    partial void OnHideEmptyPositionsChanged(bool value) => _ = LoadPositionsAsync();

    public async Task LoadAsync()
    {
        // Restore persisted monthly expense (set via property to avoid triggering save-back on load)
        SetProperty(ref _monthlyExpense, _settingsService?.Current?.MonthlyExpense ?? 0m, nameof(MonthlyExpense));
        ShowWelcomeBanner = !(_settingsService?.Current?.HasShownWelcomeBanner ?? false);

        await LoadPositionsAsync();

        await LoadCashAccountsAsync();
        await LoadLiabilitiesAsync();

        RebuildTotals();

        // Fetch live prices for crypto positions
        await RefreshCryptoPricesAsync();

        await History.LoadAsync();

        // Load trade history
        await LoadTradesAsync();

        // Under the single-truth model, balance seeding is obsolete: CashAccount /
        // LiabilityAccount no longer store Balance/OriginalAmount, so there is nothing
        // to reconcile against. Legacy users simply see 0 until they record Deposit /
        // LoanBorrow transactions themselves.

        // Backfill gaps in snapshot history — fire and forget
        _ = BackfillAndRefreshAsync();
    }

    /// <summary>Test-only hook — lets tests re-populate the Trades collection after
    /// adding rows directly to the fake repo (bypassing the normal UI flow).</summary>
    internal Task LoadTradesAsyncForTest() => LoadTradesAsync();

    private async Task LoadTradesAsync()
    {
        // 首次呼叫時建立 type filter 項目（需要 Application resources 已就緒）
        InitTradeTypeFilters();
        try
        {
            var trades = await _tradeRepo.GetAllAsync();
            Trades.Clear();
            foreach (var t in trades)
                Trades.Add(new TradeRowViewModel(t));
            HasNoTrades = Trades.Count == 0;
            HasAnyDividendTrades = Trades.Any(t => t.IsCashDividend);

            // Rebuild asset filter items — 依交易類型將 symbol 歸到
            // 投資（Buy/Sell/股利）/ 現金（Deposit/Withdrawal/Income/Interest）/ 負債（Loan*）
            RebuildTradeAssetFilters();

            RebuildRealizedPnl();   // also calls _tradesView?.Refresh()
            RefreshTradesView();

            // 以最新一筆 Buy 的折扣套到對應持倉，讓預估賣出費用反映當前券商折扣
            ApplyLatestTradeDiscounts();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[Portfolio] LoadTradesAsync failed");
        }
    }

    private void RebuildRealizedPnl()
    {
        var all = Trades.ToList();
        var sellTrades = all.Where(t => t.IsSell && t.RealizedPnl.HasValue).ToList();
        HasRealizedPnl = sellTrades.Count > 0;
        TotalRealizedPnl = sellTrades.Sum(t => t.RealizedPnl!.Value);
        IsTotalRealizedPositive = TotalRealizedPnl >= 0;

        TotalIncome = all.Where(t => t.IsIncome).Sum(t => t.CashAmount ?? 0);
        TotalDividends = all.Where(t => t.IsCashDividend).Sum(t => t.CashAmount ?? 0);
        HasIncome = TotalIncome > 0;
        HasDividends = TotalDividends > 0;

        _tradesView?.Refresh();
    }


    // Receives every QuoteStream tick and updates matching portfolio rows
    private void UpdatePrices(IReadOnlyList<StockQuote> quotes)
    {
        bool changed = false;
        foreach (var quote in quotes)
        {
            var row = Positions.FirstOrDefault(p => p.Symbol == quote.Symbol);
            if (row is null)
                continue;

            if (string.IsNullOrEmpty(row.Name) && !string.IsNullOrEmpty(quote.Name))
                row.Name = quote.Name;

            row.CurrentPrice = quote.Price;
            row.PrevClose = quote.PrevClose;
            row.IsLoadingPrice = false;
            row.Refresh();
            changed = true;
        }
        if (changed)
            RebuildTotals();
    }


    private void RebuildTotals()
    {
        TotalCost = Positions.Sum(p => p.Cost);
        TotalMarketValue = Positions.Sum(p => p.MarketValue);
        // 總損益已含每筆持倉估算的賣出費用 (符合 Plan A: 顯示淨損益)
        TotalPnl = Positions.Sum(p => p.Pnl);
        TotalPnlPercent = TotalCost > 0 ? TotalPnl / TotalCost * 100m : 0m;
        IsTotalPositive = TotalPnl >= 0;

        // 計算每筆佔投資組合淨值的百分比，供 DataGrid 「佔比」欄使用。
        // 以淨值（NetValue）為基準而非市值，與 DataGrid 顯示的主欄位一致。
        var totalNetValue = Positions.Sum(p => p.NetValue);
        foreach (var p in Positions)
            p.PercentOfPortfolio = totalNetValue > 0 ? p.NetValue / totalNetValue * 100m : 0m;

        TotalCash = CashAccounts.Sum(c => c.Balance);
        TotalLiabilities = Liabilities.Sum(l => l.Balance);
        TotalAssets = TotalMarketValue + TotalCash;
        NetWorth = TotalAssets - TotalLiabilities;

        // 本日盈虧：只計算已載入報價（PrevClose > 0）的持倉，避免尚未報價的倉位拉偏數字
        var priced = Positions.Where(p => !p.IsLoadingPrice && p.PrevClose > 0).ToList();
        HasDayPnl = priced.Count > 0;
        DayPnl = priced.Sum(p => (p.CurrentPrice - p.PrevClose) * p.Quantity);
        var dayPnlBase = priced.Sum(p => p.PrevClose * p.Quantity);
        _dayPnlPercent = dayPnlBase > 0 ? DayPnl / dayPnlBase * 100m : 0m;
        OnPropertyChanged(nameof(DayPnlPercentDisplay));
        IsDayPnlPositive = DayPnl >= 0;

        // Notify debt health derived properties
        OnPropertyChanged(nameof(DebtRatioValue));
        OnPropertyChanged(nameof(DebtRatioDisplay));
        OnPropertyChanged(nameof(LeverageRatioDisplay));
        OnPropertyChanged(nameof(IsDebtHealthy));
        OnPropertyChanged(nameof(IsDebtWarning));
        OnPropertyChanged(nameof(IsDebtDanger));
        OnPropertyChanged(nameof(DebtStatusTag));
        OnPropertyChanged(nameof(TotalOriginalLiabilities));
        OnPropertyChanged(nameof(PaidPercentValue));
        OnPropertyChanged(nameof(PaidPercentDisplay));
        OnPropertyChanged(nameof(TotalOriginalDisplay));

        // Emergency fund (depends on TotalCash)
        OnPropertyChanged(nameof(EmergencyFundMonths));
        OnPropertyChanged(nameof(EmergencyFundMonthsDisplay));
        OnPropertyChanged(nameof(EmergencyFundBarValue));
        OnPropertyChanged(nameof(IsEmergencySafe));
        OnPropertyChanged(nameof(IsEmergencyWarning));
        OnPropertyChanged(nameof(IsEmergencyDanger));
        OnPropertyChanged(nameof(EmergencyStatusTag));

        RebuildAllocationSlices();

        // Fire-and-forget: record today's snapshot once prices are live
        _ = RecordSnapshotAsync();
    }

    private static readonly IReadOnlyDictionary<AssetType, (string LabelKey, string Color)> AssetTypeColors =
        new Dictionary<AssetType, (string, string)>
        {
            [AssetType.Stock] = ("Portfolio.AssetType.Stock", "#3B82F6"),
            [AssetType.Fund] = ("Portfolio.AssetType.Fund", "#10B981"),
            [AssetType.PreciousMetal] = ("Portfolio.AssetType.Metal", "#F59E0B"),
            [AssetType.Bond] = ("Portfolio.AssetType.Bond", "#6B7280"),
            [AssetType.Crypto] = ("Portfolio.AssetType.Crypto", "#8B5CF6"),
        };

    private void RebuildAllocationSlices()
    {
        AllocationSlices.Clear();

        // Aggregate positions by AssetType (use market value if available, else cost)
        var groups = Positions
            .GroupBy(p => p.AssetType)
            .Select(g => (Type: g.Key, Value: g.Sum(p => p.MarketValue > 0 ? p.MarketValue : p.Cost)))
            .Where(g => g.Value > 0)
            .ToList();

        var cash = TotalCash;
        var total = groups.Sum(g => g.Value) + Math.Max(0, cash) + Math.Max(0, TotalLiabilities);
        if (total <= 0)
            return;

        foreach (var (type, value) in groups)
        {
            if (!AssetTypeColors.TryGetValue(type, out var meta))
                continue;
            var label = System.Windows.Application.Current?.TryFindResource(meta.LabelKey) as string ?? type.ToString();
            AllocationSlices.Add(new AssetAllocationSlice(label, value, value / total * 100m, meta.Color));
        }

        if (cash > 0)
        {
            var cashLabel = System.Windows.Application.Current?.TryFindResource("Portfolio.Header.Cash") as string ?? "Cash";
            AllocationSlices.Add(new AssetAllocationSlice(cashLabel, cash, cash / total * 100m, "#94A3B8"));
        }

        if (TotalLiabilities > 0)
        {
            var liabLabel = System.Windows.Application.Current?.TryFindResource("Portfolio.Header.Liabilities") as string ?? "Liabilities";
            AllocationSlices.Add(new AssetAllocationSlice(liabLabel, TotalLiabilities, TotalLiabilities / total * 100m, "#EF4444"));
        }

        // Build PieSeries for LiveChartsCore v2 (must use double, not decimal)
        AllocationPieSeries = AllocationSlices
            .Select(s =>
            {
                var paint = new LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint(SKColor.Parse(s.ColorHex));
                return (ISeries)new PieSeries<double>
                {
                    Values = new[] { (double)s.Value },
                    // Name drives the LiveChartsCore tooltip (built-in legend is hidden).
                    // Embed value + percent so the tooltip popup shows useful info on hover.
                    Name = $"{s.Label}  NT${s.Value:N0}  ({s.Percent:F1}%)",
                    InnerRadius = 40,
                    Fill = paint,
                    Stroke = null,
                    DataLabelsSize = 0,
                    HoverPushout = 6,
                    AnimationsSpeed = TimeSpan.Zero,
                };
            })
            .ToArray();

        OnPropertyChanged(nameof(HasAllocationData));
    }

    private async Task RecordSnapshotAsync()
    {
        try
        {
            await _snapshotService.TryRecordAsync(
                TotalCost, TotalMarketValue, TotalPnl, Positions.Count);
            // Refresh chart if a new snapshot was just written
            await History.LoadAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[Portfolio] RecordSnapshotAsync failed");
        }
    }

    /// <summary>
    /// Runs backfill in the background; reloads chart when at least one gap is filled.
    /// </summary>
    private async Task BackfillAndRefreshAsync()
    {
        try
        {
            var written = await _backfill.BackfillAsync();
            if (written > 0)
                await History.LoadAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[Portfolio] BackfillAndRefreshAsync failed");
        }
    }

    // Position log helpers — seeding helpers removed in Wave 9.3: the trade log is now
    // the single source of truth. SeedPositionLogAsync, SeedBuyTradesAsync, and
    // MigrateTradePortfolioLinksAsync have been deleted.

    /// <summary>
    /// Re-projects every cash and liability account balance from the trade history.
    /// Call after any operation that adds / removes / edits a trade so the detail panel
    /// and totals reflect the new state without reloading the page.
    /// Safe to call when repos are null (tests) — just no-ops.
    /// </summary>
    private async Task ReloadAccountBalancesAsync()
    {
        if (_assetRepo is not null)
        {
            var cashMap = await _balanceQuery.GetAllCashBalancesAsync();
            foreach (var row in CashAccounts)
                row.Balance = cashMap.TryGetValue(row.Id, out var bal) ? bal : 0m;
        }

        // Liabilities: rebuild from projections (handles new labels appearing after a LoanBorrow)
        await LoadLiabilitiesAsync();
    }

    private async Task WriteLogAsync(
        Guid entryId, string symbol, string exchange, int quantity, decimal avgPrice)
    {
        try
        {
            await _logRepo.LogAsync(new PortfolioPositionLog(
                Guid.NewGuid(),
                DateOnly.FromDateTime(DateTime.Today),
                entryId,
                symbol,
                exchange,
                quantity,
                avgPrice));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[Portfolio] WriteLogAsync failed for entry {EntryId}", entryId);
        }
    }

    private async Task WriteBuyTradeAsync(
        PortfolioEntry entry, string name, DateOnly tradeDate,
        decimal tradePrice, int quantity,
        Guid? cashAccountId = null,
        decimal? commission = null, decimal? commissionDiscount = null)
    {
        try
        {
            var trade = new Trade(
                Id: Guid.NewGuid(),
                Symbol: entry.Symbol,
                Exchange: entry.Exchange,
                Name: name,
                Type: TradeType.Buy,
                TradeDate: tradeDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                Price: tradePrice,
                Quantity: quantity,
                RealizedPnl: null,
                RealizedPnlPct: null,
                CashAccountId: cashAccountId,
                PortfolioEntryId: entry.Id,
                Commission: commission,
                CommissionDiscount: commissionDiscount);
            await _tradeRepo.AddAsync(trade);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[Portfolio] WriteBuyTradeAsync failed for {Symbol}", entry?.Symbol);
        }
    }

    private PortfolioRowViewModel ToRow(PortfolioEntry e, PositionSnapshot? snap)
    {
        var isStock = e.AssetType == AssetType.Stock;
        var qty = snap?.Quantity ?? 0m;
        var avgCost = snap?.AverageCost ?? 0m;
        var buyDate = snap?.FirstBuyDate ?? DateOnly.FromDateTime(DateTime.Today);
        var row = new PortfolioRowViewModel
        {
            Id = e.Id,
            Symbol = e.Symbol,
            Exchange = e.Exchange,
            BuyDate = buyDate,
            AssetType = e.AssetType,
            // Sell-side fee estimation (net P&L) — only meaningful for Taiwan stocks/ETFs.
            // CommissionDiscount 建立時先給 1m；LoadTradesAsync 完成後由 ApplyLatestTradeDiscounts
            // 依據該 Symbol 最新一筆 Buy 的 Trade.CommissionDiscount 補回。
            IsEtf = isStock && _search.IsEtf(e.Symbol),
            IsBondEtf = isStock && _search.IsBondEtf(e.Symbol),
            HasProjection = snap is not null,
            Quantity = qty,
            BuyPrice = avgCost,
            Cost = avgCost * qty,
            Name = isStock
                ? (!string.IsNullOrEmpty(e.DisplayName) ? e.DisplayName : (_search.GetName(e.Symbol) ?? string.Empty))
                : e.Symbol,
            Currency = e.Currency,
            IsLoadingPrice = isStock,
            // Non-stock: start with purchase cost as current value (no live feed)
            CurrentPrice = isStock ? 0 : avgCost,
        };
        row.Refresh();   // compute initial Cost, MarketValue, Pnl for all asset types
        row.AllEntryIds.Add(e.Id);
        return row;
    }

    /// <summary>
    /// Fetches live TWD prices for all Crypto positions and updates <see cref="CurrentPrice"/>.
    /// No-ops gracefully if no crypto service is registered or no crypto positions exist.
    /// </summary>
    private async Task RefreshCryptoPricesAsync()
    {
        if (_cryptoService is null)
            return;
        var cryptoRows = Positions.Where(p => p.AssetType == AssetType.Crypto).ToList();
        if (cryptoRows.Count == 0)
            return;

        var symbols = cryptoRows.Select(r => r.Symbol).Distinct().ToList();
        var prices = await _cryptoService.GetPricesTwdAsync(symbols);
        if (prices.Count == 0)
            return;

        foreach (var row in cryptoRows)
        {
            if (!prices.TryGetValue(row.Symbol, out var price))
                continue;
            row.CurrentPrice = price;
            row.IsLoadingPrice = false;
            row.Refresh();
        }
        RebuildTotals();
    }

    private void OnCurrencyChanged()
    {
        foreach (var row in Positions)
            row.NotifyCurrencyChanged();
        foreach (var trade in Trades)
            trade.NotifyCurrencyChanged();
        foreach (var cash in CashAccounts)
            cash.NotifyCurrencyChanged();
        foreach (var liab in Liabilities)
            liab.NotifyCurrencyChanged();

        OnPropertyChanged(nameof(TotalCost));
        OnPropertyChanged(nameof(TotalMarketValue));
        OnPropertyChanged(nameof(TotalPnl));
        OnPropertyChanged(nameof(DayPnl));
        OnPropertyChanged(nameof(TotalRealizedPnl));
        OnPropertyChanged(nameof(TotalCash));
        OnPropertyChanged(nameof(TotalLiabilities));
        OnPropertyChanged(nameof(TotalAssets));
        OnPropertyChanged(nameof(NetWorth));
        OnPropertyChanged(nameof(SellGrossAmount));
        OnPropertyChanged(nameof(SellCommission));
        OnPropertyChanged(nameof(SellTransactionTax));
        OnPropertyChanged(nameof(SellNetAmount));
        OnPropertyChanged(nameof(SellEstimatedPnl));
    }

    public void Dispose()
    {
        if (_currencyService is not null)
            _currencyService.CurrencyChanged -= OnCurrencyChanged;
        if (_themeService is not null && _onThemeChanged is not null)
            _themeService.ThemeChanged -= _onThemeChanged;
        _closePriceCts?.Cancel();
        _closePriceCts?.Dispose();
        _disposables.Dispose();
    }

    // Null-object fallback when DI doesn't wire trade repo (tests / legacy)

    private sealed class NullTradeRepository : ITradeRepository
    {
        public Task<IReadOnlyList<Trade>> GetAllAsync() =>
            Task.FromResult<IReadOnlyList<Trade>>([]);
        public Task<IReadOnlyList<Trade>> GetByCashAccountAsync(Guid cashAccountId) =>
            Task.FromResult<IReadOnlyList<Trade>>([]);
        public Task<IReadOnlyList<Trade>> GetByLoanLabelAsync(string loanLabel) =>
            Task.FromResult<IReadOnlyList<Trade>>([]);
        public Task AddAsync(Trade trade) => Task.CompletedTask;
        public Task UpdateAsync(Trade trade) => Task.CompletedTask;
        public Task RemoveAsync(Guid id) => Task.CompletedTask;
        public Task RemoveChildrenAsync(Guid parentId) => Task.CompletedTask;
    }

    // No-op fallback used when DI doesn't wire ITransactionService (tests / legacy).
    // Keeps the ViewModel compilable without the Infrastructure layer being present.
    private sealed class NullTransactionService : ITransactionService
    {
        public Task RecordAsync(Trade trade) => Task.CompletedTask;
        public Task DeleteAsync(Trade trade) => Task.CompletedTask;
        public Task ReplaceAsync(Trade original, Trade replacement) => Task.CompletedTask;
    }

    // Fallback when DI doesn't wire IBalanceQueryService (tests that only populate
    // stored account rows without a trade history). All balances return 0 / empty.
    private sealed class NullBalanceQueryService : IBalanceQueryService
    {
        public Task<decimal> GetCashBalanceAsync(Guid cashAccountId) =>
            Task.FromResult(0m);
        public Task<LiabilitySnapshot> GetLiabilitySnapshotAsync(string loanLabel) =>
            Task.FromResult(LiabilitySnapshot.Empty);
        public Task<IReadOnlyDictionary<Guid, decimal>> GetAllCashBalancesAsync() =>
            Task.FromResult<IReadOnlyDictionary<Guid, decimal>>(
                new Dictionary<Guid, decimal>());
        public Task<IReadOnlyDictionary<string, LiabilitySnapshot>> GetAllLiabilitySnapshotsAsync() =>
            Task.FromResult<IReadOnlyDictionary<string, LiabilitySnapshot>>(
                new Dictionary<string, LiabilitySnapshot>());
    }

    // Fallback when DI doesn't wire IPositionQueryService (tests that don't seed trades).
    // All snapshots return 0 / empty.
    private sealed class NullPositionQueryService : IPositionQueryService
    {
        public Task<PositionSnapshot?> GetPositionAsync(Guid portfolioEntryId) =>
            Task.FromResult<PositionSnapshot?>(null);
        public Task<IReadOnlyDictionary<Guid, PositionSnapshot>> GetAllPositionSnapshotsAsync() =>
            Task.FromResult<IReadOnlyDictionary<Guid, PositionSnapshot>>(
                new Dictionary<Guid, PositionSnapshot>());
        public Task<decimal> ComputeRealizedPnlAsync(
            Guid portfolioEntryId, DateTime sellDate, decimal sellPrice,
            decimal sellQty, decimal sellFees) =>
            Task.FromResult(0m);
    }
}

/// <summary>
/// 交易記錄分頁控制列的單一頁碼項目。
///   Number     = 頁碼（若 IsEllipsis=true 則忽略）
///   IsCurrent  = 是否為當前頁（高亮樣式）
///   IsEllipsis = 是否為省略號「…」佔位
/// </summary>
public sealed record TradePageItem(int Number, bool IsCurrent, bool IsEllipsis);

/// <summary>
/// 交易類型篩選的單一勾選項目。Popup 裡的每個 checkbox 對應一個此物件。
///   Key       = 邏輯鍵（"Buy", "Sell", "CashDividend"…），給 filter 比對用
///   Label     = 在地化顯示名稱
///   IsChecked = 是否勾選；變動時 PortfolioViewModel 會重新篩選 Trades
/// </summary>
public partial class TradeTypeFilterItem : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public string Key { get; }
    public string Label { get; }

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private bool _isChecked;

    public TradeTypeFilterItem(string key, string label)
    {
        Key = key;
        Label = label;
    }
}

/// <summary>
/// 交易資產篩選的單一勾選項目。Popup 裡每個 checkbox 對應一個此物件。
///   Symbol        = 代號或帳戶名稱（"00981A" / "台新活存" / "房貸"）
///   CategoryKey   = "Investment"/"Cash"/"Liability"（供分組與比對）
///   CategoryOrder = 0/1/2（決定群組顯示順序：投資 → 現金 → 負債）
///   CategoryLabel = 在地化群組名稱（給 XAML 分組 header 顯示）
///   IsChecked     = 是否勾選
/// </summary>
public partial class TradeAssetFilterItem : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public string Symbol { get; }
    public string CategoryKey { get; }
    public int CategoryOrder { get; }
    public string CategoryLabel { get; }

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private bool _isChecked;

    public TradeAssetFilterItem(string symbol, string categoryKey, int categoryOrder, string categoryLabel)
    {
        Symbol = symbol;
        CategoryKey = categoryKey;
        CategoryOrder = categoryOrder;
        CategoryLabel = categoryLabel;
    }
}
