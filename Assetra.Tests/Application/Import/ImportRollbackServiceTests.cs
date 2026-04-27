using System.Text.Json;
using Assetra.Application.Import;
using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Import;
using Assetra.Core.Models;
using Assetra.Core.Models.Import;
using Moq;
using Xunit;

namespace Assetra.Tests.Application.Import;

public class ImportRollbackServiceTests
{
    [Fact]
    public async Task Rollback_AddedEntry_RemovesNewTrade()
    {
        var newId = Guid.NewGuid();
        var removed = new List<Guid>();
        var trades = NewTradeRepo(removed: removed);
        var historyRepo = NewHistoryRepo(History(
            new ImportBatchEntry(1, ImportBatchAction.Added, newId, null)));

        var result = await new ImportRollbackService(trades.Object, historyRepo.Object)
            .RollbackAsync(Guid.NewGuid());

        Assert.Equal(1, result.Reverted);
        Assert.Equal(0, result.Restored);
        Assert.True(result.IsFullyReverted);
        Assert.Equal(new[] { newId }, removed);
    }

    [Fact]
    public async Task Rollback_OverwrittenEntry_RemovesNewAndRestoresSnapshot()
    {
        var newId = Guid.NewGuid();
        var oldTrade = new Trade(Guid.NewGuid(), "X", "TWSE", "Old", TradeType.Buy,
            new DateTime(2026, 4, 26), 500m, 100, null, null);
        var json = JsonSerializer.Serialize(oldTrade, ImportApplyService.SnapshotJsonOptions);

        var removed = new List<Guid>();
        var added = new List<Trade>();
        var trades = NewTradeRepo(removed, added);
        var historyRepo = NewHistoryRepo(History(
            new ImportBatchEntry(1, ImportBatchAction.Overwritten, newId, json)));

        var result = await new ImportRollbackService(trades.Object, historyRepo.Object)
            .RollbackAsync(Guid.NewGuid());

        Assert.Equal(1, result.Restored);
        Assert.True(result.IsFullyReverted);
        Assert.Equal(new[] { newId }, removed);
        Assert.Single(added);
        Assert.Equal(oldTrade.Id, added[0].Id);
        Assert.Equal("X", added[0].Symbol);
        Assert.Equal(500m, added[0].Price);
    }

    [Fact]
    public async Task Rollback_SkippedEntry_NoOp()
    {
        var trades = NewTradeRepo();
        var historyRepo = NewHistoryRepo(History(
            new ImportBatchEntry(1, ImportBatchAction.Skipped, null, null)));

        var result = await new ImportRollbackService(trades.Object, historyRepo.Object)
            .RollbackAsync(Guid.NewGuid());

        Assert.Equal(1, result.Skipped);
        Assert.True(result.IsFullyReverted);
        trades.Verify(t => t.RemoveAsync(It.IsAny<Guid>()), Times.Never);
        trades.Verify(t => t.AddAsync(It.IsAny<Trade>()), Times.Never);
    }

    [Fact]
    public async Task Rollback_MarksHistoryRolledBack_OnSuccess()
    {
        var historyId = Guid.NewGuid();
        var trades = NewTradeRepo();
        var historyRepo = NewHistoryRepo(History(
            historyId,
            new ImportBatchEntry(1, ImportBatchAction.Added, Guid.NewGuid(), null)));

        await new ImportRollbackService(trades.Object, historyRepo.Object)
            .RollbackAsync(historyId);

        historyRepo.Verify(r => r.MarkRolledBackAsync(historyId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Rollback_DoesNotMarkRolledBack_WhenFailuresOccur()
    {
        var trades = new Mock<ITradeRepository>();
        trades.Setup(t => t.RemoveAsync(It.IsAny<Guid>()))
            .ThrowsAsync(new InvalidOperationException("fk constraint"));
        var historyRepo = NewHistoryRepo(History(
            new ImportBatchEntry(1, ImportBatchAction.Added, Guid.NewGuid(), null)));

        var result = await new ImportRollbackService(trades.Object, historyRepo.Object)
            .RollbackAsync(Guid.NewGuid());

        Assert.False(result.IsFullyReverted);
        Assert.Single(result.Failures);
        Assert.Contains("fk constraint", result.Failures[0].Reason);
        historyRepo.Verify(r => r.MarkRolledBackAsync(It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Rollback_AlreadyRolledBack_ReturnsFailureWithoutTouchingTrades()
    {
        var trades = NewTradeRepo();
        var record = History(new ImportBatchEntry(1, ImportBatchAction.Added, Guid.NewGuid(), null))
            with
        { IsRolledBack = true, RolledBackAt = DateTimeOffset.UtcNow };
        var historyRepo = new Mock<IImportBatchHistoryRepository>();
        historyRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);

        var result = await new ImportRollbackService(trades.Object, historyRepo.Object)
            .RollbackAsync(Guid.NewGuid());

        Assert.False(result.IsFullyReverted);
        Assert.Equal("History already rolled back.", result.Failures[0].Reason);
        trades.Verify(t => t.RemoveAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task Rollback_MissingHistory_Throws()
    {
        var trades = NewTradeRepo();
        var historyRepo = new Mock<IImportBatchHistoryRepository>();
        historyRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ImportBatchHistory?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ImportRollbackService(trades.Object, historyRepo.Object).RollbackAsync(Guid.NewGuid()));
    }

    private static Mock<ITradeRepository> NewTradeRepo(List<Guid>? removed = null, List<Trade>? added = null)
    {
        var mock = new Mock<ITradeRepository>();
        mock.Setup(t => t.RemoveAsync(It.IsAny<Guid>()))
            .Callback<Guid>(id => removed?.Add(id))
            .Returns(Task.CompletedTask);
        mock.Setup(t => t.AddAsync(It.IsAny<Trade>()))
            .Callback<Trade>(t => added?.Add(t))
            .Returns(Task.CompletedTask);
        return mock;
    }

    private static Mock<IImportBatchHistoryRepository> NewHistoryRepo(ImportBatchHistory record)
    {
        var mock = new Mock<IImportBatchHistoryRepository>();
        mock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);
        mock.Setup(r => r.MarkRolledBackAsync(It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return mock;
    }

    private static ImportBatchHistory History(params ImportBatchEntry[] entries) =>
        History(Guid.NewGuid(), entries);

    private static ImportBatchHistory History(Guid id, params ImportBatchEntry[] entries) => new(
        Id: id,
        BatchId: Guid.NewGuid(),
        FileName: "x.csv",
        Format: ImportFormat.CathayUnitedBank,
        AppliedAt: DateTimeOffset.UtcNow,
        RowsApplied: entries.Count(e => e.Action != ImportBatchAction.Skipped),
        RowsSkipped: entries.Count(e => e.Action == ImportBatchAction.Skipped),
        RowsOverwritten: entries.Count(e => e.Action == ImportBatchAction.Overwritten),
        IsRolledBack: false,
        RolledBackAt: null,
        Entries: entries);
}
