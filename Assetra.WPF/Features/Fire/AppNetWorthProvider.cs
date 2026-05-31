using Assetra.Application.Portfolio.Contracts;
using Assetra.Application.Portfolio.Dtos;
using Assetra.Core.Interfaces.MultiAsset;
using Assetra.WPF.Features.Portfolio.Contracts;

namespace Assetra.WPF.Features.Fire;

public sealed class AppNetWorthProvider : IAppNetWorthProvider
{
    private readonly IFinancialOverviewQueryService _overviewQuery;
    private readonly IPortfolioPositionFeed _portfolio;
    private readonly IRealEstateValuationService _realEstate;
    private readonly IRetirementProjectionService _retirement;
    private readonly IPhysicalAssetValuationService _physicalAsset;

    public AppNetWorthProvider(
        IFinancialOverviewQueryService overviewQuery,
        IPortfolioPositionFeed portfolio,
        IRealEstateValuationService realEstate,
        IRetirementProjectionService retirement,
        IPhysicalAssetValuationService physicalAsset)
    {
        ArgumentNullException.ThrowIfNull(overviewQuery);
        ArgumentNullException.ThrowIfNull(portfolio);
        ArgumentNullException.ThrowIfNull(realEstate);
        ArgumentNullException.ThrowIfNull(retirement);
        ArgumentNullException.ThrowIfNull(physicalAsset);
        _overviewQuery = overviewQuery;
        _portfolio = portfolio;
        _realEstate = realEstate;
        _retirement = retirement;
        _physicalAsset = physicalAsset;
    }

    public async Task<decimal> GetCurrentNetWorthAsync(CancellationToken ct = default)
    {
        var overview = await _overviewQuery.BuildAsync(
            _portfolio.Positions
                .Select(p => new FinancialOverviewInvestmentItem(
                    p.Id,
                    p.Name,
                    p.Currency,
                    p.MarketValue,
                    p.AssetType))
                .ToList(),
            ct).ConfigureAwait(false);

        var balanceSheetAssets =
            overview.TotalInvestments
            + _portfolio.TotalCash
            + await _realEstate.GetTotalCurrentValueAsync(ct).ConfigureAwait(false)
            + await _retirement.GetTotalBalanceAsync(ct).ConfigureAwait(false)
            + await _physicalAsset.GetTotalCurrentValueAsync(ct).ConfigureAwait(false);

        return balanceSheetAssets - overview.TotalLiabilities;
    }
}
