using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Import;
using Assetra.Core.Models;
using Assetra.Core.Models.Import;

namespace Assetra.Application.Import;

/// <summary>
/// 將通過預覽的 <see cref="ImportBatch"/> 轉換為 <see cref="Trade"/> 並寫入資料庫。
/// 衝突列依各自的 <see cref="ImportConflictResolution"/> 處理：
/// Skip 跳過、AddAnyway 仍建立、Overwrite 視為以新覆蓋舊（先刪後建）。
/// </summary>
public sealed class ImportApplyService : IImportApplyService
{
    private readonly ITradeRepository _trades;

    public ImportApplyService(ITradeRepository trades)
    {
        _trades = trades ?? throw new ArgumentNullException(nameof(trades));
    }

    public async Task<ImportApplyResult> ApplyAsync(
        ImportBatch batch,
        ImportApplyOptions options,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(options);

        var conflictByRowIndex = batch.Conflicts.ToDictionary(c => c.Row.RowIndex);
        var warnings = new List<string>();

        int applied = 0, skipped = 0, overwritten = 0;

        foreach (var row in batch.Rows)
        {
            ct.ThrowIfCancellationRequested();

            var resolution = conflictByRowIndex.TryGetValue(row.RowIndex, out var conflict)
                ? conflict.Resolution
                : ImportConflictResolution.AddAnyway;

            if (conflictByRowIndex.ContainsKey(row.RowIndex)
                && resolution == ImportConflictResolution.Skip)
            {
                skipped++;
                continue;
            }

            if (resolution == ImportConflictResolution.Overwrite
                && conflict?.ExistingTradeId is { } existingId)
            {
                await _trades.RemoveAsync(existingId).ConfigureAwait(false);
                overwritten++;
            }

            var trade = MapToTrade(row, batch.SourceKind, options, warnings);
            if (trade is null)
            {
                skipped++;
                continue;
            }

            await _trades.AddAsync(trade).ConfigureAwait(false);
            applied++;
        }

        return new ImportApplyResult(
            RowsConsidered: batch.RowCount,
            RowsApplied: applied,
            RowsSkipped: skipped,
            RowsOverwritten: overwritten,
            Warnings: warnings);
    }

    private static Trade? MapToTrade(
        ImportPreviewRow row,
        ImportSourceKind kind,
        ImportApplyOptions options,
        List<string> warnings)
    {
        var date = row.Date.ToDateTime(TimeOnly.MinValue);
        var note = ComposeNote(row, kind, options);

        return kind switch
        {
            ImportSourceKind.BankStatement => MapBankRow(row, date, note, options),
            ImportSourceKind.BrokerStatement => MapBrokerRow(row, date, note, options, warnings),
            _ => null,
        };
    }

    private static Trade MapBankRow(ImportPreviewRow row, DateTime date, string note, ImportApplyOptions options)
    {
        var type = row.Amount >= 0 ? TradeType.Income : TradeType.Withdrawal;
        return new Trade(
            Id: Guid.NewGuid(),
            Symbol: string.Empty,
            Exchange: string.Empty,
            Name: string.Empty,
            Type: type,
            TradeDate: date,
            Price: 0m,
            Quantity: 1,
            RealizedPnl: null,
            RealizedPnlPct: null,
            CashAmount: Math.Abs(row.Amount),
            CashAccountId: options.CashAccountId,
            Note: note);
    }

    private static Trade? MapBrokerRow(
        ImportPreviewRow row,
        DateTime date,
        string note,
        ImportApplyOptions options,
        List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(row.Symbol) || row.Quantity is not { } qty || qty <= 0m)
        {
            warnings.Add($"Row {row.RowIndex}: missing symbol or quantity, skipped.");
            return null;
        }

        var quantityInt = (int)Math.Round(qty);
        var price = quantityInt > 0 ? row.Amount / quantityInt : 0m;
        var isSell = (row.Counterparty ?? string.Empty).Contains("賣", StringComparison.Ordinal)
            || (row.Counterparty ?? string.Empty).Contains("Sell", StringComparison.OrdinalIgnoreCase);

        return new Trade(
            Id: Guid.NewGuid(),
            Symbol: row.Symbol!.Trim(),
            Exchange: options.Exchange,
            Name: row.Symbol!.Trim(),
            Type: isSell ? TradeType.Sell : TradeType.Buy,
            TradeDate: date,
            Price: Math.Abs(price),
            Quantity: quantityInt,
            RealizedPnl: null,
            RealizedPnlPct: null,
            CashAccountId: options.CashAccountId,
            Note: note);
    }

    private static string ComposeNote(ImportPreviewRow row, ImportSourceKind kind, ImportApplyOptions options)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(row.Counterparty)) parts.Add(row.Counterparty!.Trim());
        if (!string.IsNullOrWhiteSpace(row.Memo)) parts.Add(row.Memo!.Trim());
        if (parts.Count == 0)
        {
            parts.Add(kind == ImportSourceKind.BankStatement
                ? (row.Amount >= 0 ? options.DefaultIncomeNote : options.DefaultExpenseNote)
                : options.DefaultIncomeNote);
        }
        return string.Join(" / ", parts);
    }
}
