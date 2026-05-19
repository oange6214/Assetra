using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.Infrastructure.Persistence;
using Xunit;

namespace Assetra.Tests.Infrastructure;

public sealed class PortfolioSnapshotServiceTests
{
    [Fact]
    public async Task TryRecordAsync_AllowsSameDayReplacementWhenValuesChange()
    {
        var repo = new RecordingPortfolioSnapshotRepository();
        var service = new PortfolioSnapshotService(repo);

        var first = await service.TryRecordAsync(100m, 1_000m, 900m, 1);
        var duplicate = await service.TryRecordAsync(100m, 1_000m, 900m, 1);
        var changed = await service.TryRecordAsync(100m, 1_100m, 1_000m, 1);

        Assert.True(first);
        Assert.False(duplicate);
        Assert.True(changed);
        Assert.Equal(2, repo.Writes.Count);
        Assert.Equal(1_100m, repo.Writes[^1].MarketValue);
    }

    private sealed class RecordingPortfolioSnapshotRepository : IPortfolioSnapshotRepository
    {
        public List<PortfolioDailySnapshot> Writes { get; } = [];

        public Task<IReadOnlyList<PortfolioDailySnapshot>> GetSnapshotsAsync(
            DateOnly? from = null,
            DateOnly? to = null,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PortfolioDailySnapshot>>(Writes);

        public Task<PortfolioDailySnapshot?> GetSnapshotAsync(DateOnly date, CancellationToken ct = default) =>
            Task.FromResult(Writes.LastOrDefault(s => s.SnapshotDate == date));

        public Task UpsertAsync(PortfolioDailySnapshot snapshot, CancellationToken ct = default)
        {
            Writes.Add(snapshot);
            return Task.CompletedTask;
        }
    }
}
