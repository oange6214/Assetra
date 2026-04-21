using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Assetra.WPF.Features.Portfolio;

/// <summary>
/// Encapsulates all trade filter, sort, and pagination state for the Trades tab.
/// <para>
/// <see cref="PortfolioViewModel"/> owns an instance of this class via
/// <see cref="PortfolioViewModel.TradeFilter"/> and delegates all filter-related
/// bindings to it. The class receives a <paramref name="getTrades"/> delegate
/// so it can operate on the live <c>Trades</c> collection without creating a
/// circular reference back to <see cref="PortfolioViewModel"/>.
/// </para>
/// </summary>
public partial class TradeFilterViewModel : ObservableObject
{
    private readonly Func<IEnumerable<TradeRowViewModel>> _getTrades;
    private readonly ILocalizationService _localization;

    /// <summary>
    /// Initialises the view-model.
    /// </summary>
    /// <param name="getTrades">
    /// Delegate that returns the current trade rows from <see cref="PortfolioViewModel.Trades"/>.
    /// Called on every filter refresh — must never return <c>null</c>.
    /// </param>
    /// <param name="localization">
    /// Localization service used to look up UI strings from the active resource dictionary.
    /// </param>
    public TradeFilterViewModel(
        Func<IEnumerable<TradeRowViewModel>> getTrades,
        ILocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(getTrades);
        ArgumentNullException.ThrowIfNull(localization);
        _getTrades = getTrades;
        _localization = localization;
    }

    // ── Type filter ──────────────────────────────────────────────────────────

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
                return _localization.Get("Portfolio.Filter.AllTypes", "所有類型");
            if (checkedItems.Count == 1)
                return checkedItems[0].Label;
            var unit = _localization.Get("Portfolio.Filter.TypesCountUnit", "個類型");
            return $"{checkedItems.Count} {unit}";
        }
    }

    // ── Asset filter ─────────────────────────────────────────────────────────

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
                return _localization.Get("Portfolio.Filter.AllAssets", "所有資產");
            if (checkedItems.Count == 1)
                return checkedItems[0].Symbol;
            var unit = _localization.Get("Portfolio.Filter.AssetsCountUnit", "項資產");
            return $"{checkedItems.Count} {unit}";
        }
    }

    // ── Text search & date range ─────────────────────────────────────────────

    [ObservableProperty] private string _tradeSearchText = string.Empty;
    [ObservableProperty] private DateTime? _tradeDateFrom;
    [ObservableProperty] private DateTime? _tradeDateTo;

    partial void OnTradeSearchTextChanged(string _)
    {
        TradeCurrentPage = 1;
        RefreshTradesView();
        OnPropertyChanged(nameof(HasActiveTradeFilter));
    }

    partial void OnTradeDateFromChanged(DateTime? _)
    {
        TradeCurrentPage = 1;
        RefreshTradesView();
        OnPropertyChanged(nameof(HasActiveTradeFilter));
    }

    partial void OnTradeDateToChanged(DateTime? _)
    {
        TradeCurrentPage = 1;
        RefreshTradesView();
        OnPropertyChanged(nameof(HasActiveTradeFilter));
    }

    // ── Pagination ───────────────────────────────────────────────────────────

    [ObservableProperty] private int _tradePageSize = 25;
    [ObservableProperty] private int _tradeCurrentPage = 1;
    [ObservableProperty] private int _tradeTotalPages = 1;
    [ObservableProperty] private int _tradeTotalCount;
    [ObservableProperty] private string _tradePageDisplay = string.Empty;

    public static IReadOnlyList<int> PageSizeOptions { get; } = [25, 50, 100];

    partial void OnTradePageSizeChanged(int _) { TradeCurrentPage = 1; RefreshTradesView(); }

    [ObservableProperty] private string _tradeJumpPageInput = "1";

    partial void OnTradeCurrentPageChanged(int value) => TradeJumpPageInput = value.ToString();

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

    [RelayCommand]
    private void GoToPage(int page)
    {
        if (page < 1 || page > TradeTotalPages || page == TradeCurrentPage)
            return;
        TradeCurrentPage = page;
        RefreshTradesView();
    }

    [RelayCommand]
    private void JumpToPage()
    {
        if (int.TryParse(TradeJumpPageInput, out var p))
            GoToPage(p);
        TradeJumpPageInput = TradeCurrentPage.ToString();
    }

    // ── Page number list ─────────────────────────────────────────────────────

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
            int windowStart, windowEnd;
            if (current <= 4)
            { windowStart = 2; windowEnd = 5; }
            else if (current >= total - 3)
            { windowStart = total - 4; windowEnd = total - 1; }
            else
            { windowStart = current - 1; windowEnd = current + 1; }

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

    // ── CollectionView ───────────────────────────────────────────────────────

    /// <summary>
    /// Stores the Ids of trades visible on the current page so
    /// <see cref="FilterTrade"/> can implement Skip/Take pagination.
    /// </summary>
    private HashSet<Guid> _visibleTradeIds = [];

    private ObservableCollection<TradeRowViewModel>? _tradesCollection;

    private ICollectionView? _tradesView;

    /// <summary>
    /// The filtered + paginated view that the DataGrid binds to.
    /// Must be initialised by calling <see cref="AttachTradesCollection"/> before use.
    /// </summary>
    public ICollectionView? TradesView => _tradesView;

    /// <summary>
    /// Attaches the view to the <paramref name="trades"/> collection owned by
    /// <see cref="PortfolioViewModel"/>. Call once after the collection exists.
    /// </summary>
    public void AttachTradesCollection(ObservableCollection<TradeRowViewModel> trades)
    {
        ArgumentNullException.ThrowIfNull(trades);
        _tradesCollection = trades;
        _tradesView = CollectionViewSource.GetDefaultView(trades);
        _tradesView.Filter = FilterTrade;
        OnPropertyChanged(nameof(TradesView));
    }

    /// <summary>ICollectionView filter — only rows in _visibleTradeIds pass.</summary>
    private bool FilterTrade(object obj)
    {
        if (obj is not TradeRowViewModel t)
            return false;
        return _visibleTradeIds.Contains(t.Id);
    }

    /// <summary>Pure criteria match (no pagination).</summary>
    private bool MatchesTradeFilter(TradeRowViewModel t, HashSet<string> checkedTypeKeys, HashSet<string> checkedAssetSymbols)
    {
        if (checkedTypeKeys.Count > 0 && !TradeMatchesAnyTypeKey(t, checkedTypeKeys))
            return false;

        if (!string.IsNullOrWhiteSpace(TradeSearchText))
        {
            var q = TradeSearchText;
            if (!t.Symbol.Contains(q, StringComparison.OrdinalIgnoreCase) &&
                !t.Name.Contains(q, StringComparison.OrdinalIgnoreCase) &&
                !(t.Note?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false))
                return false;
        }

        if (TradeDateFrom.HasValue && t.TradeDate < TradeDateFrom.Value.Date.ToUniversalTime())
            return false;
        if (TradeDateTo.HasValue && t.TradeDate > TradeDateTo.Value.Date.AddDays(1).ToUniversalTime())
            return false;

        if (checkedAssetSymbols.Count > 0 && !checkedAssetSymbols.Contains(t.Symbol))
            return false;

        return true;
    }

    private static bool TradeMatchesAnyTypeKey(TradeRowViewModel t, HashSet<string> keys)
    {
        if (t.IsBuy && keys.Contains("Buy"))
            return true;
        if (t.IsSell && keys.Contains("Sell"))
            return true;
        if (t.IsIncome && keys.Contains("Income"))
            return true;
        if (t.IsCashDividend && keys.Contains("CashDividend"))
            return true;
        if (t.Type == TradeType.StockDividend && keys.Contains("StockDividend"))
            return true;
        if (t.IsDeposit && keys.Contains("Deposit"))
            return true;
        if (t.IsWithdrawal && keys.Contains("Withdrawal"))
            return true;
        if (t.IsTransfer && keys.Contains("Transfer"))
            return true;
        if (t.IsLoanBorrow && keys.Contains("LoanBorrow"))
            return true;
        if (t.IsLoanRepay && keys.Contains("LoanRepay"))
            return true;
        return false;
    }

    /// <summary>
    /// Recomputes the page slice and refreshes the CollectionView.
    /// Call whenever the Trades collection or any filter criterion changes.
    /// </summary>
    public void RefreshTradesView()
    {
        if (_tradesView is null)
            return;

        var allTrades = _getTrades();

        // 1. Compute filter sets once (avoids per-row allocations)
        var checkedTypeKeys = TradeTypeFilters.Where(f => f.IsChecked)
                                              .Select(f => f.Key)
                                              .ToHashSet();
        var checkedAssetSymbols = TradeAssetFilters.Where(f => f.IsChecked)
                                                   .Select(f => f.Symbol)
                                                   .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 2. Apply filter criteria (no pagination)
        var filtered = allTrades.Where(t => MatchesTradeFilter(t, checkedTypeKeys, checkedAssetSymbols)).ToList();
        TradeTotalCount = filtered.Count;
        TradeTotalPages = Math.Max(1, (int)Math.Ceiling((double)TradeTotalCount / TradePageSize));
        if (TradeCurrentPage > TradeTotalPages)
            TradeCurrentPage = TradeTotalPages;

        var unit = _localization.Get("Portfolio.TradeCount.Unit", "筆");
        TradePageDisplay = $"{TradeTotalCount} {unit}";

        // 3. Compute current page slice
        _visibleTradeIds = filtered
            .Skip((TradeCurrentPage - 1) * TradePageSize)
            .Take(TradePageSize)
            .Select(t => t.Id)
            .ToHashSet();

        // 4. Refresh the CollectionView
        _tradesView.Refresh();

        // 5. Rebuild page-number buttons
        RebuildPageNumbers();
    }

    // ── Filter initialisation ─────────────────────────────────────────────────

    /// <summary>初始化 Trade type filter 清單與 collection view（供 search 過濾）。</summary>
    public void InitTradeTypeFilters()
    {
        if (TradeTypeFilters.Count > 0)
            return;
        string Label(string key) =>
            _localization.Get($"Portfolio.Filter.{key}", key);
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
            if (string.IsNullOrWhiteSpace(TradeTypeFiltersSearch))
                return true;
            return o is TradeTypeFilterItem i &&
                   i.Label.Contains(TradeTypeFiltersSearch, StringComparison.OrdinalIgnoreCase);
        };
        OnPropertyChanged(nameof(TradeTypeFiltersView));
    }

    /// <summary>
    /// 從 Trades 重建資產篩選清單，依交易類型分到投資/現金/負債三個群組。
    /// 會保留原本勾選狀態（若該 symbol 仍存在）。
    /// </summary>
    public void RebuildTradeAssetFilters()
    {
        string LabelOf(string key) =>
            _localization.Get($"Portfolio.Filter.Category.{key}", key);
        string InvestmentLabel = LabelOf("Investment");
        string CashLabel = LabelOf("Cash");
        string LiabilityLabel = LabelOf("Liability");

        var symbolCategory = new Dictionary<string, (string Key, int Order, string Label)>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in _getTrades())
        {
            var sym = t.Symbol;
            if (string.IsNullOrWhiteSpace(sym))
                continue;
            if (symbolCategory.ContainsKey(sym))
                continue;

            (string, int, string) cat = t.Type switch
            {
                TradeType.Buy or TradeType.Sell or TradeType.CashDividend or TradeType.StockDividend
                    => ("Investment", 0, InvestmentLabel),
                TradeType.LoanBorrow or TradeType.LoanRepay
                    => ("Liability", 2, LiabilityLabel),
                _ => ("Cash", 1, CashLabel),
            };
            symbolCategory[sym] = cat;
        }

        var desired = symbolCategory
            .OrderBy(kv => kv.Value.Order)
            .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => (Symbol: kv.Key, Category: kv.Value))
            .ToList();

        var desiredSymbols = new HashSet<string>(
            desired.Select(d => d.Symbol), StringComparer.OrdinalIgnoreCase);

        for (int i = TradeAssetFilters.Count - 1; i >= 0; i--)
        {
            if (!desiredSymbols.Contains(TradeAssetFilters[i].Symbol))
            {
                TradeAssetFilters[i].PropertyChanged -= OnTradeAssetFilterItemChanged;
                TradeAssetFilters.RemoveAt(i);
            }
        }

        var existing = TradeAssetFilters
            .ToDictionary(f => f.Symbol, StringComparer.OrdinalIgnoreCase);

        foreach (var (sym, (catKey, catOrder, catLabel)) in desired)
        {
            if (!existing.ContainsKey(sym))
            {
                var item = new TradeAssetFilterItem(sym, catKey, catOrder, catLabel);
                item.PropertyChanged += OnTradeAssetFilterItemChanged;
                TradeAssetFilters.Add(item);
            }
        }

        for (int i = 0; i < desired.Count; i++)
        {
            int currentIndex = -1;
            for (int j = 0; j < TradeAssetFilters.Count; j++)
            {
                if (string.Equals(TradeAssetFilters[j].Symbol, desired[i].Symbol,
                                  StringComparison.OrdinalIgnoreCase))
                {
                    currentIndex = j;
                    break;
                }
            }
            if (currentIndex != i)
                TradeAssetFilters.Move(currentIndex, i);
        }

        if (TradeAssetFiltersView is null)
        {
            TradeAssetFiltersView = CollectionViewSource.GetDefaultView(TradeAssetFilters);
            TradeAssetFiltersView.Filter = o =>
            {
                if (string.IsNullOrWhiteSpace(TradeAssetFiltersSearch))
                    return true;
                return o is TradeAssetFilterItem i &&
                       i.Symbol.Contains(TradeAssetFiltersSearch, StringComparison.OrdinalIgnoreCase);
            };
            TradeAssetFiltersView.GroupDescriptions.Add(
                new PropertyGroupDescription(nameof(TradeAssetFilterItem.CategoryLabel)));
            OnPropertyChanged(nameof(TradeAssetFiltersView));
        }
        else
        {
            TradeAssetFiltersView.Refresh();
        }
        OnPropertyChanged(nameof(TradeAssetFiltersDisplay));
    }

    // ── Filter-item event handlers ────────────────────────────────────────────

    private void OnTradeTypeFilterItemChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TradeTypeFilterItem.IsChecked))
            return;
        TradeCurrentPage = 1;
        RefreshTradesView();
        OnPropertyChanged(nameof(HasActiveTradeFilter));
        OnPropertyChanged(nameof(TradeTypeFiltersDisplay));
    }

    private void OnTradeAssetFilterItemChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TradeAssetFilterItem.IsChecked))
            return;
        TradeCurrentPage = 1;
        RefreshTradesView();
        OnPropertyChanged(nameof(HasActiveTradeFilter));
        OnPropertyChanged(nameof(TradeAssetFiltersDisplay));
    }

    // ── Filter clear commands ─────────────────────────────────────────────────

    [RelayCommand]
    private void ClearTradeTypeFilters()
    {
        foreach (var item in TradeTypeFilters)
            item.IsChecked = false;
        TradeTypeFiltersSearch = string.Empty;
    }

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
    public void FilterByDividendMonth(int month, int year)
    {
        foreach (var item in TradeTypeFilters)
            item.IsChecked = item.Key is "CashDividend" or "StockDividend";
        foreach (var item in TradeAssetFilters)
            item.IsChecked = false;
        TradeDateFrom = new DateTime(year, month, 1);
        TradeDateTo = new DateTime(year, month, DateTime.DaysInMonth(year, month));
        TradeSearchText = string.Empty;
    }

    // ── Derived / computed ────────────────────────────────────────────────────

    public bool HasActiveTradeFilter =>
        !string.IsNullOrEmpty(TradeSearchText) ||
        TradeDateFrom.HasValue || TradeDateTo.HasValue ||
        TradeTypeFilters.Any(f => f.IsChecked) ||
        TradeAssetFilters.Any(f => f.IsChecked);
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
