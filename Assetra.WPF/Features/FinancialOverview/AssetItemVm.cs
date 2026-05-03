using CommunityToolkit.Mvvm.ComponentModel;

namespace Assetra.WPF.Features.FinancialOverview;

/// <summary>UI representation of a single asset item row in the accordion.</summary>
public sealed partial class AssetItemVm : ObservableObject
{
    public Guid   Id       { get; init; }
    public string Name     { get; init; } = string.Empty;
    public string Currency { get; init; } = "TWD";

    [ObservableProperty] private decimal _currentValue;

    public string CurrentValueDisplay => MoneyFormatter.Format(CurrentValue, Currency);
    partial void OnCurrentValueChanged(decimal _) => OnPropertyChanged(nameof(CurrentValueDisplay));
}
