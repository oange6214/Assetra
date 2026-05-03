using Assetra.Core.Interfaces.Reconciliation;
using Assetra.Core.Models;
using Assetra.Core.Models.Import;

namespace Assetra.Core.DomainServices.Reconciliation;

/// <summary>
/// 預設對帳比對器：日期 ± 1 天、金額 abs 容忍 ± 0.005。
/// 交易的 SignedAmount 依交易類型投影成現金流入/流出方向。
/// </summary>
public sealed class DefaultReconciliationMatcher : IReconciliationMatcher
{
    public int DateToleranceDays { get; }
    public decimal AmountTolerance { get; }

    public DefaultReconciliationMatcher(int dateToleranceDays = 1, decimal amountTolerance = 0.005m)
    {
        if (dateToleranceDays < 0)
            throw new ArgumentOutOfRangeException(nameof(dateToleranceDays));
        if (amountTolerance < 0)
            throw new ArgumentOutOfRangeException(nameof(amountTolerance));

        DateToleranceDays = dateToleranceDays;
        AmountTolerance = amountTolerance;
    }

    public decimal SignedAmount(ImportPreviewRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return row.Amount;
    }

    public decimal SignedAmount(Trade trade)
    {
        ArgumentNullException.ThrowIfNull(trade);
        return trade.Type switch
        {
            TradeType.Income or TradeType.CashDividend or TradeType.Deposit
                => trade.CashAmount ?? 0m,
            TradeType.Withdrawal or TradeType.CreditCardPayment
                => -(trade.CashAmount ?? 0m),
            TradeType.Transfer
                => -(trade.CashAmount ?? 0m),
            TradeType.LoanBorrow
                => (trade.CashAmount ?? 0m) - (trade.Commission ?? 0m),
            TradeType.LoanRepay
                => -(trade.CashAmount ?? ((trade.Principal ?? 0m) + (trade.InterestPaid ?? 0m))),
            TradeType.Buy
                => -(trade.Price * trade.Quantity + (trade.Commission ?? 0m)),
            TradeType.Sell
                => trade.Price * trade.Quantity - (trade.Commission ?? 0m),
            _ => 0m,
        };
    }

    public DateOnly DateOf(Trade trade)
    {
        ArgumentNullException.ThrowIfNull(trade);
        return DateOnly.FromDateTime(trade.TradeDate);
    }

    public bool AmountClose(decimal a, decimal b)
        => Math.Abs(Math.Abs(a) - Math.Abs(b)) <= AmountTolerance;

    public bool IsMatch(ImportPreviewRow row, Trade trade)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(trade);

        var rowSigned = SignedAmount(row);
        var tradeSigned = SignedAmount(trade);

        if (Math.Sign(rowSigned) != Math.Sign(tradeSigned) && rowSigned != 0 && tradeSigned != 0)
            return false;

        if (!AmountClose(rowSigned, tradeSigned))
            return false;

        var dayDiff = Math.Abs((row.Date.DayNumber - DateOf(trade).DayNumber));
        return dayDiff <= DateToleranceDays;
    }
}
