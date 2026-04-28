using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Assetra.WPF.Features.Portfolio;

/// <summary>
/// PortfolioViewModel partial — collection-view filters for Positions, Cash, and Liability tabs.
/// </summary>
public partial class PortfolioViewModel
{
    [ObservableProperty] private string _filterText = string.Empty;

    /// <summary>
    /// When true, archived (soft-deleted) positions are shown in the Positions list.
    /// Plan Task 15 + Task 20 (XAML toggle to be wired later).
    /// </summary>
    [ObservableProperty] private bool _showArchivedPositions;

    /// <summary>
    /// When true, positions with zero projected quantity (fully sold-out or 'watchlist')
    /// are hidden from the Positions list.
    /// </summary>
    [ObservableProperty] private bool _hideEmptyPositions;

    // Cash / Liability filters
    [ObservableProperty] private string _cashFilterText = string.Empty;
    [ObservableProperty] private string _liabilityFilterText = string.Empty;

    private ICollectionView? _positionsView;
    public ICollectionView PositionsView
    {
        get
        {
            if (_positionsView is null)
            {
                _positionsView = CollectionViewSource.GetDefaultView(Positions);
                _positionsView.Filter = FilterPosition;
            }
            return _positionsView;
        }
    }

    partial void OnFilterTextChanged(string value) => PositionsView.Refresh();

    private bool FilterPosition(object obj)
    {
        if (obj is not PortfolioRowViewModel row)
            return false;

        if (string.IsNullOrWhiteSpace(FilterText))
            return true;

        return row.Symbol.Contains(FilterText, StringComparison.OrdinalIgnoreCase)
            || row.Name.Contains(FilterText, StringComparison.OrdinalIgnoreCase);
    }

    private ICollectionView? _cashAccountsView;
    public ICollectionView CashAccountsView
    {
        get
        {
            if (_cashAccountsView is null)
            {
                _cashAccountsView = CollectionViewSource.GetDefaultView(CashAccounts);
                _cashAccountsView.Filter = FilterCashAccount;
            }
            return _cashAccountsView;
        }
    }

    partial void OnCashFilterTextChanged(string value) => CashAccountsView.Refresh();

    private bool FilterCashAccount(object obj)
        => obj is CashAccountRowViewModel row
           && (string.IsNullOrEmpty(CashFilterText)
               || row.Name.Contains(CashFilterText, StringComparison.OrdinalIgnoreCase));

    private ICollectionView? _liabilitiesView;
    public ICollectionView LiabilitiesView
    {
        get
        {
            if (_liabilitiesView is null)
            {
                _liabilitiesView = CollectionViewSource.GetDefaultView(Liabilities);
                _liabilitiesView.Filter = FilterLiability;
            }
            return _liabilitiesView;
        }
    }

    partial void OnLiabilityFilterTextChanged(string value) => LiabilitiesView.Refresh();

    private bool FilterLiability(object obj)
        => obj is LiabilityRowViewModel row
           && (string.IsNullOrEmpty(LiabilityFilterText)
               || row.Name.Contains(LiabilityFilterText, StringComparison.OrdinalIgnoreCase));

    partial void OnShowArchivedPositionsChanged(bool value) => _ = LoadPositionsAsync();
    partial void OnHideEmptyPositionsChanged(bool value) => _ = LoadPositionsAsync();

    /// <summary>Called from DividendCalendarPanel when a month cell is clicked.</summary>
    [RelayCommand]
    private void FilterByDividendMonth(int month)
    {
        TradeFilter.FilterByDividendMonth(month, DivCalendar.Year);
    }
}
