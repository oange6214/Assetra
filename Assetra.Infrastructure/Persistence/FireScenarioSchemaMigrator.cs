using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Persistence;

internal static class FireScenarioSchemaMigrator
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
                CREATE TABLE IF NOT EXISTS fire_scenario (
                    id                         TEXT PRIMARY KEY,
                    name                       TEXT NOT NULL,
                    mode                       INTEGER NOT NULL,
                    net_worth_source           INTEGER NOT NULL,
                    portfolio_group_id         TEXT NULL,
                    current_net_worth_override TEXT NULL,
                    annual_expenses            TEXT NOT NULL,
                    annual_savings             TEXT NOT NULL,
                    expected_annual_return     TEXT NOT NULL,
                    return_mode                INTEGER NOT NULL,
                    inflation_rate             TEXT NULL,
                    savings_growth_rate        TEXT NULL,
                    expense_growth_rate        TEXT NULL,
                    withdrawal_rate            TEXT NOT NULL,
                    current_age                INTEGER NULL,
                    life_expectancy_age        INTEGER NULL,
                    retirement_annual_expenses TEXT NULL,
                    custom_target_amount       TEXT NULL,
                    include_taxes              INTEGER NOT NULL,
                    notes                      TEXT NULL,
                    is_default                 INTEGER NOT NULL,
                    created_at                 TEXT NOT NULL,
                    updated_at                 TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS fire_cash_flow_event (
                    id                 TEXT PRIMARY KEY,
                    scenario_id        TEXT NOT NULL,
                    name               TEXT NOT NULL,
                    start_year_offset  INTEGER NOT NULL,
                    end_year_offset    INTEGER NULL,
                    annual_amount      TEXT NOT NULL,
                    direction          INTEGER NOT NULL,
                    growth_mode        INTEGER NOT NULL,
                    custom_growth_rate TEXT NULL,
                    notes              TEXT NULL,
                    FOREIGN KEY (scenario_id) REFERENCES fire_scenario(id) ON DELETE CASCADE
                );

                CREATE INDEX IF NOT EXISTS idx_fire_cash_flow_event_scenario
                    ON fire_cash_flow_event (scenario_id);

                CREATE UNIQUE INDEX IF NOT EXISTS idx_fire_scenario_default
                    ON fire_scenario (is_default)
                    WHERE is_default = 1;
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
