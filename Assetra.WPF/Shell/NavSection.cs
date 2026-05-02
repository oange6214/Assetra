namespace Assetra.WPF.Shell;

public enum NavSection
{
    Portfolio,          // 六個 tab：Dashboard／投資／配置分析／帳戶／負債／交易
    FinancialOverview,  // Allocation Treemap + 再平衡 + 財務總覽
    Cashflow,           // Hub: Categories + Recurring
    Insights,           // Hub: Goals + Trends + Reports + FIRE + MonteCarlo
    MultiAsset,         // Hub: RealEstate + Insurance + Retirement + PhysicalAsset
    Alerts,
    Import,             // 匯入銀行/券商對帳單
    Settings,
    // ── Legacy direct-route values (kept for deep links / backward compat) ──
    Categories,
    Recurring,
    Reports,
    Trends,
    Goals,
    RealEstate,
    Insurance,
    Retirement,
    PhysicalAsset,
    Fire,
    MonteCarlo,
}
