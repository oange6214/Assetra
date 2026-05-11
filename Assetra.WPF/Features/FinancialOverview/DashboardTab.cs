namespace Assetra.WPF.Features.FinancialOverview;

/// <summary>
/// 4-tab 財務儀表板（Stage 2 Dashboard consolidation）的 tab 識別。
/// Tag 用於 XAML TabItem.Tag binding，<see cref="FinancialOverviewViewModel.SelectedDashboardTab"/>
/// 透過 SelectedValue 雙向同步。
/// </summary>
public enum DashboardTab
{
    /// <summary>淨值 KPI + 資產分組 accordion（+ Stage 2.5 widget）。</summary>
    Overview = 0,

    /// <summary>資產趨勢圖 + 區間 KPI + 對標（Stage 1 已交付）。</summary>
    Trends = 1,

    /// <summary>報酬日曆熱度圖（Stage 4 才填內容；Stage 2 顯示 placeholder）。</summary>
    Calendar = 2,

    /// <summary>配置分析 — 從 Portfolio 內 tab 搬入。</summary>
    Allocation = 3,
}
