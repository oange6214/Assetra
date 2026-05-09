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

    private readonly ObservableCollection<AssetItemVm> _items = [];
    public ReadOnlyObservableCollection<AssetItemVm> Items { get; }

    public AssetGroupVm()
    {
        Items = new ReadOnlyObservableCollection<AssetItemVm>(_items);
    }

    /// <summary>
    /// Builder used by <c>FinancialOverviewViewModel.ToGroupVm</c> — keeps mutation
    /// inside the type (callers use <see cref="AddItem"/> instead of touching the
    /// backing list directly).
    /// </summary>
    public void AddItem(AssetItemVm item) => _items.Add(item);
}
