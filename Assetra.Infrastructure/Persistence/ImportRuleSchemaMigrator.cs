using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Persistence;

internal static class ImportRuleSchemaMigrator
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
                CREATE TABLE IF NOT EXISTS import_rule (
                    id              TEXT PRIMARY KEY,
                    name            TEXT NOT NULL,
                    match_field     INTEGER NOT NULL,
                    match_type      INTEGER NOT NULL,
                    pattern         TEXT NOT NULL,
                    case_sensitive  INTEGER NOT NULL DEFAULT 0,
                    category_id     TEXT NOT NULL,
                    priority        INTEGER NOT NULL DEFAULT 0,
                    is_enabled      INTEGER NOT NULL DEFAULT 1,
                    created_at      TEXT NOT NULL,
                    updated_at      TEXT NOT NULL
                );

                CREATE INDEX IF NOT EXISTS idx_import_rule_priority
                    ON import_rule (is_enabled DESC, priority ASC);
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
