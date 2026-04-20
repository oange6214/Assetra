# Assetra — Fork From Stockra Design

**Date:** 2026-04-20
**Scope:** 從 Stockra 分家出獨立的資產管理應用程式 Assetra

---

## Background

Stockra 目前是一個「股票分析 + 資產管理」的整合型 WPF 應用程式，混合了即時報價、選股、AI 分析、新聞、策略編寫等分析功能，以及投資組合、現金／負債帳戶、交易記錄、資產配置分析等管理功能。

使用者希望把「資產管理」這條線抽出成獨立產品 **Assetra**，專注於「投資 + 現金 + 負債」的整體財務管理，保留盤中即時報價但移除所有股票分析類模組。Stockra 繼續作為「分析版」。

兩者為兄弟產品，共用領域模型與資料層的設計，但以 Fork 方式獨立演化，資料庫各自獨立。

---

## Decisions

| 議題 | 決定 |
|------|------|
| **新 App 名稱** | Assetra |
| **定位** | 盤中即時 + 完整資產管理（含 Rx QuoteStream） |
| **位置** | `D:\Workspaces\Finances\Assetra\`（與 Stockra 並列） |
| **資料庫** | 獨立，`%APPDATA%\Assetra\assetra.db` |
| **程式碼共用策略** | Fork（完整複製 + 裁剪，兩邊獨立維護） |
| **NavRail 結構** | 4 節：Portfolio／FinancialOverview／Alerts／Settings |
| **語言檔** | zh-TW + en-US（與 Stockra 一致，無 zh-CN） |
| **App Icon** | 另外準備新 ico，主題與資產管理相關 |
| **資料遷移** | 首次啟動自動匯入相容表，Snackbar 提示匯入筆數 |

---

## Section 1 — Solution 結構

```
D:\Workspaces\Finances\Assetra\
├── Assetra.slnx
├── Directory.Build.props          # 複製自 Stockra；MinVer 設定保留
├── .editorconfig                  # 複製
├── CLAUDE.md                      # Assetra 專屬快速指引
├── README.md
│
├── Assetra.Core\
│   ├── Models\                    # 21 個 record（見 § 2）
│   ├── Interfaces\                # 17 個介面（見 § 2）
│   └── Trading\                   # TaiwanTradeFee / TaiwanTradeFeeCalculator
│
├── Assetra.Infrastructure\
│   ├── Http\                      # 6 個檔案（TWSE／TPEX／CoinGecko）
│   ├── Persistence\               # 12 個檔案（SQLite Repo + Migrator）
│   ├── Scheduling\                # StockScheduler
│   ├── Search\                    # IStockSearchService 實作
│   ├── History\                   # IStockHistoryProvider 實作
│   ├── FinMind\                   # 精簡：僅留收盤價查詢
│   ├── Chart\                     # Portfolio 歷史走勢圖
│   ├── BalanceQueryService.cs
│   ├── PositionQueryService.cs
│   ├── TransactionService.cs
│   └── CurrencyService.cs
│
├── Assetra.WPF\
│   ├── App.xaml / App.xaml.cs
│   ├── AssemblyInfo.cs
│   ├── Package.appxmanifest       # DisplayName 改 Assetra
│   ├── Shell\                     # MainWindow / NavRail / SplashScreen
│   ├── Features\                  # 7 個模組（見 § 4）
│   ├── Infrastructure\            # DI + Theme + Localization
│   ├── Controls\                  # 5 個通用元件
│   ├── Themes\                    # Dark / Light / GlobalStyles
│   ├── Languages\                 # zh-TW / en-US
│   └── Assets\
│       └── assetra.ico            # 新設計
│
└── Assetra.Tests\
    ├── Core\
    ├── Infrastructure\
    └── WPF\
```

**namespace 全域替換**：`Stockra.*` → `Assetra.*`（建議以 `dotnet format` + sed 搭配批次處理，或 IDE rename symbol）。

**DB 路徑**：利用 `Environment.SpecialFolder.ApplicationData` + `"Assetra"` 子目錄，自動與 Stockra 隔離。

---

## Section 2 — Core 層裁剪清單

### Models：34 → 20 保留

| 分類 | 保留（20） | 刪除（14） |
|------|-----------|-----------|
| **資產核心** | `AssetItem` `AssetGroup` `AssetType` `AssetEvent` `AssetEventType` `FinancialType` | — |
| **投組** | `PortfolioEntry` `PortfolioDailySnapshot` `PortfolioPositionLog` `PositionSnapshot` | — |
| **交易** | `Trade` `TradeType` | — |
| **報價／圖** | `StockQuote` `StockSearchResult` `OhlcvPoint` `ChartData` `ChartPeriod` | — |
| **警示** | `AlertRule` | — |
| **共用** | `AppSettings` `Result` | — |
| **AI／新聞／快訊** | — | `ChatMessage` `NewsItem` `FlashNewsItem` `FlashSeverity` |
| **選股／策略／研究** | — | `CustomStrategy` `EvaluationContext` `StrategyParseResult` `ScreenerPreset` `ResearchTemplate` |
| **大盤／籌碼** | — | `MarketData` `InstitutionalData` `MarginData` `FinancialStatement` |
| **觀察名單** | — | `WatchlistEntry` |

### Interfaces：29 → 17 保留

| 分類 | 保留（17） | 刪除（12） |
|------|-----------|-----------|
| **Repository** | `IAssetRepository` `IPortfolioRepository` `IPortfolioSnapshotRepository` `IPortfolioPositionLogRepository` `ITradeRepository` `IAlertRepository` | `IWatchlistRepository` `ICustomStrategyRepository` `IScreenerPresetRepository` `IResearchTemplateRepository` |
| **Service** | `IStockService` `IStockSearchService` `IStockHistoryProvider` `IBalanceQueryService` `IPositionQueryService` `ITransactionService` `ICurrencyService` `ICryptoService` `IFinMindService` | `IMarketService` `IStockChartService` |
| **基礎設施** | `IAppSettingsService` `ILocalizationService` | `ITextTranslator` |
| **AI** | — | `IAiAnalysisService` `ILlmProvider` |
| **新聞／快訊** | — | `INewsService` `IFlashNewsService` |
| **策略評估** | — | `ICustomStrategyEvaluator` |

### 其他

| 資料夾 | 處置 |
|--------|------|
| `Core/Trading/` | ✅ 全保留（手續費／證交稅計算） |
| `Core/Ai/` | ❌ 全刪（`IAgentTool`, `LlmModels`, `OrchestratorChunk`） |

### 特別處理

- `AppSettings` record 拔掉 AI／FinMind Token 相關欄位；保留主題／語言／匯率／手續費折扣／TargetAllocations。
- `IFinMindService` 介面精簡：只保留「查詢指定日期收盤價」的方法，刪除法人／融資／財報相關方法。

---

## Section 3 — Infrastructure 層裁剪清單

### 子資料夾處置

| 子資料夾 | 處置 | 備註 |
|---------|------|------|
| `Ai/` | ❌ 全刪 | LLM providers, agent tools |
| `Chart/` | ✅ 保留 | Portfolio 歷史走勢圖 |
| `FinMind/` | ⚠️ 精簡 | 只留收盤價查詢 |
| `History/` | ✅ 保留 | `IStockHistoryProvider` 實作 |
| `Http/` | ⚠️ 精簡 | 只留 TWSE／TPEX／CoinGecko |
| `Persistence/` | ⚠️ 精簡 | 刪 4 個 Repo + 改寫 DbMigrator |
| `Scheduling/` | ✅ 保留 | `StockScheduler`（小改） |
| `Search/` | ✅ 保留 | 離線代號索引 |
| `Strategy/` | ❌ 全刪 | Roslyn 策略編譯 |
| `Translators/` | ❌ 全刪 | 新聞翻譯 |

### `Http/` 詳細

| 保留（6） | 刪除（12） |
|----------|-----------|
| `TwseClient` / `ITwseClient` | `CailianFlashService` |
| `TpexClient` / `ITpexClient` | `CnyesFlashService` |
| `CoinGeckoService`（`ICryptoService`） | `CompositeFlashNewsService` / `CompositeNewsService` |
| `MisParsing.cs` | `GoogleNewsHelper` / `GoogleNewsRssService` |
| `TwseMisModels.cs` | `ITaifexClient` / `TaifexClient` |
| | `Jin10FlashService` / `ReutersFlashService` |
| | `RssNewsService` / `YahooRssNewsService` |

### `Persistence/` 詳細

| 保留（12） | 刪除（4） |
|----------|-----------|
| `AlertSqliteRepository` | `CustomStrategySqliteRepository` |
| `AppSettingsService` | `ResearchTemplateSqliteRepository` |
| `AssetSqliteRepository` | `ScreenerPresetSqliteRepository` |
| `PortfolioSqliteRepository` | `WatchlistSqliteRepository` |
| `PortfolioBackfillService` | |
| `PortfolioPositionLogSqliteRepository` | |
| `PortfolioSnapshotService` / `PortfolioSnapshotSqliteRepository` | |
| `TradeSqliteRepository` | |
| `TransferPairMigrationService` | |
| `DbMigrator`（**重寫**為 Assetra v1 一次建完） | |
| `SqliteSchemaHelper` | |

### 需要的小修改

1. **`StockScheduler.FetchAllAsync`** — 移除 `IWatchlistRepository` 依賴與合併邏輯，只掃 portfolio entries。
2. **`DbMigrator`** — 重寫為「v1 一次建齊所有表」，Assetra 自己累積未來的遷移。
3. **`AppSettingsService`** — 讀寫路徑改 `%APPDATA%\Assetra\settings.json`；`AppSettings` 實體拔掉 AI／FinMind Token 欄位。
4. **`IFinMindService`** — 精簡介面。
5. **DI Bootstrapper 重寫** — 不再註冊 LLM／News／Flash／Strategy／Watchlist 相關服務。

---

## Section 4 — WPF 層

### Shell 層

| 檔案 | 處置 |
|------|------|
| `App.xaml` / `App.xaml.cs` | ✅ 複製；改 Title、App.Name、Package.appxmanifest |
| `MainWindow.xaml` + `.cs` + `MainViewModel.cs` | ✅ 複製；依新 NavSection 刪減 |
| `NavRailView.xaml` + `.cs` + `NavRailViewModel.cs` | ✅ 複製；縮減按鈕 |
| `SplashScreen.xaml` + `.cs` | ✅ 複製；Logo／標題改 Assetra |
| `NavSection.cs` | ⚠️ **重寫**（4 節，見下） |
| `AssemblyInfo.cs` | ✅ 複製；`[assembly: AssemblyTitle("Assetra")]` |
| `Package.appxmanifest` | ✅ 複製；`DisplayName` / `Publisher` 改 |

**`NavSection` 重寫**：

```csharp
namespace Assetra.WPF.Shell;

public enum NavSection
{
    Portfolio,          // 六個 tab：Dashboard／投資／配置分析／帳戶／負債／交易
    FinancialOverview,  // Allocation Treemap + 再平衡 + 財務總覽
    Alerts,
    Settings,
}
```

> Dashboard 不獨立為 NavSection：使用者預設落在 `Portfolio` → `PortfolioTab.Dashboard`。

### Features 層（16 → 7，Dashboard 也刪）

| 保留（7） | 刪除（9） |
|----------|-----------|
| `Portfolio/`（含所有 tab 與 Controls） | `Dashboard/` |
| `Allocation/`（Treemap + 再平衡 + 財務總覽） | `Watchlist/` |
| `AddStock/`（新增股票對話框） | `Analysis/` `Detail/` `MarketInfo/` |
| `Alerts/` | `News/` `Flash/` |
| `Settings/`（精簡） | `AiChat/` `Strategy/` |
| `Snackbar/` | |
| `StatusBar/` | |

**`Features/Settings/` 精簡項目**：
- ✅ 保留：主題切換／語言切換／主要貨幣／手續費折扣預設／Taiwan-style 切換
- ❌ 移除：AI Token 欄位／FinMind Token 欄位／選股預設 UI／策略庫 UI

### Infrastructure 層

| 檔案 | 處置 |
|------|------|
| `AppBootstrapper.cs` | ⚠️ **重寫** — 移除 AI／News／Flash／Strategy／Watchlist／Screener 的 DI 註冊 |
| `AppThemeService.cs` / `ColorSchemeService.cs` | ✅ 複製 |
| `DbInitializerService.cs` | ⚠️ 改 DB 路徑為 Assetra |
| `MarketDataHostedService.cs` | ✅ 複製（啟動 `StockScheduler`） |
| `WpfLocalizationService.cs` | ✅ 複製 |
| `Chart/` / `Controls/` / `Converters/` | ✅ 整個資料夾複製 |
| 其餘 utility | ✅ 複製（`ParseHelpers`, `WpfUtils`, `DisposableExtensions`, `PnlColorPalette`, `ISnackbarService`, `IThemeService`） |

### Controls 層

✅ 全部複製：`CalendarPanel` / `DateRangePicker` / `FormDatePicker` / `SearchBox` / `TimePanel`

### Themes 層

✅ 全部複製：`Dark.xaml` / `Light.xaml` / `GlobalStyles.xaml`

### Languages 層

| 檔案 | 處置 |
|------|------|
| `zh-TW.xaml` | 從 Stockra 複製後，刪除：`AiChat.*`、`News.*`、`Flash.*`、`Strategy.*`、`Market.*`、`Analysis.*`、`Detail.*`、`Watchlist.*`、`Screener.*` 開頭的 Key |
| `en-US.xaml` | 同步刪除對應 Key |

### Assets 層

- 新準備 `assetra.ico`，主題與資產管理相關（例：錢包、圓餅圖、保險箱、資產符號組合）
- Splash 圖若有沿用，更新 Logo 與標題為 Assetra

---

## Section 5 — 資料庫 + 資料遷移

### DB 路徑與設定

| 項目 | 路徑 |
|------|------|
| SQLite DB | `%APPDATA%\Assetra\assetra.db` |
| AppSettings | `%APPDATA%\Assetra\settings.json` |
| Log | `%APPDATA%\Assetra\logs\` |

### Schema — Assetra v1（一次建齊）

| 資料表 | 用途 |
|--------|------|
| `portfolio_entries` | 持倉主檔 |
| `trades` | 交易歷史 |
| `assets` | AssetItem（現金帳戶／負債／不動產／貴金屬） |
| `asset_groups` | 類別分組 |
| `asset_events` | 資產事件（Transaction／Valuation） |
| `portfolio_snapshots` | 每日淨值快照 |
| `portfolio_position_logs` | 持倉日誌 |
| `alert_rules` | 價格警示規則 |

**不建立**：`watchlist`、`custom_strategies`、`screener_presets`、`research_templates`、AI 聊天歷史表。

### `DbMigrator` 新設計

```csharp
public static async Task EnsureSchemaAsync(SqliteConnection conn)
{
    var current = await GetUserVersionAsync(conn);
    if (current == 0)
    {
        await ApplyV1SchemaAsync(conn);       // 一次建齊所有表
        await SetUserVersionAsync(conn, 1);
    }
    // 未來 Assetra 自己累積 v2, v3...
}
```

不繼承 Stockra 的歷史遷移腳本；Assetra 是全新 product，v1 直接建完整 schema。

### 資料遷移策略 — 自動匯入

**時機**：首次啟動（`assetra.db` 不存在時）。

**流程**：
1. 建立空的 `assetra.db` 並跑 `DbMigrator.EnsureSchemaAsync`
2. 偵測 `%APPDATA%\Stockra\stockra.db` 是否存在
3. 存在 → 跑 `StockraImportService.ImportAsync()`：
   - 直接 `ATTACH DATABASE` 跨連線複製資料
   - 對應每張表（名稱不變、欄位不變 → 直接 `INSERT INTO ... SELECT * FROM`）
   - 處理不存在的表（watchlist 等）：略過
4. 匯入完成 → Snackbar 顯示「已從 Stockra 匯入 N 筆資料」
5. 失敗 → 記錄到 log，Snackbar 顯示錯誤，使用者仍可空白起步

**匯入範圍**：

| 表 | 匯入 |
|---|---|
| `portfolio_entries` | ✅ |
| `trades` | ✅ |
| `assets` | ✅ |
| `asset_groups` | ✅ |
| `asset_events` | ✅ |
| `portfolio_snapshots` | ✅ |
| `portfolio_position_logs` | ✅ |
| `alert_rules` | ✅ |
| `watchlist` | ❌ 略過 |
| `custom_strategies` / `screener_presets` / `research_templates` | ❌ 略過 |

**AppSettings 遷移**：
- ✅ 帶過去：`TargetAllocations`、主題偏好、語言偏好、主要貨幣、手續費折扣預設
- ❌ 不帶：AI Token、FinMind Token、AI Model 偏好、選股預設

**實作位置**：`Assetra.Infrastructure/Persistence/StockraImportService.cs`，由 `DbInitializerService` 在 `DbMigrator.EnsureSchemaAsync` 之後呼叫。

---

## Implementation Risks / Open Items

### Risks

1. **`StockScheduler` 移除 `IWatchlistRepository` 相依**
   - 現有程式碼在 [StockScheduler.cs:57](Stockra.Infrastructure/Scheduling/StockScheduler.cs:57) 合併 watchlist 與 portfolio symbols
   - Assetra 要改成只讀 portfolio；建構子也要拔掉 `IWatchlistRepository` 參數
   - 測試要同步更新

2. **`AppSettings` 結構變動**
   - Stockra 的 `AppSettings` 有 `OpenAIToken`、`FinMindToken`、`PreferredModel` 等欄位
   - Assetra 的 `AppSettings` 拔掉這些；但匯入相容設定時要容忍「來源有、目的沒有」的欄位
   - JSON 反序列化時忽略未知欄位

3. **`IFinMindService` 精簡**
   - 原本介面有多個方法（歷史 K、法人、融資、財報）
   - 精簡後僅保留收盤價查詢
   - 相關 DTO／parser 可能隨之簡化

4. **Namespace 全域替換風險**
   - XAML 檔裡的 `xmlns:vm="clr-namespace:Stockra.WPF.Features.Portfolio"` 類型 QualifiedName 需要一起改
   - `DynamicResource` Key 若含 namespace 片段也要處理
   - 建議先用 `dotnet build` 編譯找出所有剩餘參照

5. **SQLite ATTACH DATABASE 相容性**
   - 若 Stockra DB 欄位順序／型別與 Assetra 期待不一致（理論上不會，因為我們複製同一份 schema 定義），需要做欄位映射
   - 首次匯入應包成一個 transaction，失敗整個 rollback

### Open Items（先不決，後面實作時再定）

- **Assetra 是否需要獨立的 CLAUDE.md？** → 建議有，內容以 Stockra CLAUDE.md 為底，更新命名空間與模組清單。
- **是否提供「匯入」再執行按鈕？** → 初版不提供；若未來需求浮現，在 Settings 加一個「重新從 Stockra 匯入」按鈕（會清空後重匯）。
- **MSI／安裝檔建置？** → 初版只求能 `dotnet run`；發佈方式後續決定。
- **assetra.ico 圖示設計？** → 方向已定（資產管理主題），實際設計後續補。

---

## Acceptance Criteria（驗收標準）

此 spec 完成實作後，應能：

1. `D:\Workspaces\Finances\Assetra\` 目錄下有完整 solution，`dotnet build Assetra.slnx` 成功。
2. 執行 `Assetra.WPF.exe`，看到以 Assetra 為名的主視窗，有 4 個 NavRail 按鈕。
3. 首次啟動若有 Stockra DB，自動匯入持倉／交易／資產／警示，Snackbar 提示「已匯入 N 筆資料」。
4. Portfolio 頁面可看到從 Stockra 匯入的所有持倉，盤中即時報價推送正常（每 10 秒更新）。
5. 資產配置頁面的 Treemap 與再平衡功能正常。
6. 警示功能可設定與觸發。
7. Settings 中只剩主題／語言／匯率／手續費折扣，看不到 AI／FinMind／策略選項。
8. Stockra 仍可正常執行，兩個 App 互不影響。

---

**Next step**：經使用者檢閱與核准後，執行 `superpowers:writing-plans` 產出逐步實作計畫（預期拆為多個 phase：Core fork → Infrastructure fork → WPF shell → Features 遷移 → 匯入服務 → 整合測試）。
