using System.IO;
using Assetra.Core.Models;
using Assetra.Core.Models.Sync;
using Assetra.Infrastructure.Persistence;
using Assetra.Infrastructure.Sync;
using Xunit;

namespace Assetra.Tests.Infrastructure;

public sealed class RecurringTransactionSqliteRepositorySyncTests : IDisposable
{
    private readonly string _dbPath;
    private readonly FixedTime _time = new(DateTimeOffset.Parse("2026-04-28T00:00:00Z"));

    public RecurringTransactionSqliteRepositorySyncTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"assetra-rt-sync-{Guid.NewGuid():N}.db");
    }

    public void Dispose() { try { File.Delete(_dbPath); } catch { /* best effort */ } }

    private RecurringTransactionSqliteRepository New(string device = "device-A")
        => new(_dbPath, device, _time);

    private static RecurringTransaction Sample(Guid? id = null) => new(
        Id: id ?? Guid.NewGuid(),
        Name: "房租",
        TradeType: TradeType.Withdrawal,
        Amount: 12000m,
        CashAccountId: null,
        CategoryId: null,
        Frequency: RecurrenceFrequency.Monthly,
        Interval: 1,
        StartDate: new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc),
        EndDate: null,
        GenerationMode: AutoGenerationMode.AutoApply,
        IsEnabled: true);

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
        await repo.UpdateAsync(r with { Name = "房租2" });

        var pending = await repo.GetPendingPushAsync();
        var env = Assert.Single(pending);
        Assert.Equal(2, env.Version.Version);
        Assert.Contains("房租2", env.PayloadJson);
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
        var env = RecurringTransactionSyncMapper.ToEnvelope(
            remote, new EntityVersion(5, _time.GetUtcNow(), "device-B"), isDeleted: false);

        await repo.ApplyRemoteAsync(new[] { env });

        var stored = await repo.GetByIdAsync(remote.Id);
        Assert.NotNull(stored);
        Assert.Equal(remote.Name, stored!.Name);
        Assert.Equal(remote.Amount, stored.Amount);
        Assert.Empty(await repo.GetPendingPushAsync());
    }

    [Fact]
    public async Task ApplyRemote_DoesNotWriteBackwards()
    {
        var repo = New();
        var r = Sample();
        await repo.AddAsync(r);
        await repo.UpdateAsync(r with { Name = "local-v2" });

        var stale = RecurringTransactionSyncMapper.ToEnvelope(
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
            r.Id, "RecurringTransaction", string.Empty,
            new EntityVersion(99, _time.GetUtcNow(), "device-B"), Deleted: true);

        await repo.ApplyRemoteAsync(new[] { tombstone });

        Assert.Null(await repo.GetByIdAsync(r.Id));
    }

    [Fact]
    public async Task ApplyRemote_TombstoneForUnknownId_StoresHidden()
    {
        var repo = New();
        var unknown = Guid.NewGuid();
        var tombstone = new SyncEnvelope(
            unknown, "RecurringTransaction", string.Empty,
            new EntityVersion(1, _time.GetUtcNow(), "device-B"), Deleted: true);

        await repo.ApplyRemoteAsync(new[] { tombstone });

        Assert.Null(await repo.GetByIdAsync(unknown));
        Assert.Empty(await repo.GetPendingPushAsync());
    }

    [Fact]
    public async Task GetActive_FiltersTombstones()
    {
        var repo = New();
        var keep = Sample();
        var drop = Sample();
        await repo.AddAsync(keep);
        await repo.AddAsync(drop);
        await repo.RemoveAsync(drop.Id);

        var active = await repo.GetActiveAsync();
        Assert.Single(active);
        Assert.Equal(keep.Id, active[0].Id);
    }

    private sealed class FixedTime : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedTime(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
