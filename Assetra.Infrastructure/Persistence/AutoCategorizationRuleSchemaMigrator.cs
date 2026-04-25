using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Persistence;

internal static class AutoCategorizationRuleSchemaMigrator
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
                CREATE TABLE IF NOT EXISTS auto_categorization_rule (
                    id                   TEXT PRIMARY KEY,
                    keyword_pattern      TEXT NOT NULL,
                    category_id          TEXT NOT NULL,
                    priority             INTEGER NOT NULL DEFAULT 0,
                    is_enabled           INTEGER NOT NULL DEFAULT 1,
                    match_case_sensitive INTEGER NOT NULL DEFAULT 0,
                    created_at           TEXT NOT NULL DEFAULT '',
                    updated_at           TEXT NOT NULL DEFAULT ''
                );
                CREATE INDEX IF NOT EXISTS idx_auto_rule_category ON auto_categorization_rule (category_id);
                CREATE INDEX IF NOT EXISTS idx_auto_rule_priority ON auto_categorization_rule (priority);
                """;
            cmd.ExecuteNonQuery();
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }
}
