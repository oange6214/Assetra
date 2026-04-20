using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Persistence;

/// <summary>
/// Shared helpers for idempotent SQLite schema migrations.
/// All methods validate table and column names against allowlists to prevent SQL injection.
/// </summary>
internal static class SqliteSchemaHelper
{
    /// <summary>
    /// All table names that are allowed to appear in schema-migration queries.
    /// Keep in sync with the tables created by the various *SqliteRepository classes.
    /// </summary>
    private static readonly HashSet<string> KnownTables = new(StringComparer.OrdinalIgnoreCase)
    {
        "portfolio",
        "liability_account",
        "cash_account",
        "trade",
        "alert",
        "portfolio_snapshot",
        "portfolio_position_log",
        "asset_group",
        "asset",
        "asset_event",
    };

    /// <summary>
    /// Returns <c>true</c> if <paramref name="column"/> exists in <paramref name="table"/>.
    /// <para>
    /// Uses <c>pragma_table_info()</c> as a table-valued function so the <em>column</em> name
    /// is fully parameterized.  The <em>table</em> name is validated against
    /// <see cref="KnownTables"/> before being interpolated into the SQL — this prevents
    /// injection while staying compatible with SQLite's PRAGMA syntax that does not accept
    /// bind parameters for the table-name argument.
    /// </para>
    /// </summary>
    public static bool ColumnExists(
        SqliteConnection conn, string table, string column, SqliteTransaction? tx = null)
    {
        if (!KnownTables.Contains(table))
            throw new ArgumentException($"Unknown table '{table}' — add it to SqliteSchemaHelper.KnownTables first.");

        using var cmd = conn.CreateCommand();
        if (tx is not null) cmd.Transaction = tx;
        // Table name is allowlist-validated above; column name is parameterized.
        cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = $col;";
        cmd.Parameters.AddWithValue("$col", column);
        return (long)(cmd.ExecuteScalar() ?? 0L) > 0;
    }

    /// <summary>
    /// Idempotent helper — adds <paramref name="column"/> with <paramref name="typeDef"/>
    /// to <paramref name="table"/> only if the column does not already exist.
    /// Both <paramref name="column"/> and <paramref name="typeDef"/> are validated against
    /// caller-supplied allowlists before being interpolated into the ALTER TABLE statement.
    /// </summary>
    /// <param name="allowedColumns">Caller's per-table allowlist for safe column names.</param>
    /// <param name="allowedTypeDefs">Caller's per-table allowlist for safe column type definitions.</param>
    public static void MigrateAddColumn(
        SqliteConnection conn,
        SqliteTransaction tx,
        string table,
        string column,
        string typeDef,
        HashSet<string> allowedColumns,
        HashSet<string> allowedTypeDefs)
    {
        if (!KnownTables.Contains(table))
            throw new ArgumentException($"Unknown table: {table}");
        if (!allowedColumns.Contains(column))
            throw new ArgumentException($"Column '{column}' not in allowlist for table '{table}'.");
        if (!allowedTypeDefs.Contains(typeDef))
            throw new ArgumentException($"Type '{typeDef}' not in allowlist for table '{table}'.");

        if (ColumnExists(conn, table, column, tx))
            return;

        using var alter = conn.CreateCommand();
        alter.Transaction = tx;
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {typeDef};";
        alter.ExecuteNonQuery();
    }
}
