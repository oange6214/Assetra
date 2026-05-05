using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Assetra.WPF.Features.Portfolio.SubViewModels;

/// <summary>
/// Owns the dividend-calendar panel state: selected year, expanded flag,
/// the 12 month cells (rebuilt whenever <see cref="Year"/> or <see cref="_trades"/>
/// change), and aggregation of cash-dividend trades by month.
/// Extracted from <see cref="PortfolioViewModel"/>; the panel is a pure
/// data-bound view (L6 — no procedural code-behind).
/// </summary>
public sealed partial class DividendCalendarViewModel : ObservableObject
{
    private readonly ObservableCollection<TradeRowViewModel> _trades;
    private readonly ObservableCollection<DividendCalendarCellViewModel> _cells = [];

    public DividendCalendarViewModel(ObservableCollection<TradeRowViewModel> trades)
    {
        ArgumentNullException.ThrowIfNull(trades);
        _trades = trades;
        Cells = new ReadOnlyObservableCollection<DividendCalendarCellViewModel>(_cells);
        _trades.CollectionChanged += OnTradesChanged;
        Rebuild();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(YearTotalDisplay))]
    [NotifyPropertyChangedFor(nameof(HasAnyDividends))]
    private int _year = DateTime.Today.Year;

    [ObservableProperty] private bool _isExpanded = true;

    public ReadOnlyObservableCollection<DividendCalendarCellViewModel> Cells { get; }

    [ObservableProperty] private decimal _yearTotal;
    public string YearTotalDisplay => YearTotal > 0
        ? string.Format(CultureInfo.InvariantCulture, "合計 {0:N0}", YearTotal)
        : string.Empty;

    /// <summary>Hides the entire panel when the user has never recorded a cash-dividend trade.</summary>
    [ObservableProperty] private bool _hasAnyDividends;

    [RelayCommand]
    private void PrevYear() => Year--;

    [RelayCommand]
    private void NextYear() => Year++;

    partial void OnYearChanged(int oldValue, int newValue) => Rebuild();

    private void OnTradesChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    private void Rebuild()
    {
        var monthlyTotals = GetDividendsByMonth(Year);
        HasAnyDividends = _trades.Any(static t => t.IsCashDividend);
        YearTotal = monthlyTotals.Values.Sum();
        OnPropertyChanged(nameof(YearTotalDisplay));

        _cells.Clear();
        for (int m = 1; m <= 12; m++)
        {
            var total = monthlyTotals.TryGetValue(m, out var v) ? v : 0m;
            var label = new DateTime(Year, m, 1).ToString("MMM", CultureInfo.CurrentCulture);
            _cells.Add(new DividendCalendarCellViewModel(m, label, total));
        }
    }

    /// <summary>Sum of cash-dividend amounts grouped by month for the given year.</summary>
    public IReadOnlyDictionary<int, decimal> GetDividendsByMonth(int year) =>
        _trades
            .Where(t => t.IsCashDividend && t.TradeDate.Year == year)
            .GroupBy(t => t.TradeDate.Month)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.CashAmount ?? 0));
}

/// <summary>
/// One month cell in the dividend calendar. Immutable per construction;
/// the parent VM rebuilds the whole list on year/trade change.
/// </summary>
public sealed record DividendCalendarCellViewModel(int Month, string Label, decimal Total)
{
    public bool HasData => Total > 0m;
    public string TotalDisplay => Total.ToString("N0", CultureInfo.InvariantCulture);
}
