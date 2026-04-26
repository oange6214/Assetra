using Assetra.Core.Dtos;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Assetra.WPF.Features.Portfolio.SubViewModels;

/// <summary>
/// Owns the financial-health metrics shown on Liability and Account summary cards:
/// debt ratio / leverage, paid percent, monthly expense, and emergency-fund runway.
/// Display strings depend on parent <c>TotalAssets</c> / <c>NetWorth</c>, which are
/// supplied via getter callbacks; persistence of <see cref="MonthlyExpense"/> is
/// delegated back to the parent via <see cref="_onMonthlyExpenseChanged"/>.
/// Extracted from <see cref="PortfolioViewModel"/>.
/// </summary>
public sealed partial class FinancialSummaryViewModel : ObservableObject
{
    private readonly Func<decimal> _getTotalAssets;
    private readonly Func<decimal> _getNetWorth;
    private readonly Action<decimal> _onMonthlyExpenseChanged;

    public FinancialSummaryViewModel(
        Func<decimal> getTotalAssets,
        Func<decimal> getNetWorth,
        Action<decimal> onMonthlyExpenseChanged)
    {
        ArgumentNullException.ThrowIfNull(getTotalAssets);
        ArgumentNullException.ThrowIfNull(getNetWorth);
        ArgumentNullException.ThrowIfNull(onMonthlyExpenseChanged);
        _getTotalAssets = getTotalAssets;
        _getNetWorth = getNetWorth;
        _onMonthlyExpenseChanged = onMonthlyExpenseChanged;
    }

    // ── 負債健康度 ──
    /// <summary>負債 / 總資產，0–100 範圍，供 ProgressBar 直接綁定。</summary>
    [ObservableProperty] private decimal _debtRatioValue;

    public string DebtRatioDisplay => _getTotalAssets() > 0 ? $"{DebtRatioValue:F1}%" : "—";

    public string LeverageRatioDisplay
    {
        get
        {
            var nw = _getNetWorth();
            return nw > 0 ? $"{_getTotalAssets() / nw:F2}" : "—";
        }
    }

    public bool IsDebtHealthy => DebtRatioValue < 30m;
    public bool IsDebtWarning => DebtRatioValue is >= 30m and < 50m;
    public bool IsDebtDanger => DebtRatioValue >= 50m;

    /// <summary>"healthy" | "warning" | "danger" | "none" — single binding for XAML color triggers.</summary>
    public string DebtStatusTag => IsDebtDanger ? "danger" : IsDebtWarning ? "warning" : IsDebtHealthy ? "healthy" : "none";

    /// <summary>所有負債的原始借款總額（OriginalAmount = 0 的條目以 Balance 補位）。</summary>
    [ObservableProperty] private decimal _totalOriginalLiabilities;

    /// <summary>已繳百分比（0–100），供 ProgressBar 直接綁定。</summary>
    [ObservableProperty] private decimal _paidPercentValue;

    public string PaidPercentDisplay => $"{PaidPercentValue:F1}%";

    public string TotalOriginalDisplay => $"NT${TotalOriginalLiabilities:N0}";

    // ── 緊急預備金 ──
    /// <summary>每月預估開銷（從 AppSettings 讀取，可由 UI 修改）。</summary>
    [ObservableProperty] private decimal _monthlyExpense;

    partial void OnMonthlyExpenseChanged(decimal value)
    {
        OnPropertyChanged(nameof(IsMonthlyExpenseSet));
        _onMonthlyExpenseChanged(value);
    }

    public bool IsMonthlyExpenseSet => MonthlyExpense > 0;

    /// <summary>可撐幾個月（無上限）。</summary>
    [ObservableProperty] private decimal _emergencyFundMonths;

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

    /// <summary>
    /// Set <see cref="MonthlyExpense"/> without triggering the change callback —
    /// used at startup to seed the value from persisted settings.
    /// </summary>
    public void InitializeMonthlyExpense(decimal value)
    {
        SetProperty(ref _monthlyExpense, value, nameof(MonthlyExpense));
        OnPropertyChanged(nameof(IsMonthlyExpenseSet));
    }

    /// <summary>
    /// Apply summary-service results. Called from the parent after each <c>RebuildTotals</c>
    /// or whenever <see cref="MonthlyExpense"/> changes.
    /// </summary>
    public void Apply(PortfolioSummaryResult summary)
    {
        TotalOriginalLiabilities = summary.TotalOriginalLiabilities;
        DebtRatioValue = summary.DebtRatioValue;
        PaidPercentValue = summary.PaidPercentValue;
        EmergencyFundMonths = summary.EmergencyFundMonths;

        // Notify derived display properties (the backing-field setters above already
        // raise PropertyChanged for the four cached properties themselves)
        OnPropertyChanged(nameof(DebtRatioDisplay));
        OnPropertyChanged(nameof(LeverageRatioDisplay));
        OnPropertyChanged(nameof(IsDebtHealthy));
        OnPropertyChanged(nameof(IsDebtWarning));
        OnPropertyChanged(nameof(IsDebtDanger));
        OnPropertyChanged(nameof(DebtStatusTag));
        OnPropertyChanged(nameof(PaidPercentDisplay));
        OnPropertyChanged(nameof(TotalOriginalDisplay));
        OnPropertyChanged(nameof(EmergencyFundMonthsDisplay));
        OnPropertyChanged(nameof(EmergencyFundBarValue));
        OnPropertyChanged(nameof(IsEmergencySafe));
        OnPropertyChanged(nameof(IsEmergencyWarning));
        OnPropertyChanged(nameof(IsEmergencyDanger));
        OnPropertyChanged(nameof(EmergencyStatusTag));
    }
}
