using System.IO;
using Assetra.Core.Models.Import;
using Assetra.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Assetra.Tests.Infrastructure;

public class ImportBatchHistorySqliteRepositoryTests : IDisposable
{
    private readonly string _dbPath;

    public ImportBatchHistorySqliteRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"import_history_test_{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    [Fact]
    public async Task Save_GetById_RoundTripsEntries()
    {
        var repo = new ImportBatchHistorySqliteRepository(_dbPath);
        var historyId = Guid.NewGuid();
        var newTradeId = Guid.NewGuid();
        var history = new ImportBatchHistory(
            Id: historyId,
            BatchId: Guid.NewGuid(),
            FileName: "stmt.csv",
            Format: ImportFormat.CathayUnitedBank,
            AppliedAt: DateTimeOffset.UtcNow,
            RowsApplied: 2,
            RowsSkipped: 1,
            RowsOverwritten: 1,
            IsRolledBack: false,
            RolledBackAt: null,
            Entries: new[]
            {
                new ImportBatchEntry(1, ImportBatchAction.Skipped, null, null),
                new ImportBatchEntry(2, ImportBatchAction.Overwritten, newTradeId, "{\"Symbol\":\"X\"}"),
                new ImportBatchEntry(3, ImportBatchAction.Added, Guid.NewGuid(), null),
            });

        await repo.SaveAsync(history);

        var loaded = await repo.GetByIdAsync(historyId);
        Assert.NotNull(loaded);
        Assert.Equal(history.FileName, loaded!.FileName);
        Assert.Equal(history.Format, loaded.Format);
        Assert.Equal(3, loaded.Entries.Count);

        var overwritten = loaded.Entries.Single(e => e.Action == ImportBatchAction.Overwritten);
        Assert.Equal(newTradeId, overwritten.NewTradeId);
        Assert.Equal("{\"Symbol\":\"X\"}", overwritten.OverwrittenTradeJson);
    }

    [Fact]
    public async Task GetRecent_OrdersByAppliedAtDesc_AndOmitsEntries()
    {
        var repo = new ImportBatchHistorySqliteRepository(_dbPath);
        await repo.SaveAsync(MakeHistory("a.csv", DateTimeOffset.Parse("2026-04-25T10:00:00Z")));
        await repo.SaveAsync(MakeHistory("b.csv", DateTimeOffset.Parse("2026-04-27T10:00:00Z")));
        await repo.SaveAsync(MakeHistory("c.csv", DateTimeOffset.Parse("2026-04-26T10:00:00Z")));

        var list = await repo.GetRecentAsync(10);
        Assert.Equal(new[] { "b.csv", "c.csv", "a.csv" }, list.Select(h => h.FileName));
        Assert.All(list, h => Assert.Empty(h.Entries));
    }

    [Fact]
    public async Task MarkRolledBack_UpdatesFlagAndTimestamp()
    {
        var repo = new ImportBatchHistorySqliteRepository(_dbPath);
        var h = MakeHistory("x.csv", DateTimeOffset.UtcNow);
        await repo.SaveAsync(h);
        var rolledAt = DateTimeOffset.Parse("2026-04-28T12:00:00Z");

        await repo.MarkRolledBackAsync(h.Id, rolledAt);

        var loaded = await repo.GetByIdAsync(h.Id);
        Assert.True(loaded!.IsRolledBack);
        Assert.Equal(rolledAt, loaded.RolledBackAt);
    }

    private static ImportBatchHistory MakeHistory(string fileName, DateTimeOffset appliedAt) => new(
        Id: Guid.NewGuid(),
        BatchId: Guid.NewGuid(),
        FileName: fileName,
        Format: ImportFormat.YuantaSecurities,
        AppliedAt: appliedAt,
        RowsApplied: 1,
        RowsSkipped: 0,
        RowsOverwritten: 0,
        IsRolledBack: false,
        RolledBackAt: null,
        Entries: new[] { new ImportBatchEntry(1, ImportBatchAction.Added, Guid.NewGuid(), null) });
}
