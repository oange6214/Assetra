using Assetra.AppLayer.Portfolio.Contracts;
using Assetra.AppLayer.Portfolio.Dtos;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.AppLayer.Portfolio.Services;

public sealed class PortfolioLoadService : IPortfolioLoadService
{
    private readonly IPortfolioRepository _portfolioRepository;
    private readonly IPositionQueryService _positionQueryService;
    private readonly ITradeRepository _tradeRepository;
    private readonly IBalanceQueryService _balanceQueryService;
    private readonly IAssetRepository? _assetRepository;

    public PortfolioLoadService(
        IPortfolioRepository portfolioRepository,
        IPositionQueryService positionQueryService,
        ITradeRepository tradeRepository,
        IBalanceQueryService balanceQueryService,
        IAssetRepository? assetRepository = null)
    {
        _portfolioRepository = portfolioRepository;
        _positionQueryService = positionQueryService;
        _tradeRepository = tradeRepository;
        _balanceQueryService = balanceQueryService;
        _assetRepository = assetRepository;
    }

    public async Task<PortfolioLoadResult> LoadAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var entriesTask = _portfolioRepository.GetEntriesAsync();
        var snapshotsTask = _positionQueryService.GetAllPositionSnapshotsAsync();
        var tradesTask = _tradeRepository.GetAllAsync();
        var cashBalancesTask = _balanceQueryService.GetAllCashBalancesAsync();
        var liabilitySnapshotsTask = _balanceQueryService.GetAllLiabilitySnapshotsAsync();

        Task<IReadOnlyList<AssetItem>> cashAccountsTask = Task.FromResult<IReadOnlyList<AssetItem>>([]);
        Task<IReadOnlyList<AssetItem>> liabilityAssetsTask = Task.FromResult<IReadOnlyList<AssetItem>>([]);

        if (_assetRepository is not null)
        {
            cashAccountsTask = _assetRepository.GetItemsByTypeAsync(FinancialType.Asset);
            liabilityAssetsTask = _assetRepository.GetItemsByTypeAsync(FinancialType.Liability);
        }

        await Task.WhenAll(
            entriesTask,
            snapshotsTask,
            tradesTask,
            cashAccountsTask,
            cashBalancesTask,
            liabilitySnapshotsTask,
            liabilityAssetsTask).ConfigureAwait(false);

        var loanAssets = liabilityAssetsTask.Result
            .Where(a => a.IsLoan)
            .ToDictionary(a => a.Name, StringComparer.Ordinal);

        return new PortfolioLoadResult(
            entriesTask.Result,
            snapshotsTask.Result,
            tradesTask.Result,
            cashAccountsTask.Result,
            cashBalancesTask.Result,
            liabilitySnapshotsTask.Result,
            loanAssets);
    }
}
