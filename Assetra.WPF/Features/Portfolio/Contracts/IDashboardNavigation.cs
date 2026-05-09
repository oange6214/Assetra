namespace Assetra.WPF.Features.Portfolio.Contracts;

/// <summary>
/// L3 — abstracts the Dashboard's writeback to the parent <see cref="PortfolioViewModel"/>.
/// Previously Dashboard called <c>_portfolio.SelectedTab = ...</c> directly, leaking
/// child→parent control flow. This interface narrows that to a single navigation method.
/// </summary>
/// <remarks>
/// Read-side proxies (NetWorth / TotalAssets / etc.) still go through the concrete
/// PortfolioViewModel for now — extending <see cref="IPortfolioPositionFeed"/> with 15+
/// derived totals is a separate refactor cluster (tracked as L3-readside-followup).
/// </remarks>
public interface IDashboardNavigation
{
    /// <summary>Switches the active Portfolio tab (Dashboard / Positions / Trades / etc.).</summary>
    void NavigateTo(PortfolioTab tab);
}
