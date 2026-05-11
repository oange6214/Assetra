namespace Assetra.WPF.Infrastructure;

/// <summary>
/// Shell-level navigation event aggregator. Lets feature VMs (e.g. transaction
/// dialog success-toast「查看交易」action) request navigation to a navrail page
/// without taking a hard dependency on the navrail VM through the entire DI tree.
///
/// <para>
/// Usage：
///   <c>ShellNavigationEvents.RequestNavigateTo("TransactionLog");</c>
/// NavRailViewModel subscribes to <see cref="NavigationRequested"/> at startup
/// and forwards the section name to its own NavigateTo handler.
/// </para>
///
/// <para>
/// Section names match the <c>NavSection</c> enum string values. Use the enum
/// in callers via <c>nameof(NavSection.TransactionLog)</c> for compile-time
/// safety; the static class itself only sees strings to avoid pulling the
/// Shell namespace into Portfolio code.
/// </para>
/// </summary>
public static class ShellNavigationEvents
{
    /// <summary>
    /// Raised when any feature wants the shell to switch the active navrail page.
    /// Subscribers are NavRailViewModel (the only one expected) — if no listener,
    /// the request is silently dropped, which is safe behaviour for tests.
    /// </summary>
    public static event Action<string>? NavigationRequested;

    /// <summary>Raises <see cref="NavigationRequested"/> with the given section name.</summary>
    public static void RequestNavigateTo(string sectionName) =>
        NavigationRequested?.Invoke(sectionName);

    /// <summary>
    /// Stage 2 (Dashboard consolidation)：要求財務儀表板切換到指定 tab。
    /// 由 NavRailViewModel 在攔截 NavSection.Trends / Reports 時觸發，
    /// FinancialOverviewViewModel 訂閱後更新 SelectedDashboardTab。
    /// 參數為 DashboardTab 的 string name（避免雙向 namespace 相依）。
    /// </summary>
    public static event Action<string>? DashboardTabRequested;

    public static void RequestDashboardTab(string tabName) =>
        DashboardTabRequested?.Invoke(tabName);

    /// <summary>
    /// 報酬日曆 cell popover 跳轉「查看當日交易」時觸發 — 攜帶單一日期。
    /// PortfolioViewModel 的 TradeFilter 訂閱後設置 TradeDateFrom = TradeDateTo
    /// = 該日，然後切到 TransactionLog 頁面。
    /// </summary>
    public static event Action<DateOnly>? TransactionDateFilterRequested;

    public static void RequestTransactionsForDate(DateOnly date)
    {
        TransactionDateFilterRequested?.Invoke(date);
        RequestNavigateTo("TransactionLog");
    }
}
