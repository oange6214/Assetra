using Microsoft.Data.Sqlite;
using Xunit;
using Assetra.Infrastructure.Persistence;

namespace Assetra.Tests.Infrastructure;

public sealed class StockraImportServiceTests : IDisposable
{
    private readonly string _srcDb;
    private readonly string _dstDb;

    public StockraImportServiceTests()
    {
        _srcDb = Path.GetTempFileName();
        _dstDb = Path.GetTempFileName();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_srcDb)) File.Delete(_srcDb);
        if (File.Exists(_dstDb)) File.Delete(_dstDb);
    }

    [Fact]
    public async Task ImportAsync_CopiesPortfolioRows_WhenSourceHasData()
    {
        await using (var src = new SqliteConnection($"Data Source={_srcDb}"))
        {
            await src.OpenAsync();
            await ExecuteAsync(src, """
                CREATE TABLE portfolio (
                    id TEXT PRIMARY KEY, symbol TEXT, exchange TEXT,
                    asset_type TEXT, display_name TEXT, currency TEXT, is_active INTEGER
                );
                INSERT INTO portfolio VALUES (
                    'e1111111-1111-1111-1111-111111111111', '2330', 'TWSE',
                    'Stock', 'TSMC', 'TWD', 1);
                """);
        }

        await using (var dst = new SqliteConnection($"Data Source={_dstDb}"))
        {
            await dst.OpenAsync();
            await ExecuteAsync(dst, """
                CREATE TABLE portfolio (
                    id TEXT PRIMARY KEY, symbol TEXT, exchange TEXT,
                    asset_type TEXT, display_name TEXT, currency TEXT, is_active INTEGER
                );
                """);
        }

        var sut = new StockraImportService(_dstDb);
        var result = await sut.ImportAsync(_srcDb);

        Assert.Equal(1, result.PerTable["portfolio"]);
        Assert.Equal(1, result.TotalRows);

        await using var verify = new SqliteConnection($"Data Source={_dstDb}");
        await verify.OpenAsync();
        await using var cmd = verify.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM portfolio;";
        var count = (long)(await cmd.ExecuteScalarAsync() ?? 0L);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task ImportAsync_SkipsTable_WhenTargetAlreadyHasRows()
    {
        const string schema = """
            CREATE TABLE portfolio (
                id TEXT PRIMARY KEY, symbol TEXT, exchange TEXT,
                asset_type TEXT, display_name TEXT, currency TEXT, is_active INTEGER
            );
            """;

        await using (var src = new SqliteConnection($"Data Source={_srcDb}"))
        {
            await src.OpenAsync();
            await ExecuteAsync(src, schema + """
                INSERT INTO portfolio VALUES
                    ('s1', 'SRC1', 'TWSE', 'Stock', 'from src', 'TWD', 1);
                """);
        }

        await using (var dst = new SqliteConnection($"Data Source={_dstDb}"))
        {
            await dst.OpenAsync();
            await ExecuteAsync(dst, schema + """
                INSERT INTO portfolio VALUES
                    ('d1', 'DST1', 'TWSE', 'Stock', 'existing', 'TWD', 1);
                """);
        }

        var sut = new StockraImportService(_dstDb);
        var result = await sut.ImportAsync(_srcDb);

        Assert.Equal(0, result.TotalRows);
        Assert.Equal(0, result.PerTable["portfolio"]);

        await using var verify = new SqliteConnection($"Data Source={_dstDb}");
        await verify.OpenAsync();
        await using var cmd = verify.CreateCommand();
        cmd.CommandText = "SELECT symbol FROM portfolio;";
        await using var rdr = await cmd.ExecuteReaderAsync();
        Assert.True(await rdr.ReadAsync());
        Assert.Equal("DST1", rdr.GetString(0));
        Assert.False(await rdr.ReadAsync());
    }

    [Fact]
    public async Task ImportAsync_IgnoresNonexistentTables()
    {
        await using (var src = new SqliteConnection($"Data Source={_srcDb}"))
        {
            await src.OpenAsync();
            await ExecuteAsync(src, """
                CREATE TABLE portfolio (
                    id TEXT PRIMARY KEY, symbol TEXT, exchange TEXT,
                    asset_type TEXT, display_name TEXT, currency TEXT, is_active INTEGER
                );
                """);
        }

        await using (var dst = new SqliteConnection($"Data Source={_dstDb}"))
        {
            await dst.OpenAsync();
            await ExecuteAsync(dst, """
                CREATE TABLE portfolio (
                    id TEXT PRIMARY KEY, symbol TEXT, exchange TEXT,
                    asset_type TEXT, display_name TEXT, currency TEXT, is_active INTEGER
                );
                CREATE TABLE alert (
                    id TEXT PRIMARY KEY, symbol TEXT, price REAL, direction TEXT
                );
                """);
        }

        var sut = new StockraImportService(_dstDb);
        var result = await sut.ImportAsync(_srcDb);

        Assert.False(result.PerTable.ContainsKey("alert"));
        Assert.Equal(0, result.TotalRows);
    }

    private static async Task ExecuteAsync(SqliteConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }
}
