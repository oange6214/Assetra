using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Assetra.Core.Interfaces;
using Assetra.WPF.Features.Portfolio;
using Assetra.WPF.Infrastructure;

namespace Assetra.WPF.Features.Allocation;

public sealed partial class AllocationViewModel : ObservableObject, IDisposable
{
    private readonly PortfolioViewModel _portfolio;
    private readonly IAppSettingsService? _settings;

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
    public ObservableCollection<AllocationRowViewModel> AllocationRows { get; } = new();

    // Tab state
    [ObservableProperty] private bool _isOverviewTab = true;
    [ObservableProperty] private bool _isRebalanceTab = false;

    [RelayCommand]
    private void SwitchToOverview() { IsOverviewTab = true; IsRebalanceTab = false; }
    [RelayCommand]
    private void SwitchToRebalance() { IsOverviewTab = false; IsRebalanceTab = true; }

    // Rebalance mode
    [ObservableProperty] private bool _isFullRebalance = true;
    [ObservableProperty] private bool _isCashRebalance = false;
    [ObservableProperty] private string _cashFlowInput = string.Empty;
    [ObservableProperty] private decimal _cashFlowTotal;     // sum of buys in cash-flow mode
    public string CashFlowTotalDisplay => $"NT${CashFlowTotal:N0}";

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

    public string TotalValueDisplay      => $"NT${TotalValue:N0}";
    public string TotalInvestmentDisplay => $"NT${TotalInvestment:N0}";
    public string TotalCashDisplay       => $"NT${TotalCash:N0}";
    public string TotalPnlDisplay        => (TotalPnl >= 0 ? "+" : "") + $"NT${TotalPnl:N0}";
    public bool   IsTotalPnlPositive     => TotalPnl >= 0;

    partial void OnTotalPnlChanged(decimal _)
    {
        OnPropertyChanged(nameof(TotalPnlDisplay));
        OnPropertyChanged(nameof(IsTotalPnlPositive));
    }
    partial void OnTotalValueChanged(decimal _)      => OnPropertyChanged(nameof(TotalValueDisplay));
    partial void OnTotalInvestmentChanged(decimal _) => OnPropertyChanged(nameof(TotalInvestmentDisplay));
    partial void OnTotalCashChanged(decimal _)       => OnPropertyChanged(nameof(TotalCashDisplay));

    // Ctor
    public AllocationViewModel(PortfolioViewModel portfolio, IAppSettingsService? settings = null)
    {
        _portfolio = portfolio;
        _settings = settings;

        // Subscribe to live collection changes
        portfolio.Positions.CollectionChanged += OnCollectionChanged;
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
        if (e.PropertyName == nameof(PortfolioViewModel.TotalCash))
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
        var total = investTotal + cashTotal;
        TotalValue = total;
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
            row.ActualPercent = total > 0 ? Math.Round(item.Value / total * 100m, 1) : 0m;
            row.TargetPercent = targets.TryGetValue(item.Symbol, out var t) ? t : 0m;
            row.EditTargetText = row.TargetPercent.ToString("G");
            allRows.Add(row);
        }

        // Cash row (neutral grey, no target, no P&L)
        if (cashTotal > 0)
        {
            var cashRow = new AllocationRowViewModel("現金", "現金", "cash", cashTotal, 0m, 0m, 0m, CashBrush);
            cashRow.ActualPercent = total > 0 ? Math.Round(cashTotal / total * 100m, 1) : 0m;
            cashRow.TargetPercent = targets.TryGetValue("現金", out var cashTarget) ? cashTarget : 0m;
            cashRow.EditTargetText = cashRow.TargetPercent.ToString("G");
            allRows.Add(cashRow);
        }

        // Final sort (cash may sit anywhere by value)
        allRows.Sort((a, b) => b.MarketValue.CompareTo(a.MarketValue));

        AllocationRows.Clear();
        foreach (var row in allRows)
            AllocationRows.Add(row);

        RebuildBuySell();
    }

    private void RebuildBuySell()
    {
        if (IsFullRebalance)
        {
            foreach (var row in AllocationRows)
            {
                if (row.IsCashRow) { row.SetBuySell(0m, row.ActualPercent); continue; }
                // When no target is set, treat as no-op (don't suggest selling everything)
                var needed = row.TargetPercent > 0
                    ? (row.TargetPercent - row.ActualPercent) / 100m * TotalValue
                    : 0m;
                row.SetBuySell(Math.Round(needed, 0), row.ActualPercent);
            }
            CashFlowTotal = 0m;
            OnPropertyChanged(nameof(CashFlowTotalDisplay));
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
            OnPropertyChanged(nameof(CashFlowTotalDisplay));
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
    }
}
