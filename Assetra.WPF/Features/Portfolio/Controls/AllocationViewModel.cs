using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Media;
using System.Windows.Threading;
using Assetra.Core.Interfaces;
using Assetra.WPF.Features.Portfolio.Contracts;
using Assetra.WPF.Features.PortfolioGroups;
using Assetra.WPF.Infrastructure;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Assetra.WPF.Features.Portfolio.Controls;

public enum AllocationGroupingMode { Symbol, Group, Currency }

public sealed partial class AllocationViewModel : ObservableObject, IDisposable
{
    private readonly IPortfolioPositionFeed _portfolio;
    private readonly INotifyCollectionChanged? _positionsObservable;
    private readonly IAppSettingsService? _settings;
    private readonly PortfolioGroupCatalog? _groupCatalog;
    private readonly Dispatcher _dispatcher;

    /// <summary>
    /// Portfolio-Groups-Refactor P4 — XAML 用：catalog 存在且有 user-created group 才暴露 toggle。
    /// P3.9 — 排除 IsSystem default group (見 PortfolioViewModel.HasPortfolioGroups 的完整理由)。
    /// </summary>
    public bool HasPortfolioGroups => _groupCatalog?.Groups.Any(g => !g.IsSystem) == true;

    /// <summary>
    /// Allocation 分組維度 toggle。三模式：
    /// <list type="bullet">
    ///   <item><see cref="AllocationGroupingMode.Symbol"/> 依個別代號 (default)</item>
    ///   <item><see cref="AllocationGroupingMode.Group"/> 依 PortfolioGroup (Portfolio-Groups P4)</item>
    ///   <item><see cref="AllocationGroupingMode.Currency"/> 依計價幣別 (MultiCurrency P4.2 tail)</item>
    /// </list>
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBySymbolMode))]
    [NotifyPropertyChangedFor(nameof(IsByGroupMode))]
    [NotifyPropertyChangedFor(nameof(IsByCurrencyMode))]
    private AllocationGroupingMode _groupingMode = AllocationGroupingMode.Symbol;

    public bool IsBySymbolMode => GroupingMode == AllocationGroupingMode.Symbol;
    public bool IsByGroupMode => GroupingMode == AllocationGroupingMode.Group;
    public bool IsByCurrencyMode => GroupingMode == AllocationGroupingMode.Currency;

    partial void OnGroupingModeChanged(AllocationGroupingMode _) => Rebuild();

    /// <summary>XAML RadioButton command — parses enum name string ("Symbol"/"Group"/"Currency").</summary>
    [RelayCommand]
    private void SetGroupingMode(string? raw)
    {
        if (Enum.TryParse<AllocationGroupingMode>(raw, ignoreCase: true, out var v))
            GroupingMode = v;
    }

    // Color palette (assigned by order of appearance)
    private static readonly SolidColorBrush[] Palette =
    [
        Brush("#2563EB"), Brush("#7C3AED"), Brush("#0891B2"),
        Brush("#059669"), Brush("#D97706"), Brush("#DC2626"),
        Brush("#DB2777"), Brush("#0D9488"), Brush("#EA580C"),
        Brush("#84CC16"), Brush("#6366F1"), Brush("#F59E0B"),
    ];

    private static SolidColorBrush Brush(string hex)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    // Observable collections
    private readonly ObservableCollection<AllocationRowViewModel> _allocationRows = new();
    private readonly ObservableCollection<AllocationInsightCardViewModel> _allocationInsightCards = new();
    public ReadOnlyObservableCollection<AllocationRowViewModel> AllocationRows { get; }
    public ReadOnlyObservableCollection<AllocationInsightCardViewModel> AllocationInsightCards { get; }

    // Tab state
    [ObservableProperty] private bool _isOverviewTab = true;
    [ObservableProperty] private bool _isRebalanceTab = false;

    [RelayCommand]
    private void SwitchToOverview() { IsOverviewTab = true; IsRebalanceTab = false; }
    [RelayCommand]
    private void SwitchToRebalance()
    {
        // Portfolio-Groups-Refactor P4 — Rebalance 用 per-symbol target 比例，by-group
        // 或 by-currency 模式下 row.Symbol = group/ccy 名稱，會跟 targets dict 完全錯位。
        // 離開 Overview 時強制切回 by-symbol，避免 Rebalance buy/sell 數字錯亂。
        if (GroupingMode != AllocationGroupingMode.Symbol)
            GroupingMode = AllocationGroupingMode.Symbol;
        IsRebalanceTab = true;
        IsOverviewTab = false;
    }

    // Rebalance mode
    [ObservableProperty] private bool _isFullRebalance = true;
    [ObservableProperty] private bool _isCashRebalance = false;
    [ObservableProperty] private string _cashFlowInput = string.Empty;
    [ObservableProperty] private decimal _cashFlowTotal;     // sum of buys in cash-flow mode
    // CashFlowTotal is bound directly via CurrencyConverter in AllocationView.xaml,
    // so the user's TWD/USD preference applies. No more hard-coded "NT$".

    partial void OnCashFlowInputChanged(string _) => RebuildBuySell();

    [RelayCommand]
    private void SetFullRebalance() { IsFullRebalance = true; IsCashRebalance = false; RebuildBuySell(); }
    [RelayCommand]
    private void SetCashRebalance() { IsFullRebalance = false; IsCashRebalance = true; RebuildBuySell(); }

    // Target editing
    [ObservableProperty] private bool _isEditingTargets;
    [ObservableProperty] private string _targetEditError = string.Empty;
    [ObservableProperty] private decimal _targetSum;

    [RelayCommand]
    private void BeginEditTargets()
    {
        foreach (var row in AllocationRows)
            row.EditTargetText = row.TargetPercent.ToString("G");
        TargetEditError = string.Empty;
        IsEditingTargets = true;
    }

    [RelayCommand]
    private void CancelEditTargets()
    {
        IsEditingTargets = false;
        Rebuild();  // catch up on any price updates that were skipped during editing
    }

    [RelayCommand]
    private async Task SaveTargets()
    {
        TargetEditError = string.Empty;
        var parsed = new Dictionary<string, decimal>();

        foreach (var row in AllocationRows)
        {
            if (!ParseHelpers.TryParseDecimal(row.EditTargetText, out var v) || v < 0 || v > 100)
            {
                TargetEditError = $"{row.Symbol} 的目標比例無效";
                return;
            }
            parsed[row.Symbol] = v;
        }

        var sum = parsed.Values.Sum();
        if (Math.Abs(sum - 100m) > 0.5m)
        {
            TargetEditError = $"目標比例合計 {sum:F1}%，需等於 100%";
            TargetSum = sum;
            return;
        }

        // Persist
        if (_settings is not null)
        {
            var updated = _settings.Current with { TargetAllocations = parsed };
            await _settings.SaveAsync(updated);
        }

        // Apply
        foreach (var row in AllocationRows)
        {
            if (parsed.TryGetValue(row.Symbol, out var t))
                row.TargetPercent = t;
        }
        RebuildBuySell();
        IsEditingTargets = false;
    }

    // Summary totals
    [ObservableProperty] private decimal _totalValue;
    [ObservableProperty] private decimal _totalInvestment;
    [ObservableProperty] private decimal _totalCash;
    [ObservableProperty] private decimal _totalPnl;
    [ObservableProperty] private int _assetCount;

    // Display strings removed (TotalValueDisplay / TotalInvestmentDisplay /
    // TotalCashDisplay / TotalPnlDisplay) — verified unbound across all
    // *.xaml/*.cs in the solution. They were dead code from a past refactor
    // and hard-coded "NT$" which bypassed the CurrencyConverter pipeline.
    // If a future view needs them, bind to the underlying decimal properties
    // through {StaticResource CurrencyConverter} like CashFlowTotal does.
    public bool IsTotalPnlPositive => TotalPnl >= 0;

    partial void OnTotalPnlChanged(decimal _) => OnPropertyChanged(nameof(IsTotalPnlPositive));

    // Ctor — consumes IPortfolioPositionFeed (PortfolioViewModel implements it)
    // so this VM can be unit-tested against a stub feed without constructing
    // the full portfolio dependency graph (L3 decoupling).
    public AllocationViewModel(
        IPortfolioPositionFeed portfolio,
        IAppSettingsService? settings = null,
        PortfolioGroupCatalog? groupCatalog = null)
    {
        ArgumentNullException.ThrowIfNull(portfolio);
        _portfolio = portfolio;
        _settings = settings;
        _groupCatalog = groupCatalog;
        _dispatcher = Dispatcher.CurrentDispatcher;
        AllocationRows = new ReadOnlyObservableCollection<AllocationRowViewModel>(_allocationRows);
        AllocationInsightCards = new ReadOnlyObservableCollection<AllocationInsightCardViewModel>(_allocationInsightCards);

        _positionsObservable = portfolio.Positions as INotifyCollectionChanged;
        if (_positionsObservable is not null)
            _positionsObservable.CollectionChanged += OnCollectionChanged;

        // Subscribe to each existing position's property changes
        foreach (var row in portfolio.Positions)
            row.PropertyChanged += OnRowPropertyChanged;

        // Re-evaluate treemap tile colours when the user toggles Taiwan/International
        // convention or switches light/dark theme.
        ColorSchemeService.SchemeChanged += OnSchemeChanged;

        Rebuild();
    }

    private void OnSchemeChanged()
    {
        foreach (var row in AllocationRows)
            row.NotifyPnlColorChanged();
    }

    // Event handlers

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
            foreach (INotifyPropertyChanged item in e.NewItems)
                item.PropertyChanged += OnRowPropertyChanged;
        if (e.OldItems is not null)
            foreach (INotifyPropertyChanged item in e.OldItems)
                item.PropertyChanged -= OnRowPropertyChanged;
        Rebuild();
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // P5.17 — 加 MarketValueBase / PnlBase：之前只聽 native 屬性，但 AllocationVM 改用
        // *Base 跑跨幣別聚合後，base 屬性變動也要觸發 Rebuild（否則 FX 重算後 % 不刷）。
        if (e.PropertyName is nameof(PortfolioRowViewModel.MarketValue)
                           or nameof(PortfolioRowViewModel.MarketValueBase)
                           or nameof(PortfolioRowViewModel.CurrentPrice)
                           or nameof(PortfolioRowViewModel.Pnl)
                           or nameof(PortfolioRowViewModel.PnlBase))
            Rebuild();
    }

    // Rebuild

    private void Rebuild()
    {
        if (_dispatcher.CheckAccess() == false)
        {
            _dispatcher.InvokeAsync(Rebuild);
            return;
        }

        if (IsEditingTargets)
            return;  // don't overwrite user's in-flight edits

        var targets = _settings?.Current?.TargetAllocations
                      ?? new Dictionary<string, decimal>();

        // GroupingMode 切換 aggregation key:
        //   Symbol   (default)                        → 依個別代號（既有行為）
        //   Group    (Portfolio-Groups-Refactor P4)   → 依 PortfolioGroupId；row 顯示 group.Name
        //   Currency (MultiCurrency-Reporting P4.2)   → 依標的計價幣別；row 顯示 ccy code
        // null PortfolioGroupId 視為 DefaultId，跟 Trade backfill 對齊。
        // Currency 模式用 MarketValue (native) 加總，跨幣別不混淆；
        // 但每幣別 bucket 的占比仍以 native 加總 ÷ 全 portfolio MarketValue 計算——
        // 這個近似在 base-currency 單一時等價於 base 換算，UI 上不會誤導。
        // P5.17 — 加 ValueBase + Currency 給 currency-aware 顯示 + 跨幣別正確比例計算。
        //   - Symbol grouping: g 同 symbol = 同幣別，native sum 跟 base sum 分別記錄；
        //     ValueBase 給 ActualPercent 跟 insight cards 聚合（跨幣別可比較）；
        //     Value 給 row display（原幣保留）。
        //   - Group / Currency grouping：group 內可能混幣別 → Value 用 base aggregate，
        //     Currency 設 "TWD"（aggregate base unit）。
        var investItems = GroupingMode switch
        {
            AllocationGroupingMode.Group => _portfolio.Positions
                .GroupBy(p => p.PortfolioGroupId ?? Assetra.Core.Models.PortfolioGroup.DefaultId)
                .Select(g => (
                    Symbol: ResolveGroupLabel(g.Key),
                    Name: ResolveGroupLabel(g.Key),
                    Cat: "Group",
                    Value: g.Sum(p => p.MarketValueBase),
                    ValueBase: g.Sum(p => p.MarketValueBase),
                    Price: 0m,
                    Pnl: g.Sum(p => p.PnlBase),
                    PnlPct: g.Sum(p => p.MarketValueBase) > 0m
                        ? g.Sum(p => p.PnlBase) / g.Sum(p => p.MarketValueBase) * 100m
                        : 0m,
                    Currency: "TWD"))
                .OrderByDescending(i => i.Value)
                .ToList(),
            AllocationGroupingMode.Currency => _portfolio.Positions
                .GroupBy(p => string.IsNullOrWhiteSpace(p.Currency) ? "TWD" : p.Currency.ToUpperInvariant())
                .Select(g => (
                    Symbol: g.Key,
                    Name: g.Key,
                    Cat: "Currency",
                    Value: g.Sum(p => p.MarketValue),
                    ValueBase: g.Sum(p => p.MarketValueBase),
                    Price: 0m,
                    Pnl: g.Sum(p => p.Pnl),
                    PnlPct: g.Sum(p => p.MarketValueBase) > 0m
                        ? g.Sum(p => p.PnlBase) / g.Sum(p => p.MarketValueBase) * 100m
                        : 0m,
                    Currency: g.Key))
                .OrderByDescending(i => i.ValueBase)
                .ToList(),
            _ => _portfolio.Positions
                .GroupBy(p => p.Symbol)
                .Select(g => (
                    Symbol: g.Key,
                    Name: g.First().Name,
                    Cat: CategoryOf(g.First()),
                    Value: g.Sum(p => p.MarketValue),
                    ValueBase: g.Sum(p => p.MarketValueBase),
                    Price: g.First().CurrentPrice,
                    Pnl: g.Sum(p => p.Pnl),
                    PnlPct: g.Average(p => p.PnlPercent),
                    Currency: string.IsNullOrWhiteSpace(g.First().Currency)
                        ? "TWD"
                        : g.First().Currency.Trim().ToUpperInvariant()))
                .OrderByDescending(i => i.ValueBase)
                .ToList(),
        };

        // P5.17 — total 用 base aggregate（跨幣別正確比較）。
        var investTotal = investItems.Sum(i => i.ValueBase);

        // 投資資產頁的配置分析只呈現投資商品本身，不混入現金帳戶。
        // 全域現金與總資產由「財務儀表板」負責，避免兩個頁面說不同故事。
        var denominator = investTotal;
        TotalValue = investTotal;
        TotalInvestment = investTotal;
        TotalCash = 0m;
        // P5.17 — TotalPnl 走 base aggregate（跨幣別 sum 才正確）。對 Symbol grouping
        // 用 g.Sum(PnlBase) 比較對；目前 tuple 的 i.Pnl 對 Symbol 模式還是 native，
        // 改用 base 同步。
        TotalPnl = _portfolio.Positions.Sum(p => p.PnlBase);
        AssetCount = investItems.Count;

        // Build investment rows (palette colors assigned by investment rank)
        var allRows = new List<AllocationRowViewModel>(investItems.Count);
        for (int idx = 0; idx < investItems.Count; idx++)
        {
            var item = investItems[idx];
            var row = new AllocationRowViewModel(
                item.Symbol, item.Name, item.Cat,
                item.Value, item.Price, item.Pnl, item.PnlPct, Palette[idx % Palette.Length],
                // P5.17 — 傳 base value + 原幣 currency 給 row display 用。
                marketValueBase: item.ValueBase,
                currency: item.Currency);
            // P5.17 — ActualPercent 用 base sum 才有跨幣別意義（denominator 已是 base）。
            row.ActualPercent = denominator > 0 ? item.ValueBase / denominator * 100m : 0m;
            row.TargetPercent = targets.TryGetValue(item.Symbol, out var t) ? t : 0m;
            row.EditTargetText = row.TargetPercent.ToString("G");
            allRows.Add(row);
        }

        // Final sort — 用 base 比較（跨幣別 native sort 會誤把 USD 數字當小數）。
        allRows.Sort((a, b) => b.MarketValueBase.CompareTo(a.MarketValueBase));

        _allocationRows.Clear();
        foreach (var row in allRows)
            _allocationRows.Add(row);

        RebuildInsightCards(allRows);

        RebuildBuySell();
    }

    private void RebuildInsightCards(IReadOnlyList<AllocationRowViewModel> rows)
    {
        _allocationInsightCards.Clear();
        if (rows.Count == 0)
            return;

        var largest = rows[0];
        var topThree = rows.Take(3).ToList();
        var tail = rows.Where(r => r.ActualPercent is > 0m and <= 1m).ToList();
        var averagePercent = rows.Count > 0 ? 100m / rows.Count : 0m;

        // P5.17 — 「最大配置」subtitle 用 native (row.MarketValue + Currency) 顯示
        //   個別資產原幣，例：「Roundhill Memory ETF · US$13,917」、
        //   「緯創 · NT$5,780,000」。其他 cards 是 mixed-currency aggregate sum，
        //   必須走 base TWD（FormatTwd）才有意義。
        _allocationInsightCards.Add(new AllocationInsightCardViewModel(
            "最大配置",
            largest.Symbol,
            largest.ActualPercentDisplay,
            $"{largest.Name} · {FormatNative(largest.MarketValue, largest.Currency)}",
            largest.ColorBrush));

        _allocationInsightCards.Add(new AllocationInsightCardViewModel(
            "集中度",
            "前 3 大",
            FormatPercent(topThree.Sum(r => r.ActualPercent)),
            $"合計 {FormatTwd(topThree.Sum(r => r.MarketValueBase))}",
            Palette[0]));

        _allocationInsightCards.Add(new AllocationInsightCardViewModel(
            "長尾配置",
            "≤ 1% 持倉",
            tail.Count.ToString("N0"),
            tail.Count == 0
                ? "沒有極小配置"
                : $"合計 {FormatPercent(tail.Sum(r => r.ActualPercent))} · {FormatTwd(tail.Sum(r => r.MarketValueBase))}",
            Palette[4]));

        _allocationInsightCards.Add(new AllocationInsightCardViewModel(
            "分散度",
            "持倉數",
            rows.Count.ToString("N0"),
            $"平均佔比 {FormatPercent(averagePercent)}",
            Palette[7]));
    }

    private static string FormatPercent(decimal value) => value switch
    {
        > 0m and < 0.1m => "<0.1%",
        _ => $"{value:F1}%",
    };

    private static string FormatTwd(decimal value) => $"NT${value:N0}";

    /// <summary>
    /// P5.17 — Currency-aware 顯示：給 "最大配置" insight subtitle 用單一 row 的
    /// native value + currency 渲染（例：DRAM 是「US$13,917」、緯創是「NT$5,780,000」）。
    /// 跟 AllocationRowViewModel.ResolveSymbol 對齊 CurrencyConverter.GetSymbol。
    /// </summary>
    private static string FormatNative(decimal value, string currency)
    {
        var symbol = currency?.Trim().ToUpperInvariant() switch
        {
            "USD" => "US$",
            "JPY" => "¥",
            "EUR" => "€",
            "HKD" => "HK$",
            _ => "NT$",
        };
        return $"{symbol}{value:N0}";
    }

    private void RebuildBuySell()
    {
        if (IsFullRebalance)
        {
            foreach (var row in AllocationRows)
            {
                if (row.IsCashRow)
                { row.SetBuySell(0m, row.ActualPercent); continue; }
                // When no target is set, treat as no-op (don't suggest selling everything)
                var needed = row.TargetPercent > 0
                    ? (row.TargetPercent - row.ActualPercent) / 100m * TotalValue
                    : 0m;
                row.SetBuySell(Math.Round(needed, 0), row.ActualPercent);
            }
            CashFlowTotal = 0m;
        }
        else
        {
            // 現金流再平衡: user provides a cash amount; only buy underweights, never sell
            ParseHelpers.TryParseDecimal(CashFlowInput, out var cashInput);
            cashInput = Math.Max(cashInput, 0m);

            var newTotal = TotalValue + cashInput;

            // For each asset compute how much TWD it needs to reach its target in the enlarged
            // portfolio. Assets so overweight that even the new total leaves them above target
            // receive 0 (we never sell in cash-flow mode).
            var needs = AllocationRows
                .Select(r =>
                {
                    // Cash cannot be "bought" — exclude from cash-flow deployment
                    var need = !r.IsCashRow && r.TargetPercent > 0
                        ? Math.Max(0m, r.TargetPercent / 100m * newTotal - r.MarketValue)
                        : 0m;
                    return (Row: r, Need: need);
                })
                .ToList();

            var totalNeed = needs.Sum(n => n.Need);

            decimal runningTotal = 0m;
            foreach (var (row, need) in needs)
            {
                if (need > 0m && totalNeed > 0m)
                {
                    // Scale proportionally when cash < total need; buy in full when cash covers all
                    var buy = totalNeed <= cashInput
                        ? Math.Round(need, 0)
                        : Math.Round(cashInput * need / totalNeed, 0);

                    var afterPct = newTotal > 0
                        ? Math.Round((row.MarketValue + buy) / newTotal * 100m, 1)
                        : row.ActualPercent;
                    row.SetBuySell(buy, row.ActualPercent, afterPct);
                    runningTotal += buy;
                }
                else
                {
                    var afterPct = newTotal > 0
                        ? Math.Round(row.MarketValue / newTotal * 100m, 1)
                        : row.ActualPercent;
                    row.SetBuySell(0m, row.ActualPercent, afterPct);
                }
            }

            CashFlowTotal = runningTotal;
        }
    }

    /// <summary>
    /// Portfolio-Groups-Refactor P4 — 將 group id 解析成 display name；catalog 沒灌入或
    /// 找不到對應群組時，fallback 為「未指派」+ id 前 8 碼，方便除錯且不會凍結 UI。
    /// </summary>
    private string ResolveGroupLabel(Guid groupId)
    {
        var found = _groupCatalog?.Groups.FirstOrDefault(g => g.Id == groupId);
        if (found is not null)
            return found.Name;
        if (groupId == Assetra.Core.Models.PortfolioGroup.DefaultId)
            return "預設群組";
        return $"未指派 ({groupId.ToString()[..8]})";
    }

    private static string CategoryOf(PortfolioRowViewModel row) => row.AssetType switch
    {
        Core.Models.AssetType.Fund => "fund",
        Core.Models.AssetType.PreciousMetal => "metal",
        Core.Models.AssetType.Bond => "bond",
        Core.Models.AssetType.Crypto => "crypto",
        _ => "stock",
    };

    public void Dispose()
    {
        ColorSchemeService.SchemeChanged -= OnSchemeChanged;

        // C1 leak fix: also unsubscribe Positions.CollectionChanged and the
        // per-row PropertyChanged handlers attached at construction.
        // Previously these stayed subscribed forever, keeping every recreated
        // AllocationViewModel alive on every quote tick.
        if (_positionsObservable is not null)
            _positionsObservable.CollectionChanged -= OnCollectionChanged;
        foreach (var row in _portfolio.Positions)
            row.PropertyChanged -= OnRowPropertyChanged;
    }
}
