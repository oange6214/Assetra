using System.Windows.Media;
using Assetra.WPF.Infrastructure;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Assetra.WPF.Features.Portfolio.Controls;

/// <summary>Single asset row for the allocation overview and rebalance table.</summary>
public sealed partial class AllocationRowViewModel : ObservableObject
{
    // Identity
    public string Symbol { get; }
    public string Name { get; }
    public string AssetCategory { get; }  // "stock" | "fund" | "crypto" | "bond" | "metal" | "cash"
    public bool IsCashRow => AssetCategory == "cash";

    // Market data (updated live via Refresh)
    [ObservableProperty] private decimal _marketValue;   // TWD
    [ObservableProperty] private decimal _currentPrice;  // per share/unit; 0 for cash
    [ObservableProperty] private decimal _pnl;
    [ObservableProperty] private decimal _pnlPercent;
    [ObservableProperty] private bool _isPnlPositive;

    partial void OnMarketValueChanged(decimal _) => RebuildDerived();
    partial void OnPnlPercentChanged(decimal _)
    {
        OnPropertyChanged(nameof(PnlColorBrush));
        OnPropertyChanged(nameof(PnlPercentTreemap));
    }

    /// <summary>
    /// Invoked by <see cref="AllocationViewModel"/> when the user toggles Taiwan/International
    /// convention or switches theme — forces the treemap tile to re-query <see cref="PnlColorBrush"/>.
    /// </summary>
    public void NotifyPnlColorChanged() => OnPropertyChanged(nameof(PnlColorBrush));

    /// <summary>
    /// Treemap tile color reflecting gain/loss magnitude. Uses the shared
    /// <see cref="PnlColorPalette"/>, which respects
    /// <see cref="ColorSchemeService.TaiwanConvention"/> and user's theme preference.
    /// </summary>
    public SolidColorBrush PnlColorBrush =>
        PnlColorPalette.PickGradient((double)PnlPercent, isNeutralRow: AssetCategory == "cash");

    // Allocation
    [ObservableProperty] private decimal _actualPercent;   // 0–100
    [ObservableProperty] private decimal _targetPercent;   // 0–100, from settings

    partial void OnTargetPercentChanged(decimal _) => RebuildDerived();
    partial void OnActualPercentChanged(decimal _)
    {
        RebuildDerived();
        OnPropertyChanged(nameof(ActualPercentWeight));
    }

    /// <summary>
    /// Double-typed weight for TreemapPanel binding.
    /// WPF's default value converter does NOT convert decimal → double for DependencyProperty
    /// binding, so we expose a typed property here (otherwise every tile falls back to the
    /// default weight 1.0 and all tiles end up equal-sized).
    /// </summary>
    public double ActualPercentWeight => (double)ActualPercent;

    public decimal Deviation { get; private set; }  // Actual − Target (signed); 0 when no target
    public decimal BuySellAmount { get; private set; }  // TWD; positive = buy, negative = sell
    public decimal AfterPercent { get; private set; }  // projected % after cash-flow buy
    public bool IsBuy { get; private set; }
    public bool IsSell { get; private set; }
    public bool IsFlat { get; private set; }
    public bool HasTarget { get; private set; }  // TargetPercent > 0

    // Scaled 0–100 for the deviation bar: ±25 pp maps to full width
    public decimal DeviationBarPct => HasTarget ? Math.Min(Math.Abs(Deviation) * 4m, 100m) : 0m;

    public string DeviationDisplay => HasTarget
        ? (Deviation >= 0 ? $"+{Deviation:F1}%" : $"{Deviation:F1}%")
        : "—";
    public string AfterPercentDisplay => $"{AfterPercent:F1}%";
    public string BuySellDisplay
    {
        get
        {
            if (!HasTarget)
                return "—";
            if (IsFlat)
                return "無動作";
            var abs = Math.Abs(BuySellAmount);
            var label = IsBuy ? "買入" : "賣出";
            return $"{label} NT${abs:N0}";
        }
    }

    // Visual
    public SolidColorBrush ColorBrush { get; }
    public string ActualPercentDisplay => $"{ActualPercent:F1}%";
    public string PnlDisplay => Pnl == 0 && AssetCategory == "cash"
        ? "-"
        : (IsPnlPositive ? "+" : "") + $"NT${Pnl:N0}";
    public string PnlPercentDisplay => Pnl == 0 && AssetCategory == "cash"
        ? ""
        : $"({(IsPnlPositive ? "+" : "")}{PnlPercent:F2}%)";

    /// <summary>Treemap-style gain display without parens, e.g. "+3.40%" or "-1.20%".</summary>
    public string PnlPercentTreemap => AssetCategory == "cash"
        ? ""
        : $"{(IsPnlPositive ? "+" : "")}{PnlPercent:F2}%";

    // Target editing (temporary, before saving)
    [ObservableProperty] private string _editTargetText = string.Empty;

    // Ctor
    public AllocationRowViewModel(
        string symbol, string name, string assetCategory,
        decimal marketValue, decimal currentPrice,
        decimal pnl, decimal pnlPercent,
        SolidColorBrush colorBrush)
    {
        Symbol = symbol;
        Name = name;
        AssetCategory = assetCategory;
        _marketValue = marketValue;
        _currentPrice = currentPrice;
        _pnl = pnl;
        _pnlPercent = pnlPercent;
        _isPnlPositive = pnl >= 0;
        ColorBrush = colorBrush;
    }

    /// <summary>Called by AllocationViewModel after computing buy/sell amounts.</summary>
    public void RebuildDerived()
    {
        HasTarget = TargetPercent > 0;
        Deviation = HasTarget ? ActualPercent - TargetPercent : 0m;
        IsBuy = HasTarget && BuySellAmount > 0.5m;
        IsSell = HasTarget && BuySellAmount < -0.5m;
        IsFlat = !IsBuy && !IsSell;
        OnPropertyChanged(nameof(HasTarget));
        OnPropertyChanged(nameof(Deviation));
        OnPropertyChanged(nameof(DeviationDisplay));
        OnPropertyChanged(nameof(DeviationBarPct));
        OnPropertyChanged(nameof(BuySellAmount));
        OnPropertyChanged(nameof(BuySellDisplay));
        OnPropertyChanged(nameof(AfterPercent));
        OnPropertyChanged(nameof(AfterPercentDisplay));
        OnPropertyChanged(nameof(IsBuy));
        OnPropertyChanged(nameof(IsSell));
        OnPropertyChanged(nameof(IsFlat));
    }

    /// <summary>Full rebalance: buy or sell.</summary>
    public void SetBuySell(decimal buySellTwd, decimal beforePct)
    {
        BuySellAmount = buySellTwd;
        AfterPercent = beforePct; // unchanged in full mode; VM sets to TargetPercent if desired
        RebuildDerived();
    }

    /// <summary>Cash-flow rebalance: buy only, with projected after %.</summary>
    public void SetBuySell(decimal buySellTwd, decimal beforePct, decimal afterPct)
    {
        BuySellAmount = buySellTwd;
        AfterPercent = afterPct;
        RebuildDerived();
    }
}
