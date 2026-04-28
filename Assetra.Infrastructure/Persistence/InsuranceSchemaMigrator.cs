using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Persistence;

internal static class InsuranceSchemaMigrator
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
                CREATE TABLE IF NOT EXISTS insurance_policy (
                    id                  TEXT PRIMARY KEY,
                    name                TEXT NOT NULL,
                    policy_number       TEXT NOT NULL DEFAULT '',
                    type                TEXT NOT NULL DEFAULT 'Other',
                    insurer             TEXT NOT NULL DEFAULT '',
                    start_date          TEXT NOT NULL,
                    maturity_date       TEXT,
                    face_value          REAL NOT NULL DEFAULT 0,
                    current_cash_value  REAL NOT NULL DEFAULT 0,
                    annual_premium      REAL NOT NULL DEFAULT 0,
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

            using var premiumCmd = conn.CreateCommand();
            premiumCmd.Transaction = tx;
            premiumCmd.CommandText = """
                CREATE TABLE IF NOT EXISTS insurance_premium_record (
                    id          TEXT PRIMARY KEY,
                    policy_id   TEXT NOT NULL,
                    paid_date   TEXT NOT NULL,
                    amount      REAL NOT NULL,
                    currency    TEXT NOT NULL DEFAULT 'TWD',
                    notes       TEXT,
                    FOREIGN KEY(policy_id) REFERENCES insurance_policy(id) ON DELETE CASCADE
                );
                """;
            premiumCmd.ExecuteNonQuery();

            using var idxCmd = conn.CreateCommand();
            idxCmd.Transaction = tx;
            idxCmd.CommandText = """
                CREATE INDEX IF NOT EXISTS idx_insurance_premium_policy ON insurance_premium_record(policy_id);
                CREATE INDEX IF NOT EXISTS idx_insurance_premium_date ON insurance_premium_record(paid_date);
                """;
            idxCmd.ExecuteNonQuery();

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }
}
