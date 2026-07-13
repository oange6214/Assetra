using Assetra.Core.Interfaces.Sync;
using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Sync;

/// <summary>
/// SQLite implementation of <see cref="IPendingPushCounter"/>. Issues one
/// <c>SELECT COUNT(*) WHERE is_pending_push = 1</c> per known table. Tables that
/// don't exist or don't have the column return 0 (tolerates older DBs and
/// non-sync domains like Goal / PortfolioGroup) — detected up-front via metadata,
/// never via caught exceptions.
/// </summary>
public sealed class SqlitePendingPushCounter : IPendingPushCounter
{
    private readonly string _connectionString;

    private static readonly (string Domain, string Table)[] Targets =
    [
        ("Trade", "trade"),
        ("Portfolio", "portfolio"),
        ("Asset", "asset"),
        ("AssetGroup", "asset_group"),
        ("AssetEvent", "asset_event"),
        ("Category", "expense_category"),
        ("AutoCategorizationRule", "auto_categorization_rule"),
        ("Recurring", "recurring_transaction"),
        ("RealEstate", "real_estate"),
        ("Insurance", "insurance_policy"),
        ("Retirement", "retirement_account"),
        ("PhysicalAsset", "physical_asset"),
        ("Alert", "alert"),
        ("FinancialGoal", "financial_goal"),
        ("PortfolioGroup", "portfolio_group"),
    ];

    public SqlitePendingPushCounter(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
    }

    public async Task<IReadOnlyDictionary<string, int>> CountPendingByDomainAsync(CancellationToken ct = default)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);

        // 先用 metadata 查出「存在且有 is_pending_push 欄位」的表，只對這些跑 COUNT。
        // 不再靠 try/catch SqliteException 判斷缺表/缺欄——那會對每個不符的 domain（如舊 DB、
        // 或 Category 表名寫錯成 category）每次呼叫都丟一顆 first-chance 例外，debug Output
        // 一直洗、也是「拿例外當控制流程」的壞味道。此服務會隨 sync 訊號/輪詢反覆呼叫，故影響放大。
        var syncable = await GetSyncableTablesAsync(conn, ct).ConfigureAwait(false);

        foreach (var (domain, table) in Targets)
        {
            ct.ThrowIfCancellationRequested();
            if (!syncable.Contains(table))
            {
                result[domain] = 0; // legacy DB / 尚無 sync infra 的 domain
                continue;
            }

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM {table} WHERE is_pending_push = 1;";
            var scalar = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            result[domain] = scalar is null or DBNull
                ? 0
                : Convert.ToInt32(scalar, System.Globalization.CultureInfo.InvariantCulture);
        }

        return result;
    }

    /// <summary>一次查出所有「有 is_pending_push 欄位」的表名（走 pragma_table_info 表函式）。</summary>
    private static async Task<HashSet<string>> GetSyncableTablesAsync(
        SqliteConnection conn, CancellationToken ct)
    {
        var tables = new HashSet<string>(StringComparer.Ordinal);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT m.name FROM sqlite_master m " +
            "JOIN pragma_table_info(m.name) p ON p.name = 'is_pending_push' " +
            "WHERE m.type = 'table';";
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            tables.Add(reader.GetString(0));
        return tables;
    }
}
