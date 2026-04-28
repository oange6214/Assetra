using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Persistence;

internal static class RetirementSchemaMigrator
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
                CREATE TABLE IF NOT EXISTS retirement_account (
                    id                            TEXT PRIMARY KEY,
                    name                          TEXT NOT NULL,
                    account_type                  TEXT NOT NULL DEFAULT 'LaborPension',
                    provider                      TEXT NOT NULL DEFAULT '',
                    balance                       REAL NOT NULL DEFAULT 0,
                    employee_contribution_rate    REAL NOT NULL DEFAULT 0,
                    employer_contribution_rate    REAL NOT NULL DEFAULT 0,
                    years_of_service              INTEGER NOT NULL DEFAULT 0,
                    legal_withdrawal_age          INTEGER NOT NULL DEFAULT 65,
                    opened_date                   TEXT NOT NULL,
                    currency                      TEXT NOT NULL DEFAULT 'TWD',
                    status                        TEXT NOT NULL DEFAULT 'Active',
                    notes                         TEXT,
                    ev_version                    INTEGER NOT NULL DEFAULT 0,
                    ev_modified_at                TEXT NOT NULL DEFAULT '',
                    ev_device_id                  TEXT NOT NULL DEFAULT '',
                    is_deleted                    INTEGER NOT NULL DEFAULT 0,
                    is_pending_push               INTEGER NOT NULL DEFAULT 1,
                    created_at                    TEXT NOT NULL DEFAULT '',
                    updated_at                    TEXT NOT NULL DEFAULT ''
                );
                """;
            cmd.ExecuteNonQuery();

            using var contribCmd = conn.CreateCommand();
            contribCmd.Transaction = tx;
            contribCmd.CommandText = """
                CREATE TABLE IF NOT EXISTS retirement_contribution (
                    id                TEXT PRIMARY KEY,
                    account_id        TEXT NOT NULL,
                    year              INTEGER NOT NULL,
                    employee_amount   REAL NOT NULL DEFAULT 0,
                    employer_amount   REAL NOT NULL DEFAULT 0,
                    currency          TEXT NOT NULL DEFAULT 'TWD',
                    notes             TEXT,
                    FOREIGN KEY(account_id) REFERENCES retirement_account(id) ON DELETE CASCADE
                );
                """;
            contribCmd.ExecuteNonQuery();

            using var idxCmd = conn.CreateCommand();
            idxCmd.Transaction = tx;
            idxCmd.CommandText = """
                CREATE INDEX IF NOT EXISTS idx_retirement_contrib_account ON retirement_contribution(account_id);
                CREATE INDEX IF NOT EXISTS idx_retirement_contrib_year ON retirement_contribution(year);
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
