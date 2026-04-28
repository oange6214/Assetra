using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Persistence;

internal static class RealEstateSchemaMigrator
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
                CREATE TABLE IF NOT EXISTS real_estate (
                    id               TEXT PRIMARY KEY,
                    name             TEXT NOT NULL,
                    address          TEXT NOT NULL DEFAULT '',
                    purchase_price   REAL NOT NULL,
                    purchase_date    TEXT NOT NULL,
                    current_value    REAL NOT NULL,
                    mortgage_balance REAL NOT NULL DEFAULT 0,
                    currency         TEXT NOT NULL DEFAULT 'TWD',
                    is_rental        INTEGER NOT NULL DEFAULT 0,
                    status           TEXT NOT NULL DEFAULT 'Active',
                    notes            TEXT,
                    ev_version       INTEGER NOT NULL DEFAULT 0,
                    ev_modified_at   TEXT NOT NULL DEFAULT '',
                    ev_device_id     TEXT NOT NULL DEFAULT '',
                    is_deleted       INTEGER NOT NULL DEFAULT 0,
                    is_pending_push  INTEGER NOT NULL DEFAULT 1,
                    created_at       TEXT NOT NULL DEFAULT '',
                    updated_at       TEXT NOT NULL DEFAULT ''
                );
                """;
            cmd.ExecuteNonQuery();

            using var rentalCmd = conn.CreateCommand();
            rentalCmd.Transaction = tx;
            rentalCmd.CommandText = """
                CREATE TABLE IF NOT EXISTS rental_income_record (
                    id               TEXT PRIMARY KEY,
                    real_estate_id   TEXT NOT NULL,
                    month            TEXT NOT NULL,
                    rent_amount      REAL NOT NULL,
                    expenses         REAL NOT NULL DEFAULT 0,
                    currency         TEXT NOT NULL DEFAULT 'TWD',
                    notes            TEXT,
                    FOREIGN KEY(real_estate_id) REFERENCES real_estate(id) ON DELETE CASCADE
                );
                """;
            rentalCmd.ExecuteNonQuery();

            using var idxCmd = conn.CreateCommand();
            idxCmd.Transaction = tx;
            idxCmd.CommandText = """
                CREATE INDEX IF NOT EXISTS idx_rental_income_property ON rental_income_record(real_estate_id);
                CREATE INDEX IF NOT EXISTS idx_rental_income_month ON rental_income_record(month);
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
