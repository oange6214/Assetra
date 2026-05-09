using System.ComponentModel;

namespace Assetra.WPF.Features.Portfolio.Contracts;

/// <summary>
/// Read-only window onto the live portfolio state needed by ancillary panels
/// (allocation, dashboard, financial overview). <see cref="PortfolioViewModel"/>
/// implements this so consumers can be unit-tested against a stub feed instead
/// of having to construct the full Portfolio VM with its 30-dependency graph
/// (see <c>docs/planning/H3-PortfolioViewModelFactory-Plan.md</c>).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Positions"/> is typed as <see cref="IReadOnlyList{T}"/> for the
/// callers that only need to enumerate. The concrete instance is also
/// <see cref="System.Collections.Specialized.INotifyCollectionChanged"/>
/// (production implementation backs it with an <c>ObservableCollection&lt;T&gt;</c>);
/// consumers that need to react to add / remove / replace events should
/// pattern-match: <c>if (Positions is INotifyCollectionChanged ncc) …</c>.
/// </para>
/// <para>
/// Property-change notifications for <see cref="TotalCash"/> arrive via the
/// inherited <see cref="INotifyPropertyChanged"/> contract.
/// </para>
/// </remarks>
public interface IPortfolioPositionFeed : INotifyPropertyChanged
{
    IReadOnlyList<PortfolioRowViewModel> Positions { get; }

    decimal TotalCash { get; }

    /// <summary>
    /// Sum of all live position market values in the portfolio's base currency.
    /// Updated asynchronously after each quote-fetch cycle; consumers that need to
    /// re-snapshot when prices land subscribe to <see cref="INotifyPropertyChanged"/>
    /// and filter on this property's name.
    /// </summary>
    decimal TotalMarketValue { get; }

    /// <summary>
    /// Sum of all position cost-bases in the portfolio's base currency.
    /// Used together with <see cref="TotalMarketValue"/> to compute investment
    /// P&amp;L (TotalMarketValue - TotalCost). Updates with the same cadence as
    /// TotalMarketValue.
    /// </summary>
    decimal TotalCost { get; }

    // L3 read-side full — derived totals proxied by Dashboard / FinancialOverview.
    // Default-implemented so existing test stubs that only care about Positions /
    // TotalCash / TotalMarketValue / TotalCost don't need to implement these
    // (they get sensible zeroes). The concrete PortfolioViewModel overrides all
    // with real values; consumers that depend on these (Dashboard) get the live
    // numbers without taking a hard reference to PortfolioViewModel.
    decimal TotalPnl              => 0m;
    decimal TotalPnlPercent       => 0m;
    bool    IsTotalPositive       => false;
    decimal TotalAssets           => 0m;
    decimal TotalLiabilities      => 0m;
    decimal NetWorth              => 0m;
    decimal DayPnl                => 0m;
    string  DayPnlPercentDisplay  => string.Empty;
    bool    IsDayPnlPositive      => false;
    bool    HasDayPnl             => false;
}
