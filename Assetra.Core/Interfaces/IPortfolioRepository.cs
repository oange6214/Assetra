using Assetra.Core.Models;

namespace Assetra.Core.Interfaces;

public interface IPortfolioRepository
{
    Task<IReadOnlyList<PortfolioEntry>> GetEntriesAsync();
    Task<IReadOnlyList<PortfolioEntry>> GetActiveAsync();  // new — is_active = 1
    Task AddAsync(PortfolioEntry entry);
    Task UpdateAsync(PortfolioEntry entry);
    Task UpdateMetadataAsync(Guid id, string displayName, string currency);
    Task RemoveAsync(Guid id);
    Task<Guid> FindOrCreatePortfolioEntryAsync(
        string symbol, string exchange,
        string? displayName, AssetType assetType,
        CancellationToken ct = default);
    Task ArchiveAsync(Guid id);
    /// <summary>Count of Trade rows referencing <paramref name="id"/> via <c>portfolio_entry_id</c>.</summary>
    Task<int> HasTradeReferencesAsync(Guid id, CancellationToken ct = default);
}
