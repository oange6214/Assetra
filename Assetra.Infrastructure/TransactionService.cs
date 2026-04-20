using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.Infrastructure;

/// <summary>
/// <see cref="ITransactionService"/> 實作。
///
/// <para>
/// 自 2026-04 起採用 <b>單一真相</b>（single source of truth）架構：
/// 本服務只負責將 <see cref="Trade"/> 寫入 / 更新 / 刪除於 journal；
/// 任何帳戶餘額查詢都必須透過 <see cref="IBalanceQueryService"/> 由交易歷史投影。
/// </para>
///
/// <para>因此原先的「同步更新 CashAccount.Balance / LiabilityAccount.Balance」副作用已全數移除。
/// 餘額與原始金額均由 Trade 歷史重算，不存在帳面與計算值發散的可能。</para>
/// </summary>
public sealed class TransactionService : ITransactionService
{
    private readonly ITradeRepository _trades;

    public TransactionService(ITradeRepository trades)
    {
        _trades = trades ?? throw new ArgumentNullException(nameof(trades));
    }

    public async Task RecordAsync(Trade trade)
    {
        ArgumentNullException.ThrowIfNull(trade);
        Validate(trade);
        await _trades.AddAsync(trade).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Trade trade)
    {
        ArgumentNullException.ThrowIfNull(trade);
        await _trades.RemoveAsync(trade.Id).ConfigureAwait(false);
    }

    public async Task ReplaceAsync(Trade original, Trade replacement)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(replacement);
        Validate(replacement);
        await _trades.UpdateAsync(replacement).ConfigureAwait(false);
    }

    // ─── Validation ──────────────────────────────────────────────────────

    private static void Validate(Trade t)
    {
        if (t.Type is TradeType.LoanBorrow or TradeType.LoanRepay
            && string.IsNullOrWhiteSpace(t.LoanLabel))
            throw new InvalidOperationException(
                $"{t.Type} 必須設定 LoanLabel（貸款名稱）。");

        if (t.Type == TradeType.Transfer && !t.ToCashAccountId.HasValue)
            throw new InvalidOperationException(
                "Transfer 必須設定 ToCashAccountId（目標現金帳戶）。");
    }
}
