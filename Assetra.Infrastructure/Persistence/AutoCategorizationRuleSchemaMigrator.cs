using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Persistence;

internal static class AutoCategorizationRuleSchemaMigrator
{
    private static readonly HashSet<string> AllowedColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "name", "match_field", "match_type", "applies_to",
        "version", "last_modified_at", "last_modified_by_device", "is_deleted", "is_pending_push",
    };

    private static readonly HashSet<string> AllowedTypeDefs = new(StringComparer.OrdinalIgnoreCase)
    {
        "TEXT",
        "TEXT NOT NULL DEFAULT ''",
        "INTEGER NOT NULL DEFAULT 0",
        "INTEGER NOT NULL DEFAULT 1",
        "INTEGER NOT NULL DEFAULT 3",
    };

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

            SqliteSchemaHelper.MigrateAddColumn(conn, tx, "auto_categorization_rule",
                "name", "TEXT", AllowedColumns, AllowedTypeDefs);
            SqliteSchemaHelper.MigrateAddColumn(conn, tx, "auto_categorization_rule",
                "match_field", "INTEGER NOT NULL DEFAULT 3", AllowedColumns, AllowedTypeDefs);
            SqliteSchemaHelper.MigrateAddColumn(conn, tx, "auto_categorization_rule",
                "match_type", "INTEGER NOT NULL DEFAULT 0", AllowedColumns, AllowedTypeDefs);
            SqliteSchemaHelper.MigrateAddColumn(conn, tx, "auto_categorization_rule",
                "applies_to", "INTEGER NOT NULL DEFAULT 3", AllowedColumns, AllowedTypeDefs);

            // v0.20.11: cloud sync columns.
            SqliteSchemaHelper.MigrateAddColumn(conn, tx, "auto_categorization_rule",
                "version", "INTEGER NOT NULL DEFAULT 0", AllowedColumns, AllowedTypeDefs);
            SqliteSchemaHelper.MigrateAddColumn(conn, tx, "auto_categorization_rule",
                "last_modified_at", "TEXT NOT NULL DEFAULT ''", AllowedColumns, AllowedTypeDefs);
            SqliteSchemaHelper.MigrateAddColumn(conn, tx, "auto_categorization_rule",
                "last_modified_by_device", "TEXT NOT NULL DEFAULT ''", AllowedColumns, AllowedTypeDefs);
            SqliteSchemaHelper.MigrateAddColumn(conn, tx, "auto_categorization_rule",
                "is_deleted", "INTEGER NOT NULL DEFAULT 0", AllowedColumns, AllowedTypeDefs);
            SqliteSchemaHelper.MigrateAddColumn(conn, tx, "auto_categorization_rule",
                "is_pending_push", "INTEGER NOT NULL DEFAULT 0", AllowedColumns, AllowedTypeDefs);

            using (var idx = conn.CreateCommand())
            {
                idx.Transaction = tx;
                idx.CommandText =
                    "CREATE INDEX IF NOT EXISTS idx_auto_rule_pending ON auto_categorization_rule (is_pending_push) WHERE is_pending_push = 1;";
                idx.ExecuteNonQuery();
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }
}
