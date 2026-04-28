using System.IO;
using Assetra.Core.Models;
using Assetra.Core.Models.Sync;
using Assetra.Infrastructure.Persistence;
using Assetra.Infrastructure.Sync;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Assetra.Tests.Infrastructure;

public sealed class AssetSqliteRepositoryGroupSyncTests : IDisposable
{
    private static readonly Guid SystemBankAccount = new("11111111-1111-1111-1111-111111111101");

    private readonly string _dbPath;
    private readonly FixedTime _time = new(DateTimeOffset.Parse("2026-04-28T00:00:00Z"));

    public AssetSqliteRepositoryGroupSyncTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"assetra-group-sync-{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { /* best effort */ }
    }

    private AssetSqliteRepository New(string device = "device-A")
        => new(_dbPath, device, _time);

    private static AssetGroup Sample(Guid? id = null) => new(
        Id: id ?? Guid.NewGuid(),
        Name: "我的銀行帳戶",
        Type: FinancialType.Asset,
        Icon: "🏦",
        SortOrder: 10,
        IsSystem: false,
        CreatedDate: new DateOnly(2026, 4, 1));

    [Fact]
    public async Task AddGroup_StampsVersion1AndPendingPush()
    {
        var repo = New();
        var g = Sample();
        await repo.AddGroupAsync(g);

        var pending = await repo.GetGroupPendingPushAsync();
        var env = Assert.Single(pending, p => p.EntityId == g.Id);
        Assert.Equal("AssetGroup", env.EntityType);
        Assert.Equal(1, env.Version.Version);
        Assert.False(env.Deleted);
    }

    [Fact]
    public async Task UpdateGroup_BumpsVersion()
    {
        var repo = New();
        var g = Sample();
        await repo.AddGroupAsync(g);
        await repo.UpdateGroupAsync(g with { Name = "改名後" });

        var pending = await repo.GetGroupPendingPushAsync();
        var env = Assert.Single(pending, p => p.EntityId == g.Id);
        Assert.Equal(2, env.Version.Version);
        Assert.Contains("改名後", env.PayloadJson);
    }

    [Fact]
    public async Task DeleteGroup_NonSystem_BecomesTombstone()
    {
        var repo = New();
        var g = Sample();
        await repo.AddGroupAsync(g);
        await repo.DeleteGroupAsync(g.Id);

        Assert.DoesNotContain(await repo.GetGroupsAsync(), x => x.Id == g.Id);
        var pending = await repo.GetGroupPendingPushAsync();
        var env = Assert.Single(pending, p => p.EntityId == g.Id);
        Assert.True(env.Deleted);
        Assert.Equal(2, env.Version.Version);
    }

    [Fact]
    public async Task DeleteGroup_System_DoesNothing()
    {
        var repo = New();
        await repo.DeleteGroupAsync(SystemBankAccount);

        var groups = await repo.GetGroupsAsync();
        Assert.Contains(groups, x => x.Id == SystemBankAccount);
        Assert.DoesNotContain(await repo.GetGroupPendingPushAsync(), e => e.EntityId == SystemBankAccount);
    }

    [Fact]
    public async Task MarkGroupPushed_ClearsPendingFlag()
    {
        var repo = New();
        var g = Sample();
        await repo.AddGroupAsync(g);

        await repo.MarkGroupPushedAsync(new[] { g.Id });

        Assert.DoesNotContain(await repo.GetGroupPendingPushAsync(), e => e.EntityId == g.Id);
    }

    [Fact]
    public async Task ApplyGroupRemote_InsertsNewGroup()
    {
        var repo = New();
        var g = Sample();
        var env = AssetGroupSyncMapper.ToEnvelope(
            g, new EntityVersion(5, _time.GetUtcNow(), "device-B"), isDeleted: false);

        await repo.ApplyGroupRemoteAsync(new[] { env });

        var groups = await repo.GetGroupsAsync();
        Assert.Contains(groups, x => x.Id == g.Id && x.Name == g.Name);
    }

    [Fact]
    public async Task ApplyGroupRemote_DoesNotWriteBackwards()
    {
        var repo = New();
        var g = Sample();
        await repo.AddGroupAsync(g);
        await repo.UpdateGroupAsync(g with { Name = "本地" });

        var stale = AssetGroupSyncMapper.ToEnvelope(
            g with { Name = "雲端舊版" },
            new EntityVersion(1, _time.GetUtcNow(), "device-B"),
            isDeleted: false);

        await repo.ApplyGroupRemoteAsync(new[] { stale });

        var groups = await repo.GetGroupsAsync();
        Assert.Contains(groups, x => x.Id == g.Id && x.Name == "本地");
    }

    [Fact]
    public async Task ApplyGroupRemote_TombstoneForSystemGroup_Ignored()
    {
        var repo = New();
        var tombstone = new SyncEnvelope(
            SystemBankAccount, "AssetGroup", string.Empty,
            new EntityVersion(99, _time.GetUtcNow(), "device-B"),
            Deleted: true);

        await repo.ApplyGroupRemoteAsync(new[] { tombstone });

        Assert.Contains(await repo.GetGroupsAsync(), x => x.Id == SystemBankAccount);
    }

    [Fact]
    public async Task ApplyGroupRemote_TombstoneDeletesUserGroup()
    {
        var repo = New();
        var g = Sample();
        await repo.AddGroupAsync(g);

        var tombstone = new SyncEnvelope(
            g.Id, "AssetGroup", string.Empty,
            new EntityVersion(99, _time.GetUtcNow(), "device-B"),
            Deleted: true);

        await repo.ApplyGroupRemoteAsync(new[] { tombstone });

        Assert.DoesNotContain(await repo.GetGroupsAsync(), x => x.Id == g.Id);
    }

    private sealed class FixedTime : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedTime(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
