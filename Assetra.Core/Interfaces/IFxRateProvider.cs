using Assetra.Core.Models;

namespace Assetra.Core.Interfaces;

/// <summary>
/// 取得歷史匯率快照。實作可走靜態表（DB / appsettings）或線上來源。
/// 同幣別查詢一律回傳 1.0；查無資料回傳 <see langword="null"/>，由呼叫端決定 fallback。
/// </summary>
public interface IFxRateProvider
{
    /// <summary>
    /// 取 <paramref name="asOf"/> 當日 1 <paramref name="from"/> = ? <paramref name="to"/> 的匯率。
    /// 若該日無資料，實作應回傳最近一個 ≤ asOf 的記錄；皆無則回 <see langword="null"/>。
    /// </summary>
    Task<decimal?> GetRateAsync(string from, string to, DateOnly asOf, CancellationToken ct = default);

    /// <summary>
    /// 取 [from, to] 區間內所有可用匯率快照，依日期升冪排序。
    /// 給 Performance / TWR 等需要逐日換算 cash flow 的場景使用。
    /// </summary>
    Task<IReadOnlyList<FxRate>> GetHistoricalSeriesAsync(
        string from, string to, DateOnly start, DateOnly end, CancellationToken ct = default);
}
