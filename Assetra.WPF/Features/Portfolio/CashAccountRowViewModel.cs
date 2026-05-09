using Assetra.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Assetra.WPF.Features.Portfolio;

/// <summary>
/// 現金帳戶列。餘額由 <c>IBalanceQueryService</c> 投影而來，於載入時注入。
/// Wave 9：新增 <see cref="IsActive"/> 供封存篩選使用。
/// </summary>
public sealed partial class CashAccountRowViewModel : ObservableObject
{
    public Guid Id { get; }
    public DateOnly CreatedDate { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BalanceAsMoney))]
    private string _name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BalanceAsMoney))]
    private decimal _balance;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BalanceAsMoney))]
    private string _currency = "TWD";

    [ObservableProperty] private bool _isDefault;
    [ObservableProperty] private bool _isActive;

    /// <summary>
    /// M1 — currency-tagged accessor for this row's balance. Use this for any
    /// cross-row aggregation that must respect currency boundaries (e.g.,
    /// ConcentrationAnalyzer, multi-currency portfolio summaries). Decimal
    /// <see cref="Balance"/> stays the primary XAML binding for backward compat.
    /// </summary>
    public Money BalanceAsMoney => new(Balance, string.IsNullOrWhiteSpace(Currency) ? "TWD" : Currency);

    public void NotifyCurrencyChanged()
    {
        OnPropertyChanged(nameof(Balance));
        OnPropertyChanged(nameof(BalanceAsMoney));
    }

    public CashAccountRowViewModel(AssetItem a, decimal projectedBalance)
    {
        Id = a.Id;
        _name = a.Name;
        _balance = projectedBalance;
        _currency = a.Currency;
        CreatedDate = a.CreatedDate;
        _isActive = a.IsActive;
    }

    public override string ToString() => Name;
}
