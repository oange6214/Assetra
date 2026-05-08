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
}
