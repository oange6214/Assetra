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
            using (var cmd = conn.CreateCommand())
            {
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
            }

            EnsureColumn(conn, tx, "name", "TEXT");
            EnsureColumn(conn, tx, "match_field", "INTEGER NOT NULL DEFAULT 3");   // AnyText
            EnsureColumn(conn, tx, "match_type", "INTEGER NOT NULL DEFAULT 0");    // Contains
            EnsureColumn(conn, tx, "applies_to", "INTEGER NOT NULL DEFAULT 3");    // Both

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    private static void EnsureColumn(SqliteConnection conn, SqliteTransaction tx, string column, string typeAndDefault)
    {
        using var probe = conn.CreateCommand();
        probe.Transaction = tx;
        probe.CommandText = $"SELECT 1 FROM pragma_table_info('auto_categorization_rule') WHERE name = '{column}';";
        var exists = probe.ExecuteScalar();
        if (exists is not null) return;

        using var alter = conn.CreateCommand();
        alter.Transaction = tx;
        alter.CommandText = $"ALTER TABLE auto_categorization_rule ADD COLUMN {column} {typeAndDefault};";
        alter.ExecuteNonQuery();
    }
}
