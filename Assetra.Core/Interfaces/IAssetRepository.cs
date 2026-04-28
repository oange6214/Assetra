using Assetra.Core.Models;

namespace Assetra.Core.Interfaces;

/// <summary>
/// Unified repository for the three-tier financial hierarchy:
/// FinancialType → AssetGroup → AssetItem, plus asset events.
/// </summary>
public interface IAssetRepository
{
    // ── Groups ───────────────────────────────────────────────────────────────
    Task<IReadOnlyList<AssetGroup>> GetGroupsAsync();
    Task                            AddGroupAsync(AssetGroup group);
    Task                            UpdateGroupAsync(AssetGroup group);
    Task                            DeleteGroupAsync(Guid id);

    // ── Items ────────────────────────────────────────────────────────────────
    Task<IReadOnlyList<AssetItem>>  GetItemsAsync();
    Task<IReadOnlyList<AssetItem>>  GetItemsByTypeAsync(FinancialType type);
    Task<AssetItem?>                GetByIdAsync(Guid id);
    Task                            AddItemAsync(AssetItem item);
    Task                            UpdateItemAsync(AssetItem item);
    Task                            DeleteItemAsync(Guid id);
    Task<Guid>                      FindOrCreateAccountAsync(string name, string currency, CancellationToken ct = default);
    Task                            ArchiveItemAsync(Guid id);
    /// <summary>Count of Trade rows referencing <paramref name="id"/>. 0 means safe to hard-delete.</summary>
    Task<int>                       HasTradeReferencesAsync(Guid id, CancellationToken ct = default);

    // ── Events ───────────────────────────────────────────────────────────────
    Task<IReadOnlyList<AssetEvent>> GetEventsAsync(Guid assetId);
    Task                            AddEventAsync(AssetEvent evt);
    Task                            DeleteEventAsync(Guid id);

    /// <summary>Returns the most recent Valuation event for <paramref name="assetId"/>, or null.</summary>
    Task<AssetEvent?>               GetLatestValuationAsync(Guid assetId);

    /// <summary>
    /// 批次取得多個 asset 的最新 Valuation event。預設實作 fallback 為逐筆呼叫 <see cref="GetLatestValuationAsync"/>，
    /// SQLite 實作 override 為單一 SQL 透過 <c>(asset_id, MAX(event_date)) GROUP BY</c>，避免 N+1 round-trip。
    /// 沒有 Valuation 的 asset 不會出現在結果字典中。
    /// </summary>
    async Task<IReadOnlyDictionary<Guid, AssetEvent>> GetLatestValuationsAsync(
        IEnumerable<Guid> assetIds, CancellationToken ct = default)
    {
        var result = new Dictionary<Guid, AssetEvent>();
        foreach (var id in assetIds.Distinct())
        {
            ct.ThrowIfCancellationRequested();
            var evt = await GetLatestValuationAsync(id).ConfigureAwait(false);
            if (evt is not null) result[id] = evt;
        }
        return result;
    }
}
