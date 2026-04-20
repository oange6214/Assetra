# Assetra Fork Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 從 Stockra 分家出獨立的 WPF 應用程式 Assetra，專注於「投資 + 現金 + 負債」整合型資產管理。

**Architecture:** 完整 Fork——複製 `Stockra.Core` / `Stockra.Infrastructure` / `Stockra.WPF` 到 `D:\Workspaces\Finances\Assetra\`，重寫 namespace，裁掉分析／新聞／AI／選股／策略模組，保留盤中 Rx 即時報價。獨立 SQLite DB 於 `%APPDATA%\Assetra\`。首次啟動自動從 Stockra DB 匯入相容資料。

**Tech Stack:** .NET 9, WPF, CommunityToolkit.Mvvm, System.Reactive, Microsoft.Data.Sqlite, Refit/HttpClient, xUnit, MinVer。

**Spec:** [docs/superpowers/specs/2026-04-20-assetra-fork-design.md](../specs/2026-04-20-assetra-fork-design.md)

---

## File Structure

新建目錄結構與檔案預計數量：

| 路徑 | 建立 | 複製 | 修改 | 刪除 |
|------|------|------|------|------|
| `Assetra.slnx` | 1 | — | — | — |
| `Directory.Build.props` / `.editorconfig` / `CLAUDE.md` / `README.md` | 4 | — | — | — |
| `Assetra.Core/*.csproj` + Models + Interfaces + Trading | 1 | 21 models + 17 interfaces + 2 trading = 40 | 2 (AppSettings, IFinMindService) | — |
| `Assetra.Infrastructure/*.csproj` + code | 1 | ~35 | 5 (StockScheduler, AppSettingsService, DbInit, IFinMindService impl, Bootstrapper-consumed) | — |
| `Assetra.Infrastructure/Persistence/StockraImportService.cs` | 1 | — | — | — |
| `Assetra.WPF/*.csproj` + Shell + Features + Infrastructure + Languages + Themes + Controls + Assets | 1 + ~120 | ~200 | AppBootstrapper 重寫, NavSection 重寫, MainViewModel/NavRail 裁剪 | 9 features |
| `Assetra.WPF/Assets/assetra.ico` | 1（placeholder） | — | — | — |
| `Assetra.Tests/*.csproj` + tests | 1 + 複製的測試 | — | 修 namespace | 與砍掉的 feature 對應的測試 |

---

## Phase 0 — Scaffolding

建立空的 solution 骨架與基礎設定檔。

### Task 0.1: 建立根目錄與 solution 檔

**Files:**
- Create: `D:\Workspaces\Finances\Assetra\Assetra.slnx`

- [ ] **Step 1: 建立根目錄**

Run:
```bash
mkdir -p "D:/Workspaces/Finances/Assetra/Assetra.Core"
mkdir -p "D:/Workspaces/Finances/Assetra/Assetra.Infrastructure"
mkdir -p "D:/Workspaces/Finances/Assetra/Assetra.WPF"
mkdir -p "D:/Workspaces/Finances/Assetra/Assetra.Tests"
```

Expected: 四個子資料夾建立成功。

- [ ] **Step 2: 寫 `Assetra.slnx`**

File: `D:/Workspaces/Finances/Assetra/Assetra.slnx`
```xml
<Solution>
  <Project Path="Assetra.Core/Assetra.Core.csproj" />
  <Project Path="Assetra.Infrastructure/Assetra.Infrastructure.csproj" />
  <Project Path="Assetra.Tests/Assetra.Tests.csproj" />
  <Project Path="Assetra.WPF/Assetra.WPF.csproj" />
</Solution>
```

- [ ] **Step 3: 初始化本地 git**

Run:
```bash
cd "D:/Workspaces/Finances/Assetra"
git init
git config user.name "Pohan"
git config user.email oange6214@gmail.com
```

Expected: `.git/` 建立；`git status` 顯示 untracked files。

- [ ] **Step 4: Commit**

```bash
cd "D:/Workspaces/Finances/Assetra"
git add Assetra.slnx
git commit -m "chore: initialize Assetra solution skeleton"
```

---

### Task 0.2: 複製基礎設定檔

**Files:**
- Copy: `D:/Workspaces/Finances/Stockra/Directory.Build.props` → `D:/Workspaces/Finances/Assetra/Directory.Build.props`
- Copy: `D:/Workspaces/Finances/Stockra/.editorconfig` → `D:/Workspaces/Finances/Assetra/.editorconfig`
- Create: `D:/Workspaces/Finances/Assetra/.gitignore`
- Create: `D:/Workspaces/Finances/Assetra/README.md`
- Create: `D:/Workspaces/Finances/Assetra/CLAUDE.md`

- [ ] **Step 1: 複製 Directory.Build.props 與 .editorconfig**

Run:
```bash
cp "D:/Workspaces/Finances/Stockra/Directory.Build.props" "D:/Workspaces/Finances/Assetra/Directory.Build.props"
cp "D:/Workspaces/Finances/Stockra/.editorconfig" "D:/Workspaces/Finances/Assetra/.editorconfig"
cp "D:/Workspaces/Finances/Stockra/.gitignore" "D:/Workspaces/Finances/Assetra/.gitignore"
```

- [ ] **Step 2: 調整 `Directory.Build.props` MinVer tag 前綴**

File: `D:/Workspaces/Finances/Assetra/Directory.Build.props`

Change `<MinVerTagPrefix>v</MinVerTagPrefix>` if needed. 目前保持 `v` 即可（Assetra 獨立 repo，tag 不衝突）。

- [ ] **Step 3: 寫精簡版 `CLAUDE.md`**

File: `D:/Workspaces/Finances/Assetra/CLAUDE.md`
```markdown
# Assetra — Claude Code 指引

## 快速導覽

| 目的 | 路徑 |
|------|------|
| DI 組合根 | `Assetra.WPF/Infrastructure/AppBootstrapper.cs` |
| 領域模型 | `Assetra.Core/Models/` |
| 服務介面 | `Assetra.Core/Interfaces/` |
| HTTP 客戶端 | `Assetra.Infrastructure/Http/` |
| 資料庫 | `Assetra.Infrastructure/Persistence/` |
| 主視窗 | `Assetra.WPF/Shell/MainWindow.xaml` |
| 全域樣式 | `Assetra.WPF/Themes/GlobalStyles.xaml` |
| 語言 Key | `Assetra.WPF/Languages/zh-TW.xaml` (正體中文，主要) |

## 建置與測試

\`\`\`bash
dotnet build Assetra.slnx
dotnet test Assetra.Tests/Assetra.Tests.csproj
dotnet format
\`\`\`

## 架構原則

- **依賴方向：** `Core ← Infrastructure ← WPF`
- **ViewModel：** `ObservableObject` + `[RelayCommand]`
- **DI：** 集中在 `AppBootstrapper.cs`；ViewModel 一律 `AddSingleton`
- **資料庫：** SQLite WAL 於 `%APPDATA%\Assetra\assetra.db`
- **UI 文字：** 所有字串放 `Languages/*.xaml`，DataGrid 欄標題用 Header 內嵌 TextBlock

## 語言系統

兩個語言檔（zh-TW + en-US），新增 UI 文字時兩檔都要加。

## 不做的事

- 不在此專案引用 `Stockra.*` 命名空間——Assetra 是獨立 fork
- 不加 AI／新聞／選股／自訂策略等模組（屬於 Stockra）
- 不在 WPF 執行緒以外直接操作 `ObservableCollection`
```

- [ ] **Step 4: 寫 `README.md`（最精簡版）**

File: `D:/Workspaces/Finances/Assetra/README.md`
```markdown
# Assetra

資產管理桌面應用程式（WPF, .NET 9）。

- 追蹤現金、負債、投資組合的整體財務狀況
- 盤中即時股票報價（TWSE / TPEX / CoinGecko）
- 資產配置分析（Treemap + 再平衡）
- 交易記錄 + 配息追蹤
- 價格警示

## 建置

\`\`\`bash
dotnet build Assetra.slnx
dotnet run --project Assetra.WPF
\`\`\`
```

- [ ] **Step 5: Commit（含已存在的設計文件）**

```bash
cd "D:/Workspaces/Finances/Assetra"
git add Directory.Build.props .editorconfig .gitignore CLAUDE.md README.md docs/
git commit -m "chore: add Assetra build props, CLAUDE.md, and design docs"
```

> 註：`docs/superpowers/specs/2026-04-20-assetra-fork-design.md` 與 `docs/superpowers/plans/2026-04-20-assetra-fork.md` 已在本 Phase 前置作業階段放入 Assetra，隨著首次 commit 一起進入 Assetra repo 歷史。

---

## Phase 1 — Core Layer Fork

目標：建立 `Assetra.Core.csproj`，複製 21 models + 17 interfaces + Trading 子資料夾，全域替換 namespace，精簡 `AppSettings` 與 `IFinMindService`。

### Task 1.1: 建立 `Assetra.Core.csproj`

**Files:**
- Create: `D:/Workspaces/Finances/Assetra/Assetra.Core/Assetra.Core.csproj`

- [ ] **Step 1: 讀取 Stockra.Core.csproj 作為範本**

Run:
```bash
cat "D:/Workspaces/Finances/Stockra/Stockra.Core/Stockra.Core.csproj"
```

- [ ] **Step 2: 寫 `Assetra.Core.csproj`**

File: `D:/Workspaces/Finances/Assetra/Assetra.Core/Assetra.Core.csproj`

內容跟 `Stockra.Core.csproj` 幾乎一樣，但：
- `<RootNamespace>` 改 `Assetra.Core`
- `<AssemblyName>` 改 `Assetra.Core`

- [ ] **Step 3: 試編譯（預期失敗：無原始碼）**

Run:
```bash
cd "D:/Workspaces/Finances/Assetra"
dotnet build Assetra.Core/Assetra.Core.csproj
```

Expected: 編譯可能成功（空專案合法）或產生空 dll。

---

### Task 1.2: 複製 Core Models（刪減版）

**Files:**
- Copy: `Stockra.Core/Models/*.cs` → `Assetra.Core/Models/*.cs`（20 個保留）

> **Spec 更正：** Spec 原文誤寫「21 個」，實際保留 20 個；刪除 14 個（原本 34 個 Model）。

保留清單（20 個）：
```
AlertRule.cs, AppSettings.cs, AssetEvent.cs, AssetEventType.cs, AssetGroup.cs,
AssetItem.cs, AssetType.cs, ChartData.cs, ChartPeriod.cs, FinancialType.cs,
OhlcvPoint.cs, PortfolioDailySnapshot.cs, PortfolioEntry.cs,
PortfolioPositionLog.cs, PositionSnapshot.cs, Result.cs, StockQuote.cs,
StockSearchResult.cs, Trade.cs, TradeType.cs
```

刪除清單（14 個）：
```
ChatMessage.cs, NewsItem.cs, FlashNewsItem.cs, FlashSeverity.cs,
CustomStrategy.cs, EvaluationContext.cs, StrategyParseResult.cs,
ScreenerPreset.cs, ResearchTemplate.cs, MarketData.cs, InstitutionalData.cs,
MarginData.cs, FinancialStatement.cs, WatchlistEntry.cs
```

- [ ] **Step 1: 複製 20 個保留 models**

Run:
```bash
cd "D:/Workspaces/Finances/Assetra/Assetra.Core"
mkdir -p Models
for f in AlertRule AppSettings AssetEvent AssetEventType AssetGroup AssetItem AssetType ChartData ChartPeriod FinancialType OhlcvPoint PortfolioDailySnapshot PortfolioEntry PortfolioPositionLog PositionSnapshot Result StockQuote StockSearchResult Trade TradeType; do
    cp "D:/Workspaces/Finances/Stockra/Stockra.Core/Models/${f}.cs" "Models/${f}.cs"
done
```

- [ ] **Step 2: 全域替換 namespace**

Run:
```bash
cd "D:/Workspaces/Finances/Assetra/Assetra.Core/Models"
# Windows bash (git bash) — use perl for in-place editing
for f in *.cs; do
    perl -i -pe 's/namespace Stockra\.Core\.Models/namespace Assetra.Core.Models/g' "$f"
    perl -i -pe 's/using Stockra\.Core/using Assetra.Core/g' "$f"
done
```

- [ ] **Step 3: 驗證替換完成**

Run:
```bash
cd "D:/Workspaces/Finances/Assetra/Assetra.Core/Models"
grep -r "Stockra\." .
```

Expected: 無結果（全部已替換）。

- [ ] **Step 4: 編譯 Core（預期仍有錯，因 Interfaces 未複製）**

Run:
```bash
cd "D:/Workspaces/Finances/Assetra"
dotnet build Assetra.Core/Assetra.Core.csproj
```

Expected: 失敗——某些 model 可能 reference 到尚未複製的 interface（如 `AppSettings` 內嵌型別），先忽略，繼續 Task 1.3。

---

### Task 1.3: 複製 Core Interfaces（刪減版）

**Files:**
- Copy: `Stockra.Core/Interfaces/*.cs` → `Assetra.Core/Interfaces/*.cs`（17 個保留）

保留清單：
```
IAlertRepository.cs, IAppSettingsService.cs, IAssetRepository.cs,
IBalanceQueryService.cs, ICryptoService.cs, ICurrencyService.cs,
IFinMindService.cs, ILocalizationService.cs,
IPortfolioPositionLogRepository.cs, IPortfolioRepository.cs,
IPortfolioSnapshotRepository.cs, IPositionQueryService.cs,
IStockHistoryProvider.cs, IStockSearchService.cs, IStockService.cs,
ITradeRepository.cs, ITransactionService.cs
```

- [ ] **Step 1: 複製 17 個保留介面**

Run:
```bash
cd "D:/Workspaces/Finances/Assetra/Assetra.Core"
mkdir -p Interfaces
for f in IAlertRepository IAppSettingsService IAssetRepository IBalanceQueryService ICryptoService ICurrencyService IFinMindService ILocalizationService IPortfolioPositionLogRepository IPortfolioRepository IPortfolioSnapshotRepository IPositionQueryService IStockHistoryProvider IStockSearchService IStockService ITradeRepository ITransactionService; do
    cp "D:/Workspaces/Finances/Stockra/Stockra.Core/Interfaces/${f}.cs" "Interfaces/${f}.cs"
done
```

- [ ] **Step 2: 全域替換 namespace**

Run:
```bash
cd "D:/Workspaces/Finances/Assetra/Assetra.Core/Interfaces"
for f in *.cs; do
    perl -i -pe 's/namespace Stockra\.Core\.Interfaces/namespace Assetra.Core.Interfaces/g' "$f"
    perl -i -pe 's/using Stockra\.Core/using Assetra.Core/g' "$f"
done
```

- [ ] **Step 3: 驗證替換**

Run:
```bash
cd "D:/Workspaces/Finances/Assetra/Assetra.Core/Interfaces"
grep -r "Stockra\." .
```

Expected: 無結果。

---

### Task 1.4: 複製 Core/Trading/

**Files:**
- Copy: `Stockra.Core/Trading/*.cs` → `Assetra.Core/Trading/*.cs`

- [ ] **Step 1: 複製並 rename namespace**

Run:
```bash
cd "D:/Workspaces/Finances/Assetra/Assetra.Core"
mkdir -p Trading
cp "D:/Workspaces/Finances/Stockra/Stockra.Core/Trading/TaiwanTradeFee.cs" Trading/
cp "D:/Workspaces/Finances/Stockra/Stockra.Core/Trading/TaiwanTradeFeeCalculator.cs" Trading/
cd Trading
for f in *.cs; do
    perl -i -pe 's/namespace Stockra\.Core\.Trading/namespace Assetra.Core.Trading/g' "$f"
    perl -i -pe 's/using Stockra\.Core/using Assetra.Core/g' "$f"
done
```

---

### Task 1.5: 精簡 `AppSettings`

**Files:**
- Modify: `D:/Workspaces/Finances/Assetra/Assetra.Core/Models/AppSettings.cs`

- [ ] **Step 1: 讀取現有 AppSettings**

Run:
```bash
cat "D:/Workspaces/Finances/Assetra/Assetra.Core/Models/AppSettings.cs"
```

- [ ] **Step 2: 移除 AI／FinMind Token 欄位**

在 `AppSettings` record 中：
- ❌ 刪除：`OpenAiApiKey`、`OpenAiModel`、`FinMindToken`、`AiEnabled`、`AnthropicApiKey`（或任何類似欄位）
- ✅ 保留：`Theme`、`Language`、`PreferredCurrency`、`CommissionDiscount`、`TargetAllocations`、`TaiwanStyleFees`、`LastOpenedTab` 等

若欄位命名無法在此處猜準，engineer 執行時需對照 Stockra 原檔視情況刪除 AI／FinMind 相關欄位。

---

### Task 1.6: 精簡 `IFinMindService`

**Files:**
- Modify: `D:/Workspaces/Finances/Assetra/Assetra.Core/Interfaces/IFinMindService.cs`

- [ ] **Step 1: 保留單一方法**

將介面縮減為只保留「查詢指定日期收盤價」的方法，例如：

```csharp
namespace Assetra.Core.Interfaces;

public interface IFinMindService
{
    Task<decimal?> GetDailyCloseAsync(string symbol, DateOnly date, CancellationToken ct = default);
}
```

若 Stockra 原介面有額外方法（法人、融資、財報等），全部刪除。

---

### Task 1.7: 編譯 Core 並驗證

- [ ] **Step 1: 編譯**

Run:
```bash
cd "D:/Workspaces/Finances/Assetra"
dotnet build Assetra.Core/Assetra.Core.csproj
```

Expected: **成功**。若有 error，多半是：
- 某個 model reference 了被刪除的 interface（檢查 `FinancialStatement`、`InstitutionalData` 等相依）
- namespace 替換遺漏

- [ ] **Step 2: 修正所有 error，直到編譯成功**

- [ ] **Step 3: Commit**

```bash
cd "D:/Workspaces/Finances/Assetra"
git add Assetra.Core
git commit -m "feat(core): fork Assetra.Core from Stockra with trimmed scope"
```

---

## Phase 2 — Infrastructure Layer Fork

目標：複製並裁剪 `Stockra.Infrastructure` 到 `Assetra.Infrastructure`，並修改 `StockScheduler` 去掉 watchlist 依賴。

### Task 2.1: 建立 `Assetra.Infrastructure.csproj`

**Files:**
- Create: `D:/Workspaces/Finances/Assetra/Assetra.Infrastructure/Assetra.Infrastructure.csproj`

- [ ] **Step 1: 複製 Stockra.Infrastructure.csproj 為範本並改名**

Run:
```bash
cp "D:/Workspaces/Finances/Stockra/Stockra.Infrastructure/Stockra.Infrastructure.csproj" \
   "D:/Workspaces/Finances/Assetra/Assetra.Infrastructure/Assetra.Infrastructure.csproj"
```

- [ ] **Step 2: 修改 csproj**

File: `D:/Workspaces/Finances/Assetra/Assetra.Infrastructure/Assetra.Infrastructure.csproj`

- `<RootNamespace>` 改 `Assetra.Infrastructure`
- `<AssemblyName>` 改 `Assetra.Infrastructure`
- `<ProjectReference Include="..\\Stockra.Core\\Stockra.Core.csproj" />` 改為 `<ProjectReference Include="..\\Assetra.Core\\Assetra.Core.csproj" />`

---

### Task 2.2: 複製頂層檔案

**Files:**
- Copy: `BalanceQueryService.cs`, `PositionQueryService.cs`, `TransactionService.cs`, `CurrencyService.cs`

- [ ] **Step 1: 複製 + rename namespace**

Run:
```bash
cd "D:/Workspaces/Finances/Assetra/Assetra.Infrastructure"
for f in BalanceQueryService PositionQueryService TransactionService CurrencyService; do
    cp "D:/Workspaces/Finances/Stockra/Stockra.Infrastructure/${f}.cs" "${f}.cs"
    perl -i -pe 's/namespace Stockra\.Infrastructure/namespace Assetra.Infrastructure/g' "${f}.cs"
    perl -i -pe 's/using Stockra\./using Assetra./g' "${f}.cs"
done
```

---

### Task 2.3: 複製 Http/ 子資料夾（保留 6 檔）

- [ ] **Step 1: 建立資料夾並選擇性複製**

Run:
```bash
cd "D:/Workspaces/Finances/Assetra/Assetra.Infrastructure"
mkdir -p Http
for f in ITwseClient TwseClient ITpexClient TpexClient CoinGeckoService MisParsing TwseMisModels; do
    cp "D:/Workspaces/Finances/Stockra/Stockra.Infrastructure/Http/${f}.cs" "Http/${f}.cs"
done
```

- [ ] **Step 2: namespace rename**

Run:
```bash
cd "D:/Workspaces/Finances/Assetra/Assetra.Infrastructure/Http"
for f in *.cs; do
    perl -i -pe 's/namespace Stockra\.Infrastructure/namespace Assetra.Infrastructure/g' "$f"
    perl -i -pe 's/using Stockra\./using Assetra./g' "$f"
done
```

- [ ] **Step 3: 驗證**

```bash
grep -r "Stockra\." "D:/Workspaces/Finances/Assetra/Assetra.Infrastructure/Http"
```
Expected: 無結果。

---

### Task 2.4: 複製 Persistence/（保留 12 檔）

- [ ] **Step 1: 選擇性複製**

Run:
```bash
cd "D:/Workspaces/Finances/Assetra/Assetra.Infrastructure"
mkdir -p Persistence
for f in AlertSqliteRepository AppSettingsService AssetSqliteRepository PortfolioSqliteRepository PortfolioBackfillService PortfolioPositionLogSqliteRepository PortfolioSnapshotService PortfolioSnapshotSqliteRepository TradeSqliteRepository TransferPairMigrationService DbMigrator SqliteSchemaHelper; do
    cp "D:/Workspaces/Finances/Stockra/Stockra.Infrastructure/Persistence/${f}.cs" "Persistence/${f}.cs"
done
```

- [ ] **Step 2: namespace rename**

```bash
cd "D:/Workspaces/Finances/Assetra/Assetra.Infrastructure/Persistence"
for f in *.cs; do
    perl -i -pe 's/namespace Stockra\.Infrastructure/namespace Assetra.Infrastructure/g' "$f"
    perl -i -pe 's/using Stockra\./using Assetra./g' "$f"
done
```

- [ ] **Step 3: 改寫 `DbMigrator.cs` — 移除 watchlist 遷移**

File: `D:/Workspaces/Finances/Assetra/Assetra.Infrastructure/Persistence/DbMigrator.cs`

- 保留 `ApplyPragmasAsync`
- 從 `MigrateAsync` 簽名中移除 `IWatchlistRepository watchlist` 參數
- 刪除 `MigrateWatchlistAsync` 整個方法
- Call site 會在後面 WPF DI 重寫時一起修正

- [ ] **Step 4: 更新 `SqliteSchemaHelper.KnownTables`**

File: `D:/Workspaces/Finances/Assetra/Assetra.Infrastructure/Persistence/SqliteSchemaHelper.cs`

從 `KnownTables` HashSet 移除：`"watchlist"`、`"custom_strategy"`、`"research_template"`。

保留：`"portfolio"`, `"liability_account"`, `"cash_account"`, `"trade"`, `"alert"`, `"portfolio_snapshot"`, `"portfolio_position_log"`, `"asset_group"`, `"asset"`, `"asset_event"`。

---

### Task 2.5: 複製 Scheduling/ 並修改 `StockScheduler`

**Files:**
- Copy: `Scheduling/StockScheduler.cs`
- Modify: 移除 `IWatchlistRepository` 依賴

- [ ] **Step 1: 複製**

```bash
cd "D:/Workspaces/Finances/Assetra/Assetra.Infrastructure"
mkdir -p Scheduling
cp "D:/Workspaces/Finances/Stockra/Stockra.Infrastructure/Scheduling/StockScheduler.cs" Scheduling/
perl -i -pe 's/namespace Stockra\.Infrastructure/namespace Assetra.Infrastructure/g' Scheduling/StockScheduler.cs
perl -i -pe 's/using Stockra\./using Assetra./g' Scheduling/StockScheduler.cs
```

- [ ] **Step 2: 修改 `StockScheduler` 建構子**

File: `D:/Workspaces/Finances/Assetra/Assetra.Infrastructure/Scheduling/StockScheduler.cs`

替換建構子：
```csharp
// Before
public StockScheduler(ITwseClient twse, ITpexClient tpex,
    IWatchlistRepository watchlist, IPortfolioRepository portfolio,
    IScheduler scheduler, TimeSpan? interval = null)
{
    _twse = twse;
    _tpex = tpex;
    _watchlist = watchlist;
    _portfolio = portfolio;
    ...
}

// After
public StockScheduler(ITwseClient twse, ITpexClient tpex,
    IPortfolioRepository portfolio,
    IScheduler scheduler, TimeSpan? interval = null)
{
    _twse = twse;
    _tpex = tpex;
    _portfolio = portfolio;
    _scheduler = scheduler;
    _interval = interval ?? TimeSpan.FromSeconds(10);
}
```

並刪除欄位 `private readonly IWatchlistRepository _watchlist;`。

- [ ] **Step 3: 修改 `FetchAllAsync` 移除 watchlist 合併**

```csharp
private async Task<IReadOnlyList<StockQuote>> FetchAllAsync()
{
    var portfolioEntries = await _portfolio.GetEntriesAsync();

    var entries = portfolioEntries
        .Select(e => (e.Symbol, e.Exchange))
        .DistinctBy(e => e.Symbol)
        .ToList();

    var twseSymbols = entries.Where(e => e.Exchange == "TWSE").Select(e => e.Symbol).ToList();
    var tpexSymbols = entries.Where(e => e.Exchange == "TPEX").Select(e => e.Symbol).ToList();

    var twseTask = twseSymbols.Count > 0
        ? _twse.FetchQuotesAsync(twseSymbols)
        : Task.FromResult<IReadOnlyList<StockQuote>>([]);
    var tpexTask = tpexSymbols.Count > 0
        ? _tpex.FetchQuotesAsync(tpexSymbols)
        : Task.FromResult<IReadOnlyList<StockQuote>>([]);

    var twseResults = await twseTask;
    var tpexResults = await tpexTask;
    return [.. twseResults, .. tpexResults];
}
```

---

### Task 2.6: 複製 Search/, History/, Chart/, FinMind/（含精簡）

- [ ] **Step 1: 整個資料夾複製**

Run:
```bash
cd "D:/Workspaces/Finances/Assetra/Assetra.Infrastructure"
cp -r "D:/Workspaces/Finances/Stockra/Stockra.Infrastructure/Search" .
cp -r "D:/Workspaces/Finances/Stockra/Stockra.Infrastructure/History" .
cp -r "D:/Workspaces/Finances/Stockra/Stockra.Infrastructure/Chart" .
cp -r "D:/Workspaces/Finances/Stockra/Stockra.Infrastructure/FinMind" .
```

- [ ] **Step 2: 全資料夾 namespace rename**

```bash
cd "D:/Workspaces/Finances/Assetra/Assetra.Infrastructure"
find Search History Chart FinMind -name "*.cs" -exec perl -i -pe 's/namespace Stockra\./namespace Assetra./g; s/using Stockra\./using Assetra./g' {} \;
```

- [ ] **Step 3: 精簡 FinMind**

File: `D:/Workspaces/Finances/Assetra/Assetra.Infrastructure/FinMind/`

- 刪除 `IFinMindService` 介面中法人／融資／財報等方法對應的實作（方法刪掉或改為 `throw new NotSupportedException()`）
- 保留「取得每日收盤價」相關的 API 呼叫與 DTO
- 相關私有 DTO 若僅用於被刪方法也一併刪除

engineer 執行時視 `FinMindService.cs` 實際結構決定刪哪些方法。

---

### Task 2.7: 編譯 Infrastructure 並驗證

- [ ] **Step 1: 編譯**

```bash
cd "D:/Workspaces/Finances/Assetra"
dotnet build Assetra.Infrastructure/Assetra.Infrastructure.csproj
```

- [ ] **Step 2: 修正所有 error**

常見錯誤：
- `DbMigrator.MigrateAsync` 簽名改變後，呼叫端（`DbInitializerService` 在 WPF 層）還未同步——可先暫時忽略（WPF 層未複製）
- 某個刪掉的 interface（`ICustomStrategyRepository` 等）被殘存的實作引用——找到後一起刪除

- [ ] **Step 3: 驗證 `grep "Stockra\\." .` 無殘留**

```bash
cd "D:/Workspaces/Finances/Assetra/Assetra.Infrastructure"
grep -r "Stockra\." .
```
Expected: 無結果（若有殘留，補替換）。

- [ ] **Step 4: Commit**

```bash
cd "D:/Workspaces/Finances/Assetra"
git add Assetra.Infrastructure
git commit -m "feat(infra): fork Assetra.Infrastructure with trimmed scope"
```

---

## Phase 3 — StockraImportService (TDD)

目標：新寫一個 import service，首次啟動時從 Stockra DB ATTACH 並複製相容資料表。

### Task 3.1: 建立 `IStockraImportService` 介面

**Files:**
- Create: `Assetra.Core/Interfaces/IStockraImportService.cs`

- [ ] **Step 1: 寫介面**

File: `D:/Workspaces/Finances/Assetra/Assetra.Core/Interfaces/IStockraImportService.cs`
```csharp
namespace Assetra.Core.Interfaces;

public interface IStockraImportService
{
    /// <summary>
    /// 從指定的 Stockra DB 檔案匯入相容資料到 Assetra DB。
    /// 相容表：portfolio, trade, asset_group, asset, asset_event,
    ///         portfolio_snapshot, portfolio_position_log, alert。
    /// 不匯入：watchlist, custom_strategy, research_template, screener_preset。
    /// 若 Assetra DB 對應表已有資料則該表跳過（避免重複匯入）。
    /// </summary>
    Task<ImportResult> ImportAsync(string stockraDbPath, CancellationToken ct = default);
}

public sealed record ImportResult(int TotalRows, IReadOnlyDictionary<string, int> PerTable);
```

- [ ] **Step 2: Commit**

```bash
cd "D:/Workspaces/Finances/Assetra"
git add Assetra.Core/Interfaces/IStockraImportService.cs
git commit -m "feat(core): add IStockraImportService interface"
```

---

### Task 3.2: 建立 `Assetra.Tests.csproj`

**Files:**
- Create: `Assetra.Tests/Assetra.Tests.csproj`

- [ ] **Step 1: 複製 Stockra.Tests.csproj 為範本**

Run:
```bash
cp "D:/Workspaces/Finances/Stockra/Stockra.Tests/Stockra.Tests.csproj" \
   "D:/Workspaces/Finances/Assetra/Assetra.Tests/Assetra.Tests.csproj"
```

- [ ] **Step 2: 修改**

File: `D:/Workspaces/Finances/Assetra/Assetra.Tests/Assetra.Tests.csproj`

- `<AssemblyName>` 改 `Assetra.Tests`
- `<RootNamespace>` 改 `Assetra.Tests`
- ProjectReference 改為 `Assetra.Core` 和 `Assetra.Infrastructure`

---

### Task 3.3: 寫 `StockraImportService` 的失敗測試

**Files:**
- Create: `Assetra.Tests/Infrastructure/StockraImportServiceTests.cs`

- [ ] **Step 1: 寫測試**

File: `D:/Workspaces/Finances/Assetra/Assetra.Tests/Infrastructure/StockraImportServiceTests.cs`
```csharp
using Microsoft.Data.Sqlite;
using Xunit;
using Assetra.Infrastructure.Persistence;
using Assetra.Core.Interfaces;

namespace Assetra.Tests.Infrastructure;

public sealed class StockraImportServiceTests : IDisposable
{
    private readonly string _srcDb;
    private readonly string _dstDb;

    public StockraImportServiceTests()
    {
        _srcDb = Path.GetTempFileName();
        _dstDb = Path.GetTempFileName();
    }

    public void Dispose()
    {
        File.Delete(_srcDb);
        File.Delete(_dstDb);
    }

    [Fact]
    public async Task ImportAsync_CopiesPortfolioRows_WhenSourceHasData()
    {
        // Arrange: create source DB with portfolio table + one row
        using (var src = new SqliteConnection($"Data Source={_srcDb}"))
        {
            await src.OpenAsync();
            await ExecuteAsync(src, """
                CREATE TABLE portfolio (
                    id TEXT PRIMARY KEY, symbol TEXT, exchange TEXT,
                    asset_type TEXT, display_name TEXT, currency TEXT, is_active INTEGER
                );
                INSERT INTO portfolio VALUES (
                    'e1111111-1111-1111-1111-111111111111', '2330', 'TWSE',
                    'Stock', '台積電', 'TWD', 1);
                """);
        }

        // Arrange: create target DB with empty portfolio table
        using (var dst = new SqliteConnection($"Data Source={_dstDb}"))
        {
            await dst.OpenAsync();
            await ExecuteAsync(dst, """
                CREATE TABLE portfolio (
                    id TEXT PRIMARY KEY, symbol TEXT, exchange TEXT,
                    asset_type TEXT, display_name TEXT, currency TEXT, is_active INTEGER
                );
                """);
        }

        // Act
        var sut = new StockraImportService(_dstDb);
        var result = await sut.ImportAsync(_srcDb);

        // Assert
        Assert.Equal(1, result.PerTable["portfolio"]);
        Assert.Equal(1, result.TotalRows);

        using var verify = new SqliteConnection($"Data Source={_dstDb}");
        await verify.OpenAsync();
        using var cmd = verify.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM portfolio;";
        var count = (long)(await cmd.ExecuteScalarAsync() ?? 0L);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task ImportAsync_SkipsTable_WhenTargetAlreadyHasRows()
    {
        const string schema = """
            CREATE TABLE portfolio (
                id TEXT PRIMARY KEY, symbol TEXT, exchange TEXT,
                asset_type TEXT, display_name TEXT, currency TEXT, is_active INTEGER
            );
            """;

        using (var src = new SqliteConnection($"Data Source={_srcDb}"))
        {
            await src.OpenAsync();
            await ExecuteAsync(src, schema + """
                INSERT INTO portfolio VALUES
                    ('s1', 'SRC1', 'TWSE', 'Stock', 'from src', 'TWD', 1);
                """);
        }

        using (var dst = new SqliteConnection($"Data Source={_dstDb}"))
        {
            await dst.OpenAsync();
            await ExecuteAsync(dst, schema + """
                INSERT INTO portfolio VALUES
                    ('d1', 'DST1', 'TWSE', 'Stock', 'existing', 'TWD', 1);
                """);
        }

        var sut = new StockraImportService(_dstDb);
        var result = await sut.ImportAsync(_srcDb);

        Assert.Equal(0, result.TotalRows);
        Assert.Equal(0, result.PerTable["portfolio"]);

        using var verify = new SqliteConnection($"Data Source={_dstDb}");
        await verify.OpenAsync();
        using var cmd = verify.CreateCommand();
        cmd.CommandText = "SELECT symbol FROM portfolio;";
        using var rdr = await cmd.ExecuteReaderAsync();
        Assert.True(await rdr.ReadAsync());
        Assert.Equal("DST1", rdr.GetString(0));  // source 未覆蓋
        Assert.False(await rdr.ReadAsync());      // 只有一筆（目標原本的）
    }

    [Fact]
    public async Task ImportAsync_IgnoresNonexistentTables()
    {
        // 來源沒有 alert 表；目標有 → 匯入時略過，不 throw
        using (var src = new SqliteConnection($"Data Source={_srcDb}"))
        {
            await src.OpenAsync();
            await ExecuteAsync(src, """
                CREATE TABLE portfolio (
                    id TEXT PRIMARY KEY, symbol TEXT, exchange TEXT,
                    asset_type TEXT, display_name TEXT, currency TEXT, is_active INTEGER
                );
                """);
        }

        using (var dst = new SqliteConnection($"Data Source={_dstDb}"))
        {
            await dst.OpenAsync();
            await ExecuteAsync(dst, """
                CREATE TABLE portfolio (
                    id TEXT PRIMARY KEY, symbol TEXT, exchange TEXT,
                    asset_type TEXT, display_name TEXT, currency TEXT, is_active INTEGER
                );
                CREATE TABLE alert (
                    id TEXT PRIMARY KEY, symbol TEXT, price REAL, direction TEXT
                );
                """);
        }

        var sut = new StockraImportService(_dstDb);
        var result = await sut.ImportAsync(_srcDb);  // 不應 throw

        Assert.False(result.PerTable.ContainsKey("alert"));  // 跳過
        Assert.Equal(0, result.TotalRows);
    }

    private static async Task ExecuteAsync(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }
}
```

- [ ] **Step 2: 執行測試確認失敗**

```bash
cd "D:/Workspaces/Finances/Assetra"
dotnet test Assetra.Tests/Assetra.Tests.csproj --filter FullyQualifiedName~StockraImportServiceTests
```

Expected: FAIL（`StockraImportService` 尚未實作，編譯失敗）。

---

### Task 3.4: 實作 `StockraImportService`

**Files:**
- Create: `Assetra.Infrastructure/Persistence/StockraImportService.cs`

- [ ] **Step 1: 寫實作**

File: `D:/Workspaces/Finances/Assetra/Assetra.Infrastructure/Persistence/StockraImportService.cs`
```csharp
using Microsoft.Data.Sqlite;
using Assetra.Core.Interfaces;

namespace Assetra.Infrastructure.Persistence;

public sealed class StockraImportService : IStockraImportService
{
    private readonly string _targetDbPath;

    // 表名：與 Stockra 的 schema 完全相同——直接透過 ATTACH + INSERT SELECT 複製
    private static readonly IReadOnlyList<string> CopyableTables = new[]
    {
        "portfolio",
        "trade",
        "asset_group",
        "asset",
        "asset_event",
        "portfolio_snapshot",
        "portfolio_position_log",
        "alert",
    };

    public StockraImportService(string targetDbPath)
    {
        _targetDbPath = targetDbPath;
    }

    public async Task<ImportResult> ImportAsync(string stockraDbPath, CancellationToken ct = default)
    {
        if (!File.Exists(stockraDbPath))
            return new ImportResult(0, new Dictionary<string, int>());

        var perTable = new Dictionary<string, int>();

        await using var conn = new SqliteConnection($"Data Source={_targetDbPath}");
        await conn.OpenAsync(ct);

        // ATTACH 來源 DB
        await using (var attach = conn.CreateCommand())
        {
            attach.CommandText = $"ATTACH DATABASE @src AS src;";
            attach.Parameters.AddWithValue("@src", stockraDbPath);
            await attach.ExecuteNonQueryAsync(ct);
        }

        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);

        try
        {
            foreach (var table in CopyableTables)
            {
                if (!await TableExistsAsync(conn, tx, "src", table, ct))
                    continue;  // source lacks this table—skip
                if (!await TableExistsAsync(conn, tx, "main", table, ct))
                    continue;  // target lacks this table—skip (schema mismatch)
                if (await HasRowsAsync(conn, tx, "main", table, ct))
                {
                    perTable[table] = 0;  // target already has data—skip
                    continue;
                }

                var rows = await CopyTableAsync(conn, tx, table, ct);
                perTable[table] = rows;
            }

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }

        await using (var detach = conn.CreateCommand())
        {
            detach.CommandText = "DETACH DATABASE src;";
            await detach.ExecuteNonQueryAsync(ct);
        }

        return new ImportResult(perTable.Values.Sum(), perTable);
    }

    private static async Task<bool> TableExistsAsync(
        SqliteConnection conn, SqliteTransaction tx, string schema, string table, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"SELECT COUNT(*) FROM {schema}.sqlite_master WHERE type='table' AND name=@t;";
        cmd.Parameters.AddWithValue("@t", table);
        var count = (long)(await cmd.ExecuteScalarAsync(ct) ?? 0L);
        return count > 0;
    }

    private static async Task<bool> HasRowsAsync(
        SqliteConnection conn, SqliteTransaction tx, string schema, string table, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"SELECT COUNT(*) FROM {schema}.{table};";
        var count = (long)(await cmd.ExecuteScalarAsync(ct) ?? 0L);
        return count > 0;
    }

    private static async Task<int> CopyTableAsync(
        SqliteConnection conn, SqliteTransaction tx, string table, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"INSERT INTO main.{table} SELECT * FROM src.{table};";
        return await cmd.ExecuteNonQueryAsync(ct);
    }
}
```

- [ ] **Step 2: 執行測試**

```bash
cd "D:/Workspaces/Finances/Assetra"
dotnet test Assetra.Tests/Assetra.Tests.csproj --filter FullyQualifiedName~StockraImportServiceTests
```

Expected: **PASS**（至少第一個測試通過）。

- [ ] **Step 3: 執行全部三個測試確認全數通過**

```bash
cd "D:/Workspaces/Finances/Assetra"
dotnet test Assetra.Tests/Assetra.Tests.csproj --filter FullyQualifiedName~StockraImportServiceTests
```

Expected: `Passed: 3`

- [ ] **Step 4: Commit**

```bash
cd "D:/Workspaces/Finances/Assetra"
git add Assetra.Core/Interfaces/IStockraImportService.cs \
        Assetra.Infrastructure/Persistence/StockraImportService.cs \
        Assetra.Tests/Infrastructure/StockraImportServiceTests.cs
git commit -m "feat(infra): add StockraImportService with ATTACH DATABASE strategy"
```

---

## Phase 4 — WPF Shell + Infrastructure

目標：複製 `Stockra.WPF` 的 Shell／Infrastructure／Controls／Themes，但先不帶 Features。

### Task 4.1: 建立 `Assetra.WPF.csproj`

**Files:**
- Create: `Assetra.WPF/Assetra.WPF.csproj`

- [ ] **Step 1: 複製 Stockra.WPF.csproj 為範本**

Run:
```bash
cp "D:/Workspaces/Finances/Stockra/Stockra.WPF/Stockra.WPF.csproj" \
   "D:/Workspaces/Finances/Assetra/Assetra.WPF/Assetra.WPF.csproj"
```

- [ ] **Step 2: 修改**

File: `D:/Workspaces/Finances/Assetra/Assetra.WPF/Assetra.WPF.csproj`

- `<AssemblyName>` / `<RootNamespace>` 改 `Assetra.WPF`
- 修正 ProjectReference 路徑指向 Assetra 兄弟專案
- `<ApplicationIcon>` 改 `Assets\\assetra.ico`（先 placeholder，後面替換）
- `<ApplicationDefinition>` 確認為 `App.xaml`

---

### Task 4.2: 複製 Shell 相關檔案

**Files:**
- Copy: `App.xaml`, `App.xaml.cs`, `AssemblyInfo.cs`, `Package.appxmanifest`, `stockra.ico`
- Copy: `Shell/*.xaml` 與 `.cs`

- [ ] **Step 1: 複製頂層 WPF 檔**

```bash
cd "D:/Workspaces/Finances/Assetra/Assetra.WPF"
cp "D:/Workspaces/Finances/Stockra/Stockra.WPF/App.xaml" .
cp "D:/Workspaces/Finances/Stockra/Stockra.WPF/App.xaml.cs" .
cp "D:/Workspaces/Finances/Stockra/Stockra.WPF/AssemblyInfo.cs" .
cp "D:/Workspaces/Finances/Stockra/Stockra.WPF/Package.appxmanifest" .
mkdir -p Assets
cp "D:/Workspaces/Finances/Stockra/Stockra.WPF/stockra.ico" Assets/assetra.ico
```

- [ ] **Step 2: 複製 Shell/**

```bash
cd "D:/Workspaces/Finances/Assetra/Assetra.WPF"
cp -r "D:/Workspaces/Finances/Stockra/Stockra.WPF/Shell" .
```

- [ ] **Step 3: 全域 namespace rename**

```bash
cd "D:/Workspaces/Finances/Assetra/Assetra.WPF"
find . -name "*.cs" -exec perl -i -pe 's/namespace Stockra\./namespace Assetra./g; s/using Stockra\./using Assetra./g' {} \;
find . -name "*.xaml" -exec perl -i -pe 's/clr-namespace:Stockra\./clr-namespace:Assetra./g; s/xmlns:(\w+)="clr-namespace:Stockra/xmlns:$1="clr-namespace:Assetra/g' {} \;
```

- [ ] **Step 4: 改寫 `NavSection.cs`**

File: `D:/Workspaces/Finances/Assetra/Assetra.WPF/Shell/NavSection.cs`
```csharp
namespace Assetra.WPF.Shell;

public enum NavSection
{
    Portfolio,
    FinancialOverview,
    Alerts,
    Settings,
}
```

- [ ] **Step 5: 修改 `AssemblyInfo.cs`**

File: `D:/Workspaces/Finances/Assetra/Assetra.WPF/AssemblyInfo.cs`

確保：
```csharp
[assembly: AssemblyTitle("Assetra")]
[assembly: AssemblyProduct("Assetra")]
```

- [ ] **Step 6: 修改 `Package.appxmanifest`**

File: `D:/Workspaces/Finances/Assetra/Assetra.WPF/Package.appxmanifest`

`<DisplayName>` 改 `Assetra`；若有 `Publisher`、`Identity Name` 也一併改。

---

### Task 4.3: 複製 Controls/, Themes/, Languages/

- [ ] **Step 1: 複製資料夾**

```bash
cd "D:/Workspaces/Finances/Assetra/Assetra.WPF"
cp -r "D:/Workspaces/Finances/Stockra/Stockra.WPF/Controls" .
cp -r "D:/Workspaces/Finances/Stockra/Stockra.WPF/Themes" .
cp -r "D:/Workspaces/Finances/Stockra/Stockra.WPF/Languages" .
```

- [ ] **Step 2: namespace rename（XAML + C#）**

```bash
cd "D:/Workspaces/Finances/Assetra/Assetra.WPF"
find Controls Themes -name "*.cs" -exec perl -i -pe 's/namespace Stockra\./namespace Assetra./g; s/using Stockra\./using Assetra./g' {} \;
find Controls Themes Languages -name "*.xaml" -exec perl -i -pe 's/clr-namespace:Stockra\./clr-namespace:Assetra./g' {} \;
```

---

### Task 4.4: 複製 WPF/Infrastructure/（含裁剪）

- [ ] **Step 1: 複製整個資料夾**

```bash
cd "D:/Workspaces/Finances/Assetra/Assetra.WPF"
cp -r "D:/Workspaces/Finances/Stockra/Stockra.WPF/Infrastructure" .
```

- [ ] **Step 2: namespace rename**

```bash
cd "D:/Workspaces/Finances/Assetra/Assetra.WPF/Infrastructure"
find . -name "*.cs" -exec perl -i -pe 's/namespace Stockra\./namespace Assetra./g; s/using Stockra\./using Assetra./g' {} \;
find . -name "*.xaml" -exec perl -i -pe 's/clr-namespace:Stockra\./clr-namespace:Assetra./g' {} \;
```

- [ ] **Step 3: 修改 `DbInitializerService.cs` DB 路徑**

File: `D:/Workspaces/Finances/Assetra/Assetra.WPF/Infrastructure/DbInitializerService.cs`

將所有「Stockra」字串在 SpecialFolder 路徑中改為「Assetra」：
```csharp
var dataDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "Assetra");  // 原本是 "Stockra"
```

同時新增 StockraImportService 的呼叫（首次 DB 建立後）：
```csharp
public async Task InitializeAsync()
{
    Directory.CreateDirectory(_dataDir);
    await DbMigrator.ApplyPragmasAsync(_dbPath);
    // 讓各 Repo 自行 EnsureTable…
    // （此部分由 DI 自動觸發，因為 Repo 建構子會呼叫 EnsureTable）

    // 首次啟動後嘗試從 Stockra 匯入
    await TryImportFromStockraAsync();
}

private async Task TryImportFromStockraAsync()
{
    var stockraDb = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Stockra",
        "stockra.db");
    if (!File.Exists(stockraDb)) return;

    try
    {
        var result = await _importService.ImportAsync(stockraDb);
        if (result.TotalRows > 0)
            _snackbar.ShowInfo($"已從 Stockra 匯入 {result.TotalRows} 筆資料");
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "Stockra import failed");
        _snackbar.ShowWarning("從 Stockra 匯入失敗，請查看 log");
    }
}
```

注意建構子需新增 `IStockraImportService` 與 `ISnackbarService` 的 DI 參數。

- [ ] **Step 4: 重寫 `AppBootstrapper.cs`**

File: `D:/Workspaces/Finances/Assetra/Assetra.WPF/Infrastructure/AppBootstrapper.cs`

從 Stockra 版本逐一檢查 DI 註冊項，**刪除**以下：
- `IAiAnalysisService`、`ILlmProvider`、OpenAI／Anthropic client registration
- `INewsService`、`IFlashNewsService`、`ITextTranslator`、各家 news/flash client
- `ICustomStrategyRepository`、`ICustomStrategyEvaluator`、Roslyn 相關
- `IScreenerPresetRepository`、`IResearchTemplateRepository`
- `IWatchlistRepository`、`WatchlistSqliteRepository`
- 對應 ViewModel（Analysis/Detail/News/Flash/AiChat/Strategy/Watchlist/Screener）

**保留 + 新增**：
- 新增：`IStockraImportService` → `StockraImportService`（transient，傳入 target db path）
- `StockScheduler` 建構子參數少一個 `IWatchlistRepository`（DI 會自動解析）

- [ ] **Step 5: 編譯（預期有 error：Features 未複製）**

```bash
cd "D:/Workspaces/Finances/Assetra"
dotnet build Assetra.WPF/Assetra.WPF.csproj
```

Expected: 大量 error——MainViewModel、NavRailViewModel 引用了尚未複製的 Feature ViewModels。保留 error，進 Phase 5。

---

## Phase 5 — WPF Features Migration

目標：複製 7 個保留 Features 到 Assetra.WPF。

### Task 5.1: 複製 7 個保留 Features

- [ ] **Step 1: 複製**

```bash
cd "D:/Workspaces/Finances/Assetra/Assetra.WPF"
mkdir -p Features
for f in Portfolio Allocation AddStock Alerts Settings Snackbar StatusBar; do
    cp -r "D:/Workspaces/Finances/Stockra/Stockra.WPF/Features/${f}" Features/
done
```

- [ ] **Step 2: namespace rename**

```bash
cd "D:/Workspaces/Finances/Assetra/Assetra.WPF/Features"
find . -name "*.cs" -exec perl -i -pe 's/namespace Stockra\./namespace Assetra./g; s/using Stockra\./using Assetra./g' {} \;
find . -name "*.xaml" -exec perl -i -pe 's/clr-namespace:Stockra\./clr-namespace:Assetra./g' {} \;
```

---

### Task 5.2: 精簡 Settings Feature

**Files:**
- Modify: `Features/Settings/SettingsView.xaml` + `SettingsViewModel.cs`

- [ ] **Step 1: 移除 AI／FinMind Token UI**

File: `D:/Workspaces/Finances/Assetra/Assetra.WPF/Features/Settings/SettingsView.xaml`

刪除：
- 任何 `<TextBox Text="{Binding OpenAiKey}"/>` 類欄位
- FinMind Token 欄位
- AI Provider 選單
- Strategy 庫 UI
- Screener 預設 UI

File: `D:/Workspaces/Finances/Assetra/Assetra.WPF/Features/Settings/SettingsViewModel.cs`

刪除對應的 `[ObservableProperty]` 與 command。

保留：
- 主題切換
- 語言切換
- 主要貨幣
- 手續費折扣預設
- Taiwan-style 切換

---

### Task 5.3: 修正 MainViewModel／NavRailViewModel

**Files:**
- Modify: `Shell/MainViewModel.cs`, `Shell/NavRailViewModel.cs`

- [ ] **Step 1: 修 MainViewModel**

File: `D:/Workspaces/Finances/Assetra/Assetra.WPF/Shell/MainViewModel.cs`

- 刪除所有 references 到不存在的 VM（`WatchlistViewModel`、`AnalysisViewModel`、`AiChatViewModel`、`NewsViewModel`、`FlashViewModel`、`StrategyViewModel`、`MarketInfoViewModel`、`DetailViewModel`、`DashboardViewModel`）
- 只保留 `PortfolioViewModel`、`AllocationViewModel` / `FinancialOverviewViewModel`、`AlertsViewModel`、`SettingsViewModel`
- `SwitchTo(NavSection)` 方法對應到上述四個 VM

- [ ] **Step 2: 修 NavRailViewModel**

File: `D:/Workspaces/Finances/Assetra/Assetra.WPF/Shell/NavRailViewModel.cs`

- 刪除所有 Watchlist/Screener/Strategy/News/Alerts 以外的 NavSection 按鈕屬性（保留 Alerts）
- 按鈕 collection 只有 4 個：Portfolio、FinancialOverview、Alerts、Settings

- [ ] **Step 3: 修 NavRailView.xaml**

File: `D:/Workspaces/Finances/Assetra/Assetra.WPF/Shell/NavRailView.xaml`

刪掉不存在的按鈕 XAML；圖示 / 語言 key 對應到 4 節。

---

### Task 5.4: 編譯 WPF

- [ ] **Step 1: 編譯**

```bash
cd "D:/Workspaces/Finances/Assetra"
dotnet build Assetra.WPF/Assetra.WPF.csproj
```

- [ ] **Step 2: 反覆修正 error 直到編譯成功**

常見 error：
- 某個 XAML 引用 `clr-namespace:Assetra.WPF.Features.Dashboard`（已刪）→ 移除 XAML 參照
- 某個 converter／behavior 殘留 Stockra 命名 → 補 rename
- DI 註冊找不到某介面對應的實作 → 檢查是否誤刪

- [ ] **Step 3: Commit**

```bash
cd "D:/Workspaces/Finances/Assetra"
git add Assetra.WPF
git commit -m "feat(wpf): fork Assetra.WPF with trimmed shell and features"
```

---

## Phase 6 — Languages Cleanup

目標：從 `zh-TW.xaml` 和 `en-US.xaml` 刪除未用的 Key（AiChat / News / Flash / Strategy / Market / Analysis / Detail / Watchlist / Screener）。

### Task 6.1: 清理 `zh-TW.xaml`

**Files:**
- Modify: `Assetra.WPF/Languages/zh-TW.xaml`

- [ ] **Step 1: 識別未用 Key**

```bash
cd "D:/Workspaces/Finances/Assetra/Assetra.WPF"
grep -oE 'x:Key="[A-Za-z.]+"' Languages/zh-TW.xaml | sort -u > /tmp/all_keys.txt
grep -roE 'DynamicResource\s+[A-Za-z.]+' Features Shell Infrastructure Controls Themes | grep -oE '[A-Za-z]+\.[A-Za-z.]+' | sort -u > /tmp/used_keys.txt
comm -23 /tmp/all_keys.txt /tmp/used_keys.txt > /tmp/unused_keys.txt
cat /tmp/unused_keys.txt
```

- [ ] **Step 2: 刪除未用 Key**

File: `D:/Workspaces/Finances/Assetra/Assetra.WPF/Languages/zh-TW.xaml`

針對 `unused_keys.txt` 列出的 Key，逐一從 XAML 中刪除 `<sys:String x:Key="...">...</sys:String>` 整行。

重點刪除 prefix（以 `.` 開頭）：
- `AiChat.*`, `Ai.*`
- `News.*`, `NewsItem.*`
- `Flash.*`
- `Strategy.*`, `Screener.*`
- `Market.*`, `MarketInfo.*`
- `Analysis.*`
- `Detail.*`
- `Watchlist.*`

- [ ] **Step 3: 若有 prefix 被誤刪（被 4 個保留 feature 使用），還原**

---

### Task 6.2: 清理 `en-US.xaml`

**Files:**
- Modify: `Assetra.WPF/Languages/en-US.xaml`

同步刪除 zh-TW 已刪的 Key。

- [ ] **Step 1: 刪除**
- [ ] **Step 2: 編譯驗證**

```bash
cd "D:/Workspaces/Finances/Assetra"
dotnet build Assetra.WPF/Assetra.WPF.csproj
```

Expected: PASS（若某個 DynamicResource 用到被刪的 Key，要還原）。

- [ ] **Step 3: Commit**

```bash
cd "D:/Workspaces/Finances/Assetra"
git add Assetra.WPF/Languages
git commit -m "chore(wpf): remove unused language keys after feature trim"
```

---

## Phase 7 — Branding

### Task 7.1: App Icon

**Files:**
- Replace: `Assetra.WPF/Assets/assetra.ico`

- [ ] **Step 1: 準備新 ico**

方向：資產管理主題（錢包、圓餅圖、保險箱、資產符號組合）。

初版可用 placeholder（例如免費商用圖示網站下載後轉 ico）；正式 logo 後續另排。

替換：
```bash
# 假設已下載到 /tmp/assetra-new.ico
cp /tmp/assetra-new.ico "D:/Workspaces/Finances/Assetra/Assetra.WPF/Assets/assetra.ico"
```

- [ ] **Step 2: 驗證**

```bash
cd "D:/Workspaces/Finances/Assetra"
dotnet build Assetra.WPF/Assetra.WPF.csproj
```

Expected: Icon 嵌入 exe 成功。

---

### Task 7.2: SplashScreen Logo / Title

**Files:**
- Modify: `Assetra.WPF/Shell/SplashScreen.xaml`

- [ ] **Step 1: 改 Splash 內顯示文字為 "Assetra"**

若有 logo 圖片，也替換為 Assetra 版本；初版可保留純文字。

- [ ] **Step 2: Commit**

```bash
cd "D:/Workspaces/Finances/Assetra"
git add Assetra.WPF/Assets Assetra.WPF/Shell/SplashScreen.xaml
git commit -m "chore(wpf): add Assetra branding (icon + splash)"
```

---

## Phase 8 — Tests Migration

目標：複製相容的單元測試；刪除對應 Watchlist/Strategy/Screener/AI 等已砍模組的測試。

### Task 8.1: 選擇性複製測試

- [ ] **Step 1: 複製測試資料夾結構**

```bash
cd "D:/Workspaces/Finances/Assetra/Assetra.Tests"
mkdir -p Core Infrastructure Shell WPF
```

- [ ] **Step 2: 逐資料夾評估後複製**

Run（範例：複製 Core 層測試）：
```bash
# 複製後再刪除與已砍 feature 相關的測試檔
cp -r "D:/Workspaces/Finances/Stockra/Stockra.Tests/Core/." "D:/Workspaces/Finances/Assetra/Assetra.Tests/Core/"
cp -r "D:/Workspaces/Finances/Stockra/Stockra.Tests/Infrastructure/." "D:/Workspaces/Finances/Assetra/Assetra.Tests/Infrastructure/"
cp -r "D:/Workspaces/Finances/Stockra/Stockra.Tests/Shell/." "D:/Workspaces/Finances/Assetra/Assetra.Tests/Shell/"
cp -r "D:/Workspaces/Finances/Stockra/Stockra.Tests/WPF/." "D:/Workspaces/Finances/Assetra/Assetra.Tests/WPF/"

# 刪除與 Watchlist/Strategy/Screener/News/AI 相關測試
find "D:/Workspaces/Finances/Assetra/Assetra.Tests" \( -name "*Watchlist*" -o -name "*Strategy*" -o -name "*Screener*" -o -name "*News*" -o -name "*Flash*" -o -name "*Ai*" -o -name "*Analysis*" -o -name "*MarketInfo*" \) -delete
```

- [ ] **Step 3: namespace rename**

```bash
cd "D:/Workspaces/Finances/Assetra/Assetra.Tests"
find . -name "*.cs" -exec perl -i -pe 's/namespace Stockra\./namespace Assetra./g; s/using Stockra\./using Assetra./g' {} \;
```

- [ ] **Step 4: 執行測試**

```bash
cd "D:/Workspaces/Finances/Assetra"
dotnet test Assetra.Tests/Assetra.Tests.csproj
```

- [ ] **Step 5: 修正失敗的測試**

測試失敗常見原因：
- 引用了已刪除的類型（如 `CustomStrategy`） → 刪除此測試檔
- StockScheduler 建構子改變 → 測試傳入參數要對齊
- 其他 refactor 因素

---

### Task 8.2: 全測試通過後 Commit

- [ ] **Step 1: 所有測試綠燈**

```bash
cd "D:/Workspaces/Finances/Assetra"
dotnet test Assetra.Tests/Assetra.Tests.csproj
```

Expected: `Passed! - Failed: 0`

- [ ] **Step 2: Commit**

```bash
cd "D:/Workspaces/Finances/Assetra"
git add Assetra.Tests
git commit -m "test: port compatible tests from Stockra.Tests"
```

---

## Phase 9 — Smoke Test + Manual QA

目標：手動執行 Assetra.WPF，確認功能正常。

### Task 9.1: 首次啟動測試（無 Stockra DB）

- [ ] **Step 1: 刪除 `%APPDATA%\Assetra\` 模擬全新安裝**

```bash
rm -rf "$APPDATA/Assetra"   # git bash
```

- [ ] **Step 2: 執行 Assetra.WPF**

```bash
cd "D:/Workspaces/Finances/Assetra"
dotnet run --project Assetra.WPF
```

- [ ] **Step 3: 檢查清單**

- [ ] 啟動 Splash 顯示 "Assetra"
- [ ] 主視窗標題 "Assetra"
- [ ] NavRail 只有 4 個按鈕（Portfolio／配置分析／警示／設定）
- [ ] Portfolio 頁面進入預設 Dashboard tab，各個 tab 可切換
- [ ] Allocation 頁面可顯示 Treemap（若有持倉；若無持倉應顯示空狀態）
- [ ] Settings 不見 AI／FinMind／策略相關欄位
- [ ] 關閉 App，`%APPDATA%\Assetra\assetra.db` 存在

---

### Task 9.2: 資料匯入測試（有 Stockra DB）

- [ ] **Step 1: 複製 Stockra DB 模擬匯入場景**

```bash
cp "$APPDATA/Stockra/stockra.db" "$APPDATA/Stockra.backup.db"  # 備份
# 保留 $APPDATA/Stockra/stockra.db，不動
rm -rf "$APPDATA/Assetra"  # 模擬首次啟動
```

- [ ] **Step 2: 執行 Assetra.WPF**

```bash
cd "D:/Workspaces/Finances/Assetra"
dotnet run --project Assetra.WPF
```

- [ ] **Step 3: 檢查清單**

- [ ] 啟動後 Snackbar 顯示「已從 Stockra 匯入 N 筆資料」
- [ ] Portfolio 頁面能看到原本 Stockra 的持倉
- [ ] 交易記錄完整
- [ ] 帳戶（現金）與負債分類正確
- [ ] 警示規則有帶過來
- [ ] 盤中時段 10 秒一次自動更新報價（市值跳動）

---

### Task 9.3: 回歸測試 Stockra

- [ ] **Step 1: 啟動 Stockra，確認仍正常**

```bash
cd "D:/Workspaces/Finances/Stockra"
dotnet run --project Stockra.WPF
```

Expected: Stockra 正常啟動，資料完整（共用 `%APPDATA%\Stockra\stockra.db`，Assetra 不會動到）。

- [ ] **Step 2: 關閉 Stockra 與 Assetra，確認兩 DB 互不干擾**

```bash
ls "$APPDATA/Stockra/"   # 有 stockra.db
ls "$APPDATA/Assetra/"   # 有 assetra.db
```

---

## Phase 10 — GitHub Repo + Initial Push

目標：建立遠端 repo 並首次推送所有 commit。

### Task 10.1: 建立 GitHub repo

- [ ] **Step 1: 確認本地有可推送的 commit 歷史**

```bash
cd "D:/Workspaces/Finances/Assetra"
git log --oneline | head -20
```

Expected: 看到 Phase 0～9 的 commits。

- [ ] **Step 2: 建立 GitHub repo**

```bash
cd "D:/Workspaces/Finances/Assetra"
gh repo create Assetra --private --source=. --remote=origin --description "Asset management WPF app (forked from Stockra)"
```

- [ ] **Step 3: 首次推送**

```bash
git branch -M master
git push -u origin master
```

- [ ] **Step 4: 確認遠端**

```bash
gh repo view --web
```

Expected: 在瀏覽器開啟 GitHub repo 頁面。

---

### Task 10.2: 打第一個 tag

- [ ] **Step 1: 建立 v0.1.0 tag**

```bash
cd "D:/Workspaces/Finances/Assetra"
git tag v0.1.0
git push origin v0.1.0
```

MinVer 會自動把 `v0.1.0` 設為 AssemblyVersion。

---

### Task 10.3: 設計文件位置確認

設計文件在本 plan 開始前已放入 Assetra 目錄：
- `D:/Workspaces/Finances/Assetra/docs/superpowers/specs/2026-04-20-assetra-fork-design.md`
- `D:/Workspaces/Finances/Assetra/docs/superpowers/plans/2026-04-20-assetra-fork.md`

並於 Phase 0 Task 0.2 Step 5 隨著其他設定檔一起 commit 進入 Assetra repo 歷史（`git add docs/` + commit）。

- [ ] **Step 1: 驗證遠端已有 docs/**

```bash
cd "D:/Workspaces/Finances/Assetra"
git log --oneline --stat -- docs/
```

Expected: 看到 Phase 0 的 commit 包含兩份設計文件。

---

## Acceptance Criteria

- [ ] `D:\Workspaces\Finances\Assetra\` 目錄存在且 `dotnet build Assetra.slnx` 成功。
- [ ] `dotnet test Assetra.Tests/Assetra.Tests.csproj` 全綠。
- [ ] `Assetra.WPF.exe` 可啟動，主視窗顯示 "Assetra"。
- [ ] NavRail 只有 4 個按鈕：Portfolio／FinancialOverview／Alerts／Settings。
- [ ] 首次啟動若有 Stockra DB → 自動匯入並 Snackbar 提示。
- [ ] 盤中 10 秒一次自動報價更新（持倉市值跳動）。
- [ ] Stockra 仍可正常執行，兩 App DB 互不干擾。
- [ ] Assetra GitHub repo 建立完成，`master` 已推送，tag `v0.1.0` 已打。

---

## Notes / Risks

### Namespace rename 可能遺漏處

- `.xaml.cs` code-behind 的 `x:Class` attribute
- `AssemblyInfo.cs` 的 `[assembly: InternalsVisibleTo("Stockra.Tests")]` → 改 `Assetra.Tests`
- `Package.appxmanifest` 的 assembly identity
- 字串常數（log 訊息、Snackbar 文字）若有寫死 "Stockra"

每次 `dotnet build` 出錯的 error 都要掃一遍，根治殘留替換。

### SQLite ATTACH DATABASE 的限制

- 兩個 DB 若同時被其他 process 開啟（Stockra 正在跑）可能有 locking 問題 → import 時確保 Stockra 已關閉，或用 `PRAGMA locking_mode=NORMAL` + retry
- 若日後 Stockra schema 加欄位導致 Assetra schema 落後，INSERT SELECT 會失敗 → `ImportAsync` 內用 try/catch 單表 rollback，不整批失敗

### 未來演進

- Stockra schema 變動後，Assetra 要手動同步（Fork 策略的代價）
- `assetra.ico` 初版是 placeholder，正式 logo 設計另排
- MSI 安裝檔製作、CI／CD、auto-update 都是後續 item，不在本 plan 範圍

---

**End of plan.**
