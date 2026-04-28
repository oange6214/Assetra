namespace Assetra.Core.Models.Sync;

/// <summary>
/// 衝突解決結果。<see cref="Manual"/> 用於 UI 介入分支：resolver 回 Manual 表示「我沒法自動裁決，請呼叫使用者」。
/// </summary>
public enum SyncResolution
{
    KeepLocal,
    KeepRemote,
    Manual,
}
