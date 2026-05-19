using Assetra.Core.Interfaces.Sync;
using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Sync;

/// <summary>
/// SQLite implementation of <see cref="IPendingPushCounter"/>. Issues one
/// <c>SELECT COUNT(*) WHERE is_pending_push = 1</c> per known table. Tables that
/// don't exist or don't have the column return 0 (tolerates older DBs and
/// non-sync domains like Goal / PortfolioGroup).
/// </summary>
public sealed class SqlitePendingPushCounter : IPendingPushCounter
{
    private readonly string _connectionString;

    private static readonly (string Domain, string Table)[] Targets =
    [
        ("Trade", "trade"),
        ("Portfolio", "portfolio"),
        ("Asset", "asset"),
        ("AssetEvent", "asset_event"),
        ("Category", "category"),
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

        foreach (var (domain, table) in Targets)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT COUNT(*) FROM {table} WHERE is_pending_push = 1;";
                var scalar = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
                var n = scalar is null or DBNull
                    ? 0
                    : Convert.ToInt32(scalar, System.Globalization.CultureInfo.InvariantCulture);
                result[domain] = n;
            }
            catch (SqliteException)
            {
                // Table or column doesn't exist yet (legacy DB / domain without
                // sync infra) — treat as 0 pending.
                result[domain] = 0;
            }
        }

        return result;
    }
}
