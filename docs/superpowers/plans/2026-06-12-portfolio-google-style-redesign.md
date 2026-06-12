# Portfolio Google-Style Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild the 投資資產 → 持股 experience to mirror Google Finance's portfolio detail page: top "投資組合" tabs, a per-portfolio header (value / change / trend chart / period) with a 股票-vs-ETF focus card, an expandable per-holding 購買細項 (lot) breakdown, and a portfolio-scoped 活動 feed — while renaming "群組" → "投資組合" in the UI.

**Architecture:** UI-layer restructure on the existing data model. `PortfolioGroup` (via singleton `PortfolioGroupCatalog`) becomes the user-facing "投資組合"; `Trade.GroupId` already links trades→portfolio, and positions resolve to a portfolio through their trades (the existing 依群組 grouping). We add a portfolio **tab strip** that replaces the old `全部/依群組/依市場/依類型` ViewMode toggle, a per-portfolio **detail header** that reuses the existing per-group KPIs + trend chart, one genuinely-new **composition card** (股票 vs ETF), an expandable **lot row**, and a filtered **activity** view. No domain/DB/identifier renames — "Group" stays in code; only user-facing strings change.

**Tech Stack:** .NET 10 WPF, CommunityToolkit.Mvvm (`[ObservableProperty]`/`[RelayCommand]`), LiveChartsCore (existing trend/pie charts), xUnit + Moq, `DesignSystem/` tokens. `TreatWarningsAsErrors` ON. Build `dotnet build Assetra.slnx`; test `dotnet test Assetra.Tests/Assetra.Tests.csproj`.

---

## Decomposition & sequencing

This is a medium-large UI feature. It is split into **4 phases, each independently shippable**. **Phase 1 is fully detailed below.** Phases 2–4 are scoped as task outlines; per the writing-plans decomposition rule each will get its own fully-detailed plan once Phase 1 lands and its concrete structure exists to build on (detailing P2–P4's exact XAML now, before P1's containers exist, would bake in unverified assumptions).

- **P1 — Foundation:** rename + portfolio tab strip + detail header (value/change/trend/period) + 股票-vs-ETF focus card. Removes the old ViewMode toggle.
- **P2 — 購買細項:** expandable per-holding lot breakdown (the buy trades composing a position).
- **P3 — 活動:** portfolio-scoped transaction feed.
- **P4 (optional) — 比較對象:** per-portfolio benchmark comparison.

**Out of scope (confirmed):** cash/liability grouping (separate FIRE/Goals bucketing), 最新消息/news (belongs to Stockra, not Assetra), 觀察清單/watchlists (#2, deferred), any rename of code identifiers / DB columns / `PortfolioGroup`/`GroupId`.

---

## Data model (read these before starting — do not re-derive)

- `Assetra.Core/Models/PortfolioGroup.cs` — `Id`, `Name`, `ColorHex`, static `DefaultId`. The user's "投資組合" == a `PortfolioGroup`.
- `Assetra.WPF/Features/PortfolioGroups/PortfolioGroupCatalog.cs` — singleton, `ReadOnlyObservableCollection<PortfolioGroup> Groups`, `Default`, `FindById(Guid?)`, `EnsureLoadedAsync`. Already injected into `PortfolioViewModel` as `GroupCatalog` (nullable).
- `Assetra.Core/Models/Trade.cs` — `GroupId` (Guid?) links each trade to a portfolio.
- `Assetra.WPF/Features/Portfolio/PortfolioViewModel.cs` — owns `Positions` (`PortfolioRowViewModel`), `Trades` (`TradeRowViewModel`). The existing 依群組 ViewMode already computes per-group KPIs exposed on rows as `PositionViewGroupItemCount / PositionViewGroupMarketValue / PositionViewGroupCost / PositionViewGroupPnl` (see `PositionsTabPanel.xaml` `PositionGroupHeaderTemplate`). **Reuse this group-resolution + KPI logic** for the tab filter and header — find it by searching `PortfolioViewModel.cs` for the ViewMode/`PositionViewGroup` build (and `_hasAppliedInitialPositionViewMode`).
- `Assetra.WPF/Features/Portfolio/PortfolioHistoryViewModel.cs` — existing trend chart + period selector (`5天/1個月/.../全部`) and per-group trend (`Portfolio.Group.Trend.*`). Reuse for the detail-header chart.
- `Assetra.Core/Models/AssetType.cs` — `Stock, Fund, PreciousMetal, Bond, Crypto, Etf`. The focus card's "股票 vs ETF" split keys on this (`Etf` → ETF bucket; everything else with holdings → 股票 bucket, OR refine per design note in Task 1.5).

---

## File Structure

**Create:**
- `Assetra.WPF/Features/Portfolio/SubViewModels/PortfolioTabsViewModel.cs` — owns the tab strip: `Tabs` (All + one per `PortfolioGroup`), `SelectedTab`, selection command. Exposes `SelectedGroupId` (null = 全部).
- `Assetra.WPF/Features/Portfolio/SubViewModels/PortfolioCompositionViewModel.cs` — computes the 投資組合焦點 split (今日漲幅 / 總漲幅 + 股票% / ETF% with values) for the selected tab's holdings.
- `Assetra.WPF/Features/Portfolio/Controls/PortfolioTabStrip.xaml` (+ `.xaml.cs`) — the `[全部]+組合` pill row.
- `Assetra.WPF/Features/Portfolio/Controls/PortfolioDetailHeader.xaml` (+ `.xaml.cs`) — name + value + change + trend chart + period selector, bound to the selected tab.
- `Assetra.WPF/Features/Portfolio/Controls/PortfolioCompositionCard.xaml` (+ `.xaml.cs`) — the bordered focus card.
- `Assetra.Tests/WPF/PortfolioTabsViewModelTests.cs`, `Assetra.Tests/WPF/PortfolioCompositionViewModelTests.cs`.

**Modify:**
- `Assetra.WPF/Features/Portfolio/PortfolioViewModel.cs` — construct + own `PortfolioTabsViewModel` and `PortfolioCompositionViewModel`; when `SelectedTab` changes, refilter the positions view + recompute composition; recompute on each `RebuildTotals`.
- `Assetra.WPF/Features/Portfolio/Controls/PositionsTabPanel.xaml` — replace the old ViewMode toggle with `PortfolioTabStrip` + `PortfolioDetailHeader` + `PortfolioCompositionCard` above the holdings list; holdings list filtered to `SelectedGroupId`.
- `Assetra.WPF/Languages/zh-TW.xaml` + `en-US.xaml` — rename strings (Task 1.1).
- `Assetra.WPF/Features/PortfolioGroups/PortfolioGroupsView.xaml` (+ lang keys) — page-title/labels 群組→投資組合; make "＋ 新增投資組合" prominent.

---

## Phase 1 — Foundation

### Task 1.1: Rename 群組 → 投資組合 (UI strings only)

**Files:**
- Modify: `Assetra.WPF/Languages/zh-TW.xaml`, `Assetra.WPF/Languages/en-US.xaml`

- [ ] **Step 1: Find every user-facing "群組" string.** Grep `群組` in `zh-TW.xaml` and the matching keys in `en-US.xaml` ("group"/"Group" in values, NOT keys). Known keys (do not rename the `x:Key`, only the `<sys:String>` value): `Portfolio.ViewMode.Group` (will be deleted in 1.6, skip), `Portfolio.Filter.Group.All`, `Portfolio.Group.*` (Detail/Holdings/MoveToGroup/Kpi.*/Trend.*/Ungrouped/Unknown/NeedsResolution), `Allocation.GroupBy.Group`, and the PortfolioGroups page strings. Build the exact list before editing.

- [ ] **Step 2: Edit the zh-TW values** — replace 群組 → 投資組合 in those values (e.g. `投資群組管理`→`投資組合管理`, `群組詳情`→`投資組合詳情`, `移至群組`→`移至投資組合`, `未分組`→`未指定組合`). Keep keys unchanged. Leave `Portfolio.Group.Ungrouped` semantics ("default/未指定") clear.

- [ ] **Step 3: Edit the en-US values** — "Group"→"Portfolio" in the same keys' values ("Group Details"→"Portfolio Details", etc.).

- [ ] **Step 4: Build to confirm no key was renamed and XAML still parses.** Run: `dotnet build Assetra.slnx` — Expected: 0/0 (no `StaticResource`/`DynamicResource` key broke).

- [ ] **Step 5: Commit.** `git commit -m "refactor(portfolio): 介面文案 群組→投資組合（識別碼不動）"`

### Task 1.2: `PortfolioTabsViewModel` — tab list + selection

**Files:**
- Create: `Assetra.WPF/Features/Portfolio/SubViewModels/PortfolioTabsViewModel.cs`
- Test: `Assetra.Tests/WPF/PortfolioTabsViewModelTests.cs`

- [ ] **Step 1: Write the failing test.**

```csharp
using System.Linq;
using Assetra.Core.Models;
using Assetra.WPF.Features.Portfolio.SubViewModels;
using Xunit;

namespace Assetra.Tests.WPF;

public class PortfolioTabsViewModelTests
{
    private static PortfolioGroup G(string name) => new(Guid.NewGuid(), name, "#3B82F6");

    [Fact]
    public void Tabs_FirstIsAll_ThenOnePerGroup_AllSelectedByDefault()
    {
        var a = G("退休"); var b = G("買房");
        var vm = new PortfolioTabsViewModel(new[] { a, b });

        Assert.Equal(3, vm.Tabs.Count);
        Assert.True(vm.Tabs[0].IsAll);
        Assert.Equal(a.Id, vm.Tabs[1].GroupId);
        Assert.Same(vm.Tabs[0], vm.SelectedTab);
        Assert.Null(vm.SelectedGroupId);             // 全部 → null filter
    }

    [Fact]
    public void SelectingGroupTab_ExposesItsGroupId()
    {
        var a = G("退休");
        var vm = new PortfolioTabsViewModel(new[] { a });
        vm.SelectedTab = vm.Tabs[1];
        Assert.Equal(a.Id, vm.SelectedGroupId);
    }
}
```

- [ ] **Step 2: Run it — Expected: FAIL (type not defined).** `dotnet test Assetra.Tests/Assetra.Tests.csproj --filter FullyQualifiedName~PortfolioTabsViewModel`

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

    public PortfolioTabsViewModel(IEnumerable<PortfolioGroup> groups, string allLabel = "全部")
    {
        Tabs.Add(new PortfolioTabViewModel { IsAll = true, Name = allLabel });
        foreach (var g in groups)
            Tabs.Add(new PortfolioTabViewModel { GroupId = g.Id, Name = g.Name, ColorHex = g.ColorHex });
        SelectedTab = Tabs[0];
    }

    partial void OnSelectedTabChanged(PortfolioTabViewModel? value) =>
        OnPropertyChanged(nameof(SelectedGroupId));

    /// <summary>Rebuilds tabs from the latest catalog, preserving the current selection by GroupId.</summary>
    public void Sync(IEnumerable<PortfolioGroup> groups, string allLabel)
    {
        var keep = SelectedGroupId;
        Tabs.Clear();
        Tabs.Add(new PortfolioTabViewModel { IsAll = true, Name = allLabel });
        foreach (var g in groups)
            Tabs.Add(new PortfolioTabViewModel { GroupId = g.Id, Name = g.Name, ColorHex = g.ColorHex });
        SelectedTab = Tabs.FirstOrDefault(t => t.GroupId == keep) ?? Tabs[0];
    }
}
```

- [ ] **Step 4: Run — Expected: PASS.**
- [ ] **Step 5: Commit.** `git commit -m "feat(portfolio): PortfolioTabsViewModel（投資組合分頁狀態）"`

### Task 1.3: Filter the holdings view by `SelectedGroupId`; own the tabs VM in `PortfolioViewModel`

**Files:**
- Modify: `Assetra.WPF/Features/Portfolio/PortfolioViewModel.cs`

- [ ] **Step 1: Read the existing position-group resolution.** In `PortfolioViewModel.cs` locate where 依群組 resolves a `PortfolioRowViewModel` → its `PortfolioGroup` (search `PositionViewGroup`, the ViewMode build, and how a row maps to a group via its trades' `GroupId`). Note the exact helper/predicate — Task 1.3 reuses it; do NOT invent a new mapping.

- [ ] **Step 2: Add the tabs VM + a filtered view.** Construct `PortfolioTabsViewModel` from `GroupCatalog.Groups` (the localized 全部 label via `_localization`). Expose `public PortfolioTabsViewModel PortfolioTabs { get; }`. Add a `CollectionViewSource`/`ICollectionView` over `Positions` whose `Filter` keeps rows whose resolved group == `PortfolioTabs.SelectedGroupId` (or all when null). Subscribe to `PortfolioTabs.PropertyChanged(SelectedGroupId)` → `view.Refresh()` + recompute header/composition (Tasks 1.4/1.5). Re-`Sync` the tabs when `GroupCatalog.Groups` changes (the catalog is observable) and after `RebuildTotals`.

- [ ] **Step 3: Bind in `PositionsTabPanel.xaml`** — point the holdings `ItemsControl`/`DataGrid` at the new filtered view instead of raw `Positions`. (XAML wiring is finished in Task 1.6.)

- [ ] **Step 4: Build + run the full suite — Expected: 0/0, all green** (no behavior regression for existing tests; the default 全部 tab shows all positions exactly as before). `dotnet build Assetra.slnx && dotnet test Assetra.Tests/Assetra.Tests.csproj`

- [ ] **Step 5: Commit.** `git commit -m "feat(portfolio): 持股依選定投資組合過濾（全部=不過濾）"`

### Task 1.4: Per-portfolio detail header (value / change / trend / period)

**Files:**
- Create: `Assetra.WPF/Features/Portfolio/Controls/PortfolioDetailHeader.xaml` (+ `.xaml.cs`)
- Modify: `Assetra.WPF/Features/Portfolio/PortfolioViewModel.cs`

- [ ] **Step 1: Expose selected-tab aggregates on `PortfolioViewModel`.** Add read-only properties `SelectedPortfolioName`, `SelectedPortfolioMarketValue`, `SelectedPortfolioCost`, `SelectedPortfolioPnl`, `SelectedPortfolioDayPnl` computed by summing the *filtered* rows (全部 → reuse existing whole-portfolio totals; a group tab → reuse the existing per-group KPI already computed for 依群組). Raise `PropertyChanged` for all of them from the same place the filter refreshes (Task 1.3 Step 2) and from `RebuildTotals`.

- [ ] **Step 2: Build `PortfolioDetailHeader.xaml`** mirroring the Google PoHan header: large `SelectedPortfolioMarketValue`, signed `SelectedPortfolioDayPnl` + %, then the existing trend chart + period selector. Reuse `PortfolioHistoryViewModel` (already on `PortfolioViewModel.History`) for the chart/period; scope its series to the selected tab (全部 → existing whole-portfolio series; group tab → the existing per-group trend `Portfolio.Group.Trend.*`). Use design-system text tokens (`Font.Size.*`, `AppText*`).

- [ ] **Step 3: No new unit test** (presentation binding over already-tested aggregates); verify via build. `dotnet build Assetra.slnx` — Expected: 0/0.

- [ ] **Step 4: Commit.** `git commit -m "feat(portfolio): 投資組合詳情頁標題列（總值/漲跌/趨勢/期間）"`

### Task 1.5: 投資組合焦點 card (股票 vs ETF) — the one genuinely-new piece

**Files:**
- Create: `Assetra.WPF/Features/Portfolio/SubViewModels/PortfolioCompositionViewModel.cs`, `Assetra.WPF/Features/Portfolio/Controls/PortfolioCompositionCard.xaml` (+ `.xaml.cs`)
- Test: `Assetra.Tests/WPF/PortfolioCompositionViewModelTests.cs`

- [ ] **Step 1: Write the failing test.** (Composition splits the selected tab's holdings into ETF vs 股票 by base-currency market value. `Apply` takes simple `(AssetType, decimal marketValueBase)` tuples so it's UI-thread-free and pure.)

```csharp
using Assetra.Core.Models;
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
            (AssetType.Etf, 4_000_000m),
            (AssetType.Stock, 6_000_000m),
        ]);

        Assert.True(vm.HasData);
        Assert.Equal(6_000_000m, vm.StockValue);
        Assert.Equal(4_000_000m, vm.EtfValue);
        Assert.Equal(60.0, vm.StockPercent, 1);   // 6M / 10M
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

- [ ] **Step 2: Run — Expected: FAIL.** `dotnet test Assetra.Tests/Assetra.Tests.csproj --filter FullyQualifiedName~PortfolioCompositionViewModel`

- [ ] **Step 3: Implement.** (Design note: `Etf` → ETF bucket; all other held asset types → 股票 bucket, matching Google's two-way split. Percentages over the summed total; guard divide-by-zero.)

```csharp
using Assetra.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Assetra.WPF.Features.Portfolio.SubViewModels;

public sealed partial class PortfolioCompositionViewModel : ObservableObject
{
    [ObservableProperty] private decimal _stockValue;
    [ObservableProperty] private decimal _etfValue;
    [ObservableProperty] private double _stockPercent;
    [ObservableProperty] private double _etfPercent;
    [ObservableProperty] private bool _hasData;

    public void Apply(IReadOnlyList<(AssetType Type, decimal MarketValueBase)> holdings)
    {
        decimal etf = 0m, stock = 0m;
        foreach (var (type, mv) in holdings)
        {
            if (mv <= 0m) continue;
            if (type == AssetType.Etf) etf += mv; else stock += mv;
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

- [ ] **Step 5: Build `PortfolioCompositionCard.xaml`** — a **bordered** card (see Task 1.7): 今日漲幅 / 總漲幅 (from the Task 1.4 aggregates) on top, then a two-segment bar + `57.0% 股票 / 43.0% ETF` rows with values, matching the screenshot. Bind to `PortfolioCompositionViewModel`. Wire `PortfolioViewModel` to call `Composition.Apply(...)` with the filtered rows' `(AssetType, MarketValueBase)` (reuse `MarketValueBase` populated by `ApplyPositionBaseValuations` in `RebuildTotals`) whenever the filter/totals change.

- [ ] **Step 6: Build + test — Expected: 0/0, green.**
- [ ] **Step 7: Commit.** `git commit -m "feat(portfolio): 投資組合焦點卡（股票 vs ETF 佔比）"`

### Task 1.6: Replace the old ViewMode toggle with the tab strip; remove 全部/依群組/依市場/依類型

**Files:**
- Create: `Assetra.WPF/Features/Portfolio/Controls/PortfolioTabStrip.xaml` (+ `.xaml.cs`)
- Modify: `Assetra.WPF/Features/Portfolio/Controls/PositionsTabPanel.xaml`, `PortfolioViewModel.cs`, `zh-TW.xaml`/`en-US.xaml`

- [ ] **Step 1: Build `PortfolioTabStrip.xaml`** — a horizontal pill row bound to `PortfolioTabs.Tabs`, `SelectedItem`↔`SelectedTab`, each pill showing `Name` (and the group `ColorHex` dot). Use the same bordered/selected styling as the NavRail leaf or the Settings category list for consistency.

- [ ] **Step 2: Rewire `PositionsTabPanel.xaml`** — at the top: `PortfolioTabStrip`, then `PortfolioDetailHeader`, then `PortfolioCompositionCard`, then the (now filtered) holdings list. **Remove** the old `全部/依群組/依市場/依類型` ViewMode control.

- [ ] **Step 3: Delete the now-dead ViewMode code + strings.** Remove the ViewMode enum/property/build logic in `PortfolioViewModel.cs` that powered 依群組/依市場/依類型 grouping (the tab strip + filter replace it). Remove keys `Portfolio.ViewMode.All/Group/Market/Type` from both language files. Grep to confirm no remaining references.

- [ ] **Step 4: Build + full suite — Expected: 0/0; fix/remove any tests asserting the old ViewMode.** Report which tests changed and why.

- [ ] **Step 5: Commit.** `git commit -m "feat(portfolio): 頂部投資組合分頁取代舊檢視模式（移除 依群組/依市場/依類型）"`

### Task 1.7: Bordered card styling (design principle from #3, applied here)

**Files:**
- Modify: `Assetra.WPF/DesignSystem/Tokens/Colors.xaml` (or wherever `AppBorder*` live) — verify, do not guess the path; `PortfolioCompositionCard.xaml`

- [ ] **Step 1: Locate the card border tokens.** Grep `AppBorder` / `AppBorderLight` / `FormCard` in `DesignSystem/`. Identify a clearly-visible border brush (the user finds `AppBorderLight` too faint). If none exists, add `AppCardBorder` (a 1px, clearly-visible neutral, e.g. ~`#D0D5DD` light / a suitable dark-theme counterpart — match existing token naming + provide both theme values).

- [ ] **Step 2: Apply it to the new cards** — `PortfolioCompositionCard` (and the detail header card) use `BorderBrush={DynamicResource AppCardBorder}` `BorderThickness="1"` `CornerRadius="12"` (Google-like), NOT `AppBorderLight`. Do NOT restyle other screens here — global card-border tuning is #3's job; this task only ensures the *new* cards have a clear outline.

- [ ] **Step 3: Build — Expected: 0/0.** Visual confirmation is the user's (relaunch).
- [ ] **Step 4: Commit.** `git commit -m "style(portfolio): 投資組合卡片改用明顯邊框（呼應 #3）"`

---

## Phase 2 — 購買細項 (expandable per-holding lot breakdown)  *(own detailed plan when P1 lands)*

**Goal:** Each holding row in 投資項目 expands to show the buy trades (lots) composing that position: date / shares / price, like Google.

**Data:** `PortfolioViewModel.Trades` (`TradeRowViewModel`) already hold the buys; group by the position's symbol (+ the selected portfolio's `GroupId`). The asset-detail KPI path (`P4.1`) may already aggregate per-position trades — reuse if so.

**Task outline:** (1) `PositionLotsViewModel` — given a position + the trades, project its buy lots (failing test: N buys → N lots, correct date/shares/price, sorted). (2) Expander row template in the holdings list bound to a lazily-built lots VM. (3) Wire expansion to populate lots on demand. (4) Tests + commit per task.

## Phase 3 — 活動 (portfolio-scoped activity feed)  *(own detailed plan when P1 lands)*

**Goal:** A 活動 sub-tab next to 投資項目 showing the selected portfolio's transactions as a feed (買入/賣出 · date · shares · price · amount), newest first.

**Data:** `Trades` filtered by the selected tab's `GroupId` (`Trade.GroupId`); reuse `TradeRowViewModel` formatting — do NOT duplicate trade logic. Global 交易記錄 (`TradesTabPanel`) stays unchanged.

**Task outline:** (1) Filtered trades `ICollectionView` keyed on `SelectedGroupId` (failing test: only that group's trades appear; 全部 → all). (2) 投資項目/活動 sub-tab switch in `PositionsTabPanel`. (3) Activity-feed item template. (4) Tests + commit per task.

## Phase 4 (optional) — 比較對象 (per-portfolio benchmarks)  *(own detailed plan, only if requested)*

**Goal:** The detail header's "比較對象" strip compares the portfolio vs benchmarks. The benchmark infrastructure exists (`PortfolioHistoryViewModel` custom benchmarks, the P5 work) but is whole-portfolio; making it per-portfolio is the real work. Deferred unless the user asks.

---

## Self-review notes
- **Spec coverage:** rename (1.1), top tabs (1.2/1.3/1.6), detail header value/change/trend/period (1.4), 股票-vs-ETF focus card (1.5), bordered cards (1.7), lot dropdown (P2), 活動 (P3), benchmarks (P4-optional), old-ViewMode removal (1.6). Out-of-scope items explicitly listed. ✔
- **Identifier safety:** only UI strings renamed; `PortfolioGroup`/`GroupId`/DB untouched. ✔
- **Reuse over fabrication:** group-resolution, per-group KPIs, trend chart/period, trade formatting, `MarketValueBase` are reused with file pointers rather than re-implemented — the implementer must read the referenced existing code (called out in 1.3 Step 1, 1.4 Step 1/2). The only fully-new, fully-specified units are `PortfolioTabsViewModel` and `PortfolioCompositionViewModel` (with complete code + tests). ✔
