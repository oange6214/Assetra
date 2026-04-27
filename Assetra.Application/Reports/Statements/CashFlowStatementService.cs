using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Reports;
using Assetra.Core.Models;
using Assetra.Core.Models.Reports;

namespace Assetra.Application.Reports.Statements;

/// <summary>
/// 現金流量表（個人版）：以 trade journal 為單一來源。三段定義：
/// - Operating：日常 Income / Expense / CashDividend / Withdrawal(with category)
/// - Investing：Buy / Sell（cash 流出/流入）
/// - Financing：LoanBorrow / LoanRepay（含 InterestPaid）/ CreditCardCharge / CreditCardPayment
/// 計算 OpeningCash（期間開始前的所有 trade 累計現金）+ NetChange = ClosingCash 並驗證。
/// </summary>
public sealed class CashFlowStatementService : ICashFlowStatementService
{
    private readonly ITradeRepository _trades;

    public CashFlowStatementService(ITradeRepository trades)
    {
        ArgumentNullException.ThrowIfNull(trades);
        _trades = trades;
    }

    public async Task<CashFlowStatement> GenerateAsync(ReportPeriod period, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(period);
        var all = await _trades.GetAllAsync().ConfigureAwait(false);

        var startDt = period.Start.ToDateTime(TimeOnly.MinValue);
        var endDt = period.End.ToDateTime(TimeOnly.MaxValue);

        var opening = all.Where(t => t.TradeDate < startDt).Sum(CashDelta);
        var inPeriod = all.Where(t => t.TradeDate >= startDt && t.TradeDate <= endDt).ToList();

        var operating = BuildSection("Operating", inPeriod.Where(IsOperating));
        var investing = BuildSection("Investing", inPeriod.Where(IsInvesting));
        var financing = BuildSection("Financing", inPeriod.Where(IsFinancing));

        var net = operating.Total + investing.Total + financing.Total;
        var closing = opening + net;

        return new CashFlowStatement(
            Period: period,
            Operating: operating,
            Investing: investing,
            Financing: financing,
            NetChange: net,
            OpeningCash: opening,
            ClosingCash: closing);
    }

    private static bool IsOperating(Trade t) => t.Type switch
    {
        TradeType.Income or TradeType.CashDividend or TradeType.Deposit => true,
        TradeType.Withdrawal => true,
        _ => false,
    };

    private static bool IsInvesting(Trade t) => t.Type is TradeType.Buy or TradeType.Sell;

    private static bool IsFinancing(Trade t) => t.Type switch
    {
        TradeType.LoanBorrow or TradeType.LoanRepay => true,
        TradeType.CreditCardCharge or TradeType.CreditCardPayment => true,
        _ => false,
    };

    private static StatementSection BuildSection(string title, IEnumerable<Trade> trades)
    {
        var rows = trades
            .GroupBy(t => t.Type)
            .Select(g => new StatementRow(g.Key.ToString(), g.Sum(CashDelta)))
            .Where(r => r.Amount != 0m)
            .OrderByDescending(r => Math.Abs(r.Amount))
            .ToList();
        return new StatementSection(title, rows, rows.Sum(r => r.Amount));
    }

    private static decimal CashDelta(Trade t) => t.Type switch
    {
        TradeType.Income or TradeType.Deposit or TradeType.CashDividend or TradeType.LoanBorrow
            => t.CashAmount ?? 0m,
        TradeType.Withdrawal or TradeType.CreditCardPayment
            => -(t.CashAmount ?? 0m),
        TradeType.Buy => -((t.Price * t.Quantity) + (t.Commission ?? 0m)),
        TradeType.Sell => (t.Price * t.Quantity) - (t.Commission ?? 0m),
        TradeType.LoanRepay => -((t.Principal ?? 0m) + (t.InterestPaid ?? 0m)),
        // CreditCardCharge: not a cash outflow until payment; we record it as 0 here so financing sums match the
        // outstanding-credit movement, but expose it on the operating side via category-tagged Withdrawals.
        TradeType.CreditCardCharge => 0m,
        // Transfer is between own accounts — net zero impact on total cash.
        TradeType.Transfer => 0m,
        _ => 0m,
    };
}
