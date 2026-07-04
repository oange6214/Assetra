# 上手友善化（引導底座 ＋ Nav 漸進揭露）Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 讓完全的新手第一次打開 Assetra 就知道怎麼開始（主線＝加第一筆持股看到損益），同時不擋熟手。

**Architecture:** 六個獨立可交付單元 —— (1) Nav 群重整核心/進階、(2) Nav 展開偏好持久化、(3) 首次啟動 welcome overlay 引導加第一筆持股、(4) 首頁空狀態 hero、(5) 全站 `EmptyState` 統一與白話文案、(6) Nav tooltip 白話化。VM/邏輯層走 TDD（xUnit）；XAML 視圖走「結構＋繫結＋資源鍵」規格 ＋ ControlsBehavior/手動驗收（本專案 WPF 視圖慣例）。

**Tech Stack:** WPF · .NET 10 · CommunityToolkit.Mvvm · xUnit · 語言檔雙語（`zh-TW` ＋ `en-US`）· 設定持久化走 `IAppSettingsService`。

**Spec:** [docs/superpowers/specs/2026-07-01-onboarding-friendly-nav-design.md](../specs/2026-07-01-onboarding-friendly-nav-design.md)

---

## File Structure

| 檔案 | 職責 | 動作 |
|------|------|------|
| `Assetra.WPF/Shell/NavRailViewModel.cs` | nav 群定義、展開狀態、持久化 | 修改（Task 1、2） |
| `Assetra.WPF/Shell/NavGroupVm.cs` | 群 VM（`IsExpanded`、`GroupKey`） | 修改（Task 2：加 `GroupKey`） |
| `Assetra.Core/Models/AppSettings.cs` | 設定 record | 修改（Task 2：加 `NavExpandedGroups`） |
| `Assetra.WPF/Languages/zh-TW.xaml`、`en-US.xaml` | UI 文字 | 修改（Task 1、3、4、5、6） |
| `Assetra.Tests/WPF/NavRailViewModelTests.cs` | nav VM 測試 | 建立（Task 1、2） |
| `Assetra.WPF/Shell/WelcomeOverlayView.xaml`(+`.cs`) | 首次啟動引導 overlay | 建立（Task 3） |
| `Assetra.WPF/Shell/MainWindow.xaml`、`MainViewModel.cs` | 掛 overlay、gating | 修改（Task 3） |
| `Assetra.WPF/Features/Settings/...` | 「重看上手引導」入口 | 修改（Task 3） |
| `Assetra.WPF/Features/FinancialOverview/FinancialOverviewView.xaml`(+VM) | 首頁空狀態 hero | 修改（Task 4） |
| 各 `Assetra.WPF/Features/*/*.xaml` | 頁面空狀態統一 | 修改（Task 5） |

---

## Task 1: Nav 群重整（核心 / 進階 ＋ 預設展開狀態）

**Files:**
- Modify: `Assetra.WPF/Shell/NavRailViewModel.cs`（`BuildGroups()`，約 62–133 行）
- Modify: `Assetra.WPF/Languages/zh-TW.xaml`、`Assetra.WPF/Languages/en-US.xaml`（加 `Nav.MoreAssets`）
- Test: `Assetra.Tests/WPF/NavRailViewModelTests.cs`（建立）

目標群結構：核心（分析／資產＝投資,現金,負債／收支）預設展開；進階（其他資產／規劃／工具）預設收合。`資產` 群拆出 `其他資產`（不動產,保險,退休,實物）。

- [ ] **Step 1: 寫失敗測試** — 建立 `Assetra.Tests/WPF/NavRailViewModelTests.cs`：

```csharp
using Assetra.WPF.Shell;
using Xunit;

namespace Assetra.Tests.WPF;

public sealed class NavRailViewModelTests
{
    [Fact]
    public void Groups_SplitCoreAndAdvanced_WithDefaultExpansion()
    {
        var vm = new NavRailViewModel(); // 無設定 ctor → 硬編預設

        // 6 群、順序：分析, 資產, 收支, 其他資產, 規劃, 工具
        Assert.Collection(vm.Groups,
            g => AssertGroup(g, "Nav.Analysis",  true,  NavSection.FinancialOverview, NavSection.Reports, NavSection.Assistant),
            g => AssertGroup(g, "Nav.Assets",    true,  NavSection.Portfolio, NavSection.CashAccounts, NavSection.Liabilities),
            g => AssertGroup(g, "Nav.Cashflow",  true,  NavSection.Categories, NavSection.Recurring, NavSection.TransactionLog, NavSection.Alerts),
            g => AssertGroup(g, "Nav.MoreAssets", false, NavSection.RealEstate, NavSection.Insurance, NavSection.Retirement, NavSection.PhysicalAsset),
            g => AssertGroup(g, "Nav.Planning",  false, NavSection.Goals, NavSection.Fire, NavSection.MonteCarlo, NavSection.Calculators),
            g => AssertGroup(g, "Nav.Tools",     false, NavSection.AuditLog));
    }

    private static void AssertGroup(NavGroupVm g, string titleKey, bool expanded, params NavSection[] sections)
    {
        Assert.Equal(titleKey, g.TitleResourceKey);
        Assert.Equal(expanded, g.IsExpanded);
        Assert.Equal(sections, g.Items.Select(i => i.Section).ToArray());
    }
}
```

- [ ] **Step 2: 跑測試確認 FAIL**

Run（App/Core/Infra 未鎖時可直接跑；VS 開著鎖 bin 時 build 到暫存目錄）：
`dotnet test Assetra.Tests/Assetra.Tests.csproj --filter "FullyQualifiedName~NavRailViewModelTests"`
Expected: FAIL（目前 5 群、`資產` 含 7 項、預設全展開）。

- [ ] **Step 3: 改 `BuildGroups()`** — 把 `資產` 群改成核心三項、在其後插入 `其他資產` 群，並對每群設 `IsExpanded`。核心群設 `IsExpanded = true`，進階群 `IsExpanded = false`。範例（沿用現有 `new NavLeafVm { ... }` 內容，只挪動 + 加群 + 設旗標）：

```csharp
// 資產（核心）：只留 投資 / 現金 / 負債
new NavGroupVm
{
    TitleResourceKey = "Nav.Assets",
    GroupIconSymbol = "Wallet24",
    IsExpanded = true,
    Items = new[]
    {
        new NavLeafVm { Section = NavSection.Portfolio,    LabelResourceKey = "Nav.Portfolio",    IconSymbol = "Briefcase24", ToolTipResourceKey = "Nav.Portfolio.Tip" },
        new NavLeafVm { Section = NavSection.CashAccounts, LabelResourceKey = "Nav.CashAccounts", IconSymbol = "Money24",     ToolTipResourceKey = "Nav.CashAccounts.Tip" },
        new NavLeafVm { Section = NavSection.Liabilities,  LabelResourceKey = "Nav.Liabilities",  IconSymbol = "Cut24",       ToolTipResourceKey = "Nav.Liabilities.Tip" },
    },
},
// 其他資產（進階，預設收合）
new NavGroupVm
{
    TitleResourceKey = "Nav.MoreAssets",
    GroupIconSymbol = "Wallet24",
    IsExpanded = false,
    Items = new[]
    {
        new NavLeafVm { Section = NavSection.RealEstate,    LabelResourceKey = "RealEstate.Title",    IconSymbol = "Home24",        ToolTipResourceKey = "RealEstate.Title" },
        new NavLeafVm { Section = NavSection.Insurance,     LabelResourceKey = "Insurance.Title",     IconSymbol = "Shield24",      ToolTipResourceKey = "Insurance.Title" },
        new NavLeafVm { Section = NavSection.Retirement,    LabelResourceKey = "Retirement.Title",    IconSymbol = "PersonClock24", ToolTipResourceKey = "Retirement.Title" },
        new NavLeafVm { Section = NavSection.PhysicalAsset, LabelResourceKey = "PhysicalAsset.Title", IconSymbol = "Box24",         ToolTipResourceKey = "PhysicalAsset.Title" },
    },
},
```

對 `分析`、`收支` 群加 `IsExpanded = true`；`規劃`、`工具` 群加 `IsExpanded = false`。
（`.Tip` tooltip 鍵在 Task 6 補文字；先建鍵指回標籤或補空字串以免缺鍵。）

- [ ] **Step 4: 加語言鍵** — `zh-TW.xaml` 加 `<sys:String x:Key="Nav.MoreAssets">其他資產</sys:String>`；`en-US.xaml` 加 `<sys:String x:Key="Nav.MoreAssets">More assets</sys:String>`（放在既有 `Nav.Assets` 附近）。

- [ ] **Step 5: 跑測試確認 PASS**
Run: `dotnet test Assetra.Tests/Assetra.Tests.csproj --filter "FullyQualifiedName~NavRailViewModelTests"` → PASS。

- [ ] **Step 6: Commit**
```bash
git add Assetra.WPF/Shell/NavRailViewModel.cs Assetra.WPF/Languages/zh-TW.xaml Assetra.WPF/Languages/en-US.xaml Assetra.Tests/WPF/NavRailViewModelTests.cs
git commit -m "feat(nav): 導覽列拆核心/進階群，進階預設收合"
```

---

## Task 2: Nav 展開偏好持久化

**Files:**
- Modify: `Assetra.Core/Models/AppSettings.cs`（加 `NavExpandedGroups`）
- Modify: `Assetra.WPF/Shell/NavGroupVm.cs`（加 `GroupKey`）
- Modify: `Assetra.WPF/Shell/NavRailViewModel.cs`（build 時還原、toggle 時儲存）
- Test: `Assetra.Tests/WPF/NavRailViewModelTests.cs`（新增案例）

策略：`NavExpandedGroups` = 逗號分隔的「已展開群 key」集合。空字串 = 用 Task 1 的預設。使用者按群標題 toggle 時，把當前展開群集合寫回設定（`raiseChanged: false`，比照 bookkeeping 儲存，避免觸發 app-wide reload —— 見 memory `settings-changed-feedback-loop-landmine`）。build 時若設定非空則據以還原。

- [ ] **Step 1: 寫失敗測試** — 在 `NavRailViewModelTests` 加：

```csharp
[Fact]
public void Groups_RestoreExpansionFromSettings()
{
    var settings = new FakeSettings(new AppSettings(NavExpandedGroups: "Nav.Analysis,Nav.Tools"));
    var vm = new NavRailViewModel(settings);

    Assert.True(GroupByKey(vm, "Nav.Analysis").IsExpanded);
    Assert.True(GroupByKey(vm, "Nav.Tools").IsExpanded);
    Assert.False(GroupByKey(vm, "Nav.Assets").IsExpanded);   // 不在集合 → 收合
    Assert.False(GroupByKey(vm, "Nav.Planning").IsExpanded);
}

[Fact]
public void TogglingGroup_PersistsExpandedSet()
{
    var settings = new FakeSettings(new AppSettings()); // 空 → 預設
    var vm = new NavRailViewModel(settings);
    var planning = GroupByKey(vm, "Nav.Planning");        // 預設收合

    planning.ToggleExpandedCommand.Execute(null);          // 使用者展開

    Assert.Contains("Nav.Planning", settings.Current.NavExpandedGroups.Split(','));
}

private static NavGroupVm GroupByKey(NavRailViewModel vm, string key) => vm.Groups.First(g => g.TitleResourceKey == key);
```

`FakeSettings` 若尚無測試替身，建一個最小 `IAppSettingsService` 假實作（`Current` getter ＋ `Update(Func<AppSettings,AppSettings>, bool)` 就地套用），或沿用測試專案既有替身。

- [ ] **Step 2: 跑測試確認 FAIL**（`NavExpandedGroups` 尚不存在 → 編譯失敗即算 red）。

- [ ] **Step 3: `AppSettings` 加欄位** — 在 record 尾端（`FirePathTab` 之後）加：

```csharp
    ,
    /// <summary>導覽列已展開的群 key 集合（逗號分隔的 TitleResourceKey）。
    /// 空字串 = 用預設（核心展開、進階收合）。使用者 toggle 群後持久化、下次啟動還原。</summary>
    string NavExpandedGroups = ""
```

- [ ] **Step 4: `NavGroupVm` 讓 toggle 可回呼** — 加 `public string GroupKey => TitleResourceKey;` 之外，讓 `ToggleExpanded` 後通知外部。最小作法：在 `NavRailViewModel` 訂閱各群 `PropertyChanged`（`IsExpanded`）→ 儲存。不必改 `NavGroupVm`。

- [ ] **Step 5: `NavRailViewModel` build 還原 ＋ toggle 儲存** —
  - `BuildGroups()` 讀 `_settings?.Current.NavExpandedGroups`；非空時，`IsExpanded = 集合.Contains(g.TitleResourceKey)`；空則用 Task 1 預設。
  - build 後對每群 `g.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(NavGroupVm.IsExpanded)) SaveNavExpansion(); }`。
  - `SaveNavExpansion()`：`var keys = string.Join(",", Groups.Where(g => g.IsExpanded).Select(g => g.TitleResourceKey)); _settings?.Update(s => s with { NavExpandedGroups = keys }, raiseChanged: false);`
  - 注意：`SyncActiveLeaf()` 的 auto-expand-active 也會改 `IsExpanded` → 會連帶存起來，屬可接受行為（導覽到進階頁後該群保持展開）。

- [ ] **Step 6: 跑測試確認 PASS**
Run: `dotnet test Assetra.Tests/Assetra.Tests.csproj --filter "FullyQualifiedName~NavRailViewModelTests"` → PASS（含 Task 1 案例）。

- [ ] **Step 7: 全套測試不回歸**
Run: `dotnet test Assetra.Tests/Assetra.Tests.csproj --filter "FullyQualifiedName!~ControlsBehavior"` → 全綠。

- [ ] **Step 8: Commit**
```bash
git add Assetra.Core/Models/AppSettings.cs Assetra.WPF/Shell/NavRailViewModel.cs Assetra.Tests/WPF/NavRailViewModelTests.cs
git commit -m "feat(nav): 記住導覽群展開/收合偏好"
```

---

## Task 3: 首次啟動 welcome overlay ＋ 引導加第一筆持股

**Files:**
- Create: `Assetra.WPF/Shell/WelcomeOverlayView.xaml`(+`.xaml.cs`)
- Modify: `Assetra.WPF/Shell/MainWindow.xaml`（掛 overlay）、`Assetra.WPF/Shell/MainViewModel.cs`（gating ＋ 命令）
- Modify: `Assetra.WPF/Languages/zh-TW.xaml`、`en-US.xaml`（改寫 `Onboarding.Banner.*` ＋ 加 CTA 文字）
- Modify: `Assetra.WPF/Features/Settings/...`（「重看上手引導」）
- Test: `Assetra.Tests/WPF/MainViewModelTests.cs` 或既有 shell 測試（gating 邏輯）

沿用既有 `AppSettings.HasShownWelcomeBanner`（現無 UI 掛載）。`ShowWelcome = !HasShownWelcomeBanner`。

- [ ] **Step 1: 寫失敗測試（gating 邏輯）** — 驗證 `MainViewModel.ShowWelcome` 依 `HasShownWelcomeBanner`，且 `DismissWelcomeCommand` 會設旗標並隱藏：

```csharp
[Fact]
public void Welcome_ShowsWhenFlagUnset_HidesAfterDismiss()
{
    var settings = new FakeSettings(new AppSettings(HasShownWelcomeBanner: false));
    var vm = CreateMainViewModel(settings);

    Assert.True(vm.ShowWelcome);
    vm.DismissWelcomeCommand.Execute(null);
    Assert.False(vm.ShowWelcome);
    Assert.True(settings.Current.HasShownWelcomeBanner);
}
```
（`CreateMainViewModel` 依 `MainViewModel` 實際相依建立；若相依過重，改測抽出的 `WelcomeGate` 小類別 —— 見 Step 3。）

- [ ] **Step 2: 跑測試確認 FAIL**

- [ ] **Step 3: gating 邏輯** — 在 `MainViewModel`（或抽一個 `WelcomeGate`，注入 `IAppSettingsService`）：
  - `public bool ShowWelcome { get; private set; }` 初值 `= !_settings.Current.HasShownWelcomeBanner;`（`[ObservableProperty]`）。
  - `[RelayCommand] DismissWelcome()`：`_settings.Update(s => s with { HasShownWelcomeBanner = true }, raiseChanged: false); ShowWelcome = false;`
  - `[RelayCommand] StartAddFirstHolding()`：`DismissWelcome();` ＋ 觸發既有新增交易/標的流程（沿用 shell 現有開新增的路徑；焦點落代號欄 —— autocomplete 已具備）。

- [ ] **Step 4: 跑測試確認 PASS**

- [ ] **Step 5: WelcomeOverlayView（XAML 規格）** — 全螢幕半透明遮罩 ＋ 置中卡片：
  - 標題 `Onboarding.Banner.Title`（改寫為定位句，見 Step 7）。
  - 主 CTA 按鈕 `{Binding StartAddFirstHoldingCommand}`，文字 `Onboarding.Welcome.PrimaryCta`（「加我的第一筆持股」）。
  - 次要連結 `{Binding DismissWelcomeCommand}`，文字 `Onboarding.Welcome.Skip`（「先自己逛逛」）。
  - 右上角 `✕` = `DismissWelcomeCommand`。
  - 可見性繫 `ShowWelcome`（`BooleanToVisibilityConverter`）。
  - 樣式沿用 DesignSystem（`AppButtonPrimary` 等既有 key）。
  掛在 `MainWindow.xaml` 最外層 `Grid` 疊在內容上方（最後一個子元素 → z-order 最上）。

- [ ] **Step 6: 設定「重看上手引導」** — 於設定頁（外觀或一般分類）加一個按鈕，命令 `_settings.Update(s => s with { HasShownWelcomeBanner = false }, raiseChanged: false)` 並提示「下次啟動會再顯示引導」。文字鍵 `Settings.ReplayOnboarding`。

- [ ] **Step 7: 語言鍵（雙語）** — 改寫/新增：
  - `Onboarding.Banner.Title` → 「歡迎使用 Assetra」／「Welcome to Assetra」（定位句放副標 `Onboarding.Welcome.Subtitle`：「把你的投資、現金、負債整合在一起看。」）。
  - `Onboarding.Welcome.PrimaryCta`、`Onboarding.Welcome.Skip`、`Settings.ReplayOnboarding`。兩檔都加。

- [ ] **Step 8: 手動驗收 ＋ 全套測試**
  - 清一份新設定（或暫時把 `HasShownWelcomeBanner` 設 false）→ 啟動見 overlay → 按 CTA 進新增流程、按略過/✕ 消失且不再出現。
  - `dotnet test ... --filter "FullyQualifiedName!~ControlsBehavior"` 全綠。

- [ ] **Step 9: Commit**
```bash
git add Assetra.WPF/Shell/WelcomeOverlayView.xaml Assetra.WPF/Shell/WelcomeOverlayView.xaml.cs Assetra.WPF/Shell/MainWindow.xaml Assetra.WPF/Shell/MainViewModel.cs Assetra.WPF/Features/Settings Assetra.WPF/Languages/zh-TW.xaml Assetra.WPF/Languages/en-US.xaml Assetra.Tests/WPF
git commit -m "feat(shell): 首次啟動 welcome 引導加第一筆持股（可略過、可重看）"
```

---

## Task 4: 首頁「財務概覽」空狀態 hero

**Files:**
- Modify: `Assetra.WPF/Features/FinancialOverview/FinancialOverviewView.xaml`、`FinancialOverviewViewModel.cs`
- Modify: `Assetra.WPF/Languages/zh-TW.xaml`、`en-US.xaml`

- [ ] **Step 1: VM 加 `IsEmpty`** — `FinancialOverviewViewModel` 曝一個 `bool HasAnyData`（有任何持股/帳戶/負債即 true）；`IsEmpty => !HasAnyData`。若已有等價旗標則沿用。加最小單元測試：無資料 → `IsEmpty` true。

- [ ] **Step 2: 跑測試確認 FAIL → 實作 → PASS**（比照本專案 VM 測試慣例）。

- [ ] **Step 3: XAML hero（規格）** — `IsEmpty` 為真時，在概覽區顯示 hero（用 `AppEmptyState*` 樣式）：
  - 標題 `Overview.Empty.Title`（「先加一筆持股，這裡就會長出你的資產總覽。」）
  - 主 CTA「加第一筆持股」→ 沿用 shell 新增流程命令。
  - 兩個次要快捷「串現金帳戶」「記一筆收支」→ 導 `CashAccounts` / `Categories`。
  - 有資料時隱藏 hero、顯示正常儀表板（`IsEmpty` 反向繫可見性）。

- [ ] **Step 4: 語言鍵（雙語）** — `Overview.Empty.Title` ＋ 三個 CTA 文字鍵，兩檔都加。

- [ ] **Step 5: 手動驗收 ＋ Commit**
```bash
git add Assetra.WPF/Features/FinancialOverview Assetra.WPF/Languages/zh-TW.xaml Assetra.WPF/Languages/en-US.xaml
git commit -m "feat(overview): 首頁空狀態改『從這裡開始』hero"
```

---

## Task 5: 全站 `EmptyState` 統一 ＋ 白話文案

**Files:**
- Modify: 各 `Assetra.WPF/Features/*/*.xaml`（無資料頁）
- Modify: `Assetra.WPF/Languages/zh-TW.xaml`、`en-US.xaml`

- [ ] **Step 1: 盤點** — 列出每個功能頁（Portfolio、CashAccounts、Liabilities、RealEstate、Insurance、Retirement、PhysicalAsset、Categories、Recurring、TransactionLog、Alerts、Goals、Fire、MonteCarlo、Calculators、Reports、AuditLog）目前空狀態現況（已用 `AppEmptyState*` / 自製 / 無）。記錄在本 task 的 checklist。

- [ ] **Step 2: 逐頁統一** — 未用共享樣式的補上；文案一律：`AppEmptyStateTitle`（「還沒有 X」）＋ `AppEmptyStateDescription`（一句「這頁用來…」白話）＋ `AppEmptyStateActionBar`（主要 CTA＝該頁主新增動作）。每頁一組語言鍵 `Empty.<Page>.Title` / `.Desc` / `.Cta`（雙語）。

- [ ] **Step 3: 驗收** — 手動逐頁在無資料時看空狀態；ControlsBehavior 若有頁面結構斷言則跑 `dotnet test ... --filter ControlsBehavior`（需在 repo 根跑）。

- [ ] **Step 4: Commit（可分頁多次 commit）**
```bash
git add Assetra.WPF/Features Assetra.WPF/Languages/zh-TW.xaml Assetra.WPF/Languages/en-US.xaml
git commit -m "feat(pages): 全站空狀態統一為 EmptyState + 白話文案"
```

---

## Task 6: Nav tooltip 白話化

**Files:**
- Modify: `Assetra.WPF/Shell/NavRailViewModel.cs`（`ToolTipResourceKey` 指向專屬 `.Tip` 鍵）
- Modify: `Assetra.WPF/Languages/zh-TW.xaml`、`en-US.xaml`

- [ ] **Step 1: 每項 nav 建 `.Tip` 鍵** — 為各 `NavLeafVm` 的 `ToolTipResourceKey` 指到專屬鍵（Task 1 已對資產群示範 `Nav.Portfolio.Tip` 等），其餘項比照。術語一句人話，例：
  - `Nav.Fire.Tip`：「財務自由／提早退休試算」
  - `Nav.MonteCarlo.Tip`：「退休提領成功率模擬」
  - `Nav.AuditLog.Tip`：「資料變更紀錄」
  - `Nav.Reports.Tip`：「每月收入／支出／淨額月結」
  - `Nav.Assistant.Tip`：「用對話問你的財務問題」

- [ ] **Step 2: 雙語鍵** — 兩檔都加對應 `.Tip`。

- [ ] **Step 3: 手動驗收（hover 顯示描述）＋ Commit**
```bash
git add Assetra.WPF/Shell/NavRailViewModel.cs Assetra.WPF/Languages/zh-TW.xaml Assetra.WPF/Languages/en-US.xaml
git commit -m "feat(nav): 導覽項加白話 tooltip 描述"
```

---

## Self-Review（作者已跑）

**Spec 覆蓋：** welcome＝Task 3；Nav 漸進揭露＝Task 1＋2；首頁空狀態＝Task 4；全站空狀態＝Task 5；白話 tooltip＝Task 6；「不擋熟手」＝ welcome 可略過只出現一次（T3）＋ nav 偏好持久化（T2）＋ 設定重看（T3）。皆有對應 task。

**Placeholder 掃描：** 邏輯層（T1/T2、T3 gating、T4 `IsEmpty`）給了完整測試＋程式碼；XAML 視圖以「結構＋繫結＋精確資源鍵＋驗收方式」規格化（本專案 WPF 視圖不做逐行單元測試，走 ControlsBehavior/手動），非佔位。

**型別/命名一致：** `NavExpandedGroups`（AppSettings ↔ NavRailViewModel）、`HasShownWelcomeBanner`（沿用既有）、`ShowWelcome`/`DismissWelcomeCommand`/`StartAddFirstHoldingCommand`、`Nav.MoreAssets`、`IsEmpty`/`HasAnyData` 前後一致。

**已知相依待執行時確認：** `IAppSettingsService.Update(...)` 的確切簽章與 `raiseChanged` 參數（比照 `PortfolioOverviewExpanded` 既有儲存路徑）；shell 開「新增交易/標的」流程的確切命令名。
