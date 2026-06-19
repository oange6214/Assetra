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
    /// <param name="overwriteLive">
    /// <see langword="true"/> 時連「已有完整 breakdown 的 live 快照」也重算覆寫——手動「重建快照」
    /// 用，名副其實地重算整個區間（否則整批已是 live 時會 0 天可重建）。<see langword="false"/>
    /// （預設）則保留 live 列、只補非 live 缺口。無價／無匯率／無持倉的略過判斷不受影響。
    /// </param>
    Task<SnapshotRebuildReport> RebuildAsync(
        DateOnly from, DateOnly to, bool dryRun, bool overwriteLive = false, CancellationToken ct = default);
}
