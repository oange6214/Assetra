using Assetra.Core.DomainServices.Reconciliation;
using Assetra.Core.Models;
using Assetra.Core.Models.Import;
using Xunit;

namespace Assetra.Tests.Application.Reconciliation;

public class DefaultReconciliationMatcherTests
{
    private static ImportPreviewRow Row(DateOnly date, decimal amount)
        => new(RowIndex: 1, Date: date, Amount: amount, Counterparty: null, Memo: null);

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

    [Fact]
    public void IsMatch_SameDate_ExactAmount_Matches()
    {
        var m = new DefaultReconciliationMatcher();
        var row = Row(new DateOnly(2026, 4, 28), 1000m);
        var trade = Trade(new DateTime(2026, 4, 28), 1000m);
        Assert.True(m.IsMatch(row, trade));
    }

    [Fact]
    public void IsMatch_OneDayApart_Within_Tolerance_Matches()
    {
        var m = new DefaultReconciliationMatcher();
        var row = Row(new DateOnly(2026, 4, 28), 1000m);
        var trade = Trade(new DateTime(2026, 4, 29), 1000m);
        Assert.True(m.IsMatch(row, trade));
    }

    [Fact]
    public void IsMatch_TwoDaysApart_DoesNotMatch()
    {
        var m = new DefaultReconciliationMatcher();
        var row = Row(new DateOnly(2026, 4, 28), 1000m);
        var trade = Trade(new DateTime(2026, 4, 30), 1000m);
        Assert.False(m.IsMatch(row, trade));
    }

    [Fact]
    public void IsMatch_AmountWithinTolerance_Matches()
    {
        var m = new DefaultReconciliationMatcher();
        var row = Row(new DateOnly(2026, 4, 28), 1000.004m);
        var trade = Trade(new DateTime(2026, 4, 28), 1000m);
        Assert.True(m.IsMatch(row, trade));
    }

    [Fact]
    public void IsMatch_AmountAboveTolerance_DoesNotMatch()
    {
        var m = new DefaultReconciliationMatcher();
        var row = Row(new DateOnly(2026, 4, 28), 1001m);
        var trade = Trade(new DateTime(2026, 4, 28), 1000m);
        Assert.False(m.IsMatch(row, trade));
    }

    [Fact]
    public void IsMatch_OppositeSign_DoesNotMatch()
    {
        var m = new DefaultReconciliationMatcher();
        var row = Row(new DateOnly(2026, 4, 28), -1000m);
        var trade = Trade(new DateTime(2026, 4, 28), 1000m);
        Assert.False(m.IsMatch(row, trade));
    }

    [Fact]
    public void Constructor_RejectsNegativeTolerances()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DefaultReconciliationMatcher(dateToleranceDays: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DefaultReconciliationMatcher(amountTolerance: -0.01m));
    }
}
