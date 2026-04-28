using Assetra.Core.Models.Sync;

namespace Assetra.Application.Sync;

/// <summary>
/// 暴露「抽走目前待解決衝突」的能力。Category / Trade 等個別 queue 與 composite 都實作，
/// 讓 conflict UI 可以一次拉光所有 entity 的 manual conflicts。
/// </summary>
public interface IManualConflictDrain
{
    IReadOnlyList<SyncConflict> DrainManualConflicts();
}
