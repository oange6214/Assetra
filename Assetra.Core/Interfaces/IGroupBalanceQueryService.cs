namespace Assetra.Core.Interfaces;

/// <summary>
/// Portfolio-Groups-Refactor P5 — 計算單一 PortfolioGroup 的目前資產淨值，
/// 給 Goal auto-tracking 使用（goal 進度 = group 淨值 / target × 100）。
/// </summary>
public interface IGroupBalanceQueryService
{
    /// <summary>
    /// Group 的「目前淨值」(主要幣別)。MVP 實作 = 該 group 內所有 trade 的累計
    /// 簽名現金流（買入 -、賣出 +、股息 +、收入 +、提款 -、…），不含未實現市值。
    /// 未來會加上 position 的 mark-to-market 與該 group 對應的 cash 帳戶餘額。
    /// 找不到該 group 的 trade 時回傳 0。
    /// </summary>
    Task<decimal> ComputeNetValueAsync(Guid groupId, CancellationToken ct = default);
}
