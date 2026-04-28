namespace Assetra.WPF.Shell;

public enum NavSection
{
    Portfolio,          // 六個 tab：Dashboard／投資／配置分析／帳戶／負債／交易
    FinancialOverview,  // Allocation Treemap + 再平衡 + 財務總覽
    Categories,         // 收支分類
    Recurring,          // 訂閱與待確認排程
    Reports,            // 月結報告
    Trends,             // 淨資產趨勢
    Goals,              // 財務目標
    Alerts,
    Import,             // 匯入銀行/券商對帳單
    RealEstate,         // 不動產（v0.23）
    Insurance,          // 保險保單（v0.23）
    Retirement,         // 退休專戶（v0.24）
    PhysicalAsset,      // 實物資產（v0.24）
    Fire,               // FIRE 計算機（v0.25）
    MonteCarlo,         // Monte Carlo 模擬（v0.26）
    Settings,
}
