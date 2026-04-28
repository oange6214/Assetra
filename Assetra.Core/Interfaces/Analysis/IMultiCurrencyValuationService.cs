namespace Assetra.Core.Interfaces.Analysis;

/// <summary>
/// 將任意幣別金額換算成基準幣別。包裝 <see cref="IFxRateProvider"/> 並處理：
/// (1) 同幣別 fast path、(2) 缺值時的 fallback 行為（由實作決定）。
/// </summary>
public interface IMultiCurrencyValuationService
{
    /// <summary>
    /// 換算 <paramref name="amount"/> 從 <paramref name="from"/> 至 <paramref name="to"/>，
    /// 取 <paramref name="asOf"/> 當日／最近一筆匯率。
    /// 找不到匯率時回傳 <see langword="null"/>，呼叫端決定 fallback。
    /// </summary>
    Task<decimal?> ConvertAsync(
        decimal amount, string from, string to, DateOnly asOf, CancellationToken ct = default);
}
