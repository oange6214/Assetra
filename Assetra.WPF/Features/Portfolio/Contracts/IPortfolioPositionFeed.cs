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
}
