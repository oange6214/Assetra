using System.Collections.Concurrent;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Xunit;

namespace Assetra.Tests.WPF;

/// <summary>
/// Lightweight in-memory fake for IAssetRepository — only the surface Task-14 tests touch.
/// </summary>
internal sealed class InMemoryAssetRepo : IAssetRepository
{
    public ConcurrentDictionary<Guid, AssetItem> Items { get; } = new();
    public HashSet<Guid> HardDeleted { get; } = new();
    public int HasTradeReferencesResult { get; set; }

    public Task<IReadOnlyList<AssetItem>> GetItemsAsync()
        => Task.FromResult<IReadOnlyList<AssetItem>>(Items.Values.ToList());

    public Task<IReadOnlyList<AssetItem>> GetItemsByTypeAsync(FinancialType type)
        => Task.FromResult<IReadOnlyList<AssetItem>>(
            Items.Values.Where(i => i.Type == type).ToList());

    public Task<AssetItem?> GetByIdAsync(Guid id)
        => Task.FromResult<AssetItem?>(Items.TryGetValue(id, out var v) ? v : null);

    public Task AddItemAsync(AssetItem item)
    { Items[item.Id] = item; return Task.CompletedTask; }

    public Task UpdateItemAsync(AssetItem item)
    { Items[item.Id] = item; return Task.CompletedTask; }

    public Task DeleteItemAsync(Guid id)
    { Items.TryRemove(id, out _); HardDeleted.Add(id); return Task.CompletedTask; }

    public Task ArchiveItemAsync(Guid id)
    {
        if (Items.TryGetValue(id, out var v))
            Items[id] = v with { IsActive = false };
        return Task.CompletedTask;
    }

    public Task<int> HasTradeReferencesAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(HasTradeReferencesResult);

    public Task<Guid> FindOrCreateAccountAsync(string name, string currency, CancellationToken ct = default)
    {
        var match = Items.Values.FirstOrDefault(
            i => i.Type == FinancialType.Asset && i.Name == name && i.Currency == currency);
        if (match is not null) return Task.FromResult(match.Id);
        var id = Guid.NewGuid();
        Items[id] = new AssetItem(id, name, FinancialType.Asset, null, currency,
            DateOnly.FromDateTime(DateTime.UtcNow), true, DateTime.UtcNow);
        return Task.FromResult(id);
    }

    // Unused for these tests — compile-time required.
    public Task<IReadOnlyList<AssetGroup>> GetGroupsAsync()
        => Task.FromResult<IReadOnlyList<AssetGroup>>(Array.Empty<AssetGroup>());
    public Task AddGroupAsync(AssetGroup group) => Task.CompletedTask;
    public Task UpdateGroupAsync(AssetGroup group) => Task.CompletedTask;
    public Task DeleteGroupAsync(Guid id) => Task.CompletedTask;
    public Task<IReadOnlyList<AssetEvent>> GetEventsAsync(Guid assetId)
        => Task.FromResult<IReadOnlyList<AssetEvent>>(Array.Empty<AssetEvent>());
    public Task AddEventAsync(AssetEvent evt) => Task.CompletedTask;
    public Task DeleteEventAsync(Guid id) => Task.CompletedTask;
    public Task<AssetEvent?> GetLatestValuationAsync(Guid assetId) => Task.FromResult<AssetEvent?>(null);
}

public sealed class PortfolioViewModelArchiveTests
{
    [Fact]
    public async Task ArchiveItemAsync_SetsIsActiveFalse_InRepo()
    {
        var repo = new InMemoryAssetRepo();
        var id = Guid.NewGuid();
        repo.Items[id] = new AssetItem(
            id, "Target", FinancialType.Asset, null, "TWD",
            DateOnly.FromDateTime(DateTime.UtcNow), true, DateTime.UtcNow);

        await repo.ArchiveItemAsync(id);

        Assert.False(repo.Items[id].IsActive);
    }

    [Fact]
    public async Task HasTradeReferences_NonZero_PreservesItem()
    {
        var repo = new InMemoryAssetRepo { HasTradeReferencesResult = 3 };
        var id = Guid.NewGuid();
        repo.Items[id] = new AssetItem(
            id, "Ref", FinancialType.Asset, null, "TWD",
            DateOnly.FromDateTime(DateTime.UtcNow), true, DateTime.UtcNow);

        var count = await repo.HasTradeReferencesAsync(id);

        Assert.Equal(3, count);
        Assert.True(repo.Items.ContainsKey(id), "Must not hard-delete when refs exist");
    }
}
