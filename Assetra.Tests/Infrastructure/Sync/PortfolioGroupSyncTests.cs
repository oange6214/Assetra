using System.IO;
using Assetra.Core.Models;
using Assetra.Core.Models.Sync;
using Assetra.Infrastructure.Persistence;
using Assetra.Infrastructure.Sync;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Assetra.Tests.Infrastructure.Sync;

/// <summary>
/// Sync-Goal-PortfolioGroup pass — covers the PortfolioGroup sync surface.
/// Includes the system-protected-group guard against remote tombstones.
/// </summary>
public sealed class PortfolioGroupSyncTests : IDisposable
{
    private readonly string _dbPath;

    public PortfolioGroupSyncTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"pgroup_sync_{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    [Fact]
    public void EnvelopeRoundTrip_PreservesAllFields()
    {
        var original = new PortfolioGroup(
            Guid.NewGuid(),
            "退休帳戶",
            ColorHex: "#3B82F6",
            Description: "FIRE 長期帳戶",
            IconKey: "PersonClock24",
            SortOrder: 2,
            DefaultCashAccountId: Guid.NewGuid(),
            IsSystem: false);

        var version = new EntityVersion(3, DateTimeOffset.UtcNow, "deviceB");
        var envelope = PortfolioGroupSyncMapper.ToEnvelope(original, version, isDeleted: false);
        var decoded = PortfolioGroupSyncMapper.FromPayload(envelope);

        Assert.Equal(original.Id, decoded.Id);
        Assert.Equal(original.Name, decoded.Name);
        Assert.Equal(original.ColorHex, decoded.ColorHex);
        Assert.Equal(original.Description, decoded.Description);
        Assert.Equal(original.IconKey, decoded.IconKey);
        Assert.Equal(original.SortOrder, decoded.SortOrder);
        Assert.Equal(original.DefaultCashAccountId, decoded.DefaultCashAccountId);
        Assert.Equal(original.IsSystem, decoded.IsSystem);
    }

    [Fact]
    public async Task AddAsync_MarksPendingPush()
    {
        var repo = new PortfolioGroupSqliteRepository(_dbPath);
        var group = new PortfolioGroup(Guid.NewGuid(), "短線交易");

        await repo.AddAsync(group);
        var pending = await repo.GetPendingPushAsync();

        // Default group (seeded by migrator) starts with version=0 and is_pending_push=0,
        // so only our new group shows up.
        var env = Assert.Single(pending);
        Assert.Equal(group.Id, env.EntityId);
        Assert.False(env.Deleted);
    }

    [Fact]
    public async Task RemoveAsync_OnUserGroup_CreatesTombstone()
    {
        var repo = new PortfolioGroupSqliteRepository(_dbPath);
        var group = new PortfolioGroup(Guid.NewGuid(), "緊急基金");
        await repo.AddAsync(group);
        await repo.MarkPushedAsync(new[] { group.Id });

        await repo.RemoveAsync(group.Id);
        var pending = await repo.GetPendingPushAsync();

        var env = Assert.Single(pending);
        Assert.Equal(group.Id, env.EntityId);
        Assert.True(env.Deleted);
    }

    [Fact]
    public async Task RemoveAsync_OnSystemGroup_Throws()
    {
        var repo = new PortfolioGroupSqliteRepository(_dbPath);
        // Seeded default group is system-protected — Remove must reject.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repo.RemoveAsync(PortfolioGroup.DefaultId));
    }

    [Fact]
    public async Task ApplyRemoteAsync_RemoteTombstoneOnSystemGroup_IsIgnored()
    {
        var repo = new PortfolioGroupSqliteRepository(_dbPath);
        // Spoof a tombstone envelope for the local default (system) group.
        var version = new EntityVersion(99, DateTimeOffset.UtcNow, "deviceX");
        var tombstone = new SyncEnvelope(
            EntityId: PortfolioGroup.DefaultId,
            EntityType: PortfolioGroupSyncMapper.EntityType,
            PayloadJson: string.Empty,
            Version: version,
            Deleted: true);

        await repo.ApplyRemoteAsync(new[] { tombstone });
        var def = await repo.GetByIdAsync(PortfolioGroup.DefaultId);

        // Default group must still exist — system protection wins over remote delete.
        Assert.NotNull(def);
        Assert.True(def!.IsSystem);
    }
}
