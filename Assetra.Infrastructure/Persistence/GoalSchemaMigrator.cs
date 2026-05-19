using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Persistence;

internal static class GoalSchemaMigrator
{
    private static readonly HashSet<string> AllowedColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "linked_asset_class",
        "portfolio_group_id",
    };

    private static readonly HashSet<string> AllowedTypeDefs = new(StringComparer.OrdinalIgnoreCase)
    {
        "TEXT",
    };

    public static void EnsureInitialized(string connectionString)
    {
        using var conn = new SqliteConnection(connectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS financial_goal (
                    id              TEXT PRIMARY KEY,
                    name            TEXT NOT NULL,
                    target_amount   REAL NOT NULL,
                    current_amount  REAL NOT NULL DEFAULT 0,
                    deadline        TEXT,
                    notes           TEXT,
                    created_at      TEXT NOT NULL DEFAULT '',
                    updated_at      TEXT NOT NULL DEFAULT ''
                );
                """;
            cmd.ExecuteNonQuery();

            using var milestoneCmd = conn.CreateCommand();
            milestoneCmd.Transaction = tx;
            milestoneCmd.CommandText = """
                CREATE TABLE IF NOT EXISTS goal_milestone (
                    id            TEXT PRIMARY KEY,
                    goal_id       TEXT NOT NULL,
                    target_date   TEXT NOT NULL,
                    target_amount REAL NOT NULL,
                    label         TEXT NOT NULL DEFAULT '',
                    is_achieved   INTEGER NOT NULL DEFAULT 0,
                    FOREIGN KEY(goal_id) REFERENCES financial_goal(id) ON DELETE CASCADE
                );
                """;
            milestoneCmd.ExecuteNonQuery();

            using var milestoneIdx = conn.CreateCommand();
            milestoneIdx.Transaction = tx;
            milestoneIdx.CommandText =
                "CREATE INDEX IF NOT EXISTS idx_goal_milestone_goal ON goal_milestone(goal_id);";
            milestoneIdx.ExecuteNonQuery();

            using var ruleCmd = conn.CreateCommand();
            ruleCmd.Transaction = tx;
            ruleCmd.CommandText = """
                CREATE TABLE IF NOT EXISTS goal_funding_rule (
                    id                      TEXT PRIMARY KEY,
                    goal_id                 TEXT NOT NULL,
                    amount                  REAL NOT NULL,
                    frequency               TEXT NOT NULL,
                    source_cash_account_id  TEXT,
                    start_date              TEXT NOT NULL,
                    end_date                TEXT,
                    is_enabled              INTEGER NOT NULL DEFAULT 1,
                    FOREIGN KEY(goal_id) REFERENCES financial_goal(id) ON DELETE CASCADE
                );
                """;
            ruleCmd.ExecuteNonQuery();

            using var ruleIdx = conn.CreateCommand();
            ruleIdx.Transaction = tx;
            ruleIdx.CommandText =
                "CREATE INDEX IF NOT EXISTS idx_goal_funding_rule_goal ON goal_funding_rule(goal_id);";
            ruleIdx.ExecuteNonQuery();

            // 2026-05-17：Goals short-term compromise — auto-track goal progress
            // from a named asset class (NetWorth / Investments / Cash / RealEstate /
            // Retirement / Physical). null = manual mode (legacy behaviour).
            // Forward-compatible: Portfolio-Groups-Refactor 上線後 migration 為 portfolio_id。
            SqliteSchemaHelper.MigrateAddColumn(conn, tx, "financial_goal",
                "linked_asset_class", "TEXT", AllowedColumns, AllowedTypeDefs);

            // Portfolio-Groups-Refactor P1 — Goal 可選連結到具體群組（升級 LinkedAssetClass
            // 的方式：之後可以更精細，例：goal 連結到「買房儲蓄」群組而非「投資」整類）。
            // 不 backfill：null 代表 user 未選，progress 走 manual / LinkedAssetClass 路徑。
            SqliteSchemaHelper.MigrateAddColumn(conn, tx, "financial_goal",
                "portfolio_group_id", "TEXT", AllowedColumns, AllowedTypeDefs);

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }
}
