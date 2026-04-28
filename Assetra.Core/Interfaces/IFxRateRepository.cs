using Assetra.Core.Models;

namespace Assetra.Core.Interfaces;

/// <summary>
/// 持久化 FX 匯率表（fx_rate）。
/// </summary>
public interface IFxRateRepository
{
    /// <summary>插入或更新一筆匯率（複合主鍵：from, to, as_of_date）。</summary>
    Task UpsertAsync(FxRate rate, CancellationToken ct = default);

    /// <summary>批次 upsert，包在單一 transaction 內。</summary>
    Task UpsertManyAsync(IReadOnlyList<FxRate> rates, CancellationToken ct = default);

    /// <summary>取 <paramref name="asOf"/> 當日的匯率；無則取最近一筆 ≤ asOf。</summary>
    Task<FxRate?> GetAsync(string from, string to, DateOnly asOf, CancellationToken ct = default);

    /// <summary>取 [start, end] 區間記錄，依 as_of_date 升冪排序。</summary>
    Task<IReadOnlyList<FxRate>> GetRangeAsync(
        string from, string to, DateOnly start, DateOnly end, CancellationToken ct = default);
}
