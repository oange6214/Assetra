namespace Assetra.Core.Interfaces;

/// <summary>
/// 在執行破壞性的整批寫入（例如重建快照歷史）之前，先對 SQLite 資料庫做一份線上備份。
/// </summary>
public interface IDatabaseBackupService
{
    /// <summary>
    /// 以 WAL-safe 的方式複製目前的資料庫，回傳備份檔的完整路徑。
    /// </summary>
    Task<string> BackupAsync(CancellationToken ct = default);
}
