using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Persistence;

internal static class PhysicalAssetSchemaMigrator
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
                CREATE TABLE IF NOT EXISTS physical_asset (
                    id                  TEXT PRIMARY KEY,
                    name                TEXT NOT NULL,
                    category            TEXT NOT NULL DEFAULT 'Other',
                    description         TEXT NOT NULL DEFAULT '',
                    acquisition_cost    REAL NOT NULL DEFAULT 0,
                    acquisition_date    TEXT NOT NULL,
                    current_value       REAL NOT NULL DEFAULT 0,
                    valuation_method    TEXT NOT NULL DEFAULT '',
                    currency            TEXT NOT NULL DEFAULT 'TWD',
                    status              TEXT NOT NULL DEFAULT 'Active',
                    notes               TEXT,
                    ev_version          INTEGER NOT NULL DEFAULT 0,
                    ev_modified_at      TEXT NOT NULL DEFAULT '',
                    ev_device_id        TEXT NOT NULL DEFAULT '',
                    is_deleted          INTEGER NOT NULL DEFAULT 0,
                    is_pending_push     INTEGER NOT NULL DEFAULT 1,
                    created_at          TEXT NOT NULL DEFAULT '',
                    updated_at          TEXT NOT NULL DEFAULT ''
                );
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
