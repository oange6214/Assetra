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
/// </summary>
public sealed record KpiCardVm(
    KpiMetric Id,
    string LabelKey,
    string ValueDisplay,
    KpiTone Tone)
{
    public string FontWeightHint => "SemiBold";
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
    /// Default KPI selection if the user has never edited it. Mirrors the
    /// Plan B recommendation (Net Worth / Investments / Investment P&L /
    /// Debt Ratio) so first-run users see balance-sheet plus performance
    /// indicators rather than the older raw "Other Assets" field.
    /// </summary>
    public static readonly IReadOnlyList<KpiMetric> Default =
    [
        KpiMetric.NetWorth,
        KpiMetric.Investments,
        KpiMetric.InvestmentPnl,
        KpiMetric.DebtRatio,
    ];

    /// <summary>
    /// Min / max number of cards a user is allowed to select in Settings.
    /// 3 keeps the row visually balanced; 6 fits a 1600 px window without
    /// per-card width collapsing below readable size.
    /// </summary>
    public const int MinSelected = 3;
    public const int MaxSelected = 6;

    public static readonly IReadOnlyList<KpiMetricInfo> All =
    [
        new(KpiMetric.NetWorth, "FinancialOverview.Header.NetWorth", "FinancialOverview.Kpi.NetWorth.Desc"),
        new(KpiMetric.TotalAssets, "FinancialOverview.Header.TotalAssets", "FinancialOverview.Kpi.TotalAssets.Desc"),
        new(KpiMetric.OtherAssets, "FinancialOverview.Header.Assets", "FinancialOverview.Kpi.OtherAssets.Desc"),
        new(KpiMetric.Investments, "FinancialOverview.Header.Investments", "FinancialOverview.Kpi.Investments.Desc"),
        new(KpiMetric.Liabilities, "FinancialOverview.Header.Liabilities", "FinancialOverview.Kpi.Liabilities.Desc"),
        new(KpiMetric.DebtRatio, "FinancialOverview.Kpi.DebtRatio", "FinancialOverview.Kpi.DebtRatio.Desc"),
        new(KpiMetric.InvestmentPnl, "FinancialOverview.Kpi.InvestmentPnl", "FinancialOverview.Kpi.InvestmentPnl.Desc"),
        new(KpiMetric.InvestmentPnlPercent, "FinancialOverview.Kpi.InvestmentPnlPercent", "FinancialOverview.Kpi.InvestmentPnlPercent.Desc"),
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
