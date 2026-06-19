using Assetra.Application.Portfolio.Dtos;
using Assetra.Application.Portfolio.Services;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Moq;
using Xunit;

namespace Assetra.Tests.Portfolio;

public sealed class TradeMetadataWorkflowServiceTests
{
    // Trade dates are stored UTC; the position log is keyed on the LOCAL date. Compute expected
    // log dates the same way the service does so the tests are timezone-independent. Midday UTC,
    // ~10 days apart, keeps Old < Sib < New ordering on any machine offset.
    private static readonly DateTime OldUtc = new(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime SibUtc = new(2026, 4, 10, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime NewUtc = new(2026, 4, 20, 12, 0, 0, DateTimeKind.Utc);

    private static DateOnly Local(DateTime utc) => DateOnly.FromDateTime(utc.ToLocalTime());

    private static Trade Sell(Guid id, DateTime tradeDateUtc, Guid? portfolioEntryId, string? note = null) =>
        new(
            Id: id,
            Symbol: "2330",
            Exchange: "TWSE",
            Name: "TSMC",
            Type: TradeType.Sell,
            TradeDate: tradeDateUtc,
            Price: 650m,
            Quantity: 1000,
            RealizedPnl: 50_000m,
            RealizedPnlPct: 8.3m,
            Note: note,
            PortfolioEntryId: portfolioEntryId);

    private static PortfolioPositionLog LogRow(Guid positionId, DateOnly date, Guid logId) =>
        new(logId, date, positionId, "2330", "TWSE", 0, 650m);

    private static (Mock<ITradeRepository> tradeRepo, Mock<IPortfolioPositionLogRepository> logRepo, Trade? captured)
        Harness(Trade original, params PortfolioPositionLog[] logs)
    {
        var tradeRepo = new Mock<ITradeRepository>();
        tradeRepo.Setup(r => r.GetAllAsync()).ReturnsAsync([original]);

        var logRepo = new Mock<IPortfolioPositionLogRepository>();
        logRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(logs);

        return (tradeRepo, logRepo, null);
    }

    [Fact]
    public async Task UpdateAsync_NonPositionTrade_UpdatesDateAndNoteOnly()
    {
        // Cash/income trades carry no PortfolioEntryId → no position log to sync; the date and
        // note update freely and every economic field is preserved.
        var tradeId = Guid.NewGuid();
        var original = Sell(tradeId, OldUtc, portfolioEntryId: null, note: "old");

        var (tradeRepo, logRepo, _) = Harness(original);
        Trade? updated = null;
        tradeRepo.Setup(r => r.UpdateAsync(It.IsAny<Trade>()))
            .Callback<Trade, CancellationToken>((t, _) => updated = t)
            .Returns(Task.CompletedTask);

        var service = new TradeMetadataWorkflowService(tradeRepo.Object, logRepo.Object);
        var result = await service.UpdateAsync(new TradeMetadataUpdateRequest(tradeId, NewUtc, "new"));

        Assert.Equal(TradeMetadataUpdateResult.Updated, result);
        Assert.NotNull(updated);
        Assert.Equal("new", updated!.Note);
        Assert.Equal(NewUtc, updated.TradeDate);
        Assert.Equal(650m, updated.Price);
        Assert.Equal(1000, updated.Quantity);
        Assert.Equal(50_000m, updated.RealizedPnl);
        // Non-position trade: the log is never even queried.
        logRepo.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_MissingTrade_ReturnsNotFound()
    {
        var (tradeRepo, logRepo, _) = Harness(Sell(Guid.NewGuid(), OldUtc, Guid.NewGuid()));

        var service = new TradeMetadataWorkflowService(tradeRepo.Object, logRepo.Object);
        var result = await service.UpdateAsync(new TradeMetadataUpdateRequest(Guid.NewGuid(), NewUtc, null));

        Assert.Equal(TradeMetadataUpdateResult.NotFound, result);
        tradeRepo.Verify(r => r.UpdateAsync(It.IsAny<Trade>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_PositionDateChange_WithOneSyncedLogRow_MovesItAndUpdatesTrade()
    {
        // WHY: this is the bug we set out to fix — editing a position trade's date must drag its
        // position-log row to the new date, or the cash flow (trade date) and the market-value drop
        // (log date) land on different days and the return calendar shows a phantom swing.
        var tradeId = Guid.NewGuid();
        var positionId = Guid.NewGuid();
        var logId = Guid.NewGuid();
        var original = Sell(tradeId, OldUtc, positionId);

        var (tradeRepo, logRepo, _) = Harness(original, LogRow(positionId, Local(OldUtc), logId));
        Trade? updated = null;
        tradeRepo.Setup(r => r.UpdateAsync(It.IsAny<Trade>()))
            .Callback<Trade, CancellationToken>((t, _) => updated = t)
            .Returns(Task.CompletedTask);

        var service = new TradeMetadataWorkflowService(tradeRepo.Object, logRepo.Object);
        var result = await service.UpdateAsync(new TradeMetadataUpdateRequest(tradeId, NewUtc, null));

        Assert.Equal(TradeMetadataUpdateResult.Updated, result);
        logRepo.Verify(r => r.UpdateLogDateAsync(logId, Local(NewUtc), It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotNull(updated);
        Assert.Equal(NewUtc, updated!.TradeDate);
    }

    [Fact]
    public async Task UpdateAsync_AmbiguousSameDayLogRows_BlocksWithoutMutating()
    {
        // WHY: two rows on the old date (e.g. a same-day two-lot sell) means we can't tell which
        // row this trade produced. Moving the wrong one corrupts history, so refuse the whole edit.
        var tradeId = Guid.NewGuid();
        var positionId = Guid.NewGuid();
        var original = Sell(tradeId, OldUtc, positionId);

        var (tradeRepo, logRepo, _) = Harness(
            original,
            LogRow(positionId, Local(OldUtc), Guid.NewGuid()),
            LogRow(positionId, Local(OldUtc), Guid.NewGuid()));

        var service = new TradeMetadataWorkflowService(tradeRepo.Object, logRepo.Object);
        var result = await service.UpdateAsync(new TradeMetadataUpdateRequest(tradeId, NewUtc, null));

        Assert.Equal(TradeMetadataUpdateResult.BlockedByPositionLog, result);
        logRepo.Verify(r => r.UpdateLogDateAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
        tradeRepo.Verify(r => r.UpdateAsync(It.IsAny<Trade>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_MoveWouldCrossSiblingTrade_Blocks()
    {
        // WHY: moving the row past another of this position's rows (a sibling buy/sell sitting
        // between the old and new dates) reorders the running-quantity sequence — qty could go
        // 100 → 0 → 50 (a resurrected position). Refuse rather than scramble it.
        var tradeId = Guid.NewGuid();
        var positionId = Guid.NewGuid();
        var original = Sell(tradeId, OldUtc, positionId);

        var (tradeRepo, logRepo, _) = Harness(
            original,
            LogRow(positionId, Local(OldUtc), Guid.NewGuid()),   // the matched row
            LogRow(positionId, Local(SibUtc), Guid.NewGuid()));  // sibling between old and new

        var service = new TradeMetadataWorkflowService(tradeRepo.Object, logRepo.Object);
        var result = await service.UpdateAsync(new TradeMetadataUpdateRequest(tradeId, NewUtc, null));

        Assert.Equal(TradeMetadataUpdateResult.BlockedByPositionLog, result);
        logRepo.Verify(r => r.UpdateLogDateAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
        tradeRepo.Verify(r => r.UpdateAsync(It.IsAny<Trade>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_NoLogRowOnOldDate_UpdatesTradeWithoutMovingLog()
    {
        // WHY: the user's repair path. Their log already sits on the real sell date while the trade
        // date is wrong, so there's no row on the OLD trade date to move. We must still let them fix
        // the trade date (which lands it on the already-correct log date) and not touch the log.
        var tradeId = Guid.NewGuid();
        var positionId = Guid.NewGuid();
        var original = Sell(tradeId, OldUtc, positionId);

        // Only row sits on the NEW date already (where the user is moving the trade to).
        var (tradeRepo, logRepo, _) = Harness(original, LogRow(positionId, Local(NewUtc), Guid.NewGuid()));
        Trade? updated = null;
        tradeRepo.Setup(r => r.UpdateAsync(It.IsAny<Trade>()))
            .Callback<Trade, CancellationToken>((t, _) => updated = t)
            .Returns(Task.CompletedTask);

        var service = new TradeMetadataWorkflowService(tradeRepo.Object, logRepo.Object);
        var result = await service.UpdateAsync(new TradeMetadataUpdateRequest(tradeId, NewUtc, null));

        Assert.Equal(TradeMetadataUpdateResult.Updated, result);
        logRepo.Verify(r => r.UpdateLogDateAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.NotNull(updated);
        Assert.Equal(NewUtc, updated!.TradeDate);
    }

    [Fact]
    public async Task UpdateAsync_NoteOnlyChange_SameDate_DoesNotTouchLog()
    {
        // WHY: a note-only edit (date unchanged) must never pay the log-sync cost or risk blocking —
        // there is no date movement to reconcile.
        var tradeId = Guid.NewGuid();
        var positionId = Guid.NewGuid();
        var original = Sell(tradeId, OldUtc, positionId, note: "old");

        var (tradeRepo, logRepo, _) = Harness(original, LogRow(positionId, Local(OldUtc), Guid.NewGuid()));
        Trade? updated = null;
        tradeRepo.Setup(r => r.UpdateAsync(It.IsAny<Trade>()))
            .Callback<Trade, CancellationToken>((t, _) => updated = t)
            .Returns(Task.CompletedTask);

        var service = new TradeMetadataWorkflowService(tradeRepo.Object, logRepo.Object);
        var result = await service.UpdateAsync(new TradeMetadataUpdateRequest(tradeId, OldUtc, "new"));

        Assert.Equal(TradeMetadataUpdateResult.Updated, result);
        Assert.Equal("new", updated!.Note);
        logRepo.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Never);
        logRepo.Verify(r => r.UpdateLogDateAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
