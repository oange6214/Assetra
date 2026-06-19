using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using Assetra.Core.Models;
using Assetra.WPF.Infrastructure;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Assetra.WPF.Features.Portfolio;

/// <summary>
/// PortfolioViewModel partial — collection-view filters for Positions, Cash, and Liability tabs.
/// </summary>
public partial class PortfolioViewModel
{
    [ObservableProperty] private string _filterText = string.Empty;

    /// <summary>
    /// When true, closed (sold-out) positions are shown in the Positions list. Default false:
    /// the list shows only current holdings. A position is "closed" once fully sold — the sell
    /// flow archives its entry (is_active=0), and/or its projected quantity nets to 0 with a
    /// trade history. Hidden by default; reappears when toggled on (or when re-bought).
    /// A never-traded watchlist entry ("+觀察", qty 0, no snapshot) is NOT closed and stays visible.
    /// Replaces the old separate HideEmpty + ShowArchived toggles with one clear control.
    /// </summary>
    [ObservableProperty] private bool _showClosedPositions;

    /// <summary>
    /// 篩選持倉資產類型；null = 顯示全部。XAML 上方 chip row 透過
    /// <see cref="SetAssetTypeFilterCommand"/> 改值；FilterPosition 套用。
    /// </summary>
    [ObservableProperty] private AssetType? _assetTypeFilter;

    partial void OnAssetTypeFilterChanged(AssetType? value)
    {
        PositionsView.Refresh();
        RaisePositionsFilterStatsChanged();
    }

    /// <summary>
    /// Portfolio-Groups-Refactor P4 — 持倉所屬群組篩選。null = 顯示全部群組。
    /// Tab strip 的 SelectedTab 透過 PortfolioViewModel ctor 的 PropertyChanged 訂閱
    /// 設定此值；也可由 SetPortfolioGroupFilterCommand 直接設定（測試用）。
    /// </summary>
    [ObservableProperty] private Guid? _portfolioGroupFilter;

    partial void OnPortfolioGroupFilterChanged(Guid? value)
    {
        PositionsView.Refresh();
        RaisePositionsFilterStatsChanged();
    }

    /// <summary>
    /// 設定群組篩選，給測試 / 程式碼內部用。傳入 null 表示「全部群組」。
    /// </summary>
    [RelayCommand]
    private void SetPortfolioGroupFilter(Guid? id) => PortfolioGroupFilter = id;

    [RelayCommand]
    private void OpenPortfolioGroups() =>
        ShellNavigationEvents.RequestOpenPortfolioGroups();

    [RelayCommand]
    private void AddPortfolioGroup() =>
        ShellNavigationEvents.RequestOpenPortfolioGroups();

    /// <summary>
    /// Syncs the Google-style tab strip with the current group catalog.
    /// Called whenever the catalog changes (initial load or user edits groups).
    /// </summary>
    /// <remarks>
    /// Threading: PortfolioTabs.Tabs is data-bound to a CollectionView that is
    /// thread-affine. If called from a background thread (e.g. after a catalog
    /// RefreshAsync completes off the UI thread), Tabs.Clear/Add throws
    /// NotSupportedException. Marshal back to the UI Dispatcher exactly as
    /// RebuildPositionPieCharts does.  In headless tests Application.Current is
    /// null, so the guard runs inline — unit tests are unaffected.
    /// </remarks>
    private void SyncPortfolioTabs()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(SyncPortfolioTabs);
            return;
        }
        PortfolioTabs.Sync(
            GroupCatalog?.Groups ?? Enumerable.Empty<PortfolioGroup>(),
            L("Common.All", "全部"),
            L("Portfolio.Group.Ungrouped", "未指定組合"));
    }

    /// <summary>
    /// 接收 "" / "Stock" / "Etf" / "Fund" / "Bond" / "Crypto" / "PreciousMetal" 字串。
    /// 空字串 → null（顯示全部）；其餘嘗試 parse 為 AssetType。
    /// </summary>
    [RelayCommand]
    private void SetAssetTypeFilter(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            AssetTypeFilter = null;
            return;
        }
        if (Enum.TryParse<AssetType>(raw, ignoreCase: true, out var parsed))
            AssetTypeFilter = parsed;
    }

    // Cash / Liability filters
    [ObservableProperty] private string _cashFilterText = string.Empty;
    [ObservableProperty] private string _liabilityFilterText = string.Empty;

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

    partial void OnFilterTextChanged(string value)
    {
        PositionsView.Refresh();
        RaisePositionsFilterStatsChanged();
    }

    /// <summary>
    /// 投資資產篩選後彙總 — Footer 用。跟著 PositionsView 同步刷新（FilterText
    /// / AssetTypeFilter / ShowClosedPositions 等任一切換時）。
    /// </summary>
    public int PositionsFilteredCount => PositionsView?.Cast<PortfolioRowViewModel>().Count() ?? 0;

    // 用 DisplayAmount（base 優先）彙總，與頂部卡片 header 一致：外幣部位必須換算成 base
    // 幣別再加總，否則 USD 等原幣金額會被當成 TWD 直接相加，footer 市值/成本/損益全失真。
    public decimal PositionsFilteredMarketValue => PositionsView?.Cast<PortfolioRowViewModel>().Sum(p => DisplayAmount(p.MarketValue, p.MarketValueBase)) ?? 0m;
    public decimal PositionsFilteredCost => PositionsView?.Cast<PortfolioRowViewModel>().Sum(p => DisplayAmount(p.Cost, p.CostBase)) ?? 0m;
    public decimal PositionsFilteredPnl => PositionsView?.Cast<PortfolioRowViewModel>().Sum(p => DisplayAmount(p.Pnl, p.PnlBase)) ?? 0m;
    public bool IsPositionsFiltered =>
        AssetTypeFilter.HasValue
        || PortfolioGroupFilter.HasValue
        || !string.IsNullOrEmpty(FilterText);
    public bool IsPositionsFilteredPnlPositive => PositionsFilteredPnl > 0m;

    // 獲利／虧損檔數 — footer 用（補回原「盈虧檔數」chip 的核心資訊）。盈虧 sign 與幣別無關，
    // 直接用原幣 Pnl 判斷即可，不必 DisplayAmount。
    public int PositionsFilteredProfitCount => PositionsView?.Cast<PortfolioRowViewModel>().Count(p => p.Pnl > 0m) ?? 0;
    public int PositionsFilteredLossCount => PositionsView?.Cast<PortfolioRowViewModel>().Count(p => p.Pnl < 0m) ?? 0;

    private void RaisePositionsFilterStatsChanged()
    {
        OnPropertyChanged(nameof(PositionsFilteredCount));
        OnPropertyChanged(nameof(PositionsFilteredMarketValue));
        OnPropertyChanged(nameof(PositionsFilteredCost));
        OnPropertyChanged(nameof(PositionsFilteredPnl));
        OnPropertyChanged(nameof(IsPositionsFiltered));
        OnPropertyChanged(nameof(IsPositionsFilteredPnlPositive));
        OnPropertyChanged(nameof(PositionsFilteredProfitCount));
        OnPropertyChanged(nameof(PositionsFilteredLossCount));
    }

    private bool FilterPosition(object obj)
    {
        if (obj is not PortfolioRowViewModel row)
            return false;

        // AssetType chip 篩選 — null 表示全部。
        // 歷史資料：`EnsureStockEntryAsync` 對股票/ETF 一律寫 AssetType.Stock，
        // 而 ETF 是另外用 IsEtf 旗標標記。如果只看 AssetType，ETF chip 永遠是空，
        // Stock chip 又會混入 ETF。所以這裡讓 chip 同時看兩個來源：
        //   Etf chip   → row.AssetType == Etf  或  (AssetType == Stock 且 IsEtf=true)
        //   Stock chip → row.AssetType == Stock 且 IsEtf == false
        //   其他 chip  → 嚴格 AssetType 對齊
        if (AssetTypeFilter.HasValue)
        {
            var f = AssetTypeFilter.Value;
            var matches = f switch
            {
                AssetType.Etf => row.AssetType == AssetType.Etf
                                   || (row.AssetType == AssetType.Stock && row.IsEtf),
                AssetType.Stock => row.AssetType == AssetType.Stock && !row.IsEtf,
                _ => row.AssetType == f,
            };
            if (!matches)
                return false;
        }

        // Portfolio-Groups-Refactor P4 — Group chip 篩選；null row group 視為 DefaultId。
        if (PortfolioGroupFilter is { } gf)
        {
            var rowGroup = row.PortfolioGroupId ?? PortfolioGroup.DefaultId;
            if (rowGroup != gf)
                return false;
        }

        if (string.IsNullOrWhiteSpace(FilterText))
            return true;

        return row.Symbol.Contains(FilterText, StringComparison.OrdinalIgnoreCase)
            || row.Name.Contains(FilterText, StringComparison.OrdinalIgnoreCase);
    }

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

    partial void OnCashFilterTextChanged(string value)
    {
        CashAccountsView.Refresh();
        RaiseCashFilterStatsChanged();
    }

    public int CashAccountsFilteredCount => CashAccountsView?.Cast<CashAccountRowViewModel>().Count() ?? 0;
    public decimal CashAccountsFilteredBalance => CashAccountsView?.Cast<CashAccountRowViewModel>().Sum(c => c.Balance) ?? 0m;
    public bool IsCashAccountsFiltered => !string.IsNullOrEmpty(CashFilterText);

    private void RaiseCashFilterStatsChanged()
    {
        OnPropertyChanged(nameof(CashAccountsFilteredCount));
        OnPropertyChanged(nameof(CashAccountsFilteredBalance));
        OnPropertyChanged(nameof(IsCashAccountsFiltered));
    }

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

    partial void OnLiabilityFilterTextChanged(string value)
    {
        LiabilitiesView.Refresh();
        RaiseLiabilityFilterStatsChanged();
    }

    public int LiabilitiesFilteredCount => LiabilitiesView?.Cast<LiabilityRowViewModel>().Count() ?? 0;
    public decimal LiabilitiesFilteredBalance => LiabilitiesView?.Cast<LiabilityRowViewModel>().Sum(l => l.Balance) ?? 0m;
    public decimal LiabilitiesFilteredMonthlyPayment =>
        LiabilitiesView?.Cast<LiabilityRowViewModel>().Sum(l => l.NextPaymentAmount ?? 0m) ?? 0m;
    public bool IsLiabilitiesFiltered => !string.IsNullOrEmpty(LiabilityFilterText);

    private void RaiseLiabilityFilterStatsChanged()
    {
        OnPropertyChanged(nameof(LiabilitiesFilteredCount));
        OnPropertyChanged(nameof(LiabilitiesFilteredBalance));
        OnPropertyChanged(nameof(LiabilitiesFilteredMonthlyPayment));
        OnPropertyChanged(nameof(IsLiabilitiesFiltered));
    }

    private bool FilterLiability(object obj)
        => obj is LiabilityRowViewModel row
           && (string.IsNullOrEmpty(LiabilityFilterText)
               || row.Name.Contains(LiabilityFilterText, StringComparison.OrdinalIgnoreCase));

    partial void OnShowClosedPositionsChanged(bool value)
    {
        _ = LoadPositionsAsync();
        _ = PersistUiPreferenceAsync(s => s with { PortfolioShowClosed = value });
    }

    /// <summary>Called from DividendCalendarPanel when a month cell is clicked.</summary>
    [RelayCommand]
    private void FilterByDividendMonth(int month)
    {
        TradeFilter.FilterByDividendMonth(month, DivCalendar.Year);
    }
}
