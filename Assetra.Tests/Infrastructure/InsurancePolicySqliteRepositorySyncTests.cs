using System.IO;
using Assetra.Core.Models.MultiAsset;
using Assetra.Core.Models.Sync;
using Assetra.Infrastructure.Persistence;
using Xunit;

namespace Assetra.Tests.Infrastructure;

public sealed class InsurancePolicySqliteRepositorySyncTests : IDisposable
{
    private readonly string _dbPath;
    private readonly FixedTime _time = new(DateTimeOffset.Parse("2026-05-02T10:00:00Z"));

    public InsurancePolicySqliteRepositorySyncTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"assetra-insurance-sync-{Guid.NewGuid():N}.db");
    }

    public void Dispose() { try { File.Delete(_dbPath); } catch { /* best effort */ } }

    [Fact]
    public async Task Remove_BecomesTombstoneWithFreshSyncMetadata()
    {
        var repo = new InsurancePolicySqliteRepository(_dbPath, "device-A", _time);
        var policy = new InsurancePolicy(
            Guid.NewGuid(),
            "Whole life",
            "P001",
            InsuranceType.WholeLife,
            "Insurer",
            new DateOnly(2024, 1, 1),
            null,
            1_000_000m,
            120_000m,
            30_000m,
            "TWD",
            InsurancePolicyStatus.Active,
            null,
            EntityVersion.Initial("device-A", _time.GetUtcNow()));

        await repo.AddAsync(policy);
        await repo.RemoveAsync(policy.Id);

        Assert.Empty(await repo.GetAllAsync());
        var pending = await repo.GetPendingPushAsync();
        var tombstone = Assert.Single(pending);
        Assert.True(tombstone.Deleted);
        Assert.Equal(2, tombstone.Version.Version);
        Assert.Equal(_time.GetUtcNow(), tombstone.Version.LastModifiedAt);
        Assert.Equal("device-A", tombstone.Version.LastModifiedByDevice);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
