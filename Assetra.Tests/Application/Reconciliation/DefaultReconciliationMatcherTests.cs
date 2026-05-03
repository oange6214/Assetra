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
    public void IsMatch_WithdrawalStoredAsPositiveCashAmount_MatchesDebit()
    {
        var m = new DefaultReconciliationMatcher();
        var row = Row(new DateOnly(2026, 4, 28), -1000m);
        var trade = Trade(new DateTime(2026, 4, 28), 1000m) with { Type = TradeType.Withdrawal };

        Assert.True(m.IsMatch(row, trade));
        Assert.Equal(-1000m, m.SignedAmount(trade));
    }

    [Fact]
    public void SignedAmount_BuyIncludesCommissionAsOutflow()
    {
        var m = new DefaultReconciliationMatcher();
        var trade = new Trade(
            Id: Guid.NewGuid(),
            Symbol: "2330",
            Exchange: "TWSE",
            Name: "TSMC",
            Type: TradeType.Buy,
            TradeDate: new DateTime(2026, 4, 28),
            Price: 600m,
            Quantity: 2,
            RealizedPnl: null,
            RealizedPnlPct: null,
            Commission: 20m);

        Assert.Equal(-1220m, m.SignedAmount(trade));
    }

    [Fact]
    public void Constructor_RejectsNegativeTolerances()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DefaultReconciliationMatcher(dateToleranceDays: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DefaultReconciliationMatcher(amountTolerance: -0.01m));
    }
}
