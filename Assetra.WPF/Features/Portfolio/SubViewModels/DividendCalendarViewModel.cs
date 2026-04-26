using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Assetra.WPF.Features.Portfolio.SubViewModels;

/// <summary>
/// Owns the dividend-calendar panel state: selected year, expanded flag,
/// and aggregation of cash-dividend trades by month.
/// Extracted from <see cref="PortfolioViewModel"/>.
/// </summary>
public sealed partial class DividendCalendarViewModel : ObservableObject
{
    private readonly ObservableCollection<TradeRowViewModel> _trades;

    public DividendCalendarViewModel(ObservableCollection<TradeRowViewModel> trades)
    {
        ArgumentNullException.ThrowIfNull(trades);
        _trades = trades;
    }

    [ObservableProperty] private int _year = DateTime.Today.Year;

    [ObservableProperty] private bool _isExpanded = true;

    [RelayCommand]
    private void PrevYear() => Year--;

    [RelayCommand]
    private void NextYear() => Year++;

    /// <summary>Sum of cash-dividend amounts grouped by month for the given year.</summary>
    public IReadOnlyDictionary<int, decimal> GetDividendsByMonth(int year) =>
        _trades
            .Where(t => t.IsCashDividend && t.TradeDate.Year == year)
            .GroupBy(t => t.TradeDate.Month)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.CashAmount ?? 0));
}
