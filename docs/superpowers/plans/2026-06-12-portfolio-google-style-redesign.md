# Portfolio Google-Style Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **Rev 2** — corrected after a 24-agent adversarial review against the real codebase (4 major + 11 minor findings applied; key corrections: `Trade.PortfolioGroupId` not `GroupId`, positions carry their group directly, the group filter already exists as `PortfolioGroupFilter`, per-group trend has no period selector, ETF = `Stock + IsEtf` flag, theme brushes live in BOTH `Themes/Light.xaml` + `Dark.xaml`).

**Goal:** Rebuild the 投資資產 → 持股 experience to mirror Google Finance's portfolio detail page: top "投資組合" tabs, a per-portfolio header (value / change / trend chart / period / ＋投資) with a 股票-vs-ETF focus card, an expandable per-holding 購買細項 (lot) breakdown, and a portfolio-scoped 活動 feed — while renaming "群組" → "投資組合" in the UI.

**Architecture:** UI-layer restructure on the existing data model. `PortfolioGroup` (via singleton `PortfolioGroupCatalog`) becomes the user-facing "投資組合". **Positions carry their portfolio directly**: source of truth is `PortfolioEntry.PortfolioGroupId`, surfaced as `PortfolioRowViewModel.PortfolioGroupId` (see the doc comment at `PortfolioRowViewModel.cs:24-26` — `Trade.PortfolioGroupId` is historical transaction context and must NOT be used for position grouping; it is used only by P3's activity filtering). The tab strip drives the **existing** `PortfolioGroupFilter` (no new collection view), replacing the old `全部/依群組/依市場/依類型` ViewMode toggle. New work: the tab strip UI, the detail header, one genuinely-new **composition card** (股票 vs ETF), an expandable **lot row** (P2), and a filtered **activity** view (P3). No domain/DB/identifier renames — "Group" stays in code; only user-facing strings change.

**Tech Stack:** .NET 10 WPF, CommunityToolkit.Mvvm, LiveChartsCore, xUnit + Moq, `DesignSystem/` tokens. `TreatWarningsAsErrors` ON. Build `dotnet build Assetra.slnx`; test `dotnet test Assetra.Tests/Assetra.Tests.csproj`.

---

## Decomposition & sequencing

4 phases, each independently shippable. **Phase 1 fully detailed**; P2–P4 are scoped outlines that get their own detailed plans once P1 lands.

- **P1 — Foundation:** rename + portfolio tab strip + detail header + 股票-vs-ETF focus card + ＋投資 button. Removes the old ViewMode toggle.
- **P2 — 購買細項:** expandable per-holding lot breakdown.
- **P3 — 活動:** portfolio-scoped transaction feed.
- **P4 (optional) — 比較對象 + per-portfolio period history:** benchmarks AND a period-selectable per-portfolio trend (snapshots carry no group dimension today — both are real new work, see Task 1.4 note).

**Out of scope (confirmed):** cash/liability grouping (separate FIRE/Goals bucketing), 最新消息/news (Stockra), 觀察清單 (#2, deferred), renames of code identifiers / DB columns.

---

## Data model & existing machinery (verified — read these, do not re-derive)

- `Assetra.Core/Models/PortfolioGroup.cs` — `Id`, `Name`, `ColorHex`, `IsSystem`, static `DefaultId` (`:39-46`). A system default group is migration-seeded; it can be renamed but not deleted. Existing UI deliberately special-cases it: `HasPortfolioGroups` counts only `!IsSystem` groups (`PortfolioViewModel.cs:98-105`), and the chip builder labels the DefaultId chip with `Portfolio.Group.Ungrouped` instead of its raw name (`PortfolioViewModel.Filtering.cs:111-118`).
- `Assetra.WPF/Features/PortfolioGroups/PortfolioGroupCatalog.cs` — singleton, `Groups`, `Default`, `FindById`, `EnsureLoadedAsync`. Injected into `PortfolioViewModel` as `GroupCatalog` (**nullable** — guard).
- **Position→portfolio link:** `PortfolioRowViewModel.PortfolioGroupId` (from `PortfolioEntry.PortfolioGroupId`). Effective group = `row.PortfolioGroupId ?? PortfolioGroup.DefaultId` (the existing pattern at `Filtering.cs:305`). `Trade.PortfolioGroupId` (`Trade.cs:160`) is per-trade historical context — P3 only.
- **The filter machinery ALREADY EXISTS** in `Assetra.WPF/Features/Portfolio/PortfolioViewModel.Filtering.cs`:
  - `PositionsView` (`:159-173`) = `CollectionViewSource.GetDefaultView(Positions)` with `Filter = FilterPosition` (combines text search + asset-type chips + group filter). The holdings XAML **already binds `PositionsView`** in two presenters (`PositionsTabPanel.xaml:1152` ListBox, `:1447` DataGrid).
  - `PortfolioGroupFilter` (`Guid?`, null = all, `:61`) + `SetPortfolioGroupFilterCommand` (`:74-75`); the group predicate incl. null→DefaultId coalesce is `FilterPosition:303-308`; `OnPortfolioGroupFilterChanged` (`:63-69`) already does `PositionsView.Refresh()` + summary/stat refreshes. Unit-tested at `PortfolioViewModelTests.cs:388-415`.
  - ⚠️ `OnPositionViewModeChanged` (`:79-93`) auto-clears `PortfolioGroupFilter` when leaving Group mode (`:83-84`) — relevant to Tasks 1.3/1.6.
- **Trend charts — two different things:**
  - `PortfolioHistoryViewModel.cs` — whole-portfolio trend + period selector (snapshot-based). It has **zero** group awareness, and `PortfolioDailySnapshot` has no group dimension, so per-group period history **cannot** be derived from stored data (→ P4).
  - `PortfolioGroupDetailViewModel.cs` — the per-group trend (`PerformanceTrendSeries`, built once in ctor from holdings' `SparklinePoints`): a **fixed recent-window estimate with NO period selector** (note string zh-TW:375 「依目前群組成員與近月價格估算」). Its ctor (`:25-30`) is also the canonical per-group aggregate pattern: sum `MarketValue/Cost/Pnl` and **`TodayChange = Σ row.DayChange`**.
- **ETF storage rule** (`Filtering.cs:281-299` documents it): the buy flow writes `AssetType.Stock` for both stocks AND ETFs; ETF-ness is the separate **`IsEtf` flag** (`PortfolioRowViewModel.cs:67`). Effective ETF predicate: `row.AssetType == AssetType.Etf || row.IsEtf`. Keying on `AssetType.Etf` alone silently misclassifies nearly all real TW ETFs as 股票.
- **Design tokens:** `AppBorder`/`AppBorderLight` are defined in **BOTH** `DesignSystem/Themes/Light.xaml:21-22` AND `Themes/Dark.xaml:22-23` (theme switch swaps exactly these dictionaries — a brush defined in only one renders an invisible border in the other, with **no build error**). Radius tokens: `Radius.Xl`=12, `Radius.2Xl`=18; the card system is standardized on `Radius.2Xl` (`Styles/Cards.xaml`).

---

## File Structure

**Create:**
- `Assetra.WPF/Features/Portfolio/SubViewModels/PortfolioTabsViewModel.cs` — tab strip state: `Tabs` (全部 + 未指定組合 + user portfolios), `SelectedTab`, `SelectedGroupId` (null = 全部).
- `Assetra.WPF/Features/Portfolio/SubViewModels/PortfolioCompositionViewModel.cs` — the 股票-vs-ETF split.
- `Assetra.WPF/Features/Portfolio/Controls/PortfolioTabStrip.xaml` (+ `.cs`), `PortfolioDetailHeader.xaml` (+ `.cs`), `PortfolioCompositionCard.xaml` (+ `.cs`).
- `Assetra.Tests/WPF/PortfolioTabsViewModelTests.cs`, `Assetra.Tests/WPF/PortfolioCompositionViewModelTests.cs`.

**Modify:**
- `Assetra.WPF/Features/Portfolio/PortfolioViewModel.cs` + `PortfolioViewModel.Filtering.cs` — own the new VMs; tab selection → `PortfolioGroupFilter`; delete ViewMode machinery (1.6).
- `Assetra.WPF/Features/Portfolio/Controls/PositionsTabPanel.xaml` — tab strip + header + composition card replace the ViewMode row; holdings list keeps binding `PositionsView` (unchanged).
- `Assetra.WPF/Languages/zh-TW.xaml` + `en-US.xaml`; `Assetra.WPF/Features/PortfolioGroups/PortfolioGroupsView.xaml`.
- `Assetra.WPF/DesignSystem/Themes/Light.xaml` + `Themes/Dark.xaml` (+ optionally `Tokens/Colors.xaml` for the raw color) — `AppCardBorder`.

---

## Phase 1 — Foundation

### Task 1.1: Rename 群組 → 投資組合 (UI strings only)

**Files:** Modify `Assetra.WPF/Languages/zh-TW.xaml`, `en-US.xaml`

- [ ] **Step 1: Find EVERY user-facing 群組 string.** Grep `群組` in `zh-TW.xaml` (≈28 occurrences, all the PortfolioGroup sense — no collisions) and the matching keys in `en-US.xaml`. Seed list (NOT exhaustive — the grep is authoritative): `Portfolio.Filter.Group.All/.Add/.Manage`, `Portfolio.Group.*`, `Portfolio.Tx.Group`, `Portfolio.EditAsset.PositionSubtitle/GroupConflict*`, `Allocation.GroupBy.Group`, `PortfolioGroups.*` + `PortfolioGroups.Nav` (nav rail), `Sync.Domain.PortfolioGroup`, `Goals.Add.PortfolioGroup(+.Hint)`, `Goals.Tracking.PortfolioGroup`, `Fire.Input.Group(+.Hint)`. Keys stay; only values change.
- [ ] **Step 2: Edit zh-TW values — sense-based rewrite, NOT blind replace.** ⚠️ Two values already contain 投資組合: `Goals.Add.PortfolioGroup` (zh:~1564 「連結到投資組合群組」→「連結到投資組合」) and `Fire.Input.Group` (zh:~2006 「選擇投資組合群組」→「選擇投資組合」) — a mechanical 群組→投資組合 replace would produce 「投資組合投資組合」. Examples: `投資群組管理`→`投資組合管理`, `群組詳情`→`投資組合詳情`, `移至群組`→`移至投資組合`, `未分組`→`未指定組合`.
- [ ] **Step 3: Edit en-US values** the same way ("Group Details"→"Portfolio Details" etc.).
- [ ] **Step 4: Build.** `dotnet build Assetra.slnx` — Expected 0/0 (keys intact).
- [ ] **Step 5: Commit.** `git commit -m "refactor(portfolio): 介面文案 群組→投資組合（識別碼不動）"`

### Task 1.2: `PortfolioTabsViewModel` — tab list + selection (with system-group semantics)

**Files:** Create `SubViewModels/PortfolioTabsViewModel.cs`; Test `Assetra.Tests/WPF/PortfolioTabsViewModelTests.cs`

Design (matches the codebase convention at `Filtering.cs:103-131` + `HasPortfolioGroups`): tabs = `[全部]` + (a `未指定組合` tab for `DefaultId`, **only when at least one user (!IsSystem) group exists**) + one tab per `!IsSystem` group. With zero user groups the strip is just `[全部]` (callers may collapse it, mirroring `HasPortfolioGroups`). The DefaultId tab uses the localized `Portfolio.Group.Ungrouped` label, never the raw system group name.

- [ ] **Step 1: Write the failing tests.**

```csharp
using Assetra.Core.Models;
using Assetra.WPF.Features.Portfolio.SubViewModels;
using Xunit;

namespace Assetra.Tests.WPF;

public class PortfolioTabsViewModelTests
{
    private static PortfolioGroup User(string name) => new(Guid.NewGuid(), name, "#3B82F6");
    private static PortfolioGroup SystemDefault() =>
        new(PortfolioGroup.DefaultId, "預設群組", null, IsSystem: true);
    // ^ Match the real PortfolioGroup ctor/record shape — read PortfolioGroup.cs:39-46 first
    //   and adjust construction (it may be init-properties rather than positional).

    [Fact]
    public void Tabs_AllFirst_ThenUngrouped_ThenUserGroups_AllSelected()
    {
        var vm = new PortfolioTabsViewModel(
            [SystemDefault(), User("退休"), User("買房")],
            allLabel: "全部", ungroupedLabel: "未指定組合");

        Assert.Equal(4, vm.Tabs.Count);                       // 全部, 未指定組合, 退休, 買房
        Assert.True(vm.Tabs[0].IsAll);
        Assert.Equal(PortfolioGroup.DefaultId, vm.Tabs[1].GroupId);
        Assert.Equal("未指定組合", vm.Tabs[1].Name);            // localized label, not raw name
        Assert.Same(vm.Tabs[0], vm.SelectedTab);
        Assert.Null(vm.SelectedGroupId);
    }

    [Fact]
    public void NoUserGroups_OnlyAllTab()
    {
        var vm = new PortfolioTabsViewModel([SystemDefault()], "全部", "未指定組合");
        Assert.Single(vm.Tabs);
        Assert.True(vm.Tabs[0].IsAll);
    }

    [Fact]
    public void SelectingGroupTab_ExposesItsGroupId()
    {
        var a = User("退休");
        var vm = new PortfolioTabsViewModel([SystemDefault(), a], "全部", "未指定組合");
        vm.SelectedTab = vm.Tabs[^1];
        Assert.Equal(a.Id, vm.SelectedGroupId);
    }

    [Fact]
    public void Sync_PreservesSelectionByGroupId()
    {
        var a = User("退休");
        var vm = new PortfolioTabsViewModel([SystemDefault(), a], "全部", "未指定組合");
        vm.SelectedTab = vm.Tabs[^1];
        vm.Sync([SystemDefault(), a, User("買房")], "全部", "未指定組合");
        Assert.Equal(a.Id, vm.SelectedGroupId);               // selection survives a catalog refresh
    }
}
```

- [ ] **Step 2: Run — Expected: FAIL.** `dotnet test Assetra.Tests/Assetra.Tests.csproj --filter FullyQualifiedName~PortfolioTabsViewModel`
- [ ] **Step 3: Implement.**

```csharp
using System.Collections.ObjectModel;
using Assetra.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Assetra.WPF.Features.Portfolio.SubViewModels;

public sealed partial class PortfolioTabViewModel : ObservableObject
{
    public bool IsAll { get; init; }
    public Guid? GroupId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? ColorHex { get; init; }
}

public sealed partial class PortfolioTabsViewModel : ObservableObject
{
    public ObservableCollection<PortfolioTabViewModel> Tabs { get; } = [];

    [ObservableProperty] private PortfolioTabViewModel? _selectedTab;

    public Guid? SelectedGroupId => SelectedTab?.GroupId;

    public PortfolioTabsViewModel(IEnumerable<PortfolioGroup> groups, string allLabel, string ungroupedLabel)
        => Rebuild(groups, allLabel, ungroupedLabel, keepSelection: null);

    partial void OnSelectedTabChanged(PortfolioTabViewModel? value) =>
        OnPropertyChanged(nameof(SelectedGroupId));

    /// <summary>Rebuilds from the latest catalog, preserving the selection by GroupId.</summary>
    public void Sync(IEnumerable<PortfolioGroup> groups, string allLabel, string ungroupedLabel)
        => Rebuild(groups, allLabel, ungroupedLabel, keepSelection: SelectedGroupId);

    private void Rebuild(IEnumerable<PortfolioGroup> groups, string allLabel, string ungroupedLabel, Guid? keepSelection)
    {
        var users = groups.Where(g => !g.IsSystem).ToList();
        Tabs.Clear();
        Tabs.Add(new PortfolioTabViewModel { IsAll = true, Name = allLabel });
        if (users.Count > 0)
        {
            // 未指定組合 tab — positions whose effective group is the system default.
            Tabs.Add(new PortfolioTabViewModel { GroupId = PortfolioGroup.DefaultId, Name = ungroupedLabel });
            foreach (var g in users)
                Tabs.Add(new PortfolioTabViewModel { GroupId = g.Id, Name = g.Name, ColorHex = g.ColorHex });
        }
        SelectedTab = Tabs.FirstOrDefault(t => t.GroupId == keepSelection) ?? Tabs[0];
    }
}
```

- [ ] **Step 4: Run — Expected: PASS.**
- [ ] **Step 5: Commit.** `git commit -m "feat(portfolio): PortfolioTabsViewModel（投資組合分頁狀態，含系統預設組合語意）"`

### Task 1.3: Wire tab selection to the EXISTING `PortfolioGroupFilter` (no new view, no rebinding)

**Files:** Modify `PortfolioViewModel.cs` (+ touch `PortfolioViewModel.Filtering.cs` only if needed)

**Do NOT create any new `CollectionViewSource`/`ICollectionView` and do NOT rebind the holdings XAML** — `PositionsView` + `FilterPosition` + `PortfolioGroupFilter` already exist, are unit-tested, and both presenters already bind `PositionsView` (see Data-model section). The whole task is:

- [ ] **Step 1: Construct + expose the tabs VM.** In `PortfolioViewModel`: `public PortfolioTabsViewModel PortfolioTabs { get; }`, built from `GroupCatalog?.Groups ?? []` with localized labels (`Common.All`/`Portfolio.Group.Ungrouped` via the existing `L(...)` helper). Re-`Sync` it where `RefreshPortfolioGroupFilterChips()` is called today (catalog changes already flow there).
- [ ] **Step 2: Selection → filter.** Subscribe to `PortfolioTabs.PropertyChanged` for `SelectedGroupId` → set `PortfolioGroupFilter = PortfolioTabs.SelectedGroupId`. `OnPortfolioGroupFilterChanged` (Filtering.cs:63-69) already refreshes `PositionsView`, group summaries, and footer stats — no manual refresh wiring. The null→DefaultId coalesce for ungrouped rows is already inside `FilterPosition:303-308`. Also hook the Task 1.4/1.5 recompute (header aggregates + composition) into this same handler.
- [ ] **Step 3: Interim-window note (no code yet).** Until Task 1.6 deletes the ViewMode, toggling the old ViewMode control auto-clears `PortfolioGroupFilter` (`Filtering.cs:83-84`) and would desync the tab strip. Accept this transient (1.6 lands in the same phase) — do NOT pre-patch it here; 1.6 deletes the whole handler.
- [ ] **Step 4: Test.** Extend `PortfolioViewModelTests` mirroring `SetPortfolioGroupFilter_FiltersPositionsToSelectedGroup` (`:388-415`): selecting a tab on `PortfolioTabs` filters `PositionsView` to that group's rows; selecting the 全部 tab restores all. Run full suite — Expected: green.
- [ ] **Step 5: Commit.** `git commit -m "feat(portfolio): 分頁選擇驅動既有 PortfolioGroupFilter（沿用 PositionsView）"`

### Task 1.4: Per-portfolio detail header (value / change / trend / period / ＋投資)

**Files:** Create `Controls/PortfolioDetailHeader.xaml` (+ `.cs`); Modify `PortfolioViewModel.cs`

- [ ] **Step 1: Selected-tab aggregates.** Add `SelectedPortfolioName / MarketValue / Cost / Pnl / DayPnl` computed by **summing the tab-filtered rows**, mirroring `PortfolioGroupDetailViewModel.cs:25-30` (note `DayPnl = Σ row.DayChange` — the old `PositionViewGroup*` values are NOT usable: they're only populated in Group ViewMode and are deleted in 1.6). 全部 tab → reuse the existing whole-portfolio totals. Raise PropertyChanged from the Task 1.3 Step 2 handler and from `RebuildTotals`.
- [ ] **Step 2: Trend — two sources, by tab type (a period-selectable per-group chart does NOT exist today; snapshots have no group dimension):**
  - 全部 tab → reuse `PortfolioViewModel.History` (`PortfolioHistoryViewModel`) **with** its period selector.
  - Portfolio tab → reuse the `PortfolioGroupDetailViewModel`-style **fixed-window** trend built from the filtered rows' `SparklinePoints` (`BuildMarketValueTrend` pattern), and **hide the period selector** (show the existing note 「依目前投資組合成員與近月價格估算」). Per-portfolio period history is real new work → P4.
- [ ] **Step 3: ＋投資 button.** In the header, a `＋ 投資` button that opens the existing buy/add-transaction flow with the selected tab's `GroupId` preselected (the transaction dialog already supports group preselection — find how `TransactionDialogViewModel` receives a group and pass `SelectedGroupId`; on the 全部 tab it opens with the default behavior).
- [ ] **Step 4: Build `PortfolioDetailHeader.xaml`** — large value, signed day change + %, trend chart area (template-switched by tab type), period selector (全部 only), ＋投資. Design-system tokens throughout.
- [ ] **Step 5: Build — Expected 0/0.** (Presentation binding; aggregates covered by the 1.3 test + existing totals tests.)
- [ ] **Step 6: Commit.** `git commit -m "feat(portfolio): 投資組合詳情標題列（總值/漲跌/趨勢/期間/＋投資）"`

### Task 1.5: 投資組合焦點 card (股票 vs ETF) — keyed on the EFFECTIVE ETF flag

**Files:** Create `SubViewModels/PortfolioCompositionViewModel.cs`, `Controls/PortfolioCompositionCard.xaml` (+ `.cs`); Test `Assetra.Tests/WPF/PortfolioCompositionViewModelTests.cs`

⚠️ ETFs are stored as `AssetType.Stock` + `IsEtf=true` (see Data-model section). The VM takes a **precomputed flag**, and the caller computes it with the same predicate the asset-type chips use.

- [ ] **Step 1: Write the failing tests.**

```csharp
using Assetra.WPF.Features.Portfolio.SubViewModels;
using Xunit;

namespace Assetra.Tests.WPF;

public class PortfolioCompositionViewModelTests
{
    [Fact]
    public void Apply_SplitsEtfVsStock_ByBaseMarketValue()
    {
        var vm = new PortfolioCompositionViewModel();
        vm.Apply(
        [
            (IsEtf: true, MarketValueBase: 4_000_000m),   // e.g. 0056 — stored Stock+IsEtf, flag already resolved
            (IsEtf: false, MarketValueBase: 6_000_000m),  // e.g. 3231
        ]);

        Assert.True(vm.HasData);
        Assert.Equal(6_000_000m, vm.StockValue);
        Assert.Equal(4_000_000m, vm.EtfValue);
        Assert.Equal(60.0, vm.StockPercent, 1);
        Assert.Equal(40.0, vm.EtfPercent, 1);
    }

    [Fact]
    public void Apply_Empty_HasNoData()
    {
        var vm = new PortfolioCompositionViewModel();
        vm.Apply([]);
        Assert.False(vm.HasData);
        Assert.Equal(0, vm.StockPercent);
    }
}
```

- [ ] **Step 2: Run — Expected: FAIL.**
- [ ] **Step 3: Implement.**

```csharp
using CommunityToolkit.Mvvm.ComponentModel;

namespace Assetra.WPF.Features.Portfolio.SubViewModels;

public sealed partial class PortfolioCompositionViewModel : ObservableObject
{
    [ObservableProperty] private decimal _stockValue;
    [ObservableProperty] private decimal _etfValue;
    [ObservableProperty] private double _stockPercent;
    [ObservableProperty] private double _etfPercent;
    [ObservableProperty] private bool _hasData;

    /// <summary>
    /// IsEtf is the EFFECTIVE flag — caller resolves it as
    /// row.AssetType == AssetType.Etf || row.IsEtf (ETFs are stored Stock+IsEtf;
    /// see PortfolioViewModel.Filtering.FilterPosition's asset-type predicate).
    /// </summary>
    public void Apply(IReadOnlyList<(bool IsEtf, decimal MarketValueBase)> holdings)
    {
        decimal etf = 0m, stock = 0m;
        foreach (var (isEtf, mv) in holdings)
        {
            if (mv <= 0m) continue;
            if (isEtf) etf += mv; else stock += mv;
        }
        var total = etf + stock;
        StockValue = stock; EtfValue = etf;
        StockPercent = total > 0m ? (double)(stock / total) * 100d : 0d;
        EtfPercent = total > 0m ? (double)(etf / total) * 100d : 0d;
        HasData = total > 0m;
    }
}
```

- [ ] **Step 4: Run — Expected: PASS.**
- [ ] **Step 5: Card + wiring.** `PortfolioCompositionCard.xaml`: 今日漲幅/總漲幅 (Task 1.4 aggregates), two-segment bar, `57.0% 股票 / 43.0% ETF` rows — **bordered** per Task 1.7. Wire `PortfolioViewModel` to call `Composition.Apply(...)` over the tab-filtered rows with `(row.AssetType == AssetType.Etf || row.IsEtf, row.MarketValueBase)` whenever filter/totals change (same hook as 1.4 Step 1).
- [ ] **Step 6: Build + test — Expected: 0/0, green.**
- [ ] **Step 7: Commit.** `git commit -m "feat(portfolio): 投資組合焦點卡（股票 vs ETF，依有效 IsEtf 旗標）"`

### Task 1.6: Tab strip control; remove the ViewMode machinery

**Files:** Create `Controls/PortfolioTabStrip.xaml` (+ `.cs`); Modify `PositionsTabPanel.xaml`, **`PortfolioViewModel.Filtering.cs`** (most ViewMode code lives HERE, not PortfolioViewModel.cs), `PortfolioViewModel.cs`, `PortfolioRowViewModel.cs`, both language files

- [ ] **Step 1: Build `PortfolioTabStrip.xaml`** — pill row bound to `PortfolioTabs.Tabs`, `SelectedItem`↔`SelectedTab`, group `ColorHex` dot. **Wrap in a horizontal `ScrollViewer`** (hidden scrollbar, wheel-scroll): the codebase deliberately removed an unbounded chip row before (comment at `PositionsTabPanel.xaml:946` 「避免使用者建立多個策略群組後擠爆工具列」) — this consciously overrides that with overflow handling, not silently regresses it.
- [ ] **Step 2: Rewire `PositionsTabPanel.xaml`** — top: `PortfolioTabStrip` → `PortfolioDetailHeader` → `PortfolioCompositionCard` → the existing holdings presenters (still bound to `PositionsView` — unchanged). Remove the ViewMode toggle row AND the `PositionGroupHeaderTemplate` GroupStyle (`:55-101`), AND the old group-filter chip row (superseded by tabs).
- [ ] **Step 3: Delete dead code — checklist (verified locations):**
  - `Filtering.cs`: `InvestmentPositionViewMode` enum (`:11-17`), `PositionViewMode` property + `OnPositionViewModeChanged` (`:77-93`, incl. the `:83-84` auto-clear), `IsGroupViewMode`/`IsAssetTypeFilterRowVisible` (`:28-29`), `ApplyPositionViewGrouping`/`RefreshPositionViewGroupSummaries`/`GetPositionViewGroupKey` (`:175-241`), `PortfolioGroupFilterChips` + builders (`:103-137`).
  - `PortfolioViewModel.cs`: `_hasAppliedInitialPositionViewMode` (`:95`), `ApplyInitialPositionViewMode` (`:898-906`, call site `:928`).
  - `PortfolioRowViewModel.cs`: the `PositionViewGroup*` properties.
  - Language keys: `Portfolio.ViewMode.All/Group/Market/Type` (both files). (ViewMode is NOT persisted in AppSettings — verified; no settings cleanup.)
- [ ] **Step 4: Fix the 5 affected tests (all in `PortfolioViewModelTests.cs`):** delete `SetPositionViewMode_GroupsPositionsViewBySelectedDimension` (`:256`) + `SetPositionViewMode_Group_UpdatesVisibleGroupSummaries` (`:272`); rewrite `LoadAsync_DefaultsToGroupViewWhenCustomGroupsExist` (`:308`) + `LoadAsync_KeepsFlatViewWhenOnlySystemGroupExists` (`:328`) to assert the new tab-strip defaults; trim the `SetPositionViewModeCommand` line (`:409`) from `SetPortfolioGroupFilter_FiltersPositionsToSelectedGroup`.
- [ ] **Step 5: Build + full suite — Expected 0/0, green. Grep `PositionViewMode|PositionViewGroup` repo-wide → zero production references.**
- [ ] **Step 6: Commit.** `git commit -m "feat(portfolio): 頂部投資組合分頁取代舊檢視模式（含 ViewMode 機制移除）"`

### Task 1.7: Bordered card styling (theme-safe)

**Files:** Modify **BOTH** `DesignSystem/Themes/Light.xaml` AND `Themes/Dark.xaml` (+ optionally `Tokens/Colors.xaml` for raw colors); `PortfolioCompositionCard.xaml`, `PortfolioDetailHeader.xaml`

- [ ] **Step 1: Add `AppCardBorder`** — a clearly-visible 1px neutral border brush — **to BOTH theme dictionaries** (Light ≈ `#D0D5DD`-class; Dark = a visible counterpart consistent with `Color.Cis.Dark.*`). ⚠️ Theme switching swaps exactly these two files; defining it in only one yields an **invisible border in the other theme with NO build error** — treat "defined in both" as an explicit checklist item.
- [ ] **Step 2: Apply to the new cards** — `BorderBrush={DynamicResource AppCardBorder}`, `BorderThickness="1"`, `CornerRadius="{StaticResource Radius.2Xl}"` (18 — the codebase card baseline per `Styles/Cards.xaml`; conformance over Google's 12). Scope: only the NEW cards — global card-border tuning is #3's job.
- [ ] **Step 3: Build — Expected 0/0.** Visual check (both themes!) is the user's on relaunch.
- [ ] **Step 4: Commit.** `git commit -m "style(portfolio): AppCardBorder 雙主題明顯邊框（新卡片）"`

---

## Phase 2 — 購買細項 *(own detailed plan when P1 lands)*

Expandable per-holding lot breakdown (buy date / shares / price). Data: `Trades` (`TradeRowViewModel`) filtered to the position's symbol; check the asset-detail KPI path (P4.1) for an existing per-position trade aggregation to reuse. Outline: `PositionLotsViewModel` (TDD) → expander row template → lazy population → tests.

## Phase 3 — 活動 *(own detailed plan when P1 lands)*

Portfolio-scoped transaction feed (買入/賣出 · date · shares · price · amount, newest first). Filter trades via **`TradeRowViewModel.PortfolioGroupId`** (`TradeRowViewModel.cs:128`, from `Trade.PortfolioGroupId` — the historical per-trade context; this is its correct use). Reuse `TradeRowViewModel` formatting. Global 交易記錄 unchanged. Outline: filtered trades view keyed on `SelectedGroupId` → 投資項目/活動 sub-tab switch → feed item template → tests.

## Phase 4 (optional) — 比較對象 + per-portfolio period history *(own plan, only if requested)*

Two real pieces of new work deferred from P1: (a) per-portfolio benchmark comparison (existing benchmark infra is whole-portfolio); (b) period-selectable per-portfolio trend — requires a group dimension on snapshots or on-the-fly historical recomputation (neither exists today).

---

## Self-review notes (Rev 2)
- All four major review findings applied: `Trade.PortfolioGroupId` + direct row→group resolution; Task 1.3 reuses `PortfolioGroupFilter`/`PositionsView` instead of inventing a view; Task 1.4 splits the trend by tab type and stops claiming a per-group period chart exists; Task 1.5 keys on the effective `IsEtf` flag with the storage rule encoded in its doc + caller.
- Minor findings applied: system-default-group tab semantics + tests (1.2), DayPnl via `row.DayChange` (1.4), ＋投資 button task (1.4), tab-strip overflow ScrollViewer + prior-design citation (1.6), 1.6 deletion checklist with verified file:line locations + the 5 named tests, theme-safe `AppCardBorder` in both dictionaries + `Radius.2Xl` (1.7), Task 1.1 double-replace warning + expanded seed list.
- Identifier safety: only UI strings renamed. ✔
