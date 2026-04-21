using Assetra.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Assetra.WPF.Features.Alerts;

public partial class AlertRowViewModel : ObservableObject
{
    public Guid Id { get; init; }
    public string Symbol { get; init; } = string.Empty;
    public string Exchange { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConditionText))]
    private AlertCondition _condition;

    [ObservableProperty] private decimal _targetPrice;

    public string ConditionText => Condition == AlertCondition.Above ? "突破" : "跌破";

    [ObservableProperty] private decimal _currentPrice;
    [ObservableProperty] private bool _isTriggered;
    [ObservableProperty] private string _triggeredAt = string.Empty;

    // Inline edit state
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private string _editCondition = string.Empty;
    [ObservableProperty] private string _editTargetPrice = string.Empty;
    [ObservableProperty] private string _editError = string.Empty;

    public IReadOnlyList<string> Conditions { get; } = ["突破", "跌破"];

    [RelayCommand]
    private void BeginEdit()
    {
        EditCondition = ConditionText;
        EditTargetPrice = TargetPrice.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        EditError = string.Empty;
        IsEditing = true;
    }

    [RelayCommand]
    private void CancelEdit()
    {
        EditError = string.Empty;
        IsEditing = false;
    }

    /// <summary>
    /// 貨幣切換時由 AlertsViewModel 呼叫，強制金額欄位重新格式化。
    /// </summary>
    public void NotifyCurrencyChanged()
    {
        OnPropertyChanged(nameof(TargetPrice));
        OnPropertyChanged(nameof(CurrentPrice));
    }

    public AlertRule ToRule() => new(
        Id, Symbol, Exchange, Condition, TargetPrice,
        IsTriggered,
        IsTriggered ? DateTimeOffset.Now : null);
}
