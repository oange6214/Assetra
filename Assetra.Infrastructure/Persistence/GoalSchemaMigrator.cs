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
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }
}
