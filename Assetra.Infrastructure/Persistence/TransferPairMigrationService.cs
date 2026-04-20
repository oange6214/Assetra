using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.Infrastructure.Persistence;

/// <summary>
/// One-time startup migration: converts legacy Transfer pairs
/// (Withdrawal "轉帳 →" + Deposit "轉帳 ←") into single
/// <see cref="TradeType.Transfer"/> records.
///
/// <para>
/// Safety rules:
/// <list type="bullet">
/// <item><description>
///   Uses <see cref="ITradeRepository.AddAsync"/> / <see cref="ITradeRepository.RemoveAsync"/>
///   directly — never <c>ITransactionService</c> — because the source/target account balances
///   are already correct from the original pair's side-effects.
/// </description></item>
/// <item><description>
///   Only migrates same-amount pairs (<c>srcAmount == dstAmount</c>). Cross-currency pairs
///   (srcAmount ≠ dstAmount) are skipped because the native Transfer record stores a single
///   <c>CashAmount</c> that is applied identically to both legs.
/// </description></item>
/// <item><description>
///   Idempotent: after migration, no more "轉帳 →/←" pairs exist, so re-running does nothing.
/// </description></item>
/// </list>
/// </para>
/// </summary>
public sealed class TransferPairMigrationService
{
    private readonly ITradeRepository _trades;

    public TransferPairMigrationService(ITradeRepository trades) =>
        _trades = trades;

    public async Task MigrateAsync()
    {
        var all = await _trades.GetAllAsync().ConfigureAwait(false);

        // ── collect candidates ───────────────────────────────────────────────
        var withdrawals = all
            .Where(t => t.Type == TradeType.Withdrawal &&
                        t.Note is not null &&
                        t.Note.StartsWith("轉帳 →", StringComparison.Ordinal))
            .ToList();

        if (withdrawals.Count == 0) return;  // nothing to do

        // Build deposit lookup by TradeDate for O(n) matching
        var depositsByDate = all
            .Where(t => t.Type == TradeType.Deposit &&
                        t.Note is not null &&
                        t.Note.StartsWith("轉帳 ←", StringComparison.Ordinal))
            .GroupBy(t => t.TradeDate)
            .ToDictionary(g => g.Key, g => g.ToList());

        var processed = new HashSet<Guid>();

        foreach (var w in withdrawals)
        {
            if (processed.Contains(w.Id) || w.CashAccountId is null) continue;

            // ── parse note: "轉帳 → {dstName} [— {userNote}]" ────────────────
            var afterArrow = w.Note!["轉帳 → ".Length..];
            string dstName;
            string? userNote;
            var separator = afterArrow.IndexOf(" — ", StringComparison.Ordinal);
            if (separator >= 0)
            {
                dstName  = afterArrow[..separator];
                userNote = afterArrow[(separator + 3)..];
                if (string.IsNullOrWhiteSpace(userNote)) userNote = null;
            }
            else
            {
                dstName  = afterArrow;
                userNote = null;
            }

            // ── find matching deposit on same date ────────────────────────────
            if (!depositsByDate.TryGetValue(w.TradeDate, out var candidates)) continue;

            // Deposit note: "轉帳 ← {srcName} [— {userNote}]"
            // srcName of the deposit must equal w.Name (the withdrawal source account name)
            var srcName = w.Name;
            var deposit = candidates.FirstOrDefault(d =>
                !processed.Contains(d.Id) &&
                d.CashAccountId.HasValue &&
                d.Note!.StartsWith($"轉帳 ← {srcName}", StringComparison.Ordinal));

            if (deposit is null) continue;

            // ── only migrate same-amount (same-currency) pairs ────────────────
            if (w.CashAmount is not { } amount || amount <= 0) continue;
            if (deposit.CashAmount != amount) continue;

            // ── create native Transfer (no balance side-effects) ──────────────
            var transfer = new Trade(
                Id:               Guid.NewGuid(),
                Symbol:           srcName,
                Exchange:         string.Empty,
                Name:             $"{srcName} → {dstName}",
                Type:             TradeType.Transfer,
                TradeDate:        w.TradeDate,
                Price:            0,
                Quantity:         1,
                RealizedPnl:      null,
                RealizedPnlPct:   null,
                CashAmount:       amount,
                CashAccountId:    w.CashAccountId,
                ToCashAccountId:  deposit.CashAccountId,
                Note:             userNote);

            await _trades.AddAsync(transfer).ConfigureAwait(false);
            await _trades.RemoveAsync(w.Id).ConfigureAwait(false);
            await _trades.RemoveAsync(deposit.Id).ConfigureAwait(false);

            processed.Add(w.Id);
            processed.Add(deposit.Id);
        }
    }
}
