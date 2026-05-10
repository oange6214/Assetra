using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Assetra.Core.Interfaces;
using Assetra.WPF.Features.Portfolio;
using Assetra.WPF.Features.Portfolio.Contracts;
using Assetra.WPF.Infrastructure;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Assetra.WPF.Features.Portfolio.Controls;

public sealed partial class AllocationViewModel : ObservableObject, IDisposable
{
    private readonly IPortfolioPositionFeed _portfolio;
    private readonly INotifyCollectionChanged? _positionsObservable;
    private readonly IAppSettingsService? _settings;
    private readonly Dispatcher _dispatcher;

    // Color palette (assigned by order of appearance) + neutral cash brush
    private static readonly SolidColorBrush CashBrush = Brush("#6B7280");
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
    public ReadOnlyObservableCollection<AllocationRowViewModel> AllocationRows { get; }

    // Tab state
    [ObservableProperty] private bool _isOverviewTab = true;
    [ObservableProperty] private bool _isRebalanceTab = false;

    /// <summary>
    /// 配置分析「含現金」開關：預設 false（純投資商品配置，符合 Morningstar /
    /// Yahoo Finance 等 portfolio tracker 的標準呈現）。勾選 true 時會把現金
    /// 視為一個防禦性配置部位納入百分比計算。狀態為 in-memory（不持久化），
    /// 跨 session 重啟會回到預設不含。
    /// </summary>
    [ObservableProperty] private bool _includeCashInAllocation;

    partial void OnIncludeCashInAllocationChanged(bool _) => Rebuild();

    [RelayCommand]
    private void SwitchToOverview() { IsOverviewTab = true; IsRebalanceTab = false; }
    [RelayCommand]
    private void SwitchToRebalance() { IsRebalanceTab = true; IsOverviewTab = false; }

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

    private readonly ILocalizationService? _localization;

    private string L(string key, string fallback) =>
        _localization?.Get(key, fallback) ?? fallback;

    // L4: localized "現金" display label. Symbol stays "現金" (domain key for
    // the target-percentage dictionary lookup); only the display Name varies.
    private string CashLabel => L("Allocation.Cash", "現金");

    // Ctor — consumes IPortfolioPositionFeed (PortfolioViewModel implements it)
    // so this VM can be unit-tested against a stub feed without constructing
    // the full portfolio dependency graph (L3 decoupling).
    public AllocationViewModel(
        IPortfolioPositionFeed portfolio,
        IAppSettingsService? settings = null,
        ILocalizationService? localization = null)
    {
        ArgumentNullException.ThrowIfNull(portfolio);
        _portfolio = portfolio;
        _settings = settings;
        _localization = localization;
        _dispatcher = Dispatcher.CurrentDispatcher;
        AllocationRows = new ReadOnlyObservableCollection<AllocationRowViewModel>(_allocationRows);

        _positionsObservable = portfolio.Positions as INotifyCollectionChanged;
        if (_positionsObservable is not null)
            _positionsObservable.CollectionChanged += OnCollectionChanged;
        portfolio.PropertyChanged += OnPortfolioPropertyChanged;

        // Subscribe to each existing position's property changes
        foreach (var row in portfolio.Positions)
            row.PropertyChanged += OnRowPropertyChanged;

        // Re-evaluate treemap tile colours when the user toggles Taiwan/International
        // convention or switches light/dark theme.
        ColorSchemeService.SchemeChanged += OnSchemeChanged;

        Rebuild();
    }

    private void OnPortfolioPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IPortfolioPositionFeed.TotalCash))
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
        if (e.PropertyName is nameof(PortfolioRowViewModel.MarketValue)
                           or nameof(PortfolioRowViewModel.CurrentPrice)
                           or nameof(PortfolioRowViewModel.Pnl))
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

        // Investment positions (sorted largest first for treemap)
        var investItems = _portfolio.Positions
            .GroupBy(p => p.Symbol)
            .Select(g => (
                Symbol: g.Key,
                Name: g.First().Name,
                Cat: CategoryOf(g.First()),
                Value: g.Sum(p => p.MarketValue),
                Price: g.First().CurrentPrice,
                Pnl: g.Sum(p => p.Pnl),
                PnlPct: g.Average(p => p.PnlPercent)))
            .OrderByDescending(i => i.Value)
            .ToList();

        var cashTotal = _portfolio.TotalCash;
        var investTotal = investItems.Sum(i => i.Value);

        // 配置百分比的分母：預設只算「投資商品」(投資組合內部配置)；勾選含現金 → 投資+現金。
        // TotalValue 仍存「總資產」(invest + cash) 給其他 binding 用，但 percentage 用的是
        // denominator (依 toggle 切換)。
        var denominator = IncludeCashInAllocation ? investTotal + cashTotal : investTotal;
        TotalValue = investTotal + cashTotal;
        TotalInvestment = investTotal;
        TotalCash = cashTotal;
        TotalPnl = investItems.Sum(i => i.Pnl);
        AssetCount = investItems.Count;

        // Build investment rows (palette colors assigned by investment rank)
        var allRows = new List<AllocationRowViewModel>(investItems.Count + 1);
        for (int idx = 0; idx < investItems.Count; idx++)
        {
            var item = investItems[idx];
            var row = new AllocationRowViewModel(
                item.Symbol, item.Name, item.Cat,
                item.Value, item.Price, item.Pnl, item.PnlPct, Palette[idx % Palette.Length]);
            row.ActualPercent = denominator > 0 ? Math.Round(item.Value / denominator * 100m, 1) : 0m;
            row.TargetPercent = targets.TryGetValue(item.Symbol, out var t) ? t : 0m;
            row.EditTargetText = row.TargetPercent.ToString("G");
            allRows.Add(row);
        }

        // Cash row：only emitted when the user explicitly opts in via IncludeCashInAllocation.
        // Default: cash excluded — pure investment allocation matches industry convention
        // (Morningstar / Yahoo Finance) and prevents diluting the rebalance signal.
        if (IncludeCashInAllocation && cashTotal > 0)
        {
            // Symbol "現金" stays as the dictionary key (matches saved target
            // percentages); Name is localized for display.
            var cashRow = new AllocationRowViewModel("現金", CashLabel, "cash", cashTotal, 0m, 0m, 0m, CashBrush);
            cashRow.ActualPercent = denominator > 0 ? Math.Round(cashTotal / denominator * 100m, 1) : 0m;
            cashRow.TargetPercent = targets.TryGetValue("現金", out var cashTarget) ? cashTarget : 0m;
            cashRow.EditTargetText = cashRow.TargetPercent.ToString("G");
            allRows.Add(cashRow);
        }

        // Final sort (cash may sit anywhere by value)
        allRows.Sort((a, b) => b.MarketValue.CompareTo(a.MarketValue));

        _allocationRows.Clear();
        foreach (var row in allRows)
            _allocationRows.Add(row);

        RebuildBuySell();
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
        _portfolio.PropertyChanged -= OnPortfolioPropertyChanged;

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
