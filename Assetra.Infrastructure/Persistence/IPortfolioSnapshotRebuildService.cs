namespace Assetra.Infrastructure.Persistence;

/// <summary>
/// 重建歷史快照的引擎抽象。抽出此介面是為了讓 UI 層（SettingsViewModel）能以 fake 注入、
/// 單元測試 dry-run 預覽與「先備份後寫入」的流程，而不必拉起整條真實的重建管線。
/// 具體實作見 <see cref="PortfolioSnapshotRebuildService"/>。
/// </summary>
public interface IPortfolioSnapshotRebuildService
{
    /// <summary>
    /// 重建 <c>[from, to]</c> 區間（含端點）每個交易日的完整 breakdown 快照。
    /// <paramref name="dryRun"/> 為 <see langword="true"/> 時只計算與回報、不寫入。
    /// </summary>
    Task<SnapshotRebuildReport> RebuildAsync(
        DateOnly from, DateOnly to, bool dryRun, CancellationToken ct = default);
}
