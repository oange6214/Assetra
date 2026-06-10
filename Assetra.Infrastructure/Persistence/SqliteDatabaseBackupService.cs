using Assetra.Core.Interfaces;
using Microsoft.Data.Sqlite;

namespace Assetra.Infrastructure.Persistence;

/// <summary>
/// 用 Microsoft.Data.Sqlite 的線上備份 API（<see cref="SqliteConnection.BackupDatabase(SqliteConnection)"/>）
/// 複製整個資料庫。這是複製「正在使用中」SQLite 檔案的正確做法：它在引擎內部走 backup
/// page-copy 流程，會把 WAL 內未 checkpoint 的頁面一併納入，因此不需要先關閉連線或停掉
/// 正在跑的 app。備份檔名為 <c>{db}.bak-{yyyyMMdd-HHmmss}</c>。
/// </summary>
public sealed class SqliteDatabaseBackupService : IDatabaseBackupService
{
    private readonly string _dbPath;

    public SqliteDatabaseBackupService(string dbPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dbPath);
        _dbPath = dbPath;
    }

    public async Task<string> BackupAsync(CancellationToken ct = default)
    {
        // DateTime.Now 在 app runtime 是合理的（本機操作、檔名只需可排序的本地時間戳）。
        var backupPath = $"{_dbPath}.bak-{DateTime.Now:yyyyMMdd-HHmmss}";

        await using var source = new SqliteConnection($"Data Source={_dbPath}");
        await source.OpenAsync(ct).ConfigureAwait(false);

        await using var destination = new SqliteConnection($"Data Source={backupPath}");
        await destination.OpenAsync(ct).ConfigureAwait(false);

        // BackupDatabase 走引擎內建的 online-backup（含 WAL 中尚未 checkpoint 的頁面），
        // 是複製使用中資料庫的安全方式。
        source.BackupDatabase(destination);

        return backupPath;
    }
}
