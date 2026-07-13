using System.IO;
using Assetra.Infrastructure.Sync;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Assetra.Tests.Infrastructure.Sync;

/// <summary>
/// <see cref="SqlitePendingPushCounter"/> 以 metadata（pragma_table_info）預先過濾出「存在且有
/// is_pending_push 欄位」的表，只對這些跑 COUNT——不再用 try/catch SqliteException 判斷缺表/缺欄。
/// <para>
/// WHY：舊版把「Category」對到不存在的 <c>category</c> 表（實際是 <c>expense_category</c>）、又用
/// catch 吞掉 SqliteException → 每次呼叫都對 category（及任何缺欄的表）丟一顆 first-chance 例外，
/// 這服務又會隨 sync 訊號反覆呼叫 → debug Output 一直洗。這裡鎖住：Category 對到正確表並計數、
/// 有表無欄／無表都回 0。
/// </para>
/// </summary>
public sealed class SqlitePendingPushCounterTests : IDisposable
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"assetra-pp-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); }
        catch { /* best effort */ }
    }

    [Fact]
    public async Task CountPendingByDomain_UsesCorrectTables_AndReturnsZeroForMissingWithoutThrowing()
    {
        await using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            await conn.OpenAsync();
            // 有 is_pending_push：2 筆 pending、1 筆非 pending。
            await Exec(conn, "CREATE TABLE trade (id TEXT PRIMARY KEY, is_pending_push INTEGER NOT NULL DEFAULT 0);");
            await Exec(conn, "INSERT INTO trade VALUES ('a',1),('b',1),('c',0);");
            // Category domain 的正確表是 expense_category（不是 category）：1 筆 pending。
            await Exec(conn, "CREATE TABLE expense_category (id TEXT PRIMARY KEY, is_pending_push INTEGER NOT NULL DEFAULT 0);");
            await Exec(conn, "INSERT INTO expense_category VALUES ('x',1);");
            // 存在但沒有 is_pending_push 欄位 → 應回 0 且不丟例外（舊版會在此丟 SqliteException）。
            await Exec(conn, "CREATE TABLE portfolio (id TEXT PRIMARY KEY);");
            // 其餘 target 表完全不存在 → 0。
        }

        var counter = new SqlitePendingPushCounter(_dbPath);
        var result = await counter.CountPendingByDomainAsync();

        Assert.Equal(2, result["Trade"]);
        Assert.Equal(1, result["Category"]);   // 對到 expense_category（舊版查 category → 0，錯）
        Assert.Equal(0, result["Portfolio"]);  // 有表無欄 → 0
        Assert.Equal(0, result["Alert"]);      // 無表 → 0
        Assert.Equal(15, result.Count);        // 每個 domain 都有一個值
    }

    private static async Task Exec(SqliteConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }
}
