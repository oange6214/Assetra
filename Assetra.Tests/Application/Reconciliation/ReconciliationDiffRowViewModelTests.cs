using Assetra.Core.Models;
using Assetra.Core.Models.Reconciliation;
using Assetra.WPF.Features.Reconciliation;
using Xunit;

namespace Assetra.Tests.Application.Reconciliation;

public class ReconciliationDiffRowViewModelTests
{
    [Fact]
    public void ExtraDiff_DisplaysTradeContext()
    {
        var trade = new Trade(
            Id: Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            Symbol: string.Empty,
            Exchange: string.Empty,
            Name: "ATM",
            Type: TradeType.Withdrawal,
            TradeDate: new DateTime(2026, 4, 28),
            Price: 0m,
            Quantity: 1,
            RealizedPnl: null,
            RealizedPnlPct: null,
            CashAmount: 1000m);
        var diff = new ReconciliationDiff(
            Id: Guid.NewGuid(),
            SessionId: Guid.NewGuid(),
            Kind: ReconciliationDiffKind.Extra,
            StatementRow: null,
            TradeId: trade.Id,
            Resolution: ReconciliationDiffResolution.Pending,
            ResolvedAt: null,
            Note: null);

        var row = new ReconciliationDiffRowViewModel(diff, trade, tradeAmount: -1000m);

        Assert.Equal("2026-04-28", row.DateDisplay);
        Assert.Equal("-1,000.00", row.AmountDisplay);
        Assert.Contains("ATM", row.CounterpartyDisplay);
        Assert.Contains("aaaaaaaa", row.CounterpartyDisplay);
        Assert.Equal("aaaaaaaa", row.TradeIdDisplay);
    }
}
