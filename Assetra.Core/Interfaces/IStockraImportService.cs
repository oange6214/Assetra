namespace Assetra.Core.Interfaces;

public interface IStockraImportService
{
    /// <summary>
    /// Import compatible data from a Stockra DB file into the Assetra DB.
    /// Compatible tables: portfolio, trade, asset_group, asset, asset_event,
    /// portfolio_snapshot, portfolio_position_log, alert.
    /// Skipped: watchlist, custom_strategy, research_template, screener_preset.
    /// If a target table already has rows, that table is skipped (no overwrite).
    /// </summary>
    Task<ImportResult> ImportAsync(string stockraDbPath, CancellationToken ct = default);
}

public sealed record ImportResult(int TotalRows, IReadOnlyDictionary<string, int> PerTable);
