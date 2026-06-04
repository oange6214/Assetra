using System.Collections.ObjectModel;
using System.Globalization;
using Assetra.Application.Portfolio.Contracts;
using Assetra.Application.Portfolio.Dtos;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.WPF.Features.Portfolio.Contracts;
using Assetra.WPF.Infrastructure;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Assetra.WPF.Features.FinancialOverview;

/// <summary>
/// Financial Overview page — shows all Asset / Investment / Liability items
/// grouped by AssetGroup in an accordion layout.
///
/// Investments are read from <see cref="Portfolio.PortfolioViewModel"/> so live
/// price updates flow in automatically (no duplicate HTTP calls).
/// Cash/Liability balances are projected from the trade journal via
/// <see cref="IBalanceQueryService"/> (single source of truth).
/// Non-cash asset values (real estate, vehicles) use the most recent
/// AssetEvent.Valuation record from <see cref="IAssetRepository"/>.
/// </summary>
public sealed partial class FinancialOverviewViewModel : ObservableObject
{
    private readonly IFinancialOverviewQueryService _queryService;
    private readonly IPortfolioPositionFeed _portfolio;
    private readonly SynchronizationContext? _uiContext;

    // ── KPI bar ──────────────────────────────────────────────────────────

    [ObservableProperty] private decimal _totalNetWorth;
    [ObservableProperty] private decimal _totalAssets;
    [ObservableProperty] private decimal _totalInvestments;
    [ObservableProperty] private decimal _totalLiabilities;
    [ObservableProperty] private string _baseCurrency = "TWD";

    public string TotalNetWorthDisplay => MoneyFormatter.Format(TotalNetWorth, BaseCurrency);
    public string TotalAssetsDisplay => MoneyFormatter.Format(TotalAssets, BaseCurrency);
    public string TotalInvestmentsDisplay => MoneyFormatter.Format(TotalInvestments, BaseCurrency);
    public string TotalLiabilitiesDisplay => MoneyFormatter.Format(TotalLiabilities, BaseCurrency);

    partial void OnTotalNetWorthChanged(decimal _) { OnPropertyChanged(nameof(TotalNetWorthDisplay)); RaiseCompositionChanged(); RebuildKpiCards(); }
    partial void OnTotalAssetsChanged(decimal _) { OnPropertyChanged(nameof(TotalAssetsDisplay)); RaiseCompositionChanged(); RebuildKpiCards(); }
    partial void OnTotalInvestmentsChanged(decimal _) { OnPropertyChanged(nameof(TotalInvestmentsDisplay)); RaiseCompositionChanged(); RebuildKpiCards(); }
    partial void OnTotalLiabilitiesChanged(decimal _)
    {
        OnPropertyChanged(nameof(TotalLiabilitiesDisplay));
        OnPropertyChanged(nameof(HasLiabilities));
        OnPropertyChanged(nameof(LiabilityShareDisplay));
        RaiseCompositionChanged();
        RebuildKpiCards();
    }
    partial void OnBaseCurrencyChanged(string _)
    {
        OnPropertyChanged(nameof(TotalNetWorthDisplay));
        OnPropertyChanged(nameof(TotalAssetsDisplay));
        OnPropertyChanged(nameof(TotalInvestmentsDisplay));
        OnPropertyChanged(nameof(TotalLiabilitiesDisplay));
        OnPropertyChanged(nameof(HeroTodayDeltaDisplay));
        RaiseCompositionChanged();
        RebuildKpiCards();
    }

    // ── Hero card ─────────────────────────────────────────────────────────
    //
    // The Hero is always-visible (淨資產 + 目標進度 + 今日漲跌).
    // v3 (2026-05-13 consolidation): goal progress switched from「所有目標 sum」
    // to「下一個 deadline / 主要目標」mental model — most users have one primary
    // goal and conflating with sum confuses progress feedback. Selection rule:
    //   1. 未達成且 deadline 最近的目標（不分過期，因 overdue 仍是「該追」的目標）
    //   2. 全部已達成 → 取 deadline 最遠的（finished but still showing aspirational）
    //   3. 無目標 → HasGoalTarget = false 顯示 CTA
    // GoalsWidget 維持顯示全列表，使用者能在那邊管理多個目標。

    /// <summary>挑出 Hero card 要顯示的「主要目標」。</summary>
    private Assetra.WPF.Features.Goals.GoalRowViewModel? PrimaryGoal
    {
        get
        {
            if (GoalsWidget is null || GoalsWidget.Goals.Count == 0)
                return null;
            // 未達成的優先；deadline 最近的優先（無 deadline 視為 MaxValue 排後）
            var primary = GoalsWidget.Goals
                .Where(g => !g.IsAchieved)
                .OrderBy(g => g.Goal.Deadline ?? DateOnly.MaxValue)
                .FirstOrDefault();
            // 全部已達成 fallback：取 deadline 最遠的（最新目標）
            return primary ?? GoalsWidget.Goals
                .OrderByDescending(g => g.Goal.Deadline ?? DateOnly.MinValue)
                .First();
        }
    }

    /// <summary>True 當有任何 goal — 控制進度條 vs 「尚未設定目標」CTA 切換。</summary>
    public bool HasGoalTarget => PrimaryGoal is not null && PrimaryGoal.Goal.TargetAmount > 0m;

    /// <summary>「目標：買房 · NT$30M」格式 — 含 goal name 讓使用者知道進度條對應哪個目標。</summary>
    public string HeroGoalTargetDisplay
    {
        get
        {
            if (PrimaryGoal is null)
                return string.Empty;
            var name = string.IsNullOrWhiteSpace(PrimaryGoal.Name) ? "" : PrimaryGoal.Name + " · ";
            return name + PrimaryGoal.TargetDisplay;
        }
    }

    /// <summary>
    /// 主要目標進度 0–100；無目標時 0。
    /// 2026-05-17 短期妥協：若 goal 設定 LinkedAssetClass，改用對應的 dashboard 即時值
    /// 算進度（auto mode），不再讀 stale 的 CurrentAmount。null = manual mode 沿用原邏輯。
    /// </summary>
    public decimal HeroGoalProgressValue
    {
        get
        {
            var g = PrimaryGoal;
            if (g is null || g.Goal.TargetAmount <= 0m)
                return 0m;

            // Portfolio-Groups-Refactor P5 — group 連結優先：用該 group cached 淨值算進度。
            // cache 未命中時退到 LinkedAssetClass / manual fallback，下一輪 refresh 命中後生效。
            if (g.Goal.PortfolioGroupId is { } groupId &&
                _groupNetValueCache.TryGetValue(groupId, out var groupValue))
            {
                return Math.Min(100m, Math.Max(0m, groupValue / g.Goal.TargetAmount * 100m));
            }

            // Auto mode：dashboard 即時值除以 target
            if (!string.IsNullOrWhiteSpace(g.Goal.LinkedAssetClass))
            {
                var currentValue = ResolveAssetClassValue(g.Goal.LinkedAssetClass);
                return Math.Min(100m, currentValue / g.Goal.TargetAmount * 100m);
            }

            // Manual mode：fallback 原本 ProgressPercent（讀 CurrentAmount）
            return g.ProgressPercent;
        }
    }

    /// <summary>
    /// 把 LinkedAssetClass 字串映射到 dashboard 對應的即時值。新增/刪除 class
    /// 時記得同步 <see cref="Assetra.Core.Models.FinancialGoal"/> 文件 + GoalsViewModel
    /// 的 LinkedAssetClassOptions。
    /// </summary>
    private decimal ResolveAssetClassValue(string linkedAssetClass) =>
        linkedAssetClass switch
        {
            "NetWorth" => BalanceSheetNetWorth,
            "TotalAssets" => BalanceSheetTotalAssets,
            "Investments" => TotalInvestments,
            "Cash" => PortfolioRef?.TotalCash ?? 0m,
            "RealEstate" => RealEstateFocusWidget?.TotalCurrentValue ?? 0m,
            "Retirement" => RetirementFocusWidget?.TotalBalance ?? 0m,
            "Physical" => PhysicalAssetFocusWidget?.TotalCurrentValue ?? 0m,
            _ => 0m,
        };

    /// <summary>
    /// Portfolio-Groups-Refactor P5 — recompute cached per-group net values for every
    /// goal currently linked to a PortfolioGroupId. After the cache fills, Hero's
    /// HeroGoalProgressValue swaps to the group-based number. Safe to call repeatedly.
    /// </summary>
    public async Task RefreshGroupNetValuesAsync(CancellationToken ct = default)
    {
        if (_groupBalanceService is null || GoalsWidget is null)
            return;
        var groupIds = GoalsWidget.Goals
            .Select(g => g.Goal.PortfolioGroupId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        if (groupIds.Count == 0)
            return;

        var changed = false;
        foreach (var id in groupIds)
        {
            try
            {
                var v = await _groupBalanceService.ComputeNetValueAsync(id, ct).ConfigureAwait(true);
                if (!_groupNetValueCache.TryGetValue(id, out var prev) || prev != v)
                {
                    _groupNetValueCache[id] = v;
                    changed = true;
                }
            }
            catch
            {
                // Skip on error — cache stays at previous value, Hero falls back to LinkedAssetClass.
            }
        }
        if (changed)
        {
            OnPropertyChanged(nameof(HeroGoalProgressValue));
            OnPropertyChanged(nameof(HeroGoalProgressDisplay));
        }
    }

    /// <summary>「目標達成 38%」字串。</summary>
    public string HeroGoalProgressDisplay =>
        HasGoalTarget
            ? HeroGoalProgressValue.ToString("F0", CultureInfo.InvariantCulture) + "%"
            : string.Empty;

    /// <summary>True 當 PortfolioRef 有報價、DayPnl 不是 0 — 控制 Hero 內「今日 +XXX」是否渲染。</summary>
    public bool HasHeroTodayDelta =>
        PortfolioRef is not null && PortfolioRef.HasDayPnl && PortfolioRef.DayPnl != 0m;

    /// <summary>今日漲跌絕對金額（含 +/- 符號 + 千分位）。</summary>
    public string HeroTodayDeltaDisplay
    {
        get
        {
            if (PortfolioRef is null || !PortfolioRef.HasDayPnl)
                return string.Empty;
            var v = PortfolioRef.DayPnl;
            var sign = v >= 0m ? "+" : "−";
            return sign + MoneyFormatter.Format(Math.Abs(v), BaseCurrency);
        }
    }

    /// <summary>True 當今日漲跌 ≥ 0（控制顏色 Up vs Down）。</summary>
    public bool IsHeroTodayPositive => PortfolioRef is null || PortfolioRef.DayPnl >= 0m;

    // ── Composition view (asset class breakdown) ──────────────────────────
    //
    // 5 asset classes (Cash / Investments / RealEstate / Retirement / Physical),
    // each a `(name, value)` pair. Composition view filters out 0-value classes
    // and renders proportional bars. Insurance excluded — annual premium isn't
    // a balance-sheet asset (it's an expense flow).

    public bool HasLiabilities => TotalLiabilities > 0m;

    // ── BalanceSheet totals (v2 — fix 總資產 bug) ────────────────────────
    //
    // `TotalAssets` from IFinancialOverviewQueryService only sums asset_item
    // records (mostly cash + manual entries). Physical / real-estate / retirement
    // live in *separate* services (PhysicalAssetService etc.) and never reach
    // that table — so the resulting `TotalAssets` undercounts. The Hero card
    // and 公式列 should display the actual balance-sheet total, which equals
    // the composition view's sum. Computed on-the-fly from the same widget VMs
    // that feed the composition slices to guarantee consistency.

    /// <summary>真實「balance sheet 總資產」— 投資 + 現金 + 不動產 + 退休 + 實物，跨所有資料源加總。</summary>
    public decimal BalanceSheetTotalAssets =>
        TotalInvestments
        + (PortfolioRef?.TotalCash ?? 0m)
        + (RealEstateFocusWidget?.TotalCurrentValue ?? 0m)
        + (RetirementFocusWidget?.TotalBalance ?? 0m)
        + (PhysicalAssetFocusWidget?.TotalCurrentValue ?? 0m);

    public string BalanceSheetTotalAssetsDisplay =>
        MoneyFormatter.Format(BalanceSheetTotalAssets, BaseCurrency);

    /// <summary>真實淨資產 = BalanceSheetTotalAssets − TotalLiabilities。</summary>
    public decimal BalanceSheetNetWorth => BalanceSheetTotalAssets - TotalLiabilities;

    public string BalanceSheetNetWorthDisplay =>
        MoneyFormatter.Format(BalanceSheetNetWorth, BaseCurrency);

    /// <summary>負債佔總資產的比例（0–100% string，例「14.7%」）— 顯示在負債獨立 bar 旁。</summary>
    public string LiabilityShareDisplay
    {
        get
        {
            var assets = TotalAssets + TotalInvestments;
            if (assets <= 0m)
                return "0%";
            return (TotalLiabilities / assets * 100m).ToString("F1", CultureInfo.InvariantCulture) + "%";
        }
    }

    /// <summary>
    /// 資產組成列表：非零的資產類，依金額由大到小排序。
    /// 五類：投資資產 / 現金 / 不動產 / 退休專戶 / 實物資產。每類有名稱、金額、佔比、顏色 token。
    /// 為了避免 binding null 造成的 fallback，這裡用具體 record，UI 直接綁。
    /// </summary>
    public IReadOnlyList<AssetCompositionSlice> CompositionSlices
    {
        get
        {
            var slices = new List<AssetCompositionSlice>();

            // 收集各類資產金額（缺 widget 就當 0）
            void Add(string nameKey, decimal value, string colorKey, string? navTag)
            {
                if (value > 0m)
                    slices.Add(new AssetCompositionSlice(nameKey, value, colorKey, navTag));
            }

            // 投資資產（含現金 → 不對，TotalInvestments 是純投資 market value）
            Add("Dashboard.Widget.AssetClass.Investments", TotalInvestments, "#3B82F6", "Investments");
            Add("Dashboard.Widget.AssetClass.Cash", PortfolioRef?.TotalCash ?? 0m, "#10B981", "Cash");
            Add("Dashboard.Widget.AssetClass.RealEstate", RealEstateFocusWidget?.TotalCurrentValue ?? 0m, "#8B5CF6", "RealEstate");
            Add("Dashboard.Widget.AssetClass.Retirement", RetirementFocusWidget?.TotalBalance ?? 0m, "#F59E0B", "Retirement");
            Add("Dashboard.Widget.AssetClass.Physical", PhysicalAssetFocusWidget?.TotalCurrentValue ?? 0m, "#F97316", "Physical");

            var total = slices.Sum(s => s.Value);
            if (total <= 0m)
                return slices;

            // 按金額降冪 + 計算各自佔比
            return slices
                .OrderByDescending(s => s.Value)
                .Select(s => s with { Percent = s.Value / total * 100m, Total = total, BaseCurrency = BaseCurrency })
                .ToList();
        }
    }

    public bool HasCompositionData => CompositionSlices.Count > 0;

    /// <summary>所有 composition slice 的總和（投資 + 現金 + 不動產 + 退休 + 實物）— 通常 = TotalAssets + TotalInvestments。</summary>
    public string CompositionTotalDisplay
    {
        get
        {
            var t = CompositionSlices.Sum(s => s.Value);
            return MoneyFormatter.Format(t, BaseCurrency);
        }
    }

    /// <summary>負債金額在 composition 區域顯示用（紅色獨立 bar）。</summary>
    public string CompositionLiabilityDisplay => MoneyFormatter.Format(TotalLiabilities, BaseCurrency);

    private void RaiseCompositionChanged()
    {
        // Portfolio-Groups-Refactor P5 — fire-and-forget refresh of per-group net values so
        // Hero goal progress reflects trade activity in real time. Underlying portfolio
        // totals firing RaiseCompositionChanged is our trade-event signal.
        _ = RefreshGroupNetValuesAsync();
        OnPropertyChanged(nameof(CompositionSlices));
        OnPropertyChanged(nameof(HasCompositionData));
        OnPropertyChanged(nameof(CompositionTotalDisplay));
        OnPropertyChanged(nameof(CompositionLiabilityDisplay));
        OnPropertyChanged(nameof(BalanceSheetTotalAssets));
        OnPropertyChanged(nameof(BalanceSheetTotalAssetsDisplay));
        OnPropertyChanged(nameof(BalanceSheetNetWorth));
        OnPropertyChanged(nameof(BalanceSheetNetWorthDisplay));
        OnPropertyChanged(nameof(HeroGoalProgressValue));
        OnPropertyChanged(nameof(HeroGoalProgressDisplay));
        OnPropertyChanged(nameof(HeroGoalTargetDisplay));
        OnPropertyChanged(nameof(HasGoalTarget));
        OnPropertyChanged(nameof(HeroTodayDeltaDisplay));
        OnPropertyChanged(nameof(HasHeroTodayDelta));
        OnPropertyChanged(nameof(IsHeroTodayPositive));
    }

    /// <summary>
    /// Snapshot of the KPI bar cards in user-selected order. Rebuilt whenever
    /// the underlying totals or the user's <see cref="AppSettings.OverviewKpis"/>
    /// selection changes. Bound to an ItemsControl in the view.
    /// </summary>
    private readonly ObservableCollection<KpiCardVm> _kpiCards = [];
    public ReadOnlyObservableCollection<KpiCardVm> KpiCards { get; }

    // ── KPI selector dialog state ─────────────────────────────────────────

    /// <summary>True while the gear-icon-triggered KPI edit dialog is open.</summary>
    [ObservableProperty]
    private bool _isKpiEditorOpen;

    /// <summary>
    /// Live editor list — one item per available metric, IsSelected toggled
    /// by checkboxes. Snapshotted from the persisted selection on open;
    /// flushed back to AppSettings on save.
    /// </summary>
    private readonly ObservableCollection<KpiSelectionItemVm> _kpiEditorItems = [];
    public ReadOnlyObservableCollection<KpiSelectionItemVm> KpiEditorItems { get; }

    [ObservableProperty]
    private int _kpiSelectedCount;

    [ObservableProperty]
    private bool _canSaveKpiSelection;

    public int KpiMinSelected => KpiMetricCatalog.MinSelected;
    public int KpiMaxSelected => KpiMetricCatalog.MaxSelected;

    // ── Accordion collections ─────────────────────────────────────────────

    private readonly ObservableCollection<AssetGroupVm> _assetGroups = [];
    private readonly ObservableCollection<AssetGroupVm> _investGroups = [];
    private readonly ObservableCollection<AssetGroupVm> _liabGroups = [];
    public ReadOnlyObservableCollection<AssetGroupVm> AssetGroups { get; }
    public ReadOnlyObservableCollection<AssetGroupVm> InvestGroups { get; }
    public ReadOnlyObservableCollection<AssetGroupVm> LiabGroups { get; }

    [ObservableProperty] private bool _isLoading;

    /// <summary>
    /// Stage 2 (Dashboard consolidation)：4-tab 儀表板的 active tab。
    /// 由 NavRailView 的 FinancialOverviewContentTemplate 內的 TabControl
    /// 透過 SelectedValue / Tag 雙向綁定。預設停留在「總覽」。
    /// </summary>
    [ObservableProperty] private DashboardTab _selectedDashboardTab = DashboardTab.Overview;

    private readonly IAppSettingsService? _settings;

    /// <summary>
    /// Stage 2.5 (Dashboard consolidation)：總覽 tab 上的 widget 直接綁這三個 VM。
    /// optional 注入，測試 / design-time 可省略。widget 不允許從首頁編輯，
    /// 只投影 + 點擊跳轉。
    /// </summary>
    public Assetra.WPF.Features.Goals.GoalsViewModel? GoalsWidget { get; }
    public Assetra.WPF.Features.Fire.FireViewModel? FireWidget { get; }
    public Assetra.WPF.Features.Assistant.AssistantViewModel? AssistantWidget { get; }

    /// <summary>
    /// Stage 2.5 polish：總覽 widget 顯示的前 3 個目標 — 按 deadline 升冪
    /// （無 deadline 排最後）。GoalsWidget.Goals 集合變動時自動 refresh。
    /// </summary>
    public IEnumerable<Assetra.WPF.Features.Goals.GoalRowViewModel> TopThreeGoals =>
        GoalsWidget is null
            ? []
            : GoalsWidget.Goals
                .OrderBy(g => g.Goal.Deadline ?? DateOnly.MaxValue)
                .Take(3);

    public bool HasNoGoals => GoalsWidget is null || GoalsWidget.Goals.Count == 0;

    public string GoalsWidgetTotalActiveGoalsDisplay =>
        (GoalsWidget?.Goals.Count ?? 0).ToString("N0", CultureInfo.CurrentCulture);

    public string GoalsWidgetNearestDeadlineDisplay
    {
        get
        {
            var next = GoalsWidget?.Goals
                .Where(g => !g.IsAchieved)
                .Select(g => new { Goal = g, Deadline = g.Goal.Deadline })
                .Where(g => g.Deadline.HasValue)
                .OrderBy(g => g.Deadline)
                .FirstOrDefault();
            if (next is null)
                return "—";

            return string.Format(
                CultureInfo.CurrentCulture,
                "{0} · {1:yyyy-MM-dd}",
                next.Goal.Name,
                next.Deadline.GetValueOrDefault());
        }
    }

    public string GoalsWidgetAttentionGoalsDisplay
    {
        get
        {
            var count = GoalsWidget?.Goals.Count(g => g.StatusTag is "warning" or "overdue") ?? 0;
            return count.ToString("N0", CultureInfo.CurrentCulture);
        }
    }

    public string GoalsWidgetTopProgressDisplay
    {
        get
        {
            var goal = GoalsWidget?.Goals
                .OrderByDescending(g => g.ProgressPercent)
                .ThenBy(g => g.Goal.Deadline ?? DateOnly.MaxValue)
                .FirstOrDefault();
            return goal is null ? "—" : $"{goal.Name} · {goal.ProgressDisplay}";
        }
    }

    private void NotifyGoalsWidgetSummaryChanged()
    {
        OnPropertyChanged(nameof(TopThreeGoals));
        OnPropertyChanged(nameof(HasNoGoals));
        OnPropertyChanged(nameof(GoalsWidgetTotalActiveGoalsDisplay));
        OnPropertyChanged(nameof(GoalsWidgetNearestDeadlineDisplay));
        OnPropertyChanged(nameof(GoalsWidgetAttentionGoalsDisplay));
        OnPropertyChanged(nameof(GoalsWidgetTopProgressDisplay));
        RaiseCompositionChanged();
    }

    // ── Today's Movers widget（盤中 / 收盤後即時損益）─────────────────────────
    /// <summary>
    /// 今日漲幅前 3 名 — 從 PortfolioRef.Positions 過濾 DayChange > 0，按
    /// |DayChange| 降冪。對應 broker app「即時損益」橫切面 view。
    /// </summary>
    public IEnumerable<Assetra.WPF.Features.Portfolio.PortfolioRowViewModel> TopGainersToday =>
        PortfolioRef is null
            ? []
            : PortfolioRef.Positions
                .Where(p => p.DayChange > 0m)
                .OrderByDescending(p => p.DayChange)
                .Take(3);

    /// <summary>今日跌幅前 3 名 — DayChange < 0，按 |DayChange| 降冪。</summary>
    public IEnumerable<Assetra.WPF.Features.Portfolio.PortfolioRowViewModel> TopLosersToday =>
        PortfolioRef is null
            ? []
            : PortfolioRef.Positions
                .Where(p => p.DayChange < 0m)
                .OrderBy(p => p.DayChange)
                .Take(3);

    /// <summary>True 當有任一 mover；空白時 widget 隱藏（避免「未開盤」期間空 card）。</summary>
    public bool HasTodaysMovers =>
        PortfolioRef is not null
        && PortfolioRef.Positions.Any(p => p.DayChange != 0m);

    /// <summary>
    /// Long-term refactor：「投資焦點卡」用的 VM。原本是 Portfolio.Dashboard 內 tab
    /// 的資料來源，現在升到「全域財務儀表板」的總覽 tab，作為對應「投資資產」
    /// 工作頁的 glance summary。Portfolio 頁本身的 Dashboard tab 已移除。
    /// </summary>
    public Assetra.WPF.Features.Portfolio.DashboardViewModel? InvestmentFocusWidget { get; }

    /// <summary>
    /// Phase C：「現金 / 負債焦點卡」共用的資料源 — PortfolioViewModel 已有
    /// TotalCash / TotalLiabilities / CashAccounts / Liabilities 集合。
    /// 不再走 IPortfolioPositionFeed 介面以方便綁定 collection.Count。
    /// </summary>
    public Assetra.WPF.Features.Portfolio.PortfolioViewModel? PortfolioRef { get; }

    /// <summary>
    /// Phase C：「不動產焦點卡」資料源。RealEstateViewModel 內已有
    /// PropertyCount / TotalCurrentValue / TotalMortgageBalance / TotalEquity。
    /// </summary>
    public Assetra.WPF.Features.RealEstate.RealEstateViewModel? RealEstateFocusWidget { get; }

    /// <summary>Phase C 擴展：保險焦點卡（PolicyCount + TotalAnnualPremium）。</summary>
    public Assetra.WPF.Features.Insurance.InsurancePolicyViewModel? InsuranceFocusWidget { get; }

    /// <summary>Phase C 擴展：退休專戶焦點卡（AccountCount + TotalBalance）。</summary>
    public Assetra.WPF.Features.Retirement.RetirementViewModel? RetirementFocusWidget { get; }

    /// <summary>Phase C 擴展：實物資產焦點卡（AssetCount + TotalCurrentValue）。</summary>
    public Assetra.WPF.Features.PhysicalAsset.PhysicalAssetViewModel? PhysicalAssetFocusWidget { get; }

    /// <summary>True 當六個資產類焦點卡至少有一個 VM 注入；全 null 時整個
    /// AssetClassFocusWidget 隱藏，避免空白 header card。</summary>
    public bool HasAnyAssetClassFocus =>
        PortfolioRef is not null
        || RealEstateFocusWidget is not null
        || InsuranceFocusWidget is not null
        || RetirementFocusWidget is not null
        || PhysicalAssetFocusWidget is not null;

    // ── v2：資產類焦點卡 6 cell 顯示偏好 ──────────────────────────────────────
    // 使用者在 settings.json 加 AssetClassFocusVisibility 即生效；
    // 例：{ "Cash": true, "Insurance": false } → 隱藏保險格。
    // 缺鍵預設 true（顯示）。
    // 每個 cell 顯示 = (對應 VM 有注入) AND (使用者沒在 settings 把它關掉)
    public bool IsCashFocusVisible => PortfolioRef is not null && IsAssetClassVisible("Cash");
    public bool IsLiabilityFocusVisible => PortfolioRef is not null && IsAssetClassVisible("Liability");
    public bool IsRealEstateFocusVisible => RealEstateFocusWidget is not null && IsAssetClassVisible("RealEstate");
    public bool IsInsuranceFocusVisible => InsuranceFocusWidget is not null && IsAssetClassVisible("Insurance");
    public bool IsRetirementFocusVisible => RetirementFocusWidget is not null && IsAssetClassVisible("Retirement");
    public bool IsPhysicalFocusVisible => PhysicalAssetFocusWidget is not null && IsAssetClassVisible("Physical");

    private bool IsAssetClassVisible(string key)
    {
        var map = _settings?.Current.AssetClassFocusVisibility;
        if (map is null)
            return true;
        return map.TryGetValue(key, out var v) ? v : true;
    }

    /// <summary>
    /// Portfolio-Groups-Refactor P5 — cached per-group net values. Refreshed when goals load
    /// or when underlying trade data changes. Lookup is sync (dictionary read); the async
    /// compute runs out-of-band so the Hero binding stays cheap.
    /// </summary>
    private readonly Dictionary<Guid, decimal> _groupNetValueCache = new();
    private readonly Assetra.Core.Interfaces.IGroupBalanceQueryService? _groupBalanceService;

    public FinancialOverviewViewModel(
        IFinancialOverviewQueryService queryService,
        IPortfolioPositionFeed portfolio,
        IAppSettingsService? settings = null,
        Assetra.WPF.Features.Goals.GoalsViewModel? goalsWidget = null,
        Assetra.WPF.Features.Fire.FireViewModel? fireWidget = null,
        Assetra.WPF.Features.Assistant.AssistantViewModel? assistantWidget = null,
        Assetra.WPF.Features.Portfolio.DashboardViewModel? investmentFocusWidget = null,
        Assetra.WPF.Features.Portfolio.PortfolioViewModel? portfolioRef = null,
        Assetra.WPF.Features.RealEstate.RealEstateViewModel? realEstateFocusWidget = null,
        Assetra.WPF.Features.Insurance.InsurancePolicyViewModel? insuranceFocusWidget = null,
        Assetra.WPF.Features.Retirement.RetirementViewModel? retirementFocusWidget = null,
        Assetra.WPF.Features.PhysicalAsset.PhysicalAssetViewModel? physicalAssetFocusWidget = null,
        Assetra.Core.Interfaces.IGroupBalanceQueryService? groupBalanceService = null)
    {
        _groupBalanceService = groupBalanceService;
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
        _portfolio = portfolio ?? throw new ArgumentNullException(nameof(portfolio));
        _settings = settings;
        GoalsWidget = goalsWidget;
        FireWidget = fireWidget;
        AssistantWidget = assistantWidget;
        InvestmentFocusWidget = investmentFocusWidget;
        PortfolioRef = portfolioRef;
        RealEstateFocusWidget = realEstateFocusWidget;
        InsuranceFocusWidget = insuranceFocusWidget;
        RetirementFocusWidget = retirementFocusWidget;
        PhysicalAssetFocusWidget = physicalAssetFocusWidget;
        _uiContext = SynchronizationContext.Current;

        // BalanceSheet totals depend on each focus widget's running total. When
        // a property like RealEstate.TotalCurrentValue / Retirement.TotalBalance /
        // PhysicalAsset.TotalCurrentValue changes (after add/edit/delete), refresh
        // the Hero + 公式列 + composition view via RaiseCompositionChanged.
        if (RealEstateFocusWidget is not null)
            RealEstateFocusWidget.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(Assetra.WPF.Features.RealEstate.RealEstateViewModel.TotalCurrentValue))
                    RaiseCompositionChanged();
            };
        if (RetirementFocusWidget is not null)
            RetirementFocusWidget.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(Assetra.WPF.Features.Retirement.RetirementViewModel.TotalBalance))
                    RaiseCompositionChanged();
            };
        if (PhysicalAssetFocusWidget is not null)
            PhysicalAssetFocusWidget.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(Assetra.WPF.Features.PhysicalAsset.PhysicalAssetViewModel.TotalCurrentValue))
                    RaiseCompositionChanged();
            };

        // Stock prices arrive asynchronously after Portfolio.LoadAsync() completes.
        // Subscribing to TotalMarketValue lets FinancialOverview re-snapshot once
        // the first batch of live prices lands; without this hook the Investments
        // section freezes at TWD 0 because LoadAsync was called pre-price-fetch.
        _portfolio.PropertyChanged += OnPortfolioPropertyChanged;

        // Stage 2 (Dashboard consolidation)：接收 NavRail 攔截 Trends leaf 後送
        // 過來的 tab 切換請求。lifetime = application；無需 unsubscribe。
        Assetra.WPF.Infrastructure.ShellNavigationEvents.DashboardTabRequested += OnDashboardTabRequested;

        // Stage 2.5 polish：監聽 Goals 集合變化以 refresh TopThreeGoals + HasNoGoals。
        // Also raise Hero card props (HasGoalTarget / progress) because the Hero
        // bar shows goal progress and needs to redraw when a goal is added / removed.
        if (GoalsWidget is not null && GoalsWidget.Goals is System.Collections.Specialized.INotifyCollectionChanged ncc)
        {
            ncc.CollectionChanged += (_, _) =>
            {
                NotifyGoalsWidgetSummaryChanged();
                // Portfolio-Groups-Refactor P5 — refresh per-group cache when goals collection changes.
                _ = RefreshGroupNetValuesAsync();
            };
        }
        // GoalsWidget aggregate properties (TotalTarget / OverallProgressPercent)
        // fire PropertyChanged when individual goal rows change their target.
        if (GoalsWidget is not null)
        {
            GoalsWidget.PropertyChanged += (_, ge) =>
            {
                if (ge.PropertyName is "GoalCount" or "TotalTarget" or "TotalCurrent"
                    or "OverallProgressPercent" or "TotalTargetDisplay" or "OverallProgressDisplay")
                {
                    NotifyGoalsWidgetSummaryChanged();
                }
            };
        }

        // Today's Movers：監聽 Positions 集合與每 row 的 DayChange 變化。
        // Positions 是 ReadOnlyObservableCollection，先聽 CollectionChanged；
        // 每 row 的 DayChange / DayChangePercent 改動透過 PropertyChanged
        // 重新觸發 TopGainers/TopLosers/HasTodaysMovers 通知。
        if (PortfolioRef is not null
            && PortfolioRef.Positions is System.Collections.Specialized.INotifyCollectionChanged posNcc)
        {
            posNcc.CollectionChanged += (_, _) => RefreshTodaysMovers();
            foreach (var row in PortfolioRef.Positions)
                HookRowPnl(row);
            // 補上未來新增的 row（CollectionChanged.NewItems 也 hook）
            posNcc.CollectionChanged += (_, e) =>
            {
                if (e.NewItems is null)
                    return;
                foreach (Assetra.WPF.Features.Portfolio.PortfolioRowViewModel r in e.NewItems)
                    HookRowPnl(r);
            };
        }

        KpiCards = new ReadOnlyObservableCollection<KpiCardVm>(_kpiCards);
        KpiEditorItems = new ReadOnlyObservableCollection<KpiSelectionItemVm>(_kpiEditorItems);
        AssetGroups = new ReadOnlyObservableCollection<AssetGroupVm>(_assetGroups);
        InvestGroups = new ReadOnlyObservableCollection<AssetGroupVm>(_investGroups);
        LiabGroups = new ReadOnlyObservableCollection<AssetGroupVm>(_liabGroups);

        if (_settings is not null)
            _settings.Changed += RebuildKpiCards;

        RebuildKpiCards();
    }

    // Stage 2.5：widget「→ 看全部」連結
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void NavigateToGoals() =>
        Assetra.WPF.Infrastructure.ShellNavigationEvents.RequestNavigateTo("Goals");

    /// <summary>
    /// 2026-05-17：Hero「點此設定 →」CTA 用此 command — 跳到 Goals tab + 直接打開
    /// add-goal dialog，省一步點擊。GoalsWidget VM 已注入，直接 set IsFormOpen=true。
    /// 沒注入 GoalsWidget (test/headless) 時 fallback 為單純 navigate。
    /// </summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void NavigateToGoalsAndAdd()
    {
        if (GoalsWidget is not null)
        {
            // 確保是「新增」模式而非殘留的編輯狀態
            GoalsWidget.EditingId = null;
            GoalsWidget.IsFormOpen = true;
        }
        Assetra.WPF.Infrastructure.ShellNavigationEvents.RequestNavigateTo("Goals");
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void NavigateToFire() =>
        Assetra.WPF.Infrastructure.ShellNavigationEvents.RequestNavigateTo("Fire");

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void NavigateToAssistant() =>
        Assetra.WPF.Infrastructure.ShellNavigationEvents.RequestNavigateTo("Assistant");

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void NavigateToPortfolio() =>
        Assetra.WPF.Infrastructure.ShellNavigationEvents.RequestNavigateTo("Portfolio");

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void NavigateToCashAccounts() =>
        Assetra.WPF.Infrastructure.ShellNavigationEvents.RequestNavigateTo("CashAccounts");

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void NavigateToLiabilities() =>
        Assetra.WPF.Infrastructure.ShellNavigationEvents.RequestNavigateTo("Liabilities");

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void NavigateToRealEstate() =>
        Assetra.WPF.Infrastructure.ShellNavigationEvents.RequestNavigateTo("RealEstate");

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void NavigateToInsurance() =>
        Assetra.WPF.Infrastructure.ShellNavigationEvents.RequestNavigateTo("Insurance");

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void NavigateToRetirement() =>
        Assetra.WPF.Infrastructure.ShellNavigationEvents.RequestNavigateTo("Retirement");

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void NavigateToPhysicalAsset() =>
        Assetra.WPF.Infrastructure.ShellNavigationEvents.RequestNavigateTo("PhysicalAsset");

    // ── v2：KPI 編輯 dialog 內 reorder（▲▼）─────────────────────────────────
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void MoveKpiUp(KpiSelectionItemVm? item)
    {
        if (item is null)
            return;
        var idx = _kpiEditorItems.IndexOf(item);
        if (idx <= 0)
            return;
        _kpiEditorItems.Move(idx, idx - 1);
        RecomputeKpiEditorState();
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void MoveKpiDown(KpiSelectionItemVm? item)
    {
        if (item is null)
            return;
        var idx = _kpiEditorItems.IndexOf(item);
        if (idx < 0 || idx >= _kpiEditorItems.Count - 1)
            return;
        _kpiEditorItems.Move(idx, idx + 1);
        RecomputeKpiEditorState();
    }

    private void HookRowPnl(Assetra.WPF.Features.Portfolio.PortfolioRowViewModel row)
    {
        row.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(row.DayChange)
                || e.PropertyName == nameof(row.DayChangePercent))
            {
                RefreshTodaysMovers();
            }
        };
    }

    private void RefreshTodaysMovers()
    {
        OnPropertyChanged(nameof(TopGainersToday));
        OnPropertyChanged(nameof(TopLosersToday));
        OnPropertyChanged(nameof(HasTodaysMovers));
    }

    private void OnDashboardTabRequested(string tabName)
    {
        if (Enum.TryParse<DashboardTab>(tabName, ignoreCase: true, out var tab))
        {
            // 確保 marshal 回 UI thread；event 可能來自任何 caller。
            if (_uiContext is not null && SynchronizationContext.Current != _uiContext)
                _uiContext.Post(_ => SelectedDashboardTab = tab, null);
            else
                SelectedDashboardTab = tab;
        }
    }

    private void OnPortfolioPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // TotalMarketValue / TotalCash 改變時重新載入 — 涵蓋價格更新、現金流、
        // 帳戶 metadata 編輯（PortfolioViewModel 在 ReloadAfterAccountChanged 末端
        // 主動 raise TotalCash PropertyChanged，即使數值未變）。
        if (e.PropertyName == nameof(IPortfolioPositionFeed.TotalMarketValue)
            || e.PropertyName == nameof(IPortfolioPositionFeed.TotalCash))
            AsyncHelpers.SafeFireAndForget(LoadAsync, "FinancialOverview.Load");

        // Investment P&L KPI cards depend on both TotalMarketValue and TotalCost.
        // Rebuild on either so the card refreshes when price quotes land or when
        // a new buy/sell shifts the cost basis.
        if (e.PropertyName == nameof(IPortfolioPositionFeed.TotalMarketValue)
            || e.PropertyName == nameof(IPortfolioPositionFeed.TotalCost))
            RebuildKpiCards();

        // Hero card + composition：DayPnl / TotalCash 改動會影響 Hero 今日漲跌
        // 以及 composition slice（現金那一格金額）。
        if (e.PropertyName is "DayPnl" or "HasDayPnl" or "IsDayPnlPositive"
            || e.PropertyName == nameof(IPortfolioPositionFeed.TotalCash))
        {
            RaiseCompositionChanged();
        }
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (IsLoading)
            return;
        IsLoading = true;
        try
        {
            // v3 bug fix 2026-05-17：Hero card 用 GoalsWidget.Goals 算 PrimaryGoal /
            // HasGoalTarget，但 GoalsWidget.LoadAsync 原本只在 Goals tab 變 visible
            // 時觸發。Dashboard 一開 app 時 Goals 還沒載 → Hero 顯示「尚未設定目標」CTA。
            // 切換 Goals tab 後它載入完才 fire CollectionChanged → Hero 才更新。
            //
            // 修法：FinancialOverview.LoadAsync 主動推 GoalsWidget 載入（fire-and-forget
            // 不阻塞 dashboard 主流程；IsLoaded gate 確保不會 double-load）。GoalsWidget
            // 已有 IsLoading + IsLoaded protection。
            if (GoalsWidget is not null)
                AsyncHelpers.SafeFireAndForget(GoalsWidget.LoadAsync, "FinancialOverview.GoalsPrefetch");

            await LoadInternalAsync().ConfigureAwait(true);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadInternalAsync()
    {
        var result = await _queryService.BuildAsync(
            _portfolio.Positions
                .Select(p => new FinancialOverviewInvestmentItem(
                    p.Id,
                    p.Name,
                    p.Currency,
                    p.MarketValue,
                    p.AssetType))
                .ToList())
            .ConfigureAwait(true);

        void ApplyResult()
        {
            _assetGroups.Clear();
            foreach (var g in result.AssetGroups)
                _assetGroups.Add(ToGroupVm(g));

            _investGroups.Clear();
            foreach (var g in result.InvestmentGroups)
                _investGroups.Add(ToGroupVm(g));

            _liabGroups.Clear();
            foreach (var g in result.LiabilityGroups)
                _liabGroups.Add(ToGroupVm(g));

            BaseCurrency = result.BaseCurrency;
            TotalAssets = result.TotalAssets;
            TotalInvestments = result.TotalInvestments;
            TotalLiabilities = result.TotalLiabilities;
            TotalNetWorth = result.TotalAssets + result.TotalInvestments - result.TotalLiabilities;
        }

        if (_uiContext is null || SynchronizationContext.Current == _uiContext)
        {
            ApplyResult();
            return;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _uiContext.Post(_ =>
        {
            try
            {
                ApplyResult();
                completion.SetResult();
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        }, null);
        await completion.Task.ConfigureAwait(false);
    }

    private static AssetGroupVm ToGroupVm(FinancialOverviewGroup group)
    {
        var vm = new AssetGroupVm { Icon = group.Icon, Name = group.Name, Currency = group.Currency };
        foreach (var item in group.Items)
            vm.AddItem(new AssetItemVm { Id = item.Id, Name = item.Name, Currency = item.Currency, CurrentValue = item.CurrentValue });
        vm.Subtotal = group.Subtotal;
        return vm;
    }

    // ── KPI bar — user-selectable cards ──────────────────────────────────

    private void RebuildKpiCards()
    {
        // Triggered from multiple sources, some of which fire on background
        // threads (IPortfolioPositionFeed.PropertyChanged after a quote-fetch
        // worker, IAppSettingsService.Changed after a save Task). Marshal to
        // the UI thread before mutating the ObservableCollection — WPF's
        // CollectionView throws NotSupportedException on cross-thread edits.
        if (_uiContext is not null && SynchronizationContext.Current != _uiContext)
        {
            _uiContext.Post(_ => RebuildKpiCardsCore(), null);
            return;
        }
        RebuildKpiCardsCore();
    }

    private void RebuildKpiCardsCore()
    {
        var persisted = _settings?.Current.OverviewKpis;
        var ids = string.IsNullOrWhiteSpace(persisted)
            ? KpiMetricCatalog.Default
            : KpiMetricCatalog.ParseSelection(persisted.Split(','));

        _kpiCards.Clear();
        foreach (var id in ids)
            _kpiCards.Add(BuildCard(id));
    }

    private KpiCardVm BuildCard(KpiMetric id)
    {
        var info = KpiMetricCatalog.All.First(m => m.Id == id);
        return id switch
        {
            KpiMetric.NetWorth =>
                Money(id, info.LabelKey, TotalNetWorth, KpiTone.Neutral),
            KpiMetric.TotalAssets =>
                Money(id, info.LabelKey, TotalAssets + TotalInvestments, KpiTone.Up),
            KpiMetric.OtherAssets =>
                Money(id, info.LabelKey, TotalAssets, KpiTone.Neutral),
            KpiMetric.Investments =>
                Money(id, info.LabelKey, TotalInvestments, KpiTone.Accent),
            KpiMetric.Liabilities =>
                Money(id, info.LabelKey, TotalLiabilities, KpiTone.Down),
            KpiMetric.DebtRatio =>
                // v2：負債比率主值 + 槓桿比副值在同一張 card（兩者為同一財務事實的不同表述）。
                // 槓桿 = 總資產 / 淨資產；淨資產 ≤ 0 時無意義，副值留空。
                Ratio(id, info.LabelKey, TotalLiabilities, TotalAssets + TotalInvestments, KpiTone.Neutral)
                    with
                {
                    SecondaryLabelKey = "Portfolio.Liability.LeverageRatio",
                    SecondaryValueDisplay = TotalNetWorth > 0m
                            ? ((TotalAssets + TotalInvestments) / TotalNetWorth).ToString("F2", CultureInfo.InvariantCulture)
                            : string.Empty,
                },
            KpiMetric.InvestmentPnl =>
                Money(id, info.LabelKey,
                    _portfolio.TotalMarketValue - _portfolio.TotalCost,
                    SignTone(_portfolio.TotalMarketValue - _portfolio.TotalCost)),
            KpiMetric.InvestmentPnlPercent =>
                Percent(id, info.LabelKey,
                    _portfolio.TotalCost == 0m ? 0m
                        : (_portfolio.TotalMarketValue - _portfolio.TotalCost) / _portfolio.TotalCost,
                    SignTone(_portfolio.TotalMarketValue - _portfolio.TotalCost)),
            _ => new KpiCardVm(id, info.LabelKey, "—", KpiTone.Neutral),
        };
    }

    private KpiCardVm Money(KpiMetric id, string labelKey, decimal value, KpiTone tone) =>
        new(id, labelKey, MoneyFormatter.Format(value, BaseCurrency), tone);

    private static KpiCardVm Ratio(KpiMetric id, string labelKey, decimal numerator, decimal denominator, KpiTone tone)
    {
        var pct = denominator == 0m ? 0m : numerator / denominator * 100m;
        return new KpiCardVm(id, labelKey, pct.ToString("F1", CultureInfo.InvariantCulture) + "%", tone);
    }

    private static KpiCardVm Percent(KpiMetric id, string labelKey, decimal fraction, KpiTone tone)
    {
        var sign = fraction >= 0m ? "+" : string.Empty;
        return new KpiCardVm(id, labelKey, sign + (fraction * 100m).ToString("F2", CultureInfo.InvariantCulture) + "%", tone);
    }

    private static KpiTone SignTone(decimal value) =>
        value > 0m ? KpiTone.Up
        : value < 0m ? KpiTone.Down
        : KpiTone.Neutral;

    // ── KPI editor commands ──────────────────────────────────────────────

    [RelayCommand]
    private void OpenKpiEditor()
    {
        // 讀使用者已儲存的 KPI 順序（CSV of IDs, 已選擇者的順序）
        var current = string.IsNullOrWhiteSpace(_settings?.Current.OverviewKpis)
            ? KpiMetricCatalog.Default
            : KpiMetricCatalog.ParseSelection(_settings.Current.OverviewKpis.Split(','));
        var selectedSet = current.ToHashSet();

        _kpiEditorItems.Clear();

        // ⚠ 重點：要先依「已儲存順序」放入已選 KPI，再補上未選的（依 catalog 預設順序）。
        // 不這樣做會發生：用戶用 ▲▼ 在 dialog 內重新排序、儲存後 → 主頁排序正確；
        // 下次再開 dialog 卻回到 catalog 靜態順序（不見得是用戶剛存的順序）。
        var catalogById = KpiMetricCatalog.All.ToDictionary(x => x.Id);
        foreach (var id in current)
        {
            if (catalogById.TryGetValue(id, out var info))
                _kpiEditorItems.Add(new KpiSelectionItemVm(info, true, RecomputeKpiEditorState));
        }
        foreach (var info in KpiMetricCatalog.All)
        {
            if (!selectedSet.Contains(info.Id))
                _kpiEditorItems.Add(new KpiSelectionItemVm(info, false, RecomputeKpiEditorState));
        }

        RecomputeKpiEditorState();
        IsKpiEditorOpen = true;
    }

    [RelayCommand]
    private void CloseKpiEditor() => IsKpiEditorOpen = false;

    [RelayCommand(CanExecute = nameof(CanSaveKpiSelection))]
    private async Task SaveKpiSelectionAsync()
    {
        if (_settings is null)
            return;

        var selected = KpiEditorItems.Where(i => i.IsSelected).Select(i => i.Id).ToList();
        var serialised = KpiMetricCatalog.Serialize(selected);
        var next = _settings.Current with { OverviewKpis = serialised };
        await _settings.SaveAsync(next).ConfigureAwait(true);

        IsKpiEditorOpen = false;
        // RebuildKpiCards is triggered automatically by IAppSettingsService.Changed.
    }

    private void RecomputeKpiEditorState()
    {
        KpiSelectedCount = KpiEditorItems.Count(i => i.IsSelected);
        CanSaveKpiSelection = KpiSelectedCount >= KpiMetricCatalog.MinSelected
            && KpiSelectedCount <= KpiMetricCatalog.MaxSelected;
        SaveKpiSelectionCommand.NotifyCanExecuteChanged();
    }
}
