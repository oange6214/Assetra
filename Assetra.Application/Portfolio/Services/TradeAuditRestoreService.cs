using System.Text.Json;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.Application.Portfolio.Services;

/// <summary>
/// Restores a deleted/replaced trade from its <see cref="TradeAuditEntry.TradeJson"/>
/// snapshot. Inserts the recovered trade with a NEW <see cref="Guid"/> so it never
/// collides with an active row that may already exist (edit-replace path: the audit
/// captured BEFORE delete; user may have continued working — adding the original Id
/// back risks a UNIQUE constraint failure or, worse, silently overwriting newer
/// data).
///
/// <para>
/// Returns the new <see cref="Trade.Id"/>. Caller (AuditLogViewModel) shows a
/// notification suggesting the user review the restored row in the trade log.
/// </para>
/// </summary>
public sealed class TradeAuditRestoreService
{
    private readonly ITradeRepository _trades;

    public TradeAuditRestoreService(ITradeRepository trades)
    {
        _trades = trades ?? throw new ArgumentNullException(nameof(trades));
    }

    public async Task<Guid> RestoreAsync(string tradeJson, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tradeJson);

        var original = JsonSerializer.Deserialize<Trade>(tradeJson)
            ?? throw new InvalidOperationException("Audit row's trade snapshot was empty after deserialisation.");

        // Always insert with a new Id to avoid colliding with whatever the user
        // has done since the snapshot was captured.
        var restored = original with { Id = Guid.NewGuid() };
        await _trades.AddAsync(restored, ct).ConfigureAwait(false);
        return restored.Id;
    }
}
