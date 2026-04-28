using System.IO;
using Assetra.Core.Models;
using Assetra.Core.Models.Sync;
using Assetra.Infrastructure.Persistence;
using Assetra.Infrastructure.Sync;
using Xunit;

namespace Assetra.Tests.Infrastructure;

public sealed class AutoCategorizationRuleSqliteRepositorySyncTests : IDisposable
{
    private readonly string _dbPath;
    private readonly FixedTime _time = new(DateTimeOffset.Parse("2026-04-28T00:00:00Z"));

    public AutoCategorizationRuleSqliteRepositorySyncTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"assetra-rule-sync-{Guid.NewGuid():N}.db");
    }

    public void Dispose() { try { File.Delete(_dbPath); } catch { /* best effort */ } }

    private AutoCategorizationRuleSqliteRepository New(string device = "device-A")
        => new(_dbPath, device, _time);

    private static AutoCategorizationRule Sample(Guid? id = null) => new(
        Id: id ?? Guid.NewGuid(),
        KeywordPattern: "全聯",
        CategoryId: Guid.NewGuid(),
        Priority: 5,
        IsEnabled: true,
        MatchCaseSensitive: false,
        Name: "雜貨",
        MatchField: AutoCategorizationMatchField.Memo,
        MatchType: AutoCategorizationMatchType.Contains,
        AppliesTo: AutoCategorizationScope.Both);

    [Fact]
    public async Task Add_StampsVersion1AndPendingPush()
    {
        var repo = New();
        var r = Sample();
        await repo.AddAsync(r);

        var pending = await repo.GetPendingPushAsync();
        var env = Assert.Single(pending);
        Assert.Equal(r.Id, env.EntityId);
        Assert.Equal(1, env.Version.Version);
        Assert.Equal("device-A", env.Version.LastModifiedByDevice);
        Assert.False(env.Deleted);
    }

    [Fact]
    public async Task Update_BumpsVersion()
    {
        var repo = New();
        var r = Sample();
        await repo.AddAsync(r);
        await repo.UpdateAsync(r with { Name = "雜貨2" });

        var pending = await repo.GetPendingPushAsync();
        var env = Assert.Single(pending);
        Assert.Equal(2, env.Version.Version);
        Assert.Contains("雜貨2", env.PayloadJson);
    }

    [Fact]
    public async Task Remove_BecomesTombstone()
    {
        var repo = New();
        var r = Sample();
        await repo.AddAsync(r);
        await repo.RemoveAsync(r.Id);

        Assert.Empty(await repo.GetAllAsync());
        var pending = await repo.GetPendingPushAsync();
        var env = Assert.Single(pending);
        Assert.True(env.Deleted);
        Assert.Equal(string.Empty, env.PayloadJson);
        Assert.Equal(2, env.Version.Version);
    }

    [Fact]
    public async Task MarkPushed_ClearsPendingFlag()
    {
        var repo = New();
        var r = Sample();
        await repo.AddAsync(r);
        await repo.MarkPushedAsync(new[] { r.Id });

        Assert.Empty(await repo.GetPendingPushAsync());
        Assert.Single(await repo.GetAllAsync());
    }

    [Fact]
    public async Task ApplyRemote_InsertsNewEntity()
    {
        var repo = New();
        var remote = Sample();
        var env = AutoCategorizationRuleSyncMapper.ToEnvelope(
            remote, new EntityVersion(5, _time.GetUtcNow(), "device-B"), isDeleted: false);

        await repo.ApplyRemoteAsync(new[] { env });

        var stored = await repo.GetByIdAsync(remote.Id);
        Assert.NotNull(stored);
        Assert.Equal(remote.KeywordPattern, stored!.KeywordPattern);
        Assert.Empty(await repo.GetPendingPushAsync());
    }

    [Fact]
    public async Task ApplyRemote_DoesNotWriteBackwards()
    {
        var repo = New();
        var r = Sample();
        await repo.AddAsync(r);
        await repo.UpdateAsync(r with { Name = "local-v2" });

        var stale = AutoCategorizationRuleSyncMapper.ToEnvelope(
            r with { Name = "remote-stale" },
            new EntityVersion(1, _time.GetUtcNow().AddDays(1), "device-B"),
            isDeleted: false);

        await repo.ApplyRemoteAsync(new[] { stale });

        var stored = await repo.GetByIdAsync(r.Id);
        Assert.Equal("local-v2", stored!.Name);
    }

    [Fact]
    public async Task ApplyRemote_TombstoneDeletesLocally()
    {
        var repo = New();
        var r = Sample();
        await repo.AddAsync(r);

        var tombstone = new SyncEnvelope(
            r.Id, "AutoCategorizationRule", string.Empty,
            new EntityVersion(99, _time.GetUtcNow(), "device-B"), Deleted: true);

        await repo.ApplyRemoteAsync(new[] { tombstone });

        Assert.Null(await repo.GetByIdAsync(r.Id));
    }

    [Fact]
    public async Task GetAll_FiltersOutTombstones()
    {
        var repo = New();
        var keep = Sample();
        var drop = Sample();
        await repo.AddAsync(keep);
        await repo.AddAsync(drop);
        await repo.RemoveAsync(drop.Id);

        var all = await repo.GetAllAsync();
        Assert.Single(all);
        Assert.Equal(keep.Id, all[0].Id);
    }

    private sealed class FixedTime : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedTime(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
