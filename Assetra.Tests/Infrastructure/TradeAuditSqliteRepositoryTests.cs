using System.IO;
using Assetra.Core.Models;
using Assetra.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Assetra.Tests.Infrastructure;

/// <summary>
/// Round-trip + invariant tests for the append-only audit log. The repo only
/// exposes <c>AppendAsync</c>; we verify rows actually persist and that the
/// schema migrator wires up the indexes the audit-write path relies on.
/// </summary>
public sealed class TradeAuditSqliteRepositoryTests : IDisposable
{
    private readonly string _dbPath;

    public TradeAuditSqliteRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"trade_audit_test_{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    [Fact]
    public async Task AppendAsync_PersistsAllFields()
    {
        var sut = new TradeAuditSqliteRepository(_dbPath);
        var entry = new TradeAuditEntry(
            Id: Guid.NewGuid(),
            TradeId: Guid.NewGuid(),
            Action: "delete",
            TradeJson: """{"id":"abc","amount":100}""",
            RecordedAt: new DateTime(2026, 5, 8, 13, 30, 0, DateTimeKind.Utc),
            Note: "user-initiated delete");

        await sut.AppendAsync(entry);

        var rows = ReadAll();
        Assert.Single(rows);
        var row = rows[0];
        Assert.Equal(entry.Id.ToString(), row.id);
        Assert.Equal(entry.TradeId.ToString(), row.trade_id);
        Assert.Equal("delete", row.action);
        Assert.Equal(entry.TradeJson, row.trade_json);
        Assert.Equal("user-initiated delete", row.note);
    }

    /// <summary>
    /// Edit-replace path writes Action="edit-replace" (driven by
    /// TradeDeletionRequest.Reason). Manual deletes write "delete". Audit
    /// readers join on this column to distinguish user delete from implicit
    /// edit delete.
    /// </summary>
    [Fact]
    public async Task AppendAsync_StoresEditReplaceAction()
    {
        var sut = new TradeAuditSqliteRepository(_dbPath);
        var entry = new TradeAuditEntry(
            Id: Guid.NewGuid(),
            TradeId: Guid.NewGuid(),
            Action: "edit-replace",
            TradeJson: "{}",
            RecordedAt: DateTime.UtcNow,
            Note: null);

        await sut.AppendAsync(entry);

        var row = Assert.Single(ReadAll());
        Assert.Equal("edit-replace", row.action);
    }

    [Fact]
    public async Task AppendAsync_AcceptsNullNote()
    {
        var sut = new TradeAuditSqliteRepository(_dbPath);
        var entry = new TradeAuditEntry(
            Id: Guid.NewGuid(),
            TradeId: Guid.NewGuid(),
            Action: "delete",
            TradeJson: "{}",
            RecordedAt: DateTime.UtcNow,
            Note: null);

        await sut.AppendAsync(entry);

        var rows = ReadAll();
        Assert.Single(rows);
        Assert.Null(rows[0].note);
    }

    [Fact]
    public async Task AppendAsync_RejectsDuplicatePrimaryKey()
    {
        // Append-only contract: a duplicate Id should error rather than silently
        // overwrite. SQLite raises a UNIQUE constraint violation on PK collision.
        var sut = new TradeAuditSqliteRepository(_dbPath);
        var sharedId = Guid.NewGuid();
        var first = new TradeAuditEntry(sharedId, Guid.NewGuid(), "delete", "{}", DateTime.UtcNow, null);
        var second = first with { TradeJson = """{"changed":true}""" };

        await sut.AppendAsync(first);

        await Assert.ThrowsAsync<SqliteException>(() => sut.AppendAsync(second));
    }

    [Fact]
    public async Task SchemaMigrator_CreatesExpectedIndexes()
    {
        // The repo's ctor invokes EnsureInitialized. We verify the audit
        // log indexes exist so per-trade history queries stay efficient.
        _ = new TradeAuditSqliteRepository(_dbPath);

        await using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='index' AND tbl_name='trade_audit';";
        var indexes = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            indexes.Add(reader.GetString(0));

        Assert.Contains("idx_trade_audit_trade_id", indexes);
        Assert.Contains("idx_trade_audit_recorded", indexes);
    }

    [Fact]
    public async Task AppendAsync_PreservesInsertionOrder()
    {
        // Round-trip several entries to confirm we can reconstruct the
        // chronological audit trail (recorded_at is the canonical sort key).
        var sut = new TradeAuditSqliteRepository(_dbPath);
        var t0 = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        var entries = new[]
        {
            new TradeAuditEntry(Guid.NewGuid(), Guid.NewGuid(), "delete", "a", t0.AddSeconds(1), null),
            new TradeAuditEntry(Guid.NewGuid(), Guid.NewGuid(), "delete", "b", t0.AddSeconds(2), null),
            new TradeAuditEntry(Guid.NewGuid(), Guid.NewGuid(), "delete", "c", t0.AddSeconds(3), null),
        };
        foreach (var e in entries)
            await sut.AppendAsync(e);

        var rows = ReadAll();
        Assert.Equal(3, rows.Count);
        Assert.Equal(new[] { "a", "b", "c" }, rows.OrderBy(r => r.recorded_at).Select(r => r.trade_json).ToArray());
    }

    // ── helpers ──

    private record AuditRow(string id, string trade_id, string action, string trade_json, string recorded_at, string? note);

    private List<AuditRow> ReadAll()
    {
        var rows = new List<AuditRow>();
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, trade_id, action, trade_json, recorded_at, note FROM trade_audit;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new AuditRow(
                id: reader.GetString(0),
                trade_id: reader.GetString(1),
                action: reader.GetString(2),
                trade_json: reader.GetString(3),
                recorded_at: reader.GetString(4),
                note: reader.IsDBNull(5) ? null : reader.GetString(5)));
        }
        return rows;
    }
}
