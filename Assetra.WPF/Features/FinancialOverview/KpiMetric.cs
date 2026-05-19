using Assetra.WPF.Infrastructure;

namespace Assetra.WPF.Features.FinancialOverview;

/// <summary>
/// Stable IDs for the FinancialOverview KPI bar metrics. Saved into
/// <c>AppSettings.OverviewKpis</c> as strings so the user's choice
/// survives across releases without a schema migration when new metrics
/// are added (unknown IDs are silently dropped at load time).
/// </summary>
public enum KpiMetric
{
    NetWorth,
    TotalAssets,
    OtherAssets,
    Investments,
    Liabilities,
    DebtRatio,
    InvestmentPnl,
    InvestmentPnlPercent,
}

/// <summary>
/// One snapshot row in the KPI bar. The ViewModel rebuilds the list
/// whenever the underlying numbers or the user's selection changes.
///
/// <para>
/// <see cref="SecondaryLabelKey"/> + <see cref="SecondaryValueDisplay"/> are
/// optional — used when a single financial concept has two equivalent framings
/// worth showing together. Example: <c>DebtRatio</c> card displays「負債比率 27.1%」
/// as primary and「槓桿比 1.37」as secondary小字 below, since the two are different
/// expressions of the same leverage position. Empty strings = no secondary line.
/// </para>
/// </summary>
public sealed record KpiCardVm(
    KpiMetric Id,
    string LabelKey,
    string ValueDisplay,
    KpiTone Tone,
    string SecondaryLabelKey = "",
    string SecondaryValueDisplay = "")
{
    public string FontWeightHint => "SemiBold";

    /// <summary>True 當有副值要顯示；XAML 用此控制副線 Visibility。</summary>
    public bool HasSecondary =>
        !string.IsNullOrWhiteSpace(SecondaryLabelKey)
        && !string.IsNullOrWhiteSpace(SecondaryValueDisplay);
}

/// <summary>
/// Visual emphasis for the KPI value text. The view template translates
/// this to the matching theme brush. <c>Neutral</c> = default text colour;
/// <c>Up</c> = green / gain; <c>Down</c> = red / loss; <c>Accent</c> =
/// brand navy used for "your portfolio" investment values.
/// </summary>
public enum KpiTone
{
    Neutral,
    Up,
    Down,
    Accent,
}

/// <summary>
/// Centralised metadata for every available KPI metric — used by both the
/// FinancialOverview KPI bar and the Settings page multi-select. Keeping
/// the list in one place ensures the catalog can grow without scattering
/// label strings or default lists across the codebase.
/// </summary>
public static class KpiMetricCatalog
{
    /// <summary>
    /// Default KPI selection if the user has never edited it.
    /// v3 (2026-05-13 consolidation)：再砍 <c>Investments</c>，因為 InvestmentFocusWidget
    /// 內已固定顯示「總市值」，KPI row 再放一次是雙重訊息。剩下的 4 個都是「衍生 /
    /// 比率 / 績效」指標，沒被其他 dashboard 區塊覆蓋：
    ///   - <c>OtherAssets</c>      其他資產（現金 + 不動產 + 實物 …）
    ///   - <c>InvestmentPnl</c>    投資淨損益（絕對金額）
    ///   - <c>InvestmentPnlPercent</c> 投資報酬率 (%)
    ///   - <c>DebtRatio</c>        負債比率（含槓桿比副值）
    /// 老使用者勾選的 Investments 仍可顯示（沒從 All 移除，只是 default 不再帶）。
    /// </summary>
    public static readonly IReadOnlyList<KpiMetric> Default =
    [
        KpiMetric.OtherAssets,
        KpiMetric.InvestmentPnl,
        KpiMetric.InvestmentPnlPercent,
        KpiMetric.DebtRatio,
    ];

    /// <summary>
    /// Min / max number of cards a user is allowed to select in Settings.
    /// 3 keeps the row visually balanced; 6 fits a 1600 px window without
    /// per-card width collapsing below readable size.
    /// </summary>
    public const int MinSelected = 3;
    public const int MaxSelected = 6;

    /// <summary>
    /// v2 — 從 dialog 移除 NetWorth / TotalAssets / Liabilities：這三個指標已經
    /// 永久顯示在 Hero card 跟「公式列」上方，再讓使用者挑只是雙重出現，浪費空間。
    /// 老 user 存的設定若還有這三個值，<see cref="ParseSelection"/> 會在讀取時靜默
    /// 過濾掉（保留其他合法選擇）；enum 本身不刪，<see cref="BuildCard"/> 內 switch
    /// 仍能處理，主要是讓 dialog UI 不再出現這三個 checkbox。
    /// </summary>
    public static readonly IReadOnlyList<KpiMetricInfo> All =
    [
        // KpiMetric.NetWorth     — moved to Hero card (permanent)
        // KpiMetric.TotalAssets  — moved to 公式列 (permanent)
        // KpiMetric.Liabilities  — moved to 公式列 (permanent)
        new(KpiMetric.OtherAssets, "FinancialOverview.Header.Assets", "FinancialOverview.Kpi.OtherAssets.Desc"),
        new(KpiMetric.Investments, "FinancialOverview.Header.Investments", "FinancialOverview.Kpi.Investments.Desc"),
        new(KpiMetric.DebtRatio, "FinancialOverview.Kpi.DebtRatio", "FinancialOverview.Kpi.DebtRatio.Desc"),
        new(KpiMetric.InvestmentPnl, "FinancialOverview.Kpi.InvestmentPnl", "FinancialOverview.Kpi.InvestmentPnl.Desc"),
        new(KpiMetric.InvestmentPnlPercent, "FinancialOverview.Kpi.InvestmentPnlPercent", "FinancialOverview.Kpi.InvestmentPnlPercent.Desc"),
    ];

    /// <summary>v2 — 永遠顯示在 Hero / 公式列、不再給使用者勾的指標。</summary>
    private static readonly HashSet<KpiMetric> ImplicitInHero =
    [
        KpiMetric.NetWorth,
        KpiMetric.TotalAssets,
        KpiMetric.Liabilities,
    ];

    /// <summary>
    /// Parse the persisted string list (from AppSettings.OverviewKpis) into
    /// enum values, dropping unknowns. Falls back to <see cref="Default"/>
    /// when the persisted list is null, empty, or fully invalid — so a
    /// fresh install or an upgrade-with-stale-state never shows zero cards.
    /// </summary>
    public static IReadOnlyList<KpiMetric> ParseSelection(IEnumerable<string>? persisted)
    {
        if (persisted is null)
            return Default;

        var parsed = persisted
            .Select(s => Enum.TryParse<KpiMetric>(s, ignoreCase: true, out var v) ? v : (KpiMetric?)null)
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            // v2：silently drop metrics that are now permanently in Hero / 公式列.
            // Old saved selections from before the redesign included these; we don't
            // throw them away from the enum (the renderer is still capable) but we
            // hide them from the user's selectable list so they don't get rendered
            // twice on the dashboard.
            .Where(v => !ImplicitInHero.Contains(v))
            .Distinct()
            .ToList();

        if (parsed.Count == 0)
            return Default;

        if (parsed.Count > MaxSelected)
            return parsed.Take(MaxSelected).ToList();

        return parsed;
    }

    public static string Serialize(IEnumerable<KpiMetric> metrics) =>
        string.Join(",", metrics.Select(m => m.ToString()));
}

public sealed record KpiMetricInfo(KpiMetric Id, string LabelKey, string DescriptionKey);
