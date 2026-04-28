using Assetra.Core.Models;

namespace Assetra.Core.Interfaces;

public interface IPortfolioRepository
{
    Task<IReadOnlyList<PortfolioEntry>> GetEntriesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<PortfolioEntry>> GetActiveAsync(CancellationToken ct = default);
    Task AddAsync(PortfolioEntry entry, CancellationToken ct = default);
    Task UpdateAsync(PortfolioEntry entry, CancellationToken ct = default);
    Task UpdateMetadataAsync(Guid id, string displayName, string currency, CancellationToken ct = default);
    Task RemoveAsync(Guid id, CancellationToken ct = default);
    Task<Guid> FindOrCreatePortfolioEntryAsync(
        string symbol, string exchange,
        string? displayName, AssetType assetType,
        string? currency = null,
        bool isEtf = false,
        CancellationToken ct = default);
    Task ArchiveAsync(Guid id, CancellationToken ct = default);
    Task UnarchiveAsync(Guid id, CancellationToken ct = default);
    /// <summary>Count of Trade rows referencing <paramref name="id"/> via <c>portfolio_entry_id</c>.</summary>
    Task<int> HasTradeReferencesAsync(Guid id, CancellationToken ct = default);
}
