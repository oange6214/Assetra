using Assetra.Core.Interfaces.Reconciliation;
using Assetra.Core.Models;
using Assetra.Core.Models.Import;

namespace Assetra.Core.DomainServices.Reconciliation;

/// <summary>
/// 預設對帳比對器：日期 ± 1 天、金額 abs 容忍 ± 0.005。
/// 交易的 SignedAmount 取 <see cref="Trade.CashAmount"/>，若為 <c>null</c> 則退回 <c>Price × Quantity</c>。
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
        return trade.CashAmount ?? trade.Price * trade.Quantity;
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
