using System.Globalization;
using Assetra.Core.Interfaces.Fire;
using Assetra.Core.Models.Fire;
using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Persistence;

public sealed class FireScenarioSqliteRepository : IFireScenarioRepository, IAsyncDisposable
{
    private readonly string _connectionString;

    private const string ScenarioSelect = """
        id, name, mode, net_worth_source, portfolio_group_id, current_net_worth_override,
        annual_expenses, annual_savings, expected_annual_return, return_mode, inflation_rate,
        savings_growth_rate, expense_growth_rate, withdrawal_rate, current_age, life_expectancy_age,
        retirement_annual_expenses, custom_target_amount, include_taxes, notes, is_default,
        created_at, updated_at
        """;

    public FireScenarioSqliteRepository(string dbPath)
    {
        _connectionString = $"Data Source={dbPath};Foreign Keys=True";
        FireScenarioSchemaMigrator.EnsureInitialized(_connectionString);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public async Task<IReadOnlyList<FireScenario>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {ScenarioSelect} FROM fire_scenario ORDER BY is_default DESC, name ASC;";

        var rows = new List<FireScenario>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            rows.Add(ReadScenario(reader));
        return rows;
    }

    public async Task<FireScenario?> GetDefaultAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {ScenarioSelect} FROM fire_scenario WHERE is_default = 1 LIMIT 1;";

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? ReadScenario(reader) : null;
    }

    public async Task<FireScenario?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {ScenarioSelect} FROM fire_scenario WHERE id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", id.ToString());

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? ReadScenario(reader) : null;
    }

    public async Task<IReadOnlyList<FireCashFlowEvent>> GetCashFlowEventsAsync(
        Guid scenarioId,
        CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, scenario_id, name, start_year_offset, end_year_offset, annual_amount,
                   direction, growth_mode, custom_growth_rate, notes
            FROM fire_cash_flow_event
            WHERE scenario_id = $scenario_id
            ORDER BY start_year_offset ASC, name ASC;
            """;
        cmd.Parameters.AddWithValue("$scenario_id", scenarioId.ToString());

        var rows = new List<FireCashFlowEvent>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            rows.Add(ReadEvent(reader));
        return rows;
    }

    public async Task UpsertAsync(
        FireScenario scenario,
        IReadOnlyList<FireCashFlowEvent> cashFlowEvents,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(cashFlowEvents);

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            if (scenario.IsDefault)
            {
                await using var clearDefault = conn.CreateCommand();
                clearDefault.Transaction = tx;
                clearDefault.CommandText = "UPDATE fire_scenario SET is_default = 0 WHERE is_default = 1;";
                await clearDefault.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                WriteUpsertScenario(cmd, scenario);
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            await using (var deleteEvents = conn.CreateCommand())
            {
                deleteEvents.Transaction = tx;
                deleteEvents.CommandText = "DELETE FROM fire_cash_flow_event WHERE scenario_id = $scenario_id;";
                deleteEvents.Parameters.AddWithValue("$scenario_id", scenario.Id.ToString());
                await deleteEvents.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            foreach (var cashFlowEvent in cashFlowEvents)
            {
                ct.ThrowIfCancellationRequested();
                await using var insertEvent = conn.CreateCommand();
                insertEvent.Transaction = tx;
                WriteInsertEvent(insertEvent, cashFlowEvent);
                await insertEvent.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            await tx.CommitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM fire_scenario WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static void WriteUpsertScenario(SqliteCommand cmd, FireScenario scenario)
    {
        cmd.CommandText = """
            INSERT INTO fire_scenario
                (id, name, mode, net_worth_source, portfolio_group_id, current_net_worth_override,
                 annual_expenses, annual_savings, expected_annual_return, return_mode, inflation_rate,
                 savings_growth_rate, expense_growth_rate, withdrawal_rate, current_age, life_expectancy_age,
                 retirement_annual_expenses, custom_target_amount, include_taxes, notes, is_default,
                 created_at, updated_at)
            VALUES
                ($id, $name, $mode, $net_worth_source, $portfolio_group_id, $current_net_worth_override,
                 $annual_expenses, $annual_savings, $expected_annual_return, $return_mode, $inflation_rate,
                 $savings_growth_rate, $expense_growth_rate, $withdrawal_rate, $current_age, $life_expectancy_age,
                 $retirement_annual_expenses, $custom_target_amount, $include_taxes, $notes, $is_default,
                 $created_at, $updated_at)
            ON CONFLICT(id) DO UPDATE SET
                name = excluded.name,
                mode = excluded.mode,
                net_worth_source = excluded.net_worth_source,
                portfolio_group_id = excluded.portfolio_group_id,
                current_net_worth_override = excluded.current_net_worth_override,
                annual_expenses = excluded.annual_expenses,
                annual_savings = excluded.annual_savings,
                expected_annual_return = excluded.expected_annual_return,
                return_mode = excluded.return_mode,
                inflation_rate = excluded.inflation_rate,
                savings_growth_rate = excluded.savings_growth_rate,
                expense_growth_rate = excluded.expense_growth_rate,
                withdrawal_rate = excluded.withdrawal_rate,
                current_age = excluded.current_age,
                life_expectancy_age = excluded.life_expectancy_age,
                retirement_annual_expenses = excluded.retirement_annual_expenses,
                custom_target_amount = excluded.custom_target_amount,
                include_taxes = excluded.include_taxes,
                notes = excluded.notes,
                is_default = excluded.is_default,
                updated_at = excluded.updated_at;
            """;
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("$id", scenario.Id.ToString());
        cmd.Parameters.AddWithValue("$name", scenario.Name);
        cmd.Parameters.AddWithValue("$mode", (int)scenario.Mode);
        cmd.Parameters.AddWithValue("$net_worth_source", (int)scenario.NetWorthSource);
        cmd.Parameters.AddWithValue("$portfolio_group_id", scenario.PortfolioGroupId?.ToString() ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$current_net_worth_override", ToDb(scenario.CurrentNetWorthOverride));
        cmd.Parameters.AddWithValue("$annual_expenses", ToDb(scenario.AnnualExpenses));
        cmd.Parameters.AddWithValue("$annual_savings", ToDb(scenario.AnnualSavings));
        cmd.Parameters.AddWithValue("$expected_annual_return", ToDb(scenario.ExpectedAnnualReturn));
        cmd.Parameters.AddWithValue("$return_mode", (int)scenario.ReturnMode);
        cmd.Parameters.AddWithValue("$inflation_rate", ToDb(scenario.InflationRate));
        cmd.Parameters.AddWithValue("$savings_growth_rate", ToDb(scenario.SavingsGrowthRate));
        cmd.Parameters.AddWithValue("$expense_growth_rate", ToDb(scenario.ExpenseGrowthRate));
        cmd.Parameters.AddWithValue("$withdrawal_rate", ToDb(scenario.WithdrawalRate));
        cmd.Parameters.AddWithValue("$current_age", scenario.CurrentAge ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$life_expectancy_age", scenario.LifeExpectancyAge ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$retirement_annual_expenses", ToDb(scenario.RetirementAnnualExpenses));
        cmd.Parameters.AddWithValue("$custom_target_amount", ToDb(scenario.CustomTargetAmount));
        cmd.Parameters.AddWithValue("$include_taxes", scenario.IncludeTaxes ? 1 : 0);
        cmd.Parameters.AddWithValue("$notes", scenario.Notes ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$is_default", scenario.IsDefault ? 1 : 0);
        cmd.Parameters.AddWithValue("$created_at", scenario.CreatedAt.ToUniversalTime().ToString("O"));
        cmd.Parameters.AddWithValue("$updated_at", scenario.UpdatedAt.ToUniversalTime().ToString("O"));
    }

    private static void WriteInsertEvent(SqliteCommand cmd, FireCashFlowEvent cashFlowEvent)
    {
        cmd.CommandText = """
            INSERT INTO fire_cash_flow_event
                (id, scenario_id, name, start_year_offset, end_year_offset, annual_amount,
                 direction, growth_mode, custom_growth_rate, notes)
            VALUES
                ($id, $scenario_id, $name, $start_year_offset, $end_year_offset, $annual_amount,
                 $direction, $growth_mode, $custom_growth_rate, $notes);
            """;
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("$id", cashFlowEvent.Id.ToString());
        cmd.Parameters.AddWithValue("$scenario_id", cashFlowEvent.ScenarioId.ToString());
        cmd.Parameters.AddWithValue("$name", cashFlowEvent.Name);
        cmd.Parameters.AddWithValue("$start_year_offset", cashFlowEvent.StartYearOffset);
        cmd.Parameters.AddWithValue("$end_year_offset", cashFlowEvent.EndYearOffset ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$annual_amount", ToDb(cashFlowEvent.AnnualAmount));
        cmd.Parameters.AddWithValue("$direction", (int)cashFlowEvent.Direction);
        cmd.Parameters.AddWithValue("$growth_mode", (int)cashFlowEvent.GrowthMode);
        cmd.Parameters.AddWithValue("$custom_growth_rate", ToDb(cashFlowEvent.CustomGrowthRate));
        cmd.Parameters.AddWithValue("$notes", cashFlowEvent.Notes ?? (object)DBNull.Value);
    }

    private static FireScenario ReadScenario(SqliteDataReader reader) =>
        new(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            (FireScenarioMode)reader.GetInt32(2),
            (FireNetWorthSource)reader.GetInt32(3),
            reader.IsDBNull(4) ? null : Guid.Parse(reader.GetString(4)),
            ReadNullableDecimal(reader, 5),
            ReadDecimal(reader, 6),
            ReadDecimal(reader, 7),
            ReadDecimal(reader, 8),
            (FireReturnMode)reader.GetInt32(9),
            ReadNullableDecimal(reader, 10),
            ReadNullableDecimal(reader, 11),
            ReadNullableDecimal(reader, 12),
            ReadDecimal(reader, 13),
            reader.IsDBNull(14) ? null : reader.GetInt32(14),
            reader.IsDBNull(15) ? null : reader.GetInt32(15),
            ReadNullableDecimal(reader, 16),
            ReadNullableDecimal(reader, 17),
            reader.GetInt32(18) != 0,
            reader.IsDBNull(19) ? null : reader.GetString(19),
            reader.GetInt32(20) != 0,
            DateTimeOffset.Parse(reader.GetString(21), CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(reader.GetString(22), CultureInfo.InvariantCulture));

    private static FireCashFlowEvent ReadEvent(SqliteDataReader reader) =>
        new(
            Guid.Parse(reader.GetString(0)),
            Guid.Parse(reader.GetString(1)),
            reader.GetString(2),
            reader.GetInt32(3),
            reader.IsDBNull(4) ? null : reader.GetInt32(4),
            ReadDecimal(reader, 5),
            (FireCashFlowDirection)reader.GetInt32(6),
            (FireCashFlowGrowthMode)reader.GetInt32(7),
            ReadNullableDecimal(reader, 8),
            reader.IsDBNull(9) ? null : reader.GetString(9));

    private static object ToDb(decimal value) =>
        value.ToString(CultureInfo.InvariantCulture);

    private static object ToDb(decimal? value) =>
        value.HasValue ? ToDb(value.Value) : DBNull.Value;

    private static decimal ReadDecimal(SqliteDataReader reader, int ordinal) =>
        decimal.Parse(reader.GetString(ordinal), NumberStyles.Number, CultureInfo.InvariantCulture);

    private static decimal? ReadNullableDecimal(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : ReadDecimal(reader, ordinal);
}
