using Assetra.Application.Portfolio.Contracts;
using Assetra.Application.Portfolio.Dtos;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.Application.Portfolio.Services;

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

        var entries = await entriesTask.ConfigureAwait(false);
        var snapshots = await snapshotsTask.ConfigureAwait(false);
        var trades = await tradesTask.ConfigureAwait(false);
        var cashAccounts = await cashAccountsTask.ConfigureAwait(false);
        var cashBalances = await cashBalancesTask.ConfigureAwait(false);
        var liabilitySnapshots = await liabilitySnapshotsTask.ConfigureAwait(false);
        var liabilityAssetItems = await liabilityAssetsTask.ConfigureAwait(false);

        var liabilityAssets = liabilityAssetItems
            .ToDictionary(a => a.Name, StringComparer.Ordinal);

        return new PortfolioLoadResult(
            entries,
            snapshots,
            trades,
            cashAccounts,
            cashBalances,
            liabilitySnapshots,
            liabilityAssets);
    }
}
