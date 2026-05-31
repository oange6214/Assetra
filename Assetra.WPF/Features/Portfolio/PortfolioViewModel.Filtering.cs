using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using Assetra.Core.Models;
using Assetra.WPF.Infrastructure;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Assetra.WPF.Features.Portfolio;

public enum InvestmentPositionViewMode
{
    All,
    Group,
    Market,
    Type,
}

/// <summary>
/// PortfolioViewModel partial — collection-view filters for Positions, Cash, and Liability tabs.
/// </summary>
public partial class PortfolioViewModel
{
    [ObservableProperty] private string _filterText = string.Empty;

    public ObservableCollection<PortfolioGroupFilterChipViewModel> PortfolioGroupFilterChips { get; } = [];

    public bool IsGroupViewMode => PositionViewMode == InvestmentPositionViewMode.Group;
    public bool IsAssetTypeFilterRowVisible => PositionViewMode != InvestmentPositionViewMode.Group;
    public bool HasPortfolioGroupFilterChips => PortfolioGroupFilterChips.Count > 0;

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

    /// <summary>
    /// 篩選持倉資產類型；null = 顯示全部。XAML 上方 chip row 透過
    /// <see cref="SetAssetTypeFilterCommand"/> 改值；FilterPosition 套用。
    /// </summary>
    [ObservableProperty] private AssetType? _assetTypeFilter;

    partial void OnAssetTypeFilterChanged(AssetType? value)
    {
        PositionsView.Refresh();
        RefreshPositionViewGroupSummaries();
        RaisePositionsFilterStatsChanged();
    }

    /// <summary>
    /// Portfolio-Groups-Refactor P4 — 持倉所屬群組篩選。null = 顯示全部群組。
    /// XAML chip row 由 PortfolioViewModel.Filtering 設定為使用者選擇的 PortfolioGroup.Id。
    /// </summary>
    [ObservableProperty] private Guid? _portfolioGroupFilter;

    partial void OnPortfolioGroupFilterChanged(Guid? value)
    {
        RefreshPortfolioGroupFilterChipSelection();
        PositionsView.Refresh();
        RefreshPositionViewGroupSummaries();
        RaisePositionsFilterStatsChanged();
    }

    /// <summary>
    /// 設定群組篩選，給 XAML chip 用。傳入 null 表示「全部群組」。
    /// </summary>
    [RelayCommand]
    private void SetPortfolioGroupFilter(Guid? id) => PortfolioGroupFilter = id;

    [ObservableProperty] private InvestmentPositionViewMode _positionViewMode = InvestmentPositionViewMode.All;

    partial void OnPositionViewModeChanged(InvestmentPositionViewMode value)
    {
        OnPropertyChanged(nameof(IsGroupViewMode));
        OnPropertyChanged(nameof(IsAssetTypeFilterRowVisible));
        if (value != InvestmentPositionViewMode.Group && PortfolioGroupFilter.HasValue)
            PortfolioGroupFilter = null;

        ApplyPositionViewGrouping();
        PositionsView.Refresh();
        RefreshPositionViewGroupSummaries();
        RaisePositionsFilterStatsChanged();
    }

    [RelayCommand]
    private void SetPositionViewMode(InvestmentPositionViewMode mode) => PositionViewMode = mode;

    [RelayCommand]
    private void OpenPortfolioGroups() =>
        ShellNavigationEvents.RequestNavigateTo("PortfolioGroups");

    [RelayCommand]
    private void AddPortfolioGroup() =>
        ShellNavigationEvents.RequestNavigateTo("PortfolioGroups");

    private void RefreshPortfolioGroupFilterChips()
    {
        PortfolioGroupFilterChips.Clear();
        PortfolioGroupFilterChips.Add(new PortfolioGroupFilterChipViewModel(
            null,
            L("Portfolio.Filter.Group.All", "全部群組"),
            isSystem: true));

        var groups = GroupCatalog?.Groups ?? Enumerable.Empty<PortfolioGroup>();
        var defaultGroup = groups.FirstOrDefault(group => group.Id == PortfolioGroup.DefaultId);
        PortfolioGroupFilterChips.Add(new PortfolioGroupFilterChipViewModel(
            PortfolioGroup.DefaultId,
            L("Portfolio.Group.Ungrouped", "未分組"),
            isSystem: true));

        foreach (var group in groups.Where(group => !group.IsSystem))
        {
            PortfolioGroupFilterChips.Add(new PortfolioGroupFilterChipViewModel(
                group.Id,
                group.Name,
                isSystem: false));
        }

        if (defaultGroup is null && PortfolioGroupFilter == PortfolioGroup.DefaultId)
            PortfolioGroupFilter = null;

        RefreshPortfolioGroupFilterChipSelection();
        OnPropertyChanged(nameof(HasPortfolioGroupFilterChips));
    }

    private void RefreshPortfolioGroupFilterChipSelection()
    {
        foreach (var chip in PortfolioGroupFilterChips)
            chip.IsSelected = chip.Id == PortfolioGroupFilter;
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
                ApplyPositionViewGrouping();
                RefreshPositionViewGroupSummaries();
            }
            return _positionsView;
        }
    }

    private void ApplyPositionViewGrouping()
    {
        if (_positionsView is null)
            return;

        using (_positionsView.DeferRefresh())
        {
            _positionsView.GroupDescriptions.Clear();
            var propertyName = PositionViewMode switch
            {
                InvestmentPositionViewMode.Group => nameof(PortfolioRowViewModel.PortfolioGroupDisplay),
                InvestmentPositionViewMode.Market => nameof(PortfolioRowViewModel.MarketDisplay),
                InvestmentPositionViewMode.Type => nameof(PortfolioRowViewModel.AssetTypeDisplay),
                _ => null,
            };

            if (propertyName is not null)
                _positionsView.GroupDescriptions.Add(new PropertyGroupDescription(propertyName));
        }
    }

    private void RefreshPositionViewGroupSummaries()
    {
        if (_positionsView is null)
            return;

        var visibleRows = _positionsView.Cast<PortfolioRowViewModel>().ToList();
        var visibleSet = visibleRows.ToHashSet();
        var summaries = visibleRows
            .GroupBy(row => GetPositionViewGroupKey(row))
            .Select(group => new
            {
                Key = group.Key,
                Count = group.Count(),
                MarketValue = group.Sum(row => row.MarketValue),
                Cost = group.Sum(row => row.Cost),
                Pnl = group.Sum(row => row.Pnl),
            })
            .ToDictionary(summary => summary.Key, StringComparer.Ordinal);

        foreach (var row in Positions)
        {
            if (!visibleSet.Contains(row) || !summaries.TryGetValue(GetPositionViewGroupKey(row), out var summary))
            {
                row.PositionViewGroupItemCount = 0;
                row.PositionViewGroupMarketValue = 0m;
                row.PositionViewGroupCost = 0m;
                row.PositionViewGroupPnl = 0m;
                row.IsPositionViewGroupPnlPositive = true;
                continue;
            }

            row.PositionViewGroupItemCount = summary.Count;
            row.PositionViewGroupMarketValue = summary.MarketValue;
            row.PositionViewGroupCost = summary.Cost;
            row.PositionViewGroupPnl = summary.Pnl;
            row.IsPositionViewGroupPnlPositive = summary.Pnl >= 0m;
        }
    }

    private string GetPositionViewGroupKey(PortfolioRowViewModel row) => PositionViewMode switch
    {
        InvestmentPositionViewMode.Group => row.PortfolioGroupDisplay,
        InvestmentPositionViewMode.Market => row.MarketDisplay,
        InvestmentPositionViewMode.Type => row.AssetTypeDisplay,
        _ => string.Empty,
    };

    partial void OnFilterTextChanged(string value)
    {
        PositionsView.Refresh();
        RefreshPositionViewGroupSummaries();
        RaisePositionsFilterStatsChanged();
    }

    /// <summary>
    /// 投資資產篩選後彙總 — Footer 用。跟著 PositionsView 同步刷新（FilterText
    /// / AssetTypeFilter / ShowArchived / HideEmpty 等任一切換時）。
    /// </summary>
    public int PositionsFilteredCount => PositionsView?.Cast<PortfolioRowViewModel>().Count() ?? 0;
    public decimal PositionsFilteredMarketValue => PositionsView?.Cast<PortfolioRowViewModel>().Sum(p => p.MarketValue) ?? 0m;
    public decimal PositionsFilteredCost => PositionsView?.Cast<PortfolioRowViewModel>().Sum(p => p.Cost) ?? 0m;
    public decimal PositionsFilteredPnl => PositionsView?.Cast<PortfolioRowViewModel>().Sum(p => p.Pnl) ?? 0m;
    public bool IsPositionsFiltered =>
        AssetTypeFilter.HasValue
        || PortfolioGroupFilter.HasValue
        || !string.IsNullOrEmpty(FilterText)
        || ShowArchivedPositions
        || HideEmptyPositions;
    public bool IsPositionsFilteredPnlPositive => PositionsFilteredPnl > 0m;

    private void RaisePositionsFilterStatsChanged()
    {
        OnPropertyChanged(nameof(PositionsFilteredCount));
        OnPropertyChanged(nameof(PositionsFilteredMarketValue));
        OnPropertyChanged(nameof(PositionsFilteredCost));
        OnPropertyChanged(nameof(PositionsFilteredPnl));
        OnPropertyChanged(nameof(IsPositionsFiltered));
        OnPropertyChanged(nameof(IsPositionsFilteredPnlPositive));
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

    partial void OnShowArchivedPositionsChanged(bool value) => _ = LoadPositionsAsync();
    partial void OnHideEmptyPositionsChanged(bool value) => _ = LoadPositionsAsync();

    /// <summary>Called from DividendCalendarPanel when a month cell is clicked.</summary>
    [RelayCommand]
    private void FilterByDividendMonth(int month)
    {
        TradeFilter.FilterByDividendMonth(month, DivCalendar.Year);
    }
}
