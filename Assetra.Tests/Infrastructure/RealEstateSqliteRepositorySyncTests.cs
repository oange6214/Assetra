using System.IO;
using Assetra.Core.Models.MultiAsset;
using Assetra.Core.Models.Sync;
using Assetra.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Assetra.Tests.Infrastructure;

public sealed class RealEstateSqliteRepositorySyncTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"assetra-realestate-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task AddAsync_StampsVersionAndDeviceId()
    {
        var repo = new RealEstateSqliteRepository(_dbPath, () => "device-A");

        var property = MakeProperty();
        await repo.AddAsync(property);

        var pending = await repo.GetPendingPushAsync();
        var envelope = Assert.Single(pending);
        Assert.Equal(1, envelope.Version.Version);
        Assert.Equal("device-A", envelope.Version.LastModifiedByDevice);
    }

    [Fact]
    public async Task UpdateAsync_BumpsExistingVersionAndDeviceId()
    {
        var property = MakeProperty();
        await new RealEstateSqliteRepository(_dbPath, () => "device-A").AddAsync(property);
        var repo = new RealEstateSqliteRepository(_dbPath, () => "device-B");

        await repo.UpdateAsync(property with { CurrentValue = 12_000_000m });

        var pending = await repo.GetPendingPushAsync();
        var envelope = Assert.Single(pending);
        Assert.Equal(2, envelope.Version.Version);
        Assert.Equal("device-B", envelope.Version.LastModifiedByDevice);
    }

    [Fact]
    public async Task RemoveAsync_BumpsTombstoneVersionAndDeviceId()
    {
        var property = MakeProperty();
        await new RealEstateSqliteRepository(_dbPath, () => "device-A").AddAsync(property);
        var repo = new RealEstateSqliteRepository(_dbPath, () => "device-B");

        await repo.RemoveAsync(property.Id);

        var pending = await repo.GetPendingPushAsync();
        var envelope = Assert.Single(pending);
        Assert.True(envelope.Deleted);
        Assert.Equal(2, envelope.Version.Version);
        Assert.Equal("device-B", envelope.Version.LastModifiedByDevice);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    private static RealEstate MakeProperty() =>
        new(
            Guid.NewGuid(),
            "House",
            "Taipei",
            8_000_000m,
            new DateOnly(2020, 1, 1),
            10_000_000m,
            3_000_000m,
            "TWD",
            false,
            RealEstateStatus.Active,
            null,
            new EntityVersion());
}
