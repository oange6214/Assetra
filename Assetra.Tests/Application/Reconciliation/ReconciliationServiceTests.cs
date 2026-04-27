using Assetra.Application.Reconciliation;
using Assetra.Core.DomainServices.Reconciliation;
using Assetra.Core.Models;
using Assetra.Core.Models.Import;
using Assetra.Core.Models.Reconciliation;
using Xunit;

namespace Assetra.Tests.Application.Reconciliation;

public class ReconciliationServiceTests
{
    private static ImportPreviewRow Row(int idx, DateOnly date, decimal amount)
        => new(RowIndex: idx, Date: date, Amount: amount, Counterparty: $"row{idx}", Memo: null);

    private static Trade Trade(DateTime date, decimal cashAmount)
        => new(
            Id: Guid.NewGuid(),
            Symbol: string.Empty,
            Exchange: string.Empty,
            Name: string.Empty,
            Type: TradeType.Income,
            TradeDate: date,
            Price: 0m,
            Quantity: 1,
            RealizedPnl: null,
            RealizedPnlPct: null,
            CashAmount: cashAmount);

    private static readonly DefaultReconciliationMatcher Matcher = new();

    [Fact]
    public void ComputeDiffs_ProducesMissing_WhenStatementHasUnmatchedRow()
    {
        var sessionId = Guid.NewGuid();
        var rows = new[] { Row(1, new DateOnly(2026, 4, 28), 1000m) };
        var trades = Array.Empty<Trade>();

        var diffs = ReconciliationService.ComputeDiffs(sessionId, rows, trades, Matcher);

        Assert.Single(diffs);
        Assert.Equal(ReconciliationDiffKind.Missing, diffs[0].Kind);
        Assert.NotNull(diffs[0].StatementRow);
        Assert.Null(diffs[0].TradeId);
    }

    [Fact]
    public void ComputeDiffs_ProducesExtra_WhenTradeHasNoStatementRow()
    {
        var sessionId = Guid.NewGuid();
        var trade = Trade(new DateTime(2026, 4, 28), 1000m);

        var diffs = ReconciliationService.ComputeDiffs(sessionId, Array.Empty<ImportPreviewRow>(), new[] { trade }, Matcher);

        Assert.Single(diffs);
        Assert.Equal(ReconciliationDiffKind.Extra, diffs[0].Kind);
        Assert.Equal(trade.Id, diffs[0].TradeId);
        Assert.Null(diffs[0].StatementRow);
    }

    [Fact]
    public void ComputeDiffs_NoDiff_WhenAmountAndDateMatchExactly()
    {
        var sessionId = Guid.NewGuid();
        var rows = new[] { Row(1, new DateOnly(2026, 4, 28), 1000m) };
        var trades = new[] { Trade(new DateTime(2026, 4, 28), 1000m) };

        var diffs = ReconciliationService.ComputeDiffs(sessionId, rows, trades, Matcher);

        Assert.Empty(diffs);
    }

    [Fact]
    public void ComputeDiffs_AmountMismatch_WhenDifferenceExactlyAtBoundary()
    {
        var sessionId = Guid.NewGuid();
        // 0.004 within tolerance (0.005) so they pair up; but values differ → AmountMismatch.
        var rows = new[] { Row(1, new DateOnly(2026, 4, 28), 1000.004m) };
        var trades = new[] { Trade(new DateTime(2026, 4, 28), 1000m) };

        var diffs = ReconciliationService.ComputeDiffs(sessionId, rows, trades, Matcher);

        Assert.Single(diffs);
        Assert.Equal(ReconciliationDiffKind.AmountMismatch, diffs[0].Kind);
    }

    [Theory]
    [InlineData(ReconciliationDiffKind.Missing, ReconciliationDiffResolution.Created, true)]
    [InlineData(ReconciliationDiffKind.Missing, ReconciliationDiffResolution.MarkedResolved, true)]
    [InlineData(ReconciliationDiffKind.Missing, ReconciliationDiffResolution.Ignored, true)]
    [InlineData(ReconciliationDiffKind.Missing, ReconciliationDiffResolution.Deleted, false)]
    [InlineData(ReconciliationDiffKind.Missing, ReconciliationDiffResolution.OverwrittenFromStatement, false)]
    [InlineData(ReconciliationDiffKind.Extra, ReconciliationDiffResolution.Deleted, true)]
    [InlineData(ReconciliationDiffKind.Extra, ReconciliationDiffResolution.MarkedResolved, true)]
    [InlineData(ReconciliationDiffKind.Extra, ReconciliationDiffResolution.Created, false)]
    [InlineData(ReconciliationDiffKind.AmountMismatch, ReconciliationDiffResolution.OverwrittenFromStatement, true)]
    [InlineData(ReconciliationDiffKind.AmountMismatch, ReconciliationDiffResolution.MarkedResolved, true)]
    [InlineData(ReconciliationDiffKind.AmountMismatch, ReconciliationDiffResolution.Created, false)]
    public void EnsureLegalTransition_FollowsMatrix(
        ReconciliationDiffKind kind,
        ReconciliationDiffResolution resolution,
        bool expectedLegal)
    {
        if (expectedLegal)
        {
            ReconciliationService.EnsureLegalTransition(kind, resolution);
        }
        else
        {
            Assert.Throws<InvalidOperationException>(
                () => ReconciliationService.EnsureLegalTransition(kind, resolution));
        }
    }

    [Fact]
    public void EnsureLegalTransition_RejectsPending()
    {
        Assert.Throws<InvalidOperationException>(
            () => ReconciliationService.EnsureLegalTransition(
                ReconciliationDiffKind.Missing, ReconciliationDiffResolution.Pending));
    }
}
