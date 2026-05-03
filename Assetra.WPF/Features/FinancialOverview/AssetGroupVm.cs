using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Assetra.WPF.Features.FinancialOverview;

/// <summary>UI representation of an asset group row in the accordion.</summary>
public sealed partial class AssetGroupVm : ObservableObject
{
    public string Icon { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Currency { get; init; } = "TWD";

    [ObservableProperty] private decimal _subtotal;

    public string SubtotalDisplay => MoneyFormatter.Format(Subtotal, Currency);
    partial void OnSubtotalChanged(decimal _) => OnPropertyChanged(nameof(SubtotalDisplay));

    public ObservableCollection<AssetItemVm> Items { get; } = [];
}
