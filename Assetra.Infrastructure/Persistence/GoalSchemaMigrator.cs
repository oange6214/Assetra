using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Persistence;

internal static class GoalSchemaMigrator
{
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

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }
}
