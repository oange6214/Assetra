using Microsoft.Data.Sqlite;
using Assetra.Core.Interfaces;

namespace Assetra.Infrastructure.Persistence;

public sealed class StockraImportService : IStockraImportService
{
    private readonly string _targetDbPath;

    private static readonly IReadOnlyList<string> CopyableTables = new[]
    {
        "portfolio",
        "trade",
        "asset_group",
        "asset",
        "asset_event",
        "portfolio_snapshot",
        "portfolio_position_log",
        "alert",
    };

    public StockraImportService(string targetDbPath)
    {
        _targetDbPath = targetDbPath;
    }

    public async Task<ImportResult> ImportAsync(string stockraDbPath, CancellationToken ct = default)
    {
        if (!File.Exists(stockraDbPath))
            return new ImportResult(0, new Dictionary<string, int>());

        var perTable = new Dictionary<string, int>();

        await using var conn = new SqliteConnection($"Data Source={_targetDbPath}");
        await conn.OpenAsync(ct);

        await using (var attach = conn.CreateCommand())
        {
            attach.CommandText = "ATTACH DATABASE @src AS src;";
            attach.Parameters.AddWithValue("@src", stockraDbPath);
            await attach.ExecuteNonQueryAsync(ct);
        }

        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);

        try
        {
            foreach (var table in CopyableTables)
            {
                if (!await TableExistsAsync(conn, tx, "src", table, ct))
                    continue;
                if (!await TableExistsAsync(conn, tx, "main", table, ct))
                    continue;
                if (await HasRowsAsync(conn, tx, "main", table, ct))
                {
                    perTable[table] = 0;
                    continue;
                }

                var rows = await CopyTableAsync(conn, tx, table, ct);
                perTable[table] = rows;
            }

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }

        await using (var detach = conn.CreateCommand())
        {
            detach.CommandText = "DETACH DATABASE src;";
            await detach.ExecuteNonQueryAsync(ct);
        }

        return new ImportResult(perTable.Values.Sum(), perTable);
    }

    private static async Task<bool> TableExistsAsync(
        SqliteConnection conn, SqliteTransaction tx, string schema, string table, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"SELECT COUNT(*) FROM {schema}.sqlite_master WHERE type='table' AND name=@t;";
        cmd.Parameters.AddWithValue("@t", table);
        var count = (long)(await cmd.ExecuteScalarAsync(ct) ?? 0L);
        return count > 0;
    }

    private static async Task<bool> HasRowsAsync(
        SqliteConnection conn, SqliteTransaction tx, string schema, string table, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"SELECT COUNT(*) FROM {schema}.{table};";
        var count = (long)(await cmd.ExecuteScalarAsync(ct) ?? 0L);
        return count > 0;
    }

    private static async Task<int> CopyTableAsync(
        SqliteConnection conn, SqliteTransaction tx, string table, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"INSERT INTO main.{table} SELECT * FROM src.{table};";
        return await cmd.ExecuteNonQueryAsync(ct);
    }
}
