using System.IO;
using Assetra.Application.Sync;
using Assetra.Core.Models;
using Assetra.Infrastructure.Persistence;
using Assetra.Infrastructure.Sync;
using Xunit;

namespace Assetra.Tests.Application.Sync;

/// <summary>
/// 端到端：兩台「裝置」（兩個本地 SQLite + 各自 queue）共用同一個 in-memory cloud provider，
/// 驗證 add → sync(A) → sync(B) → B 看到 A 的變更（含 tombstone 反向）。
/// </summary>
public sealed class CategoryEndToEndSyncTests : IDisposable
{
    private readonly string _dbA;
    private readonly string _dbB;
    private readonly FixedTime _time = new(DateTimeOffset.Parse("2026-04-28T00:00:00Z"));

    public CategoryEndToEndSyncTests()
    {
        _dbA = Path.Combine(Path.GetTempPath(), $"assetra-e2e-A-{Guid.NewGuid():N}.db");
        _dbB = Path.Combine(Path.GetTempPath(), $"assetra-e2e-B-{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        try { File.Delete(_dbA); } catch { /* best effort */ }
        try { File.Delete(_dbB); } catch { /* best effort */ }
    }

    [Fact]
    public async Task DeviceA_Adds_DeviceB_PullsAndApplies()
    {
        var cloud = new InMemoryCloudSyncProvider();
        var resolver = new LastWriteWinsResolver();

        var repoA = new CategorySqliteRepository(_dbA, "device-A", _time);
        var orchA = new SyncOrchestrator(
            cloud,
            new CategoryLocalChangeQueue(repoA),
            new InMemorySyncMetadataStore("device-A"),
            resolver,
            _time);

        var repoB = new CategorySqliteRepository(_dbB, "device-B", _time);
        var orchB = new SyncOrchestrator(
            cloud,
            new CategoryLocalChangeQueue(repoB),
            new InMemorySyncMetadataStore("device-B"),
            resolver,
            _time);

        var c = new ExpenseCategory(Guid.NewGuid(), "餐飲", CategoryKind.Expense, ColorHex: "#FF8800");
        await repoA.AddAsync(c);

        var pushResult = await orchA.SyncAsync();
        Assert.Equal(1, pushResult.PushedCount);

        var pullResult = await orchB.SyncAsync();
        Assert.Equal(1, pullResult.PulledCount);

        var onB = await repoB.GetByIdAsync(c.Id);
        Assert.NotNull(onB);
        Assert.Equal("餐飲", onB!.Name);
        Assert.Equal("#FF8800", onB.ColorHex);
    }

    [Fact]
    public async Task DeviceA_Deletes_DeviceB_AppliesTombstone()
    {
        var cloud = new InMemoryCloudSyncProvider();
        var resolver = new LastWriteWinsResolver();

        var repoA = new CategorySqliteRepository(_dbA, "device-A", _time);
        var queueA = new CategoryLocalChangeQueue(repoA);
        var orchA = new SyncOrchestrator(cloud, queueA,
            new InMemorySyncMetadataStore("device-A"), resolver, _time);

        var repoB = new CategorySqliteRepository(_dbB, "device-B", _time);
        var queueB = new CategoryLocalChangeQueue(repoB);
        var orchB = new SyncOrchestrator(cloud, queueB,
            new InMemorySyncMetadataStore("device-B"), resolver, _time);

        var c = new ExpenseCategory(Guid.NewGuid(), "to-delete", CategoryKind.Expense);
        await repoA.AddAsync(c);
        await orchA.SyncAsync();
        await orchB.SyncAsync();
        Assert.NotNull(await repoB.GetByIdAsync(c.Id));

        await repoA.RemoveAsync(c.Id);
        await orchA.SyncAsync();
        await orchB.SyncAsync();

        Assert.Null(await repoB.GetByIdAsync(c.Id));
    }

    private sealed class FixedTime : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedTime(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
