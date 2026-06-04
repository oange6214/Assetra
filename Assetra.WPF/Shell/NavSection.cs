namespace Assetra.WPF.Shell;

public enum NavSection
{
    Portfolio,
    FinancialOverview,
    Categories,
    Recurring,
    Goals,
    Trends,
    Reports,
    Fire,
    MonteCarlo,
    Calculators,
    RealEstate,
    Insurance,
    Retirement,
    PhysicalAsset,
    Alerts,
    Import,
    Settings,
    /// <summary>Cash bank accounts. Promoted from Portfolio inner-tab to top-level nav (Option B + Portfolio-tabs refactor).</summary>
    CashAccounts,
    /// <summary>Liabilities (loans + credit cards). Promoted from Portfolio inner-tab to top-level nav.</summary>
    Liabilities,
    /// <summary>Cross-cutting trade / transaction log. Promoted from Portfolio inner-tab to top-level nav.</summary>
    TransactionLog,
    /// <summary>AI 財務助手 — 自然語言查詢 (Phase 1 rule-based; Phase 2/3 spec'd).</summary>
    Assistant,
    /// <summary>交易稽核日誌 — 顯示 trade_audit 表內容（被刪/被替換的歷史 Trade）。</summary>
    AuditLog,
    /// <summary>Portfolio Groups (群組) CRUD — see Portfolio-Groups-Refactor P2.</summary>
    PortfolioGroups,
}
