# Changelog

## v0.21.4 - 2026-04-28

Code review hardening：收斂同步、報表換匯與語系化修補，並同步修正 docs 對匯入管線成熟度的描述。

### 變更

- **BalanceSheet 投資市值換匯**：`PortfolioDailySnapshot.MarketValue` 現在會依 snapshot currency 換算至目前 base currency，避免現金 / 負債已換匯但投資市值混幣別加總。
- **Sync device id stamp**：sync-aware repositories 改為每次 mutation 透過 provider 讀取目前 `SyncDeviceId`，避免首次同步產生 device id 後仍持續寫入 `local`。
- **Sync / Conflict 狀態語系化**：`SyncSettingsViewModel` 與 `ConflictResolutionViewModel` 的 runtime status 改用 `Settings.Sync.*` resource key，避免繁中 UI 操作後混入英文。
- **Docs 對齊實作**：Technical Architecture / Feature Roadmap 更新 PDF / OCR parser 已落地、`AutoCategorizationRule` 取代舊 `ImportRule`，並補上券商匯入 match key 會納入方向、數量與單價。

### 測試

- `dotnet build .\Assetra.slnx --no-restore -v minimal` ✅
- `dotnet test .\Assetra.Tests\Assetra.Tests.csproj --no-build -v minimal` ✅（914/914）

### 後續 sprint 預告

- **v0.22.0**：AI 財務助理。

## v0.21.3 - 2026-04-28

程式碼 vs Docs 名稱誤記修正（code-gap audit 後修補）。三處 docs 引用了不存在的型別 / 服務名稱：

### 變更

- **`docs/architecture/Bounded-Contexts.md`** §9 Tax context：
  - 主要模型：移除不存在的獨立 `OverseasIncome` VO，改為「`TaxSummary` 含 `OverseasIncomeTotal` decimal 欄位」；移除不存在的 `DividendIncome` 型別，改為實際存在的 `DividendIncomeRecord`。
  - 主要服務：移除不存在的 `DividendTrackingService`，改為實際架構：`TaxSummaryService` / `TaxCalculationService` + WPF 層 `DividendCalendarViewModel`（配息 UI 邏輯在 ViewModel，無獨立 application-layer tracking service）。
- **`docs/planning/Roadmap-v0.14-to-v1.0.md`** §二 v0.18.0 D1：
  - `OverseasIncome` 修正為 `OverseasIncomeTotal`（decimal 欄位，非獨立 VO）；`OverseasIncomeRecord` 修正為「無此型別」。

### 驗證

- 程式碼確認：`OverseasIncomeTotal` 確實是 `TaxSummary` record 內的 decimal 欄位（`Assetra.Core/Models/TaxRecords.cs`）。
- `DividendCalendarViewModel` 存在於 WPF 層；無 application-layer `DividendTrackingService`。
- OCR 介面正確名稱為 `IOcrAdapter`（`TesseractOcrAdapter` 為預設實作），docs 中「OCR adapter」通稱已準確，不需更改。

### 測試

- 純文件變更，913/913 測試保持綠。

### 後續 sprint 預告

- **v0.22.0**：AI 財務助理。

## v0.21.2 - 2026-04-28

跨文件一致性修補：對照 CHANGELOG v0.21.1 現況，把 `README`、`architecture/`、`reviews/` 中殘留的過時版本戳記、斷鏈、錯誤「尚未落地」標記全部修正。

### 變更

- **`README.md`**：feature list 補 v0.14–v0.21 全部出貨功能（外幣/美股、Goals 完整、趨勢增強、稅務、雲端同步）；里程碑從 `v0.13.0` → `v0.21.1`；bounded contexts 清單補 Analysis / Tax / Sync / FX / Reconciliation。
- **`docs/architecture/Architecture.md`**：Application context 清單補 `Analysis/`、`Goals/`、`Tax/`、`Import/`、`Reconciliation/`、`Sync/`、`Fx/`，每項標注出貨版本。
- **`docs/architecture/Bounded-Contexts.md`**：Goals §4 從「MVP」改為「完整子系統 v0.16.0」；Reconciliation §6b 移除兩個 ⏳ 標記（v0.10.0 已出貨）；新增 §9 Tax、§10 FX/Currency、§11 Sync 三個 context；§三 context 關係圖加入 Tax / FX / Sync；§六總結清單補三個新 context。
- **`docs/architecture/Technical-Architecture-Blueprint.md`**：修正頂部斷鏈（`Next-Sprint-v0.6.0.md` → `CHANGELOG.md`）；Goals §4 現況戳記從「MVP」→「v0.16.0 完整」；匯入管線 §六.1 現況從「v0.7.0 MVP」→「v0.19.0 完整」；報表 §七現況從「月結 MVP」→「v0.11.0 三大報表全落地」；§十一演進順序補 Phase 1–3 ✅ 標記與 AI / 多元資產 / 情境 / 多端版號；§十二總結重寫反映 v0.21.1 現況與 Phase 4 架構方向。
- **`docs/architecture/Sync-Wire-Protocol.md`**：標題 `(v0.20.2)` → `(v0.21.0)`。
- **`docs/reviews/Docs-Gap-Review.md`**：頂部加「⚠️ 多數描述已過時」警示；§三 Goals / Trends 過時差距加刪除線並補「已解決」說明；§四全部重寫為出貨狀態總覽表（匯入 / 績效 / 報表 / Goals / 風險 / 稅務 / 雲端同步全部 ✅），剩餘 Phase 4 待做項明確列出。

### 測試

- 純文件變更，913/913 測試保持綠。

### 後續 sprint 預告

- **v0.22.0**：AI 財務助理（LLM adapter、自然語言查詢 → service 路由、查詢 UI）。

## v0.21.1 - 2026-04-28

雲端同步**使用者文件**：補完 v0.21.0 後續預告中 v1.0.0 GA item #2（README / docs 新增 sync 設定步驟與 troubleshooting）。同時順手清理 `docs/planning/` 累積的歷史檔。

### 變更

- **`docs/guides/Cloud-Sync-Setup.md`（新增）**：分六節覆蓋（1）端到端加密設計概念；（2）後端準備、`Settings → Sync` 各欄位語意、首次同步流程、第二台裝置 onboarding；（3）Conflict 解決面板操作；（4）安全模型（密語不上傳、AES-GCM、salt 16-byte、key 32-byte）；（5）Troubleshooting 對照表（7 個常見錯誤訊息 → 處理方式）；（6）相關檔案索引。Salt 跨裝置同步的 v0.21.0 暫行作法（手抄）也明確寫進 troubleshooting。
- **`docs/INDEX.md`**：Guides 區塊加 Cloud Sync Setup 連結。
- **`docs/planning/` 整理**：
    - 移 `Next-Sprint-v0.{8,9,10,11,12,13}.0.md` 6 份歷史 sprint 文件至 `docs/archive/planning/`（內容均已隨對應版本出貨）。
    - `Implementation-Roadmap.md`：補打勾 Goals 完整子系統（v0.16）、趨勢圖事件標註與堆疊圖（v0.17）、外幣/美股（v0.14/v0.15）、稅務（v0.18）、進階匯入（v0.19）、雲端同步（v0.20.x → v0.21.0）；AI 財務助理標記為待重編版號。
    - `Assetra-Feature-Blueprint-and-Roadmap.md`：現況表加上 v0.7 → v0.21 全部出貨的對照欄、§B/§C/§K 改寫為「已完成」、優先級摺成 P2-only（已出貨的 P0/P1 不再重複列）、§五重排為 Phase 1–3 ✅ + Phase 4 進行中。
    - `Roadmap-v0.14-to-v1.0.md`：頂部加版號重編註記（v0.20 一個 sprint 展開為 v0.20.0–v0.20.12 + v0.21.0 共 13 個 sub-version）；總覽表 v0.14–v0.21 ✅；AI sprint 標註版號需重編。

### 設計取捨

- **走 v0.21.1 patch 而非 v0.22.0**：本版只動文件、無程式碼變更、無測試異動，符合 patch 語意。AI 財務助理待開工的 sprint 將另起 v0.22.0（roadmap 中原 v0.22.0「多元資產」自動順延一格）。
- **Salt 同步暫不加自動化**：v0.21.0 把 salt 跨裝置一致性留給後端職責或使用者手抄；要做成全自動需要在後端定義 metadata 換手協定，與本版 docs 範疇不符，留給 v1.0.0 GA gate 評估。
- **Cloud-Sync-Setup.md 收在 `docs/guides/`，不進 README**：使用者實際開頁的入口是 INDEX → Guides；README 是給開發者讀的，不應堆 end-user 操作步驟。

### 測試

- 純文件變更，913/913 測試保持綠（與 v0.21.0 同基線）。

### 後續 sprint 預告

- **v0.22.0**：roadmap 原訂 v0.21.0 的「AI 財務助理」實作（LLM adapter、自然語言查詢 → service 路由、查詢 UI），原 v0.22.0「多元資產（不動產 + 保險）」順延至 v0.23.0；後續 sprint 全部往後移一格。

## v0.21.0 - 2026-04-28

第一個 GA milestone：盤點 v0.20.x 累積的雲端同步基建後，三個 v0.20.12 預告項中**兩個已在過往 sprint 默默完成**，本版只需補上剩下的一塊。

- **i18n 複校（item #2）**：`zh-TW.xaml` ↔ `en-US.xaml` 902/902 keys 完全對齊，`Settings.Sync.Conflicts.*` 字串兩邊都早已就位 → no-op。
- **HttpCloudSyncProvider staging 煙霧測試（item #1）**：需真實後端 endpoint，留作 GA gate 的人工驗收項目，不在自動測試範疇 → 延後。
- **SyncCoordinator chaos 測試（item #3，本版）**：把 HTTP 5xx 5xx 從後端冒上來、`CancellationToken` 在 in-flight HTTP 期間取消這兩個 production 真會踩到的失敗路徑用 `HttpMessageHandler` stub 鎖進測試。`AesGcmEncryptionService` 的 passphrase mismatch 已由 `EncryptingCloudSyncProviderTests` 覆蓋，不在 coordinator 層重複。

### 變更

- **`Assetra.Tests/WPF/SyncCoordinatorTests.cs`**：加 `SyncAsync_PropagatesHttp5xx_FromBackend` 與 `SyncAsync_HonorsCancellation` 兩個 chaos case，注入 `HttpMessageHandler` stub 模擬 503 與 hang，驗證 `HttpRequestException` / `OperationCanceledException` 透出 `SyncCoordinator` 而不被吞。檔案內加 `StubHandler` 內部類沿用 `HttpCloudSyncProviderTests` 的對等模式，避免引入 Moq 對 `HttpClient` 的脆弱包裝。

### 設計取捨

- **chaos 測試聚焦在 coordinator 層**：HTTP wire format 由 `HttpCloudSyncProviderTests` 把關、AES-GCM 失敗由 `EncryptingCloudSyncProviderTests` 把關、版本比較邏輯由 `LastWriteWinsResolverTests` + 各 repo 的 `ApplyRemote_DoesNotWriteBackwards` 把關。Coordinator 層真正未覆蓋的剩 HTTP 失敗傳遞與 cancellation——本版只補這兩個。
- **passphrase mismatch 不在此層測**：在 `SyncCoordinator` 層要 round-trip 兩次同步流程才能觸發解密失敗，會把測試寫成 mini-E2E。`EncryptingCloudSyncProvider` 已有針對 wrong-key 的單元測試，更貼近實際失敗點。
- **staging 煙霧測試延後到 GA gate**：自動化測試需要環境憑證 / 部署網址 / 重置流程，與單元測試 / 整合測試的脈絡不同。GA cut release 前的人工驗收清單比較合適。

### 測試

- 7/7 `SyncCoordinatorTests`（5 既有 + 2 新 chaos）。
- 913/913 全測試綠（+2）。

### 後續 sprint 預告

- **v1.0.0 (GA)**：（1）人工執行 staging 煙霧測試，把 device-A → real backend → device-B 跑通；（2）`README.md` / `docs/user-guide/cloud-sync.md` 新增 sync 設定步驟與 troubleshooting；（3）監控/遙測：`SyncResult` 的 push/pull/conflict 數字寫進 log 給使用者主動回報之外有事後追查管道。

## v0.20.12 - 2026-04-28

雲端同步**端到端整合測試**：在 `SyncEndToEndIntegrationTests` 把 v0.20.4–v0.20.11 累積的 8 個 entity 類型（`Category` / `Trade` / `Asset` / `AssetGroup` / `AssetEvent` / `Portfolio` / `AutoCategorizationRule` / `RecurringTransaction`）以**真實** SQLite repository、`CompositeLocalChangeQueue`、`SyncOrchestrator`、`InMemoryCloudSyncProvider` 串成 round-trip：device-A 各加一筆 → push → device-B pull → 驗證每個 repo 都能查到、且不會 echo back（pulled 的 row 不會被誤標為 pending）。Tombstone 路徑（device-A delete → cloud → device-B 軟刪）也含一個獨立 case。

實際 v0.20.11 預告的「打包 9 個 entity 進實際 push/pull HTTP 流程」一節，盤點後發現 `HttpCloudSyncProvider` / `SyncOrchestrator` / `CompositeLocalChangeQueue` / `ConflictResolutionViewModel` + `SettingsView.xaml` 的 manual drain 區塊在 v0.20.4–v0.20.11 期間早已就位；缺的只是「把所有 entity 類型實際綁在一起跑一次」的整合驗證。本版補上這個信心檢查。實際 entity 數為 8（不是 9——`Category` v0.20.4 已就位、原本數錯一個）。

### 變更

- **`Assetra.Tests/Integration/Sync/SyncEndToEndIntegrationTests.cs`（新增）**：
    - `RoundTrip_AllEightEntityTypes_DeviceAToDeviceB`：建兩個 SQLite DB，各掛 `CategorySqliteRepository` / `TradeSqliteRepository` / `AssetSqliteRepository` / `PortfolioSqliteRepository` / `AutoCategorizationRuleSqliteRepository` / `RecurringTransactionSqliteRepository` 共 6 個 concrete repo（`AssetSqliteRepository` 同時提供 Asset / AssetGroup / AssetEvent 三個 sync store），組成 `CompositeLocalChangeQueue` 8-key map。device-A AddAsync × 8 → `Orchestrator.SyncAsync()` 推 8 筆到 cloud → device-B `Orchestrator.SyncAsync()` 拉 8 筆並 apply → 透過 device-B 的 repo `GetByIdAsync` / `GetEntriesAsync` / `GetEventsAsync` 逐筆驗證；最後檢查 device-B 的 composite `GetPendingAsync` 為空（pulled remote 不會被誤標為 pending）。
    - `RoundTrip_TombstoneFromDeviceA_DeletesOnDeviceB`：device-A 加一筆 Category、雙端 sync 同步 → device-A `RemoveAsync` 軟刪、雙端再 sync → device-B `GetByIdAsync` 回 null。

### 設計取捨

- **以 InMemoryCloudSyncProvider 而非 HttpCloudSyncProvider 串測**：HttpCloudSyncProvider 已有自己的 wire-format 單元測試（`HttpCloudSyncProviderTests`），不需要在這裡再跑一次 HTTP。整合測試的目的是驗證「8 個 entity 透過 composite + orchestrator 串通」，cloud 側用 in-memory provider 把焦點留在 routing / mapping / version 處理上。
- **共用 6 個 SQLite repo 物件、8 個 EntityType 路由 key**：`AssetSqliteRepository` 一個 instance 對外實作三個 sync store（`IAssetSyncStore` / `IAssetGroupSyncStore` / `IAssetEventSyncStore`），因此 6 個 concrete repo 物件即可餵滿 8 個 composite map key；mirror v0.20.10–v0.20.11 的 production DI 拓撲。
- **PendingRecurringEntry / PortfolioSnapshot / PortfolioPositionLog 不入 round-trip**：分別是 per-device materialized queue / 計算快照 / 記錄表，依設計不同步，整合測試保持與 production 同樣的同步邊界。
- **Entity 數修正為 8**：v0.20.11 CHANGELOG 預告「9 個 entity」是把 Category 和 AutoCategorizationRule 重複算了一次。實際清單就是 8 個，本版本 CHANGELOG 訂正並把它寫入 round-trip 測試名稱（`AllEightEntityTypes`）以免下次再算錯。

### 測試

- 2/2 `SyncEndToEndIntegrationTests`（8 entity round-trip / Category tombstone round-trip）。
- 911/911 全測試綠（+2）。

### 後續 sprint 預告

- **v0.21.0**：第一個 GA milestone。打包：（1）`HttpCloudSyncProvider` 對接真實後端的 staging 環境煙霧測試；（2）conflict 解決面板的 i18n 複校（en-US 缺 `Settings.Sync.Conflicts.*` 字串檢查）；（3）`SyncCoordinator` 的 retry / backoff 行為跑一次 chaos 測試（時鐘漂移、HTTP 5xx、passphrase mismatch）。

## v0.20.11 - 2026-04-28

雲端同步**AutoCategorizationRule + RecurringTransaction 接線**：補完最後兩塊使用者管理的元資料（Category 已於 v0.20.4 接線）。兩者各自加入 5 個 sync 欄位、partial pending index，並 mirror Category / Asset 的 stamp / soft-delete / ApplyRemote 模式；`CompositeLocalChangeQueue` 加入 `AutoCategorizationRule` 與 `RecurringTransaction` 路由。`PendingRecurringEntry`（per-device 的 materialized queue，需要使用者本地確認）刻意排除於同步外，由 `RecurringTransaction` 在每台裝置自行 re-materialize。

### 變更

- **`Assetra.Core/Interfaces/Sync/IAutoCategorizationRuleSyncStore.cs`（新增）** / **`IRecurringTransactionSyncStore.cs`（新增）**：標準三方法契約（`GetPendingPushAsync` / `MarkPushedAsync` / `ApplyRemoteAsync`），由各自的 `*SqliteRepository` 同時實作。
- **`Assetra.Infrastructure/Persistence/AutoCategorizationRuleSchemaMigrator.cs`（修改）**：`AllowedColumns` / `AllowedTypeDefs` 各擴充 5 個 sync 欄位，`MigrateAddColumn` 5 次 + `idx_auto_rule_pending` partial index（`WHERE is_pending_push = 1`）。
- **`Assetra.Infrastructure/Persistence/RecurringSchemaMigrator.cs`（重寫）**：從 inline-only 改為 `AllowedColumns` + `AllowedTypeDefs` 模式（同 Asset / Trade）；保留 `recurring_transaction` 與 `pending_recurring_entry` 兩 table；sync 欄位**只**加到 `recurring_transaction`，`pending_recurring_entry` 維持 device-local；新增 `idx_recurring_pending` partial index。
- **`Assetra.Infrastructure/Sync/AutoCategorizationRuleSyncMapper.cs`（新增）**：10 欄 record ↔ snake_case JSON。Enum（`MatchField` / `MatchType` / `AppliesTo`）以 int 序列化，避免 wire format 受未來重命名影響。EntityType = `"AutoCategorizationRule"`。
- **`Assetra.Infrastructure/Sync/RecurringTransactionSyncMapper.cs`（新增）**：15 欄 record ↔ snake_case JSON。`Amount` 以 InvariantCulture decimal 字串避免 double 漂移；`StartDate` / `EndDate` / `LastGeneratedAt` / `NextDueAt` 用 ISO-8601 round-trip；nullable 欄位 round-trip 為 null。EntityType = `"RecurringTransaction"`。
- **`Assetra.Infrastructure/Persistence/AutoCategorizationRuleSqliteRepository.cs`（重寫）**：`: IAutoCategorizationRuleRepository, IAutoCategorizationRuleSyncStore`。建構子新增 `deviceId` / `TimeProvider`。`GetAllAsync` / `GetByIdAsync` 加 `is_deleted = 0` 過濾；`AddAsync` stamp `version=1, is_pending_push=1`；`UpdateAsync` bump version；`RemoveAsync` 改 soft delete；ApplyRemote 走 probe → 防回退 → ON CONFLICT(id) DO UPDATE 路徑，tombstone 用 placeholder INSERT（`category_id=Guid.Empty`，無 FK 約束所以安全）。
- **`Assetra.Infrastructure/Persistence/RecurringTransactionSqliteRepository.cs`（重寫）**：`: IRecurringTransactionRepository, IRecurringTransactionSyncStore`。建構子新增 `deviceId` / `TimeProvider`。`GetAllAsync` / `GetActiveAsync` / `GetByIdAsync` 加 `is_deleted = 0` 過濾；`AddAsync` / `UpdateAsync` / `RemoveAsync` 套同樣 stamp + soft-delete；ApplyRemote tombstone 用 placeholder INSERT（`name=''`, `trade_type=0`, `amount='0'`, `frequency=0`, `interval_value=1`, `start_date=$now`, `generation_mode=0`），無 FK 約束。
- **`Assetra.Application/Sync/AutoCategorizationRuleLocalChangeQueue.cs`（新增）** / **`RecurringTransactionLocalChangeQueue.cs`（新增）**：mirror `CategoryLocalChangeQueue`，pass-through 至對應 sync store，manual conflict 暫存於記憶體 list。
- **`Assetra.WPF/Infrastructure/BudgetServiceCollectionExtensions.cs`（修改）**：`CategorySqliteRepository` lambda 改帶入 `SyncDeviceId`；`AutoCategorizationRuleSqliteRepository` 改 concrete singleton + `IAutoCategorizationRuleRepository` / `IAutoCategorizationRuleSyncStore` 兩介面別名解析到同一 instance。
- **`Assetra.WPF/Infrastructure/RecurringServiceCollectionExtensions.cs`（修改）**：`RecurringTransactionSqliteRepository` 改 concrete singleton + `IRecurringTransactionRepository` / `IRecurringTransactionSyncStore` 兩介面別名解析到同一 instance；帶入 `SyncDeviceId`。
- **`Assetra.WPF/Infrastructure/ServiceCollectionExtensions.cs`（修改）**：`AddAssetraSync` 註冊 `AutoCategorizationRuleLocalChangeQueue` / `RecurringTransactionLocalChangeQueue`，`CompositeLocalChangeQueue` 的 map 加入 `[AutoCategorizationRuleSyncMapper.EntityType]` 與 `[RecurringTransactionSyncMapper.EntityType]`。

### 設計取捨

- **PendingRecurringEntry 不同步**：每台裝置都有自己的 confirmation queue（使用者按下「確認」才會 materialize 為 Trade）。如果 entry 也同步，會出現「A 裝置確認、B 裝置又看到」、或「A 已 dismiss、B 又跳出」的雙重 UX 問題。改為 `RecurringTransaction` 是 source of truth，每台裝置依 `next_due_at` 各自 re-materialize 自己的 pending entries。`recurring_transaction` 的 sync 欄位只加在主表，`pending_recurring_entry` 維持原 schema。
- **Enum 以 int 序列化**：`AutoCategorizationMatchField` / `AutoCategorizationMatchType` / `AutoCategorizationScope` / `RecurrenceFrequency` / `TradeType` / `AutoGenerationMode` 皆以 int 寫入 payload。未來若重命名 enum member（重構時很容易發生），int wire format 不受影響；同時與 DB 表示一致。代價是若新增 enum member，舊版 client 收到未知 int 會 cast 失敗——但這個風險在 enum string 路徑同樣存在，不是 int vs string 的差異點。
- **Decimal 用 InvariantCulture string**：`RecurringTransaction.Amount` 之前已用 string 寫入 DB，sync mapper 維持同樣表示法（`ToString(CultureInfo.InvariantCulture)`），避免 double 來回轉換產生精度漂移。
- **Tombstone placeholder 無 FK 顧慮**：兩 table 的 `category_id` / `cash_account_id` 皆只是 TEXT NULL，沒有 `REFERENCES expense_category(id)` 之類的 FK，因此 placeholder（`Guid.Empty` 或 `NULL`）寫入安全，不需要像 `asset_event` 那樣對 unknown id 採 no-op。

### 測試

- 7/7 `AutoCategorizationRuleSqliteRepositorySyncTests`（add stamp / update bump / remove tombstone / mark pushed / apply remote 新增 / 防回退 / tombstone 刪除 / GetAll 過濾 tombstone）。
- 9/9 `RecurringTransactionSqliteRepositorySyncTests`（add stamp / update bump / remove tombstone / mark pushed / apply remote 新增 / 防回退 / tombstone 刪除 / unknown id tombstone 安全 / GetActive 過濾 tombstone）。
- 6/6 `AutoCategorizationRuleSyncMapperTests` + 8/8 `RecurringTransactionSyncMapperTests`（round-trip / nullable round-trip / tombstone 空 payload / 拒 tombstone payload / 拒錯 EntityType / snake_case + CJK 不轉義 / decimal invariant string）。
- 909/909 全測試綠（+30）。

### 後續 sprint 預告

- **v0.20.12**：把 v0.20.4–v0.20.11 累積的 9 個 entity（Category / AutoCategorizationRule / RecurringTransaction / Trade / Asset / AssetGroup / AssetEvent / Portfolio + 一個未確定）打包進實際 push/pull HTTP 流程，串通 `SyncOrchestrator` 的端到端 round-trip，並補上 conflict UI 的 manual drain。

## v0.20.10 - 2026-04-28

雲端同步**AssetGroup + AssetEvent 接線**：補完 v0.20.9 的延後 scope。`AssetGroup`（FinancialType 下的分類，含 5 筆系統 seeded）與 `AssetEvent`（asset 子表，混合 transaction / valuation 兩種事件型）分別加入 5 個 sync 欄位並 mirror Asset / Portfolio 的 stamp / soft-delete / ApplyRemote 模式；`CompositeLocalChangeQueue` 加入 `AssetGroup` 與 `AssetEvent` 路由。系統 group（`is_system=1`）對遠端 tombstone 免疫，本地 `DeleteGroupAsync` 也維持 `WHERE is_system=0` 保護。`AssetEvent` 因為對 `asset(id)` 有 FK，tombstone 路徑只在本地已存在該 row 時才寫入（unknown id 視為 no-op），避免 phantom tombstone 違反 FK 約束。

### 變更

- **`Assetra.Core/Interfaces/Sync/IAssetGroupSyncStore.cs`（新增）** / **`IAssetEventSyncStore.cs`（新增）**：兩個介面方法名分別加 `Group` / `Event` 字首（`GetGroupPendingPushAsync` 等），讓同一個 `AssetSqliteRepository` 同時實作 `IAssetSyncStore` / `IAssetGroupSyncStore` / `IAssetEventSyncStore` 不會發生 method signature 衝突。
- **`Assetra.Infrastructure/Persistence/AssetSchemaMigrator.cs`（修改）**：對 `asset_group` 與 `asset_event` 各自走 `MigrateAddColumn` 補 5 個 sync 欄位，並建立 `idx_asset_group_pending` / `idx_asset_event_pending` 兩個 partial pending index（`WHERE is_pending_push = 1`）。
- **`Assetra.Infrastructure/Persistence/AssetSqliteRepository.cs`（修改）**：class declaration 加上兩個 sync 介面；`GetGroupsAsync` / `GetEventsAsync` / `GetLatestValuationsAsync` / `GetLatestValuationAsync` 全部加 `is_deleted = 0` 過濾；`AddGroupAsync` / `UpdateGroupAsync` / `DeleteGroupAsync` 套 stamp + soft delete + 維持 `WHERE is_system=0`；`AddEventAsync` 抽出 `BindEvent` helper + stamp；`DeleteEventAsync` 改 soft delete；新增 `IAssetGroupSyncStore` / `IAssetEventSyncStore` 三組方法（pending push / mark pushed / apply remote）。
- **`Assetra.Infrastructure/Sync/AssetGroupSyncMapper.cs`（新增）**：7 欄 record ↔ snake_case JSON（`financial_type` 為 enum string，`created_date` 為 `yyyy-MM-dd`）；EntityType = `"AssetGroup"`。
- **`Assetra.Infrastructure/Sync/AssetEventSyncMapper.cs`（新增）**：9 欄 record ↔ snake_case JSON；`event_type` 為 enum string，`event_date` / `created_at` 用 ISO-8601 round-trip，`amount` / `quantity` 以 InvariantCulture decimal 字串避免 double 漂移；EntityType = `"AssetEvent"`。
- **`Assetra.Application/Sync/AssetGroupLocalChangeQueue.cs`（新增）** / **`AssetEventLocalChangeQueue.cs`（新增）**：mirror `AssetLocalChangeQueue`，pass-through 至對應 sync store，manual conflict 暫存於記憶體 list。
- **`Assetra.WPF/Infrastructure/PortfolioServiceCollectionExtensions.cs`（修改）**：`AssetSqliteRepository` concrete singleton 已存在，新增 `IAssetGroupSyncStore` / `IAssetEventSyncStore` 兩個介面別名解析到同一 instance。
- **`Assetra.WPF/Infrastructure/ServiceCollectionExtensions.cs`（修改）**：`AddAssetraSync` 註冊 `AssetGroupLocalChangeQueue` / `AssetEventLocalChangeQueue`，`CompositeLocalChangeQueue` 的 map 加入 `[AssetGroupSyncMapper.EntityType]` 與 `[AssetEventSyncMapper.EntityType]`。

### 設計取捨

- **方法名以實體前綴避免 signature 衝突**：因為 `AssetSqliteRepository` 已實作 `IAssetSyncStore`，若再新增同樣命名的 `GetPendingPushAsync` 會與既有方法衝突。改用 `GetGroupPendingPushAsync` / `GetEventPendingPushAsync` 並排，比 explicit interface implementation 更直接（外部呼叫端、DI 別名都能透過 concrete instance 直接看到）。
- **系統 group 對遠端 tombstone 免疫**：`ApplyGroupRemoteAsync` 的 probe 同時讀 `version` 與 `is_system`，若 `is_system=1 && env.Deleted` 直接 `continue`。配合本地 `UPDATE … WHERE is_system=0` 保護，五筆 seeded 不會被任一裝置誤刪。
- **AssetEvent tombstone 對 unknown id 採 no-op**：`asset_event` 對 `asset(id)` 有 FK，placeholder（`Guid.Empty`）會違反約束。設計上一個 event tombstone 若連對應 asset 都不存在，刪除「不存在的東西」本來就是 no-op；本地不建 phantom row。已存在的 row 走 `ON CONFLICT(id) DO UPDATE SET is_deleted=1`，原 `asset_id` 保留不變。
- **AssetGroup 仍保留 placeholder INSERT**：`asset_group` 沒有 FK 依賴，所以 unknown id 的 tombstone 直接寫一筆 placeholder（`name=''`, `asset_type='Asset'`, `is_system=0`），讓裝置對未來反向操作（先收 tombstone、再收 update）保有對齊能力。

### 測試

- 9/9 `AssetSqliteRepositoryGroupSyncTests`（add stamp / update bump / delete tombstone / 系統 group 本地刪不變 / mark pushed / apply remote 新增 / 防回退 / 系統 group 對 remote tombstone 免疫 / user group tombstone 生效）。
- 7/7 `AssetSqliteRepositoryEventSyncTests`（add stamp / delete tombstone / mark pushed / apply remote 新增 / 防回退 / tombstone 刪除 / unknown id tombstone no-op）。
- 5/5 `AssetGroupSyncMapperTests` + 6/6 `AssetEventSyncMapperTests`（round-trip / tombstone 空 payload / 拒 tombstone payload / 拒錯 EntityType / snake_case + CJK 不轉義 / nullable decimal round-trip）。
- 879/879 全測試綠（+27）。

### 後續 sprint 預告

- **v0.20.11**：`Category` / `AutoCategorizationRule` 同步（補完最後一塊使用者管理的元資料）；考慮把 `RecurringTransaction` 也納入 sync。

## v0.20.9 - 2026-04-28

雲端同步**PortfolioEntry 接線**：`PortfolioEntry`（symbol 級持倉節點）加入 5 個 sync 欄位並 mirror Asset / Trade 的 stamp / soft-delete / ApplyRemote 模式；`CompositeLocalChangeQueue` 加入 `Portfolio` 路由。原本的 `idx_portfolio_symbol_exchange` UNIQUE INDEX 改為 partial（`WHERE is_deleted = 0`），讓多個 tombstone 不會在 placeholder symbol/exchange 上互相碰撞。`AssetGroup` / `AssetEvent` 仍未接線，移至 v0.20.10 評估。

### 變更

- **`Assetra.Core/Interfaces/Sync/IPortfolioSyncStore.cs`（新增）**：mirror `IAssetSyncStore` 契約，由 `PortfolioSqliteRepository` 同時實作，與 `IPortfolioRepository` 共用同一 instance。
- **`Assetra.Infrastructure/Persistence/PortfolioSchemaMigrator.cs`（修改）**：`portfolio` 表新增 5 個 sync 欄位 + `idx_portfolio_pending` partial index（`WHERE is_pending_push = 1`）；既有 `idx_portfolio_symbol_exchange` 升級為 partial（`WHERE is_deleted = 0`），對歷史 DB 偵測非 partial 後 DROP + 重建。`AllowedColumns` 同步擴充以滿足 ALTER TABLE 白名單。
- **`Assetra.Infrastructure/Persistence/PortfolioSqliteRepository.cs`（重寫）**：建構子新增 `deviceId` / `TimeProvider`；實作 `IPortfolioSyncStore`；`AddAsync` stamp `version=1, last_modified, is_pending_push=1`；`UpdateAsync` / `UpdateMetadataAsync` / `ArchiveAsync` / `UnarchiveAsync` bump version；`RemoveAsync` 改 soft delete；所有 `Get*Entries` 加 `is_deleted = 0` 過濾；`FindOrCreatePortfolioEntryAsync` 新建路徑也 stamp sync metadata；`HasTradeReferencesAsync` 改只看 live trade（`is_deleted = 0`）；`ApplyRemoteAsync` tombstone 路徑用 `__tombstone_{guid:N}` 占位 symbol/exchange，避免 placeholder 對撞。
- **`Assetra.Infrastructure/Sync/PortfolioSyncMapper.cs`（新增）**：mirror `AssetSyncMapper`。8 欄 record ↔ snake_case JSON；CJK 不轉義；EntityType = `"Portfolio"`。
- **`Assetra.Application/Sync/PortfolioLocalChangeQueue.cs`（新增）**：mirror `AssetLocalChangeQueue`，pass-through 至 `IPortfolioSyncStore`，manual conflict 暫存於記憶體 list。
- **`Assetra.WPF/Infrastructure/PortfolioServiceCollectionExtensions.cs`（修改）**：`PortfolioSqliteRepository` 改 concrete singleton + `IPortfolioRepository` / `IPortfolioSyncStore` 兩介面解析到同一 instance；建構子帶入 `IAppSettingsService.Current.SyncDeviceId`。
- **`Assetra.WPF/Infrastructure/ServiceCollectionExtensions.cs`（修改）**：`AddAssetraSync` 註冊 `PortfolioLocalChangeQueue`，`CompositeLocalChangeQueue` 的 map 加入 `[PortfolioSyncMapper.EntityType]`。

### 設計取捨

- **scope 縮窄至 PortfolioEntry**：原 v0.20.8 預告涵蓋 `AssetGroup` / `AssetEvent` / `PortfolioEntry` 三項。`AssetGroup` 多為 system seeded（5 筆固定 UUID），同步收益低；`AssetEvent` 是子表且最新估值用 `ROW_NUMBER()` window function，路由與衝突解析較複雜——兩者一起延後到 v0.20.10。`PortfolioEntry` 形狀單純（8 欄 flat record，無子表），先把它打通。
- **Tombstone placeholder 用 GUID-derived**：`portfolio` 有 `idx_portfolio_symbol_exchange` UNIQUE INDEX，若多個 tombstone 都用 `('', '')` 占位會違反 UNIQUE。改用 `__tombstone_{guid:N}` 保證每筆 tombstone 占位皆唯一。再把 INDEX 改 partial（`WHERE is_deleted = 0`）讓 tombstone 完全脫離 UNIQUE 範圍——雙重保險。
- **Partial UNIQUE INDEX 升級的歷史 DB 路徑**：偵測 `sqlite_master.sql` 是否含 `WHERE is_deleted` 字串，若無則 DROP + 重建。整個流程在 transaction 內，失敗 rollback。
- **`is_active` 與 `is_deleted` 的拆分**：`ArchiveAsync` 設 `is_active=0`（停用，UI 仍可見），`RemoveAsync` 設 `is_deleted=1`（軟刪，UI 不可見）。兩者都會 bump version + mark pending；archive 把 `is_active=false` 寫入 payload，雲端可重建狀態。
- **`UpdateMetadataAsync` 與 `UpdateAsync` 並列**：兩者各自 bump version——`UpdateAsync` 改 asset_type，`UpdateMetadataAsync` 改 display_name + currency。沒合併成單一 update path 因為呼叫端語意不同（UI 上是兩種不同操作），保留兩者讓 sync log 更可讀。

### 測試

- 14/14 `PortfolioSqliteRepositorySyncTests`（add stamps / update bumps / update metadata bumps / remove 變 tombstone / archive bump+pending / unarchive bump / mark pushed / apply remote 新增 / 防回退 / tombstone 刪除 / 未知 id tombstone 安全 / 多筆 tombstone 不對撞 / GetEntries 過濾 tombstone / FindOrCreate 新建 stamp + existing 不重複建立）。
- 5/5 `PortfolioSyncMapperTests`（round-trip 全欄位 / tombstone 空 payload / FromPayload 拒 tombstone / 拒錯 EntityType / snake_case + CJK 不轉義）。
- 既有 `PortfolioSqliteRepositoryTests` 保持綠燈，無 API 破壞。
- 852/852 全測試綠（+20）。

### 後續 sprint 預告

- **v0.20.10**：`AssetGroup` 同步（評估系統 group 的 5 筆 seeded 是否需要在多裝置間同步）；`AssetEvent` 同步（valuation history 子表，需處理 `ROW_NUMBER()` 路徑與衝突解析）。

## v0.20.8 - 2026-04-28

雲端同步**Asset 接線 + Trade cascade soft-delete**：`AssetItem`（合併 `CashAccount` / `LiabilityAccount` 後的統一實體）加入 5 個 sync 欄位並 mirror Trade 的 stamp / soft-delete / ApplyRemote 模式；`CompositeLocalChangeQueue` 加入 `Asset` 路由。Trade 的三條 cascade bulk-delete（`RemoveChildren` / `RemoveByAccountId` / `RemoveByLiability`）由 hard delete 改為 soft delete，cloud 從此可收到由 Account/Liability 連動觸發的 trade tombstone。`AssetGroup` / `AssetEvent` 暫不同步（後續 sprint）。

### 變更

- **`Assetra.Core/Interfaces/Sync/IAssetSyncStore.cs`（新增）**：mirror `ITradeSyncStore`，由 `AssetSqliteRepository` 同時實作，與 `IAssetRepository` 共用同一 instance。AssetGroup / AssetEvent 暫未涵蓋。
- **`Assetra.Infrastructure/Persistence/AssetSchemaMigrator.cs`（修改）**：`asset` 表新增 5 個 sync 欄位 + `idx_asset_pending` partial index（`WHERE is_pending_push = 1`）。`AssetAllowedColumns` / `AssetAllowedTypeDefs` 同步擴充以滿足 ALTER TABLE 白名單（含 `INTEGER NOT NULL DEFAULT 0`）。
- **`Assetra.Infrastructure/Persistence/AssetSqliteRepository.cs`（重寫）**：建構子新增 `deviceId` / `TimeProvider`；實作 `IAssetSyncStore`；`AddItemAsync` stamp `version=1, last_modified, is_pending_push=1`；`UpdateItemAsync` / `ArchiveItemAsync` bump version；`DeleteItemAsync` 改 soft delete；所有 `Get*Items` 加 `is_deleted = 0` 過濾；`FindOrCreateAccountAsync` 在新建路徑也 stamp sync metadata；`HasTradeReferencesAsync` 改只看 live trade（`is_deleted = 0`）。
- **`Assetra.Infrastructure/Sync/AssetSyncMapper.cs`（新增）**：mirror `TradeSyncMapper`。18 欄 record ↔ snake_case JSON；`decimal?` 以 invariant culture string 序列化；CJK 不轉義；EntityType = `"Asset"`。
- **`Assetra.Application/Sync/AssetLocalChangeQueue.cs`（新增）**：mirror `TradeLocalChangeQueue`，pass-through 至 `IAssetSyncStore`，manual conflict 暫存於記憶體 list。
- **`Assetra.Infrastructure/Persistence/TradeSqliteRepository.cs`（修改）**：`RemoveChildrenAsync` / `RemoveByAccountIdAsync` / `RemoveByLiabilityAsync` 由 `DELETE FROM trade WHERE …` 改為 `UPDATE … SET is_deleted = 1, version = version + 1, is_pending_push = 1 WHERE … AND is_deleted = 0`，與 `RemoveAsync` 對齊；移除 `TODO(v0.20.8)` 註記，class doc 更新成「cloud 會收到每筆連動刪除的 tombstone」。
- **`Assetra.WPF/Infrastructure/PortfolioServiceCollectionExtensions.cs`（修改）**：`AssetSqliteRepository` 改 concrete singleton + `IAssetRepository` / `IAssetSyncStore` 兩個介面解析到同一 instance（鏡 Trade 模式）；建構子帶入 `IAppSettingsService.Current.SyncDeviceId`。
- **`Assetra.WPF/Infrastructure/ServiceCollectionExtensions.cs`（修改）**：`AddAssetraSync` 註冊 `AssetLocalChangeQueue`，`CompositeLocalChangeQueue` 的 map 加入 `[AssetSyncMapper.EntityType]`。

### 設計取捨

- **AssetGroup / AssetEvent 暫不同步**：`AssetGroup` 大多為系統 seeded 資料（5 筆固定 UUID，使用者極少新增）；`AssetEvent` 是子表，且最新估值查詢走 `ROW_NUMBER()` window function，路由 + 衝突解析較複雜。先把核心 `AssetItem`（包含原 `CashAccount` / `LiabilityAccount`）打通，下一個 sprint 再評估子表是否值得同步。
- **`is_active` 與 `is_deleted` 的拆分**：「歸檔」（`ArchiveItemAsync` → `is_active = 0`）與「刪除」（`DeleteItemAsync` → `is_deleted = 1` 軟刪）語意不同，前者使用者可在 UI 看到「已停用」的歷史紀錄，後者完全消失。兩者都會 bump version + mark pending；archive 把 `is_active = false` 寫入 payload，cloud 端可重建相同狀態。
- **Tombstone 的 NOT NULL 占位**：`asset` 表的 `name` / `asset_type` / `created_date` 為 NOT NULL，未知 id 收到 tombstone 時 INSERT 路徑塞 `('', 'Asset', $today)`，後續 ON CONFLICT DO UPDATE 只更新 sync 欄位 — 與 Trade 同樣不暴露真實內容。
- **Cascade soft delete 用單一 `UPDATE … WHERE` 而非逐筆 `RemoveAsync`**：保留批次 SQL 的效能（避免 N 次 round-trip），同時用 `WHERE … AND is_deleted = 0` 確保已是 tombstone 的 row 不會被二次 bump version。
- **沒有改 `AssetItem` record 形狀**：sync 欄位是 row 屬性而非 domain 欄位，UI / domain 完全不感知，與 Trade / Category 一致。
- **`FindOrCreateAccountAsync` 的競賽路徑保留**：UNIQUE INDEX 衝突後重新 SELECT 的容錯邏輯不變；只是新增路徑現在會 stamp sync metadata，避免新建帳戶不被推送至 cloud。

### 測試

- 11/11 `AssetSqliteRepositorySyncTests`（add stamps / update bumps / delete 變 tombstone / archive bump+pending / mark pushed / apply remote 新增 / 防回退 / tombstone 刪除 / 未知 id tombstone 安全 / GetItems 過濾 tombstone / FindOrCreateAccount stamp sync metadata）。
- 5/5 `AssetSyncMapperTests`（round-trip 全欄位 / tombstone 空 payload / FromPayload 拒 tombstone / 拒錯 EntityType / snake_case + CJK 不轉義）。
- 3/3 新增 `TradeSqliteRepositorySyncTests`（`RemoveChildren` / `RemoveByAccountId` / `RemoveByLiability` 三條 cascade 都 soft delete + 推送 tombstone）。
- 既有 `AssetSqliteRepositoryTests`（含 system seed、cash account migration）保持綠燈，無 API 破壞。
- 832/832 全測試綠（+19）。

### 後續 sprint 預告

- **v0.20.9**：`AssetGroup` 同步（評估系統 group 的特殊處理）；`AssetEvent` 同步（valuation history，需處理子表的衝突解析）；`PortfolioEntry` 評估。

## v0.20.7 - 2026-04-28

雲端同步**Trade 接線 + Composite 路由**：`Trade` 實體加入 5 個 sync 欄位（version / last_modified_at / last_modified_by_device / is_deleted / is_pending_push）並 mirror Category 的 stamp / soft-delete / ApplyRemote 模式。`CompositeLocalChangeQueue` 依 `EntityType` 路由到具體 queue，`SyncCoordinator` 改吃 composite，`ConflictResolutionViewModel` 改透過 `IManualConflictDrain` + `ILocalChangeQueue` 同時處理 Category + Trade 衝突。

### 變更

- **`Assetra.Core/Interfaces/Sync/ITradeSyncStore.cs`（新增）**：mirror `ICategorySyncStore`，由 `TradeSqliteRepository` 同時實作，與 `ITradeRepository` 共用同一 instance。
- **`Assetra.Infrastructure/Persistence/TradeSchemaMigrator.cs`（修改）**：新增 5 個 sync 欄位 + `idx_trade_pending` partial index（`WHERE is_pending_push = 1`）。`AllowedColumns` / `AllowedTypes` 同步擴充以滿足 ALTER TABLE 白名單。
- **`Assetra.Infrastructure/Persistence/TradeSqliteRepository.cs`（重寫）**：建構子新增 `deviceId` / `TimeProvider`；實作 `ITradeSyncStore`（`GetPendingPushAsync` / `MarkPushedAsync` / `ApplyRemoteAsync`）；`AddAsync` stamp `version=1, last_modified, is_pending_push=1`；`UpdateAsync` bump version；`RemoveAsync` 改為 soft delete（同時把 children 的 `parent_trade_id` 設 NULL）；所有 `Get*` 查詢加 `is_deleted = 0` 過濾；`ApplyAtomicAsync` 的 add/remove 路徑也走 sync stamp。Bulk cascade 路徑（`RemoveChildrenAsync` / `RemoveByAccountIdAsync` / `RemoveByLiabilityAsync`）暫時保留 hard delete + TODO（待 v0.20.8 Account/Liability 接 sync 時統一改）。
- **`Assetra.Infrastructure/Sync/TradeSyncMapper.cs`（新增）**：mirror `CategorySyncMapper`。24 欄 record ↔ snake_case JSON；金額欄位以 invariant culture string 序列化（避免 decimal/double 漂移）；CJK 不轉義。
- **`Assetra.Application/Sync/TradeLocalChangeQueue.cs`（新增）**：mirror `CategoryLocalChangeQueue`，pass-through 至 `ITradeSyncStore`，manual conflict 暫存於記憶體 list。
- **`Assetra.Application/Sync/IManualConflictDrain.cs`（新增）**：抽出「抽走 manual conflicts」介面，讓 conflict UI 可以一次拉光所有 entity-specific queue 的衝突。Category / Trade queue 與 Composite 都實作。
- **`Assetra.Application/Sync/CompositeLocalChangeQueue.cs`（新增）**：依 `EntityType` 將 `ApplyRemoteAsync` / `RecordManualConflictAsync` 分派到對應 queue；`GetPendingAsync` / `MarkPushedAsync` / `DrainManualConflicts` 聚合所有 queue。未知 EntityType 的 envelope 安全忽略。
- **`Assetra.WPF/Infrastructure/SyncCoordinator.cs`（修改）**：建構子改吃 `ILocalChangeQueue`（將收到 composite）而非具體 `CategoryLocalChangeQueue`；`Queue` getter 型別改為 `ILocalChangeQueue`。
- **`Assetra.WPF/Features/Settings/ConflictResolutionViewModel.cs`（修改）**：建構子改吃 `IManualConflictDrain` + `ILocalChangeQueue`，`UseRemoteAsync` 透過 `ILocalChangeQueue.ApplyRemoteAsync` 依 `EntityType` 路由（不再寫死 Category store）。
- **`Assetra.WPF/Infrastructure/PortfolioServiceCollectionExtensions.cs`（修改）**：`TradeSqliteRepository` 改 concrete singleton + `ITradeRepository` / `ITradeSyncStore` 兩個介面解析到同一 instance（鏡 `BudgetServiceCollectionExtensions` Category 模式）；建構子帶入 `IAppSettingsService.Current.SyncDeviceId`。
- **`Assetra.WPF/Infrastructure/ServiceCollectionExtensions.cs`（修改）**：`AddAssetraSync` 註冊 `TradeLocalChangeQueue` / `CompositeLocalChangeQueue`（singleton）；`ILocalChangeQueue` / `IManualConflictDrain` 都解析到 composite；`SyncCoordinator` 改吃 `ILocalChangeQueue`。

### 設計取捨

- **共用 instance**：`TradeSqliteRepository` 不能讓 `ITradeRepository` 與 `ITradeSyncStore` 各自 `new`——兩個 instance 會各自走自己的 `_deviceId` / `_time`，且讀寫快照不一致。透過 concrete singleton 兩端解析到同一 instance（與 Category 對稱）。
- **金額欄位以 string 序列化**：Trade 有大量 `decimal?` 欄位，`System.Text.Json` 預設把 decimal 轉成 number 會走 double round-trip，可能漂移。改成 `ToString(CultureInfo.InvariantCulture)` + `decimal.Parse` 確保完全一致。
- **Bulk cascade 暫不接 sync**：`RemoveChildrenAsync` / `RemoveByAccountIdAsync` / `RemoveByLiabilityAsync` 是被刪 Account/Liability 觸發的 cascade ops，那些實體本身尚未接 sync，先保留 hard delete + TODO。已知限制：cloud 不會收到這些 cascade 刪除事件，等 v0.20.8 一起處理。
- **Composite 對未知 EntityType 安全忽略**：未來新增 entity 接線時，舊版 client 仍能跑（不會炸），只是不處理它不認識的 envelope。`MarkPushedAsync` 廣播給所有 queue（id 沒攜帶 EntityType），代價是幾個 no-op `UPDATE WHERE id = ?`，可接受。
- **`SyncConflict.Local.EntityType` 用作路由 key**：`SyncConflict` 已暴露 `EntityType` getter（取自 Local envelope）。Local / Remote 同 EntityType 是 invariant（不同型不可能進到同一個 conflict），所以用 Local 即可。
- **沒有改 `Trade` record 形狀**：sync 欄位（version / pending）是 row 屬性而非 domain 欄位，存在 DB 層即可，UI / domain 完全不感知。

### 測試

- 10/10 `TradeSqliteRepositorySyncTests`（add stamps / update bumps / remove 變 tombstone / remove 解開 children / mark pushed / apply remote 新增 / 防回退 / tombstone 刪除 / 未知 id tombstone 安全 / GetAll 過濾 tombstone）。
- 5/5 `TradeSyncMapperTests`（round-trip 全欄位 / tombstone 空 payload / FromPayload 拒 tombstone / 拒錯 EntityType / snake_case + CJK 不轉義）。
- 5/5 `CompositeLocalChangeQueueTests`（GetPending 聚合 / ApplyRemote 路由 / 忽略未知 type / RecordManualConflict 路由 / DrainManualConflicts 聚合）。
- 既有 `ConflictResolutionViewModelTests` / `SettingsViewModelTests` 改用新介面（queue 既是 `IManualConflictDrain` 也是 `ILocalChangeQueue`）。
- 813/813 全測試綠（+20）。

### 後續 sprint 預告

- **v0.20.8**：`Asset` / `Account` / `LiabilityAccount` 接線；補完 cascade bulk delete 的 sync 路徑；`PortfolioEntry` 評估是否同步。
- **v0.20.9+**：Cloudflare Workers + R2 reference backend + deployment guide。

## v0.20.6 - 2026-04-28

雲端同步**衝突解決面板 + 背景定時同步**：手動 conflict 從先前每次 `SyncCoordinator.SyncAsync` 重建的 in-memory list（同步結束就遺失）升級為 DI singleton `CategoryLocalChangeQueue`，讓 conflicts 跨多次同步累積；Settings 新增清單 UI 讓使用者逐筆「保留本機 / 採用雲端」。背景 sync timer（`BackgroundSyncService`）讀使用者明確勾選的 in-process 密語快取，每 N 分鐘觸發一次同步。

### 變更

- **`Assetra.Core/Models/AppSettings.cs`（修改）**：新增 `SyncIntervalMinutes`（預設 0 = 停用背景同步）。
- **`Assetra.Application/Sync/CategoryLocalChangeQueue.cs`**：本身未改邏輯，但用法變了——從每次 `SyncCoordinator.SyncAsync` `new` 一個改為 DI singleton。
- **`Assetra.WPF/Infrastructure/SyncCoordinator.cs`（修改）**：建構子改吃 `CategoryLocalChangeQueue` 注入而非 `ICategorySyncStore`，每次同步重用同一個 queue instance。新增 `Queue` getter 給 ConflictResolutionViewModel 與測試使用。
- **`Assetra.WPF/Infrastructure/SyncPassphraseCache.cs`（新增）**：thread-safe in-process 密語快取。`Set` / `TryGet` / `Clear`，**不持久化**。由 `SyncSettingsViewModel` 在使用者明確勾選「記住密語」且手動同步成功時填，由 `BackgroundSyncService` 在 timer tick 讀取。
- **`Assetra.WPF/Infrastructure/BackgroundSyncService.cs`（新增）**：`BackgroundService` 子類別。每 30 秒輪詢一次 (`PollGranularity`)，當 `SyncEnabled` ∧ `IntervalMinutes > 0` ∧ 上次成功跑完已超過 N 分鐘 ∧ 密語已快取時觸發 `SyncCoordinator.SyncAsync`。失敗只記 log + 退一個間隔，不擲、不重試風暴。
- **`Assetra.WPF/Features/Settings/ConflictResolutionViewModel.cs`（新增）**：列出 `DrainManualConflicts()` 的內容並提供 `KeepLocalCommand` / `UseRemoteCommand`。`KeepLocalAsync` 是純 UI 操作（local 已 pending push，下次同步會送出）；`UseRemoteAsync` 呼叫 `ApplyRemoteAsync` 強制覆寫本機。`ConflictRowViewModel` 投影 envelope 元資料（version / lastModifiedAt / device / deleted flag）給 DataTemplate 顯示。
- **`Assetra.WPF/Features/Settings/SyncSettingsViewModel.cs`（修改）**：新增 `IntervalMinutes` / `CachePassphraseForBackground` 兩個 `[ObservableProperty]`；`SaveSettingsAsync` 寫回 `SyncIntervalMinutes`；`SyncNowAsync` 成功後依 checkbox 將密語存進 / 從 cache 清掉。建構子加入 `SyncPassphraseCache` 依賴。
- **`Assetra.WPF/Features/Settings/SettingsViewModel.cs`（修改）**：暴露 `Conflicts: ConflictResolutionViewModel` 屬性供 XAML 綁定。建構子加入 `ConflictResolutionViewModel` 依賴。
- **`Assetra.WPF/Features/Settings/SettingsView.xaml`（修改）**：Sync 區塊新增間隔分鐘 TextBox + 「記住密語」CheckBox + 衝突清單 ItemsControl（每筆顯示 EntityType / Id / local v? @t / remote v? @t + 兩個按鈕）。
- **`Assetra.WPF/Infrastructure/ServiceCollectionExtensions.cs`（修改）**：`AddAssetraSync` 註冊 `CategoryLocalChangeQueue`（singleton）/ `SyncPassphraseCache`（singleton）/ `ConflictResolutionViewModel`（singleton）/ `BackgroundSyncService`（hosted service）。
- **`Assetra.WPF/Languages/zh-TW.xaml` / `en-US.xaml`（修改）**：8 組新 sync UI 文字 key（IntervalMinutes / CachePassphrase / Conflicts.Reload / Conflicts.KeepLocal / Conflicts.UseRemote 等 + hint 文字）。

### 設計取捨

- **Conflict queue 必須 singleton**：v0.20.5 時 `CategoryLocalChangeQueue` 在 `SyncCoordinator.SyncAsync` 內部 `new`，導致 `RecordManualConflictAsync` 寫進去的 conflict 同步結束就 GC。本版升級為 DI singleton 才能讓 UI 在多次同步後一次處理累積的衝突。
- **「保留本機」是 no-op**：local row 在進入 manual conflict 前已是 `is_pending_push = 1`，下次 push 會自動送出。對 UI 來說只是把 row 從清單移除即可，不需動 DB。代價：使用者按下後若沒有觸發下次同步，conflict 不會「消失」於遠端視角；但這是預期語意，下次 sync 自然解決。
- **「採用雲端」呼叫 `ApplyRemoteAsync(remote)`**：依賴 sync repo 的版本回退保護判斷是否覆寫。若 remote.version > local 會直接 upsert；若版本相等（manual conflict 常見情境），upsert 仍會覆蓋（因為版本不**小於**），這是想要的行為。
- **背景同步需使用者明確 opt-in 快取密語**：CheckBox 預設 false。Trade-off 是首次啟動 + 直到使用者第一次手動同步成功並勾選前，背景 timer 不會跑——這是有意的安全預設（密語不是「靜默常駐」）。
- **`BackgroundSyncService` 30 秒輪詢而非按 timer 排程**：實作簡單、cancellation 反應快。間隔時間以 `lastRunAt` 記錄判定，避免修改 settings 後要重啟 timer。代價是即使 `IntervalMinutes = 0` 也每 30 秒跳一次 loop（commodity CPU < 0.001%，可接受）。
- **失敗不重試風暴**：`SyncAsync` 拋例外後 `lastRunAt = UtcNow`，相當於「失敗也消耗一個間隔」，避免後端短暫 5xx 把流量打爆。代價是若使用者改設定後立即驗證需要等下個間隔。
- **未抽 `IBackgroundSyncTrigger`**：MVP 只一個觸發源（timer），尚不需要 abstract command bus。`SyncCoordinator.SyncAsync` 已是公開 entry point。

### 測試

- 5/5 `SyncPassphraseCacheTests`（empty / set+get / clear / set throws on empty / overwrite）。
- 4/4 `ConflictResolutionViewModelTests`（reload drains queue / 空清單訊息 / keep local 不動 store / use remote 呼叫 ApplyRemoteAsync）。
- 既有 `SyncCoordinatorTests` / `SyncSettingsViewModelTests` / `SettingsViewModelTests` 補新建構子簽章參數。
- 793/793 全測試綠（+9）。

### 後續 sprint 預告

- **v0.20.7**：`Trade` entity 接線；同時抽 `CompositeLocalChangeQueue` 路由 by `EntityType`，供 ConflictResolutionViewModel 處理多 entity 類型。
- **v0.20.8**：`Asset` / `Account` 接線。
- **v0.20.9+**：Cloudflare Workers + R2 reference backend + deployment guide。

## v0.20.5 - 2026-04-28

雲端同步**手動觸發 MVP**：Settings 新增「雲端同步」區塊—— 啟用開關 / 後端 URL / 認證 token / 密語輸入 / 立即同步按鈕 / 上次同步狀態回顯。密語只活在記憶體（KDF → AES key 用完即丟），`device_id` 與 PBKDF2 salt 在首次同步時自動產生並持久化。背景 timer 與 conflict 解決面板留待 v0.20.6。

### 變更

- **`Assetra.Core/Models/AppSettings.cs`（修改）**：新增 5 個 sync 欄位—— `SyncEnabled` / `SyncBackendUrl` / `SyncAuthToken` / `SyncDeviceId` / `SyncPassphraseSalt`。**密語本身不存**，只存 salt（首次產生的 16 bytes 隨機 base64）。
- **`Assetra.Infrastructure/Sync/JsonSyncMetadataStore.cs`（新增）**：`ISyncMetadataStore` JSON 檔實作（`%APPDATA%\Assetra\sync-meta.json`）。寫入採 atomic-rename（`.tmp` → `File.Move overwrite`），單行程內以 `SemaphoreSlim` 序列化。
- **`Assetra.WPF/Infrastructure/SyncCoordinator.cs`（新增）**：每次 `SyncAsync` 依 `IAppSettingsService.Current` + 使用者輸入密語動態組裝完整管線：`HttpCloudSyncProvider` → `EncryptingCloudSyncProvider` → `SyncOrchestrator` + `CategoryLocalChangeQueue` + `JsonSyncMetadataStore`。首次呼叫自動產生 device GUID + 16 bytes salt 並 `SaveAsync` 持久化。
- **`Assetra.WPF/Features/Settings/SyncSettingsViewModel.cs`（新增）**：`[ObservableProperty]` 持久化欄位（Enabled / BackendUrl / AuthToken）+ 記憶體欄位（Passphrase）+ 狀態回顯（LastSyncAt / LastPulled / LastPushed / AutoResolved / ManualConflicts / IsSyncing / StatusMessage）。`SaveSettingsAsync` / `SyncNowAsync` 兩個 RelayCommand；`SyncNowAsync` 在 `finally` 把 `Passphrase` 清為空字串。
- **`Assetra.WPF/Features/Settings/SettingsView.xaml(.cs)`（修改）**：新增「雲端同步」區塊（`<Border>`），CheckBox / TextBox / `PasswordBox` 透過 `SyncPassphrase_Changed` 即時推進 ViewModel；上次同步以 `↓N ↑M ⚠K` 一行顯示。
- **`Assetra.WPF/Features/Settings/SettingsViewModel.cs`（修改）**：建構子加入 `SyncSettingsViewModel sync`，暴露 `Sync` 屬性供 XAML `DataContext="{Binding Sync}"`。
- **`Assetra.WPF/Infrastructure/ServiceCollectionExtensions.cs`（修改）**：新增 `AddAssetraSync(dataDir)`，註冊 `IConflictResolver`（`LastWriteWinsResolver`）+ `SyncCoordinator`；`AddAssetraShell` 加入 `SyncSettingsViewModel`。
- **`Assetra.WPF/Infrastructure/BudgetServiceCollectionExtensions.cs`（修改）**：拆 `CategorySqliteRepository` 為 concrete singleton，`ICategoryRepository` / `ICategorySyncStore` 兩個 interface 都解析到同一個 instance（避免兩條 sync state 不一致）。
- **`Assetra.WPF/Infrastructure/AppBootstrapper.cs`（修改）**：`AddAssetraSync(paths.DataDir)` 接到 host pipeline。
- **`Assetra.WPF/Languages/zh-TW.xaml` / `en-US.xaml`（修改）**：11 組新 sync UI 文字 key。

### 設計取捨

- **密語永遠不持久化**：`AppSettings` 只存 salt，密語存活區間 = ViewModel 屬性 + KDF 計算 + AES key 派生，`finally` block 清空。代價是每次「立即同步」都要重輸；但避免了密語落 disk → 樣本攻擊風險。Salt 持久化是必要的（不存就沒辦法跨次同步派生同一把 key）。
- **`device_id` 自動產生**：首次同步時 `Guid.NewGuid()` 寫進 settings；使用者不需手動配置。Trade-off：本地 DB 重置 / settings 清空後會視為新裝置（需要重新處理 conflict），但這是預期語意。
- **`SyncCoordinator` 放在 `Assetra.WPF/Infrastructure/`**：需要組合 `Application/Sync/SyncOrchestrator` + `Infrastructure/Sync/Http*` + `Infrastructure/Sync/JsonSyncMetadataStore`，依賴方向上 Application 不能引 Infrastructure，因此放在 WPF 層做 composition root。
- **JsonSyncMetadataStore atomic-rename**：避免 process crash 在 `Save` 中途留下截斷 JSON。`File.Move(..., overwrite: true)` 在 NTFS 上是原子操作。
- **`CategorySqliteRepository` 共享 instance**：`ICategoryRepository`（用戶端）與 `ICategorySyncStore`（同步端）都指向同一個 concrete singleton，否則兩個 instance 各自快取連線 / TimeProvider 會不一致。
- **背景 timer 不在本 sprint**：v0.20.5 只做手動觸發，先驗證端到端 pipeline 在生產 settings 下能正常運作；自動 sync 排程留 v0.20.6 接 conflict UI 一起做。
- **無 `Microsoft.Extensions.Http` 依賴**：`SyncCoordinator` 接受 `Func<HttpClient>?` 而非 `IHttpClientFactory`，避免為單一 sync 場景拉整個 IHttpClientFactory pipeline。預設 `new HttpClient()` 在 sync 結束時 dispose。

### 測試

- 6/6 `JsonSyncMetadataStoreTests`（missing file → empty / roundtrip / overwrite / 不留 .tmp / 空 path / 空 deviceId）。
- 5/5 `SyncCoordinatorTests`（disabled throws / 空 backend throws / 空密語 throws / 首次 sync 產 device id + salt / 已有 id 不重產）。
- 4/4 `SyncSettingsViewModelTests`（Reload 從 settings 帶值 / SyncNowCommand CanExecute 邏輯 × 2 / SaveSettings trim 並寫回）。
- 既有 `SettingsViewModelTests` 補 `SyncSettingsViewModel` 建構子參數（不影響 assertion）。
- 784/784 全測試綠（+15）。

### 後續 sprint 預告

- **v0.20.6**：Conflict 解決面板（消費 `DrainManualConflicts`）+ 背景 sync timer（每 N 分鐘觸發一次 `SyncCoordinator.SyncAsync`，需要密語快取或自動降級為「只 pull」）。
- **v0.20.7**：第二個 entity 接線（建議 `Trade`），同時抽 `CompositeLocalChangeQueue` 路由 by `EntityType`。
- **v0.20.8+**：Cloudflare Workers + R2 reference backend + deployment guide。

## v0.20.4 - 2026-04-28

雲端同步首個 entity 接線：**Category**（`ExpenseCategory`）。SQLite schema 補上 sync 欄位、Repository 在 mutation 時 bump version 並寫 outbox flag、`Remove` 改為 tombstone（soft delete）。新增 `ICategorySyncStore` / `CategorySyncMapper` / `CategoryLocalChangeQueue`，串起 SyncOrchestrator → InMemoryCloudSyncProvider → 真 SQLite 端到端。

### 變更

- **`Assetra.Infrastructure/Persistence/CategorySchemaMigrator.cs`（修改）**：以 `pragma_table_info` 偵測缺欄、`ALTER TABLE` 補上 `version` / `last_modified_at` / `last_modified_by_device` / `is_deleted` / `is_pending_push` 五欄；新增 `idx_category_pending` 索引。既有 DB 不需資料遷移腳本（DEFAULT 0 / 空字串自動帶值）。
- **`Assetra.Core/Interfaces/Sync/ICategorySyncStore.cs`（新增）**：sync 端對 Category 倉庫的窄介面——`GetPendingPushAsync` / `MarkPushedAsync` / `ApplyRemoteAsync`。獨立於 `ICategoryRepository`，避免 ViewModel 看到 sync metadata。
- **`Assetra.Infrastructure/Persistence/CategorySqliteRepository.cs`（修改）**：建構子加 `deviceId`（預設 `"local"`）+ `TimeProvider`（預設 `System`）；`AddAsync` / `UpdateAsync` 寫 `version` / `last_modified_*` / `is_pending_push = 1`，`UpdateAsync` 用 `version = version + 1` SQL bump；`RemoveAsync` 改 soft delete；`GetAllAsync` / `GetByIdAsync` / `AnyAsync` 過濾 `is_deleted = 0`。同類別實作 `ICategorySyncStore`：`GetPendingPushAsync` `WHERE is_pending_push = 1`、`ApplyRemoteAsync` 用 `INSERT … ON CONFLICT DO UPDATE` upsert，**版本回退保護**（incoming.Version ≤ stored.Version 時跳過）。
- **`Assetra.Infrastructure/Sync/CategorySyncMapper.cs`（新增）**：`ExpenseCategory` ↔ `SyncEnvelope` JSON。snake_case + 顯式 `JsonPropertyName`、`UnsafeRelaxedJsonEscaping` 保留 CJK 原字（避免 `\uXXXX` 體積膨脹；外層加密本來就會 base64 包覆，HTML escape 沒必要）。Tombstone payload 為空字串。
- **`Assetra.Application/Sync/CategoryLocalChangeQueue.cs`（新增）**：`ILocalChangeQueue` 實作；pass-through 到 `ICategorySyncStore`。`RecordManualConflictAsync` 暫存記憶體 list、`DrainManualConflicts` 給 v0.20.5 conflict UI 拉取。

### 設計取捨

- **不改 `ExpenseCategory` record**：sync 欄位（version / device / timestamp）只活在 SQL 層 + envelope 層；ViewModel / RowViewModel / 業務邏輯都看不到。代價是 repo 內要 read-modify-write 處理 version bump（SQL 用 `version = version + 1` 一次 round trip 解決）。換來的是 ZERO ripple change：DI 配置、ViewModel、既有 769 條測試都不需改動。
- **Soft delete 而非 hard delete**：刪除必須能 push 給其他裝置（否則該裝置永遠不知道刪除事件）。Tombstone row 留下 `id` + `version` + `is_deleted=1`，payload 清空節省空間。`GetAllAsync` filter `is_deleted = 0` 對 ViewModel 透明。
- **Outbox 用 row-level flag 而非獨立 table**：`is_pending_push` 加在 entity row 上，`MarkPushedAsync` 是單欄 UPDATE。優點是 zero-overhead、無 join、無 outbox-row vs entity-row 一致性問題；缺點是 outbox 與 entity lifecycle 綁死（刪除 entity 也刪 outbox 紀錄）—— 對 Category 這類 user-managed entity 是想要的行為。Trade / Asset 等高頻 entity 若日後需要批次 push 再考慮獨立 outbox table。
- **ApplyRemote 防回退**：先 `SELECT version` 比對，incoming ≤ stored 直接跳過。LWW resolver 已處理大部分衝突，但 race / 二次 push 可能讓舊 envelope 又流過，本層保險。
- **`deviceId` 暫用 `"local"` 預設值**：v0.20.5 才會在 Settings 產生並持久化 device GUID；本 sprint 先讓現有 DI 配置不破，預設值在單機 / 雙裝置混用前都安全（push 時被覆蓋為實際 device id）。
- **`CategoryLocalChangeQueue` 直接綁 Category 一個 store**：v0.20.4 只接一個 entity，dispatch logic 多餘。多個 entity 接上後再抽 `CompositeLocalChangeQueue` 路由 by `EntityType`。
- **`ICategorySyncStore` 在 Core 而非 Infrastructure**：是 domain-level 介接契約（給 Application 層的 `CategoryLocalChangeQueue` 用），實作放 Infrastructure；遵循依賴方向 `Core ← Infrastructure / Application`。

### 測試

- 5/5 `CategorySyncMapperTests`（roundtrip / tombstone / 拒絕 tombstone payload / 拒絕錯 EntityType / snake_case 欄名）。
- 9/9 `CategorySqliteRepositorySyncTests`（add stamps v1 + pending / update bumps version / remove → tombstone / mark pushed clears flag / apply remote inserts / 防回退 / tombstone 刪除本地 / 未知 id tombstone 安全寫入 / GetAll filter tombstone）。
- 5/5 `CategoryLocalChangeQueueTests`（pass-through delegation / manual conflict in-memory + drain / null guards）。
- 2/2 `CategoryEndToEndSyncTests`（雙 SQLite + InMemoryCloud 端到端：device A add → device B 看到；device A delete → device B 跟著刪）。
- 769/769 全測試綠（+21）。

### 後續 sprint 預告

- **v0.20.5**：Settings → Sync UI（密語輸入 → KDF → AES-GCM key、後端 URL / token、`device_id` 產生並持久化、上次同步時間 / 拉推件數顯示、`DrainManualConflicts` 接 conflict 解決面板、背景 sync timer）。
- **v0.20.6+**：第二、第三個 entity 接線（建議 `Trade` → `Asset`），抽 `CompositeLocalChangeQueue`。
- **v0.20.7+**：Cloudflare Workers + R2 reference implementation（純 JS）+ deployment 文件。

## v0.20.3 - 2026-04-28

雲端同步流程編排：把 `ICloudSyncProvider` + `ILocalChangeQueue` + `ISyncMetadataStore` + `IConflictResolver` 串成完整 pull → resolve → push → save 的 `SyncOrchestrator`（Application 層）。**零 NuGet、零 schema migration、零 entity 動工** —— 本 sprint 只交付 orchestration 邏輯與 in-memory 替身；接到真 entity 留 v0.20.4。

### 變更

- **`Assetra.Core/Interfaces/Sync/ISyncMetadataStore.cs`（新增）**：`GetAsync` / `SaveAsync` 兩方法；本裝置 `SyncMetadata` 持久化抽象。實作可選 AppSettings JSON 或獨立 keystore。
- **`Assetra.Core/Interfaces/Sync/ILocalChangeQueue.cs`（新增）**：本端 entity ↔ envelope 橋樑——`GetPendingAsync` / `MarkPushedAsync` / `ApplyRemoteAsync` / `RecordManualConflictAsync`。orchestrator 與「真倉庫 + 變更 outbox」之間的抽象，v0.20.3 只提供 in-memory 實作給測試。
- **`Assetra.Application/Sync/SyncResult.cs`（新增）**：`(PulledCount, PushedCount, AutoResolvedConflicts, ManualConflicts, CompletedAt)`，給 UI 顯示「上次同步：拉 X 推 Y、Z 件待解決」。
- **`Assetra.Application/Sync/SyncOrchestrator.cs`（新增）**：核心流程——
  1. 讀 metadata。
  2. Pull → `ApplyRemoteAsync` 寫入本端。
  3. `GetPendingAsync` → `PushAsync`。
  4. `MarkPushedAsync` 已接受 ids。
  5. Conflicts 套 resolver：`KeepLocal` 以 `remote.Version+1` 重 push 一次（單次重試）；`KeepRemote` 寫 ApplyRemote；`Manual` 進 RecordManualConflict。
  6. 更新 cursor + LastSyncAt → SaveAsync。
- **`Assetra.Infrastructure/Sync/InMemorySyncMetadataStore.cs`（新增）**：lock + 單一 `SyncMetadata` 欄位，給測試 / orchestrator 開發替身。
- **`Assetra.Infrastructure/Sync/InMemoryLocalChangeQueue.cs`（新增）**：pending / applied remotes / manual conflicts 三 list；`MarkPushedAsync` 從 pending 移除對應 entity。`AppliedRemotes` 與 `ManualConflicts` 為 public list 讓測試直接 inspect。

### 設計取捨

- **先做 orchestrator、暫不接 entity**：entity 接線涉及 SQLite schema migration（每 entity 加 `Version` / `LastModifiedAt` / `LastModifiedByDevice` 欄位）+ repository 改寫 + integration tests，是獨立大話題。orchestrator 是先決條件，把 pull/push/conflict 流程穩定下來，entity 才有東西可接。
- **Conflict 重試僅一次**：`KeepLocal` 衝突→bumped version→重 push；若再衝突直接進 manual queue，避免無限 retry。第二輪是「server 又被別的裝置改」的少見情況，UI 介入較合理。
- **`ILocalChangeQueue` mutation 須 idempotent**：`ApplyRemoteAsync` / `MarkPushedAsync` 若被重跑須無副作用——orchestrator 失敗策略是 fail-fast（任何一步 throw 直接冒，caller 決定 retry / toast），重跑時不該寫部分狀態。in-memory 實作以 list append + remove-by-id 達成。
- **Cursor 推進策略**：優先用 `push.NextCursor`，否則 `pull.NextCursor`，否則保留原 cursor。push 也可能推進 cursor 是因為某些 provider（如 R2 + Workers）會在 push response 回傳新游標。
- **`TimeProvider` 注入**：所有 `now` 取得走 `TimeProvider.GetUtcNow()`，測試用 `FixedTimeProvider`（內嵌於 test class）固定時間驗證 LastSyncAt 設定。
- **`SyncResult` 用 count 而非 list**：本層輸出給 UI 摘要，envelope 細節已在 queue / store 內；count 比 list 輕、序列化便宜，需要明細可從 `InMemoryLocalChangeQueue.AppliedRemotes` 等 inspect。

### 測試

- 9/9 `SyncOrchestratorTests`（pull-only / push-only / mixed / KeepLocal 重試 / KeepRemote adopt / Manual 記錄 / 二輪 conflict 進 manual / no-changes 仍存 metadata / null guards）。`FlakyAlwaysConflictProvider` 內嵌 fake 模擬 server 永遠拒絕 push，驗證 retry 不會無限循環。
- 748/748 全測試綠（+9）。

### 後續 sprint 預告

- **v0.20.4**：把 sync 接到第一個 entity（建議 Category——欄位少、無金額、影響面最小）→ SQLite schema migration + repository 帶 `EntityVersion.Bump` + entity ↔ envelope mapper + integration tests。
- **v0.20.5**：Settings → Sync UI（密語 → KDF → AES-GCM key、後端 URL / token、同步狀態 / 上次同步時間、conflict 介入 UI）。

## v0.20.2 - 2026-04-28

雲端同步 HTTP 線路層：定義後端 wire protocol、實作 `HttpCloudSyncProvider`，**零 NuGet**（BCL `HttpClient` + `System.Text.Json`）。後端候選定為 **Cloudflare R2 + Workers**；契約抽象使後端選擇可隨時換（Supabase / 自架 ASP.NET 都能套同協議）。

### 變更

- **`Assetra.Infrastructure/Sync/Http/SyncWireDtos.cs`（新增，internal）**：snake_case JSON DTOs（`EntityVersionWireDto` / `EnvelopeWireDto` / `PullResponseDto` / `PushRequestDto` / `ConflictWireDto` / `PushResponseDto`）。屬性以 `JsonPropertyName` 顯式映射，避免 .NET naming policy 影響 wire 格式。
- **`Assetra.Infrastructure/Sync/Http/SyncEndpointOptions.cs`（新增）**：`(BaseUrl, AuthToken)` record；空 token 時不送 `Authorization` header（給測試 / 公開後端）。
- **`Assetra.Infrastructure/Sync/Http/HttpCloudSyncProvider.cs`（新增）**：`ICloudSyncProvider` 的 HTTP 實作。`GET /sync/pull[?cursor=…]`、`POST /sync/push`；非 2xx → `HttpRequestException`。Domain ↔ wire DTO 轉換 internal helpers 暴露給測試。本層**不知道加密** —— caller 應在外層用 `EncryptingCloudSyncProvider` 包裝。
- **`docs/architecture/Sync-Wire-Protocol.md`（新增）**：完整 wire protocol 文件（endpoints、conflict 規則、Cloudflare Workers + R2 參考實作 pseudocode、後端替代方案討論）。供使用者實際在 Worker 端實作時參照。

### 設計取捨

- **後端選擇定為 R2 + Workers，但介面保留可換性**：(1) R2 無 egress 費用，個人使用實際免費；(2) 客戶端加密 → server 純 blob 儲存最匹配；(3) Workers 寫 < 200 行 JS、運維負擔小。同時 wire protocol 抽象到 HTTP，未來改 Supabase 只需重寫 server 端、客戶端不動。
- **Wire 用 snake_case JSON**：serverless 後端（Workers / Supabase Edge Functions）的 JS / TS 慣例；以 `JsonPropertyName` 顯式映射避免 .NET 預設 camelCase 影響。
- **Internal DTOs**：wire format 是 implementation detail，domain layer 只看 `SyncEnvelope`；`InternalsVisibleTo("Assetra.Tests")` 已存在，測試不受影響。
- **不在這層做加密**：分離 concerns —— 加密是 `EncryptingCloudSyncProvider` 的職責、HTTP 傳輸是 `HttpCloudSyncProvider` 的職責，兩者用 decorator pattern 組合，可獨立測試 / 替換。
- **錯誤策略：fail fast**：4xx / 5xx 直接 throw，不做內建 retry / circuit breaker。retry 該做但屬 cross-cutting concern，後續若需可以 `Polly` decorator 加上去（而非塞進 provider）。
- **Wire protocol 寫在 docs/architecture，非 docs/planning**：這是架構契約而非 sprint 計畫；未來改後端時應更新此文件。

### 測試

- 8/8 `HttpCloudSyncProviderTests`（無 cursor pull / 帶 cursor pull URL escaping / Bearer auth header / 無 token 不送 auth / 4xx throw / push body shape + conflict 解析 / 5xx throw / null guards）。
- 739/739 全測試綠（+8）。

### 後續 sprint 預告

- **v0.20.3**：把 sync 接到第一個 entity（建議 Trade 或 Category）—— 寫 entity ↔ envelope mapper、entity 寫入時帶 `EntityVersion.Bump`、background sync service 定期 pull/push。
- **v0.20.4**：Settings → Sync UI（密語輸入 → KDF → AES-GCM key 衍生、後端 URL / token 設定、同步狀態顯示、conflict 介入 UI）。

## v0.20.1 - 2026-04-28

雲端同步加密層：AES-256-GCM + PBKDF2-SHA256，全部以 BCL 內建型別實作（**零 NuGet**）。以 decorator 形式包住既有 `ICloudSyncProvider`，後端決定後此層可直接套用、無需改寫。

### 變更

- **`Assetra.Core/Models/Sync/EncryptedPayload.cs`（新增）**：AES-GCM 三元組 record（Nonce / Ciphertext / Tag），純 raw bytes，序列化由上層決定。
- **`Assetra.Core/Interfaces/Sync/IEncryptionService.cs`（新增）**：`Encrypt(span) → EncryptedPayload` / `Decrypt(EncryptedPayload) → byte[]`。實作須使用 AEAD，竄改 / 錯 key 必須 throw。
- **`Assetra.Core/Interfaces/Sync/IKeyDerivationService.cs`（新增）**：`DeriveKey(passphrase, salt, length=32)`，相同輸入 → 相同輸出。
- **`Assetra.Infrastructure/Sync/AesGcmEncryptionService.cs`（新增）**：BCL `System.Security.Cryptography.AesGcm`；32-byte key、12-byte 隨機 nonce（每次加密 `RandomNumberGenerator.Fill`）、16-byte tag。長度 / null guards 完整。
- **`Assetra.Infrastructure/Sync/Pbkdf2KeyDerivationService.cs`（新增）**：BCL `Rfc2898DeriveBytes.Pbkdf2`，預設 600,000 iterations（OWASP 2023 PBKDF2-SHA256 下限）。Salt 至少 16 bytes、key 長度 16–64 bytes。
- **`Assetra.Infrastructure/Sync/EncryptingCloudSyncProvider.cs`（新增）**：`ICloudSyncProvider` decorator。Push 時加密 `PayloadJson` → `base64(nonce ‖ tag ‖ ciphertext)`；Pull 時解密還原。`EntityId` / `EntityType` / `Version` / `Deleted` 維持明文（後端需要它們做索引 / cursor / conflict）。Tombstone（`Deleted=true && Payload=""`）直接 pass-through 不加密。Conflict 從 inner 回來時自動解密 Local + Remote 兩端，呼叫端永遠看到 plaintext。

### 設計取捨

- **PBKDF2 而非 Argon2id**：Argon2 是 memory-hard、抗 GPU 暴力破解較佳，但 BCL 沒有，需 `Konscious.Security.Cryptography` NuGet。本 sprint 嚴守零 NuGet 原則；600k iterations 對個人理財 app 威脅模型已足夠（線下暴力破解單一密語 ~1 秒/嘗試 in CPU）。介面 `IKeyDerivationService` 已抽象化，後續直接換 Argon2 實作即可。
- **只加密 PayloadJson、不加密 metadata**：(1) 後端要靠 `EntityId` / `Version` / `LastModifiedAt` 做 conflict / cursor — 如果這些欄位也加密，server 會降為純 blob 儲存，無法做 server-side conflict pre-check。(2) 這些欄位本身不含金額 / 對手方 / 個資，洩漏成本低。
- **AEAD（GCM）而非 CBC + HMAC**：GCM 一個原語同時提供 confidentiality + integrity；wrong key / 竄改密文 / 改 tag 都會在 `Decrypt` throw `AuthenticationTagMismatchException`（屬 `CryptographicException` 子類），符合 `IEncryptionService` 契約。
- **Nonce 隨機生成 vs counter**：AES-GCM 同 key 下 nonce **絕不可重用**。Counter 模式需要 cross-process / cross-device 的單調保證（複雜）；隨機 12-byte nonce 在每 key 加密 ~2³² 次後才有非可忽略碰撞機率，個人 app 一輩子也用不到，安全簡單。
- **Wire format 封閉在 decorator 內**：`base64(nonce ‖ tag ‖ ciphertext)` 是 implementation detail，沒洩到 `ICloudSyncProvider` 介面，未來改用 protobuf / CBOR 也只動這個 class。

### 測試

- 6/6 `AesGcmEncryptionServiceTests`（roundtrip / nonce 隨機性 / 竄改密文 throw / 錯 key throw / 錯 key length throw / 錯 nonce length throw）。
- 6/6 `Pbkdf2KeyDerivationServiceTests`（同 input 同 output / 不同 passphrase 不同 key / 不同 salt 不同 key / null passphrase / 太短 salt / 太少 iterations）。
- 6/6 `EncryptingCloudSyncProviderTests`（roundtrip / inner store 看不到明文 / 錯 key pull throw / tombstone pass-through / conflict remote 自動解密 / null guards）。
- 731/731 全測試綠（+18）。

### 後續 sprint 預告

- **v0.20.2**：後端選擇 + 第一個真實 `ICloudSyncProvider` 實作（Supabase 或 R2 + Workers）。
- **v0.20.3**：把 sync 接到第一個 entity（建議 Trade / Category）→ end-to-end 走通。
- **v0.20.4**：Settings → Sync UI（密語輸入 → KDF → AES-GCM key、同步狀態、conflict 列表）。

## v0.20.0 - 2026-04-28

雲端同步骨架：domain models + interfaces + 預設策略 + 純記憶體 provider。**零 NuGet、零 schema migration、零 I/O**——本 sprint 僅交付抽象層與測試替身，加密、後端選擇、雙向同步留待 v0.20.1+。

### 變更

- **`Assetra.Core/Models/Sync/EntityVersion.cs`（新增）**：`(Version, LastModifiedAt, LastModifiedByDevice)` 三人組 record，附 `Initial(deviceId, now)` / `Bump(deviceId, now)` factory，預設值代表「從未同步」。
- **`Assetra.Core/Models/Sync/SyncMetadata.cs`（新增）**：本裝置同步狀態：`DeviceId` + `LastSyncAt?` + provider-specific `Cursor?`。
- **`Assetra.Core/Models/Sync/SyncEnvelope.cs`（新增）**：provider-agnostic 傳輸封包：`(EntityId, EntityType, PayloadJson, Version, Deleted)`。Payload 統一 JSON 字串，避免 `ICloudSyncProvider` generic over T。
- **`Assetra.Core/Models/Sync/SyncConflict.cs`（新增）**：本端 / 遠端 envelope 對照，給 resolver 處理。
- **`Assetra.Core/Models/Sync/SyncResolution.cs`（新增）**：`KeepLocal / KeepRemote / Manual` enum。
- **`Assetra.Core/Models/Sync/SyncBatch.cs`（新增）**：`SyncPullResult` / `SyncPushResult` records，含 next cursor 與 conflicts 列表。
- **`Assetra.Core/Interfaces/Sync/IVersionedEntity.cs`（新增）**：標記受同步 entity 暴露 `EntityId` + `Version`；既有 entity **暫不強制實作**（避免大規模 schema 改動）。
- **`Assetra.Core/Interfaces/Sync/ICloudSyncProvider.cs`（新增）**：`PullAsync(metadata) → SyncPullResult` + `PushAsync(metadata, envelopes) → SyncPushResult`。介面 stateless（不持有 device 狀態），方便測試與多租戶。
- **`Assetra.Core/Interfaces/Sync/IConflictResolver.cs`（新增）**：`Resolve(SyncConflict) → SyncResolution`。
- **`Assetra.Infrastructure/Sync/LastWriteWinsResolver.cs`（新增）**：預設策略，`LastModifiedAt` 較新者勝；時間戳同分以 `Version` 較大者勝；完全 tie 時固定回 `KeepRemote`（deterministic）。
- **`Assetra.Infrastructure/Sync/InMemoryCloudSyncProvider.cs`（新增）**：純記憶體 store + 單調遞增 sequence cursor。`PushAsync` 以 `Version` 比較偵測 stale write，被拒寫入回 conflict。給單元 / 整合測試與開發期 dogfood 使用。

### 設計取捨

- **零 schema migration**：v0.20 roadmap 原案要求所有 mutable entity 加 version 欄位，但這會 break 既有 SQLite schema 並影響 ~30 個 repository。本 sprint 改為 opt-in：先建抽象層 + `IVersionedEntity` 標記介面，等實際接 provider 再逐 entity 啟用，降低 sprint 風險。
- **JSON envelope 而非 generic**：`ICloudSyncProvider<T>` 看起來型別安全，但實際 batch 同步必然混合多 entity type。改用 `SyncEnvelope.PayloadJson` + `EntityType` 字串，serialization 由 caller 負責，介面收斂為單一非泛型型別，後端實作（Supabase / R2）也好寫。
- **後端選擇延後**：roadmap F3 Backend 候選有 Supabase / Cloudflare R2 / 自架，這需使用者決策（牽涉成本、隱私、自架運維能力）；本 sprint 只提供 in-memory 替身，介面穩定後再與使用者一起選後端。
- **加密延後**：roadmap F1 AES-256 加密層需 KDF（Argon2id / scrypt）+ 金鑰管理 UI，是獨立大話題。先讓同步邏輯 / 衝突解決成熟，加密 layer 包在 envelope 周圍即可後加。
- **LWW tie-breaker 固定回 remote**：當兩端 `LastModifiedAt` 與 `Version` 完全相同（極罕見、幾乎只在測試中出現），固定回 `KeepRemote` 比 `KeepLocal` 安全：避免 device A 與 device B 都主張本端勝、永遠收斂不到一致狀態。
- **InMemoryCloudSyncProvider 不在 DI 預設註冊**：測試直接 `new`，production code 需 explicit 啟用 cloud sync 後再決定 provider。

### 測試

- 4/4 `EntityVersionTests`（Initial / Bump / null device / default = never-synced）。
- 5/5 `LastWriteWinsResolverTests`（local newer / remote newer / 時間戳同分以 version 決勝 / 完全 tie 回 remote / null guard）。
- 8/8 `InMemoryCloudSyncProviderTests`（empty pull / new push 全收 / stale push 回 conflict / push→pull 跨裝置可見 / cursor-based 增量 pull / bumped version 接受 / null guards）。
- 713/713 全測試綠。

### 後續 sprint 預告

- **v0.20.1**：選定後端 + ICloudSyncProvider 第一個真實實作 + 加密層骨架。
- **v0.20.2**：把 sync 接到第一個 entity（建議 Trade / Category，影響面小）→ end-to-end 走通。
- **v0.20.3**：Settings → Sync UI（登入 / 密語 / 同步狀態 / conflict 列表）。

## v0.19.4 - 2026-04-28

PDF / OCR 匯入收尾：把 v0.19.2 的 `TesseractOcrAdapter` 接到 settings UI，使用者可在「設定 → OCR」自行指定 tessdata 路徑與語言代碼，無需重啟即可生效。Per-bank PDF pattern 待真實樣本檔（國泰 / 玉山 / 中信）收集後再做。

### 變更

- **`Assetra.Core/Models/AppSettings.cs`**：新增 `OcrTessdataPath = ""` 與 `OcrLanguage = "eng"` 兩個 record 末端 optional 欄位（向後相容）。
- **`Assetra.WPF/Features/Settings/SettingsViewModel.cs`**：新增 `OcrTessdataPath` / `OcrLanguage` `[ObservableProperty]`；`LoadFromSettings` / `SaveAsync` 讀寫對應欄位；`On*Changed` 觸發自動儲存；`BrowseOcrTessdataCommand` 開 `Microsoft.Win32.OpenFolderDialog` 讓使用者選 tessdata 資料夾。
- **`Assetra.WPF/Features/Settings/SettingsView.xaml`**：新增「OCR (PDF 對帳單匯入)」section（tessdata 路徑 + Browse + 語言代碼），放在資料 section 之後。
- **`Assetra.WPF/Languages/zh-TW.xaml` / `en-US.xaml`**：新增 `Common.Browse`、`Settings.Section.Ocr`、`Settings.Ocr.TessdataPath` (+ Hint)、`Settings.Ocr.Language` (+ Hint)。
- **`Assetra.Infrastructure/Import/ImportParserFactory.cs`**：將 `IOcrAdapter?` 改為 `Func<IOcrAdapter?>?` 動態 factory，每次 `Create(...)` 重新解析 → 設定變更後立即生效；保留 `(IPdfStatementParser?, IOcrAdapter?)` 重載維持向後相容。
- **`Assetra.WPF/Infrastructure/ImportServiceCollectionExtensions.cs`**：註冊 `Func<IOcrAdapter?>`，每次 invoke 讀取 `IAppSettingsService.Current`、檢查 tessdata 目錄存在後構造 `TesseractOcrAdapter`，否則回 null（圖片頁 PDF 自然產出 0 列、不 crash）。

### 設計取捨

- **動態 OCR factory 而非靜態註冊**：使用者通常在設定 OCR 路徑後不會重啟 app；以 `Func<IOcrAdapter?>` 注入讓 `ImportParserFactory.Create` 每次重新解析，比 hot-reloadable singleton 簡單得多，也不需在 settings 變更時推播。
- **路徑無效時靜默回 null**：Tesseract 初始化失敗會 throw native exception；factory 包 try/catch 直接 fallback null，配合既有「沒有 OCR 時圖片頁略過」邏輯，使用者最差只會看到「圖片 PDF 沒抓到資料」而非崩潰。
- **延後 per-bank patterns**：手上沒有真實對帳單樣本，speculative regex 無法驗證、容易變死碼。等使用者實際匯入時再依失敗樣本逆向出 pattern。

### 測試

- 4/4 新增 `ImportParserFactoryTests`（缺 PdfStatementParser → throw、Pdf 路徑每次 invoke OCR factory、factory 回 null 仍產 PdfImportParser、Csv 路徑不 invoke OCR factory）。
- 696/696 全測試綠。

## v0.19.3 - 2026-04-28

PDF / OCR 匯入第四步：pipeline 整合與 UI surface。把前三 sprint 交付的 `IPdfStatementParser` / `IOcrAdapter` / `PdfRowExtractor` 串成 `IImportParser` 實作，wire 進 `ImportParserFactory`、`ImportFormatDetector`、`ImportViewModel`，並讓低信心 OCR 列在 preview grid 標紅。至此 PDF 對帳單可走完上傳 → 偵測 → 解析 → 預覽 → 衝突 → Apply 完整流程。

### 變更

- **`Assetra.Core/Models/Import/PdfPage.cs`**：新增 `byte[]? ImageBytes = null`，由 `PdfPigStatementParser` 為純圖片頁面填入（PNG 優先、否則 raw bytes），讓上層 OCR pipeline 取得頁面影像。
- **`Assetra.Core/Models/Import/ImportPreviewRow.cs`**：新增 `double? OcrConfidence = null`（end-of-record，向後相容）。CSV / Excel / 文字 PDF 一律 null；OCR 來源頁面才帶值。Reconciliation context 共用此型別，新增 optional 欄位不影響既有 binding。
- **`Assetra.Infrastructure/Import/Pdf/PdfPigStatementParser.cs`**：純圖片頁面額外抽取第一張內嵌圖（`IPdfImage.TryGetPng` → fallback `RawMemory.ToArray()`）並寫入 `PdfPage.ImageBytes`。
- **`Assetra.Infrastructure/Import/Pdf/PdfRowExtractor.cs`（移自 Application）**：將 OCR 頁面的 `OcrConfidence` 連帶寫入產出的 `ImportPreviewRow.OcrConfidence`，讓 UI 能標紅。命名空間從 `Assetra.Application.Import.Pdf` 改為 `Assetra.Infrastructure.Import.Pdf`（Infrastructure 不能引用 Application；改放同一層更貼近 PdfImportParser）。
- **`Assetra.Infrastructure/Import/Pdf/PdfRowPatterns.cs`（新增）**：內建 `Generic` pattern（`yyyy-MM-dd ＜對手方＞ ＜金額＞`），`For(format)` 對應銀行 / 券商 PDF 的 future extension point。
- **`Assetra.Infrastructure/Import/Pdf/PdfImportParser.cs`（新增）**：`IImportParser` 實作，串接 `IPdfStatementParser` → 可選的 `IOcrAdapter`（image-only 頁面以 `RecognizeAsync(ImageBytes, ct)` 補回文字 + 信心分數）→ `PdfRowExtractor.Extract`。`Format` / `FileType=Pdf` 由建構子帶入。
- **`Assetra.Infrastructure/Import/ImportParserFactory.cs`**：新增 `ImportParserFactory(IPdfStatementParser?, IOcrAdapter?)` 建構子；`Create(format, ImportFileType.Pdf)` 走 `PdfImportParser` 路徑；未注入 PDF parser 時 throw 指引訊息。Csv / Excel 路徑與既有 API 完全相容（保留 parameterless ctor）。
- **`Assetra.Infrastructure/Import/ImportFormatDetector.cs`**：新增 PDF magic-byte 檢查（`%PDF-`），命中時回 `ImportFormat.Generic`、不命中視為損毀檔回 `null`；既有 Csv / Excel header 偵測流程不變。
- **`Assetra.WPF/Features/Import/ImportViewModel.cs`**：`FileFilters` 新增 PDF；`ResolveFileType` 接 `.pdf` → `ImportFileType.Pdf`；錯誤訊息更新為「Only .csv, .xlsx, .xls and .pdf」。
- **`Assetra.WPF/Features/Import/ImportRowViewModel.cs`**：新增 `OcrConfidence` / `OcrConfidenceDisplay` / `IsLowOcrConfidence`（< 0.85）binding；`StatusTag` 在無衝突但 OCR 信心偏低時回 `"warning"`，讓 preview grid 自動標黃 / 紅。
- **`Assetra.WPF/Infrastructure/ImportServiceCollectionExtensions.cs`**：DI 註冊 `IPdfStatementParser → PdfPigStatementParser`，並把它（以及可選 `IOcrAdapter`）餵進 `ImportParserFactory`。`IOcrAdapter` 暫不註冊預設實作（caller 需自行配置 tessdata 路徑後再 `AddSingleton<IOcrAdapter>(...)`）。

### 設計取捨

- **OcrConfidence 加在 ImportPreviewRow 為 optional**：原型別跨 Importing / Reconciliation context 共用，新欄位放在 record 末端、預設 null，既有呼叫者完全不用動；同時讓 UI 不需另存 page-row mapping 即可 binding。
- **PdfRowExtractor 移到 Infrastructure**：原放 Application 是依 v0.18 / v0.19.0 「純函式服務 → Application」慣例；但 Infrastructure 引用 Application 違反 dependency direction，且本 extractor 純 Core 模型操作、沒有實際的 application-level 邏輯。移到 Infrastructure 與 PdfImportParser / PdfPigStatementParser 同層，dependency clean。
- **ImportFormatDetector 不解析 per-bank PDF 指紋**：目前無實際樣本檔可建指紋；magic-byte 確認後一律 fallback `Generic`，配合 `PdfRowPatterns.Generic` 通用 regex。實際銀行 PDF（國泰 / 玉山 / 中信 …）需另收集樣本後再加 per-bank `PdfRowPattern`。
- **IOcrAdapter 不在 DI 預設註冊**：Tesseract 需 caller 提供 tessdata 路徑與 traineddata 檔案，若預設註冊將造成 runtime crash；改由 user 在 settings 設定 OCR 後手動 wire。沒有 OCR adapter 時，PDF 圖片頁面自然產出 0 列（不會 crash）。
- **IsLowOcrConfidence 門檻 0.85**：Tesseract 在乾淨掃描下 mean confidence 通常 ≥ 0.85；< 0.85 表示頁面品質欠佳、值得使用者人工確認。`PdfRowPattern.MinOcrConfidence` 預設 0.7 是「低於這值整頁略過」的硬門檻；UI 紅標的 0.85 是「軟提示」門檻，兩者各司其職。
- **不重寫 Csv / Excel 路徑**：本 sprint 嚴格只新增 PDF 鏈路；既有 67/67 ImportFormatDetector / ConfigurableCsvParser / ConfigurableExcelParser 測試全綠，沒有 regression 風險。

### 測試

- 8/8 `PdfImportParserTests` 綠（純文字無 OCR call、image+OCR enhances、低信心被 extractor 過濾、image 無 bytes 不 OCR、null OCR adapter 不 throw、null guard、Format / FileType 公開）。
- 3/3 `ImportFormatDetectorPdfTests` 綠（magic-byte 命中、wrong magic 回 null、stream position 還原）。
- 既有 13 `PdfRowExtractorTests` + 6 `PdfPigStatementParserTests` + 8 `TesseractOcrAdapterTests` 都照舊綠。
- 692/692 全測試綠。

## v0.19.2 - 2026-04-28

PDF / OCR 匯入第三步：Tesseract.NET 離線 OCR adapter。引入 `Tesseract 5.2.0` NuGet（含 native libtesseract binding），實作 `IOcrAdapter`。本 sprint 只交付 adapter 與單元測試；實際 tessdata 部署、image 抽取（從 PdfPig 取出 PDF 圖片頁的 raw bytes）、pipeline 整合留待 v0.19.3。

### 變更

- **`Directory.Packages.props` / `Assetra.Infrastructure.csproj`**：新增 `Tesseract 5.2.0` 套件參考。
- **`Assetra.Infrastructure/Import/Pdf/TesseractOcrAdapter.cs`（新增）**：
  - 建構子接受 `tessdataPath` + `language`（預設 `"eng"`，多語言用 `+` 串接如 `"eng+chi_tra"`）。
  - `RecognizeAsync(ReadOnlyMemory<byte>, ct)`：先 `ToArray()` 拷貝（避免 caller 修改原 buffer），交給 `Task.Run` 執行（Tesseract API 為同步、CPU-bound，不可阻塞 UI thread）。
  - 內部 `Recognize` 呼叫 `TesseractEngine` + `Pix.LoadFromMemory` + `engine.Process`，讀取 `GetText()` 與 `GetMeanConfidence()`，組成 `OcrResult`。
  - `ClampConfidence`：把 NaN / 負值夾到 0、超過 1 的夾到 1，防禦 Tesseract 邊界回傳。
  - 內部 constructor + `recognizeOverride` 參數（`internal` + `InternalsVisibleTo("Assetra.Tests")`）讓測試能注入假識別函式，免依賴實際 traineddata。

### 設計取捨

- **不在 sprint 內跑 native end-to-end**：traineddata 動輒 30+ MB，且 Tesseract 對 native binary loader 路徑敏感；每次 CI run 帶 traineddata 不划算。改用 `recognizeOverride` 注入測試替身，把 adapter 的入口邏輯（null/empty guard、cancellation、ToArray、Task.Run）獨立驗證；實際 OCR roundtrip 留 v0.19.3 在 ImportPipeline 整合測試做（手動 fixture）。
- **不暴露 `TesseractEngine` 給 caller**：保持 `IOcrAdapter` 為唯一抽象；換引擎（Azure Vision / Google Vision）只需另實作 `IOcrAdapter`，不需動 caller。
- **bytes 拷貝而非 zero-copy**：`Task.Run` 的 closure 跨 thread；若直接傳 `ReadOnlyMemory<byte>` 而 caller 在 worker 執行前修改原 buffer 會造成 data race。`ToArray()` cost 比資料正確性低很多。
- **不集中 DI**：Adapter 為「需要使用者選 tessdata 路徑」的設定型 service，留待 v0.19.3 視 UI 設定面板需求再 wire 進 DI；目前 caller 直接 `new TesseractOcrAdapter(...)`。

### 測試

- 8/8 `TesseractOcrAdapterTests` 綠（覆蓋 null/empty path、empty language、empty image、bytes pass-through、pre-cancelled token、background thread offload、buffer 拷貝隔離）。
- 681/681 全測試綠。

## v0.19.1 - 2026-04-28

PDF / OCR 匯入第二步：PdfPig 文字模式 concrete impl。引入 `PdfPig 0.1.10` NuGet（純 .NET、無外部相依），實作 `IPdfStatementParser`。本 sprint 仍不碰 OCR 與 UI；圖片模式頁面僅標記為 `PdfPageSource.Ocr` 並回傳空字串，留待 v0.19.2+ 補上 Tesseract 實作後串接。

### 變更

- **`Directory.Packages.props` / `Assetra.Infrastructure.csproj`**：新增 `PdfPig 0.1.10` 套件參考。
- **`Assetra.Infrastructure/Import/Pdf/PdfPigStatementParser.cs`（新增）**：
  - 用 `PdfDocument.Open(byte[])` + `ContentOrderTextExtractor.GetText(page)` 抽取排序後的頁面文字。
  - 文字非空白 → `PdfPageSource.Text`；文字全空白且頁面有 image XObject → `PdfPageSource.Ocr`（空字串、`OcrConfidence: null`，由 caller 串接 `IOcrAdapter`）。
  - 文字全空白且無圖片 → 仍回 `PdfPageSource.Text`（純空白頁，例如 PDF cover）。
  - 串流先複製到 `byte[]`（PdfPig 需要 seekable input）；既有 `MemoryStream` 直接 `ToArray()` 避免重複拷貝。
  - `PageIndex` 為 0-based（PdfPig 的 `Page.Number` 是 1-based）。

### 設計取捨

- **不碰 OCR**：PdfPigStatementParser 只處理「能直接 extract 文字」的頁面；圖片頁交由後續 `IOcrAdapter` 實作補。這讓 v0.19.1 可獨立交付：純文字 PDF 對帳單已可直接走完 parse → row extract → preview pipeline。
- **不擴 `ImportFormatDetector`**：既有 detector 是 bank-fingerprint（Cathay / Esun / 富邦…）而非 file-type detection；PDF 還沒實際樣本檔可建指紋，等 v0.19.x 整合 UI 時再加。
- **stream → byte[] 緩衝**：PdfPig 內部需 random access；非 seekable stream 先 `CopyToAsync` 進 `MemoryStream` 再 `ToArray()`，避免改寫成 `Stream` 抽象。
- **空白頁不算錯誤**：實務上 PDF 對帳單常有頁尾廣告 / 空白封底；空白頁回傳空 Text 而非 throw，由 `PdfRowExtractor` 自然略過。

### 測試

- 6/6 `PdfPigStatementParserTests` 綠（覆蓋單頁、多頁 PageIndex、空白頁、non-seekable stream、null guard、cancellation）。
- 用 `PdfDocumentBuilder` + Standard14 Helvetica 在記憶體中合成 PDF round-trip，無外部 fixture 檔案。
- 673/673 全測試綠。

## v0.19.0 - 2026-04-28

PDF / OCR 匯入的領域層骨架：建立 `IPdfStatementParser` / `IOcrAdapter` 介面 + `PdfPage` / `OcrResult` / `PdfRowPattern` 中介模型 + `PdfRowExtractor` 純函式行擷取器。本 sprint 不引入 PdfPig / Tesseract.NET NuGet，具體 parser 與 OCR adapter 實作留待 v0.19.1+；既有 CSV / Excel 匯入路徑完全不受影響。

### 變更

- **`Assetra.Core/Models/Import/ImportFileType.cs`**：新增 `Pdf` 列舉值（銀行 / 信用卡 PDF 對帳單，文字模式直抽、圖片模式經 OCR）。
- **`Assetra.Core/Models/Import/PdfPage.cs`（新增）**：單頁中介結構（PageIndex / Text / Source / OcrConfidence）；`PdfPageSource` 區分 Text vs Ocr。
- **`Assetra.Core/Models/Import/OcrResult.cs`（新增）**：OCR adapter 回傳結果（Text + 0~1 平均信心分數）。
- **`Assetra.Core/Models/Import/PdfRowPattern.cs`（新增）**：行級擷取規則（`Regex LinePattern` 命名群組 `date` / `amount` / `counterparty` / `memo` + `DateFormat` + `MinOcrConfidence` 預設 0.7 + `PreserveSign`）。
- **`Assetra.Core/Interfaces/Import/IPdfStatementParser.cs`（新增）**：`ExtractPagesAsync(Stream, ct)` — 文字模式直接 extract，圖片模式委派給 `IOcrAdapter`。
- **`Assetra.Core/Interfaces/Import/IOcrAdapter.cs`（新增）**：`RecognizeAsync(ReadOnlyMemory<byte> imageBytes, ct)` — 純抽象，未綁定具體引擎。
- **`Assetra.Application/Import/Pdf/PdfRowExtractor.cs`（新增）**：純函式 static class — `Extract(IReadOnlyList<PdfPage>, PdfRowPattern) → IReadOnlyList<ImportPreviewRow>`；逐頁逐行套用 regex、解析日期 / 金額（含千分位）、可選對手方 / 摘要、低信心 OCR 頁面整頁略過、`PreserveSign=false` 取絕對值。

### 設計取捨

- **無 NuGet 依賴**：v0.19.0 僅交付介面與純函式擷取器；PdfPig 文字抽取與 Tesseract.NET OCR 留待 v0.19.1+ 在 `Assetra.Infrastructure.Import.Pdf` 補上。`IPdfStatementParser` / `IOcrAdapter` 可由 caller 自行實作以保持測試替身彈性。
- **不入 DI**：沿用 v0.16 / v0.17 / v0.18 領域骨架慣例，`PdfRowExtractor` 為純函式 static、由 caller 直接呼叫；DI wiring 留待 v0.19.x UI 整合時再加。
- **OCR 信心門檻整頁過濾**：低於 `MinOcrConfidence` 的 OCR 頁面整頁略過，而非逐行篩選；簡化判斷且符合「整頁掃描品質決定可信度」的實務。`PdfPageSource.Text` 不受門檻影響。
- **Regex 命名群組為契約**：`date` / `amount` 為必要群組，`counterparty` / `memo` 為可選群組；caller 可針對不同銀行 / 券商提供不同 `PdfRowPattern`。

### 測試

- 13/13 `PdfRowExtractorTests` 綠（覆蓋 empty、單頁解析、跳過空白／不匹配、跨頁 RowIndex 連號、OCR 信心門檻、null confidence 視為 0、Text 不受門檻、PreserveSign=false、無效日期、千分位金額、可選 memo 群組、null guard）。

## v0.18.0 - 2026-04-28

稅務模組 MVP 的領域層骨架：建立 `TaxSummary` / `DividendIncomeRecord` / `CapitalGainRecord` 領域模型 + `TaxCalculationService` 純函式聚合器。本 sprint 聚焦於零依賴的稅務計算層，schema migration、PDF/CSV 匯出、TaxView UI 留待 v0.18.1+。

### 變更

- **`Assetra.Core/Models/TaxRecords.cs`（新增）**：
  - `DividendIncomeRecord`：單筆股利所得（TradeId / Date / Symbol / Exchange / Country / Amount / IsOverseas）。
  - `CapitalGainRecord`：單筆已實現資本利得（從 `Sell` 的 `RealizedPnl` 抽取）。
  - `TaxSummary`：年度彙整 — 國內/海外股利、國內/海外資本利得、海外所得合計、AMT 申報門檻觸發旗標、明細列表。
- **`Assetra.Application/Tax/TaxCalculationService.cs`（新增）**：
  - `CalculateForYear(year, trades)`：純函式，遍歷 trade journal、依 `TradeType` 與 `StockExchangeRegistry.Country` 拆分國內 / 海外，聚合 `TaxSummary`。
  - `AmtDeclarationThreshold = 1_000_000m`：個人海外所得 100 萬 NTD 申報門檻；`OverseasIncomeTotal >= threshold` 時 `TriggersAmtDeclaration = true`。
  - 未知 Exchange 預設視為本國（`"TW"`），讓既有資料不會誤判為海外。
  - `Sell` 必須有 `RealizedPnl`（`null` 視為尚未填，不入帳）；其他 TradeType（Buy / Deposit / Income / Loan / CreditCard …）不屬於本服務範圍。

### 設計取捨

- **無 service interface**：沿用 v0.16.0 / v0.17.0 領域骨架慣例，純函式 static class、不入 DI、由 caller 直接呼叫。介面 / DI / repository 留待 v0.18.1+ 視 UI 需求再加。
- **無 SQLite schema migration**：`TaxSummary` 為 derived view，不持久化；每次查詢即時從 trade table 算出。若日後需要快照存檔（年度封存）再加。
- **多幣別交給 caller**：本服務不換算匯率。caller 應先用 v0.14 `IMultiCurrencyValuationService` 把 `Trade.CashAmount` / `RealizedPnl` 換成 base currency，再傳入。
- **Country 判定**：透過 `StockExchangeRegistry.TryGet(exchange)?.Country` 解析；TW 為本國，其他國別為海外（NASDAQ → US，HKEX → HK …）。

### 測試

- 13/13 `TaxCalculationServiceTests` 綠（覆蓋 empty、domestic dividend、overseas dividend、mixed、capital gain split、年度濾除、非稅務 trade type、AMT 門檻 (at threshold / below / 跨類加總)、未知 exchange、null guard）。
- 654/654 全測試綠。

## v0.17.5 - 2026-04-28

第三輪 pre-v0.17 refactor 稽核：N+1 與 load-then-filter 收斂。新增兩個批次／區間查詢方法（皆 default interface implementation 相容既有 fakes，SQLite 提供 SQL pushdown override），把 Application 層三處 GetAllAsync-then-filter 改為 SQL-level 過濾。

### 變更

- **`IAssetRepository.GetLatestValuationsAsync(IEnumerable<Guid>, ct)` 批次方法**：新增 default implementation（fallback 為逐筆 `GetLatestValuationAsync`）；`AssetSqliteRepository` override 為 `ROW_NUMBER() OVER (PARTITION BY asset_id ORDER BY event_date DESC, rowid DESC)` CTE，單一 SQL 取得所有 asset 的最新 Valuation event。
- **`FinancialOverviewQueryService` 收斂 N+1**：原本對每個 non-cash asset item 在 foreach 內各跑一次 `GetLatestValuationAsync` (含獨立 connection / SQL round-trip)，改為先 `GetLatestValuationsAsync` 取得 lookup dictionary，foreach 內 `ResolveAssetValue` 同步查表。
- **`ITradeRepository.GetByPortfolioEntryIdsAsync(entryIds, ct)`**：新增介面方法 + default impl + `TradeSqliteRepository` SQL `IN ($p0, $p1, ...)` override。
- **`PositionDeletionWorkflowService.DeleteAsync`**：原本 `GetAllAsync` + in-memory `Where(t => entryIds.Contains(...))`，改為 `GetByPortfolioEntryIdsAsync(request.EntryIds, ct)`；`RemoveChildrenAsync` / `RemoveAsync` 也順便補上 ct。
- **`MonthlyBudgetSummaryService.BuildAsync`**：原本 `GetAllAsync` + Year/Month 過濾，改為 `GetByPeriodAsync(monthStart, monthEnd, ct)`，UTC 邊界與 SQL `trade_date BETWEEN` 對齊。
- **`IncomeStatementService.GenerateAsync`**：原本 `GetAllAsync(ct)`，改為 `GetByPeriodAsync(prior.Start, period.End, ct)`，只撈當期 + Prior 等長度比較區間，避免拉全表。

### 校驗（已確認、未動）

- **`CashFlowStatementService`** / **`BalanceSheetService`** 仍保留 `GetAllAsync`：兩者需要「期間以前」的累計現金流 / 資產負債，無法用 `GetByPeriodAsync` 替代；需另行考慮 `GetUpToAsync(asOf)` 但屬延後優化。
- **#5 ConfigureAwait on `await using cmd`** 明確跳過：`SqliteCommand` 的 `DisposeAsync` 在 in-memory native handle 下實質為同步，加 `.ConfigureAwait(false)` 須改寫為兩行扔掉強型別 `cmd` 變數，機械性 churn 大於收益。
- **#3 TradeDeletionWorkflow Sell**, **#7 ImportConflictDetector**：個人財務資料規模下，per-symbol portfolio entry 比對 / per-trade key 建立屬 micro-optimization，新增介面方法擴張 API surface 不划算。

### 測試

- 612/612 tests 綠。批次 valuation / SQL `IN` query 邏輯由現有 `AssetSqliteRepositoryTests` (latest valuation roundtrip) 與 `MonthlyBudgetSummaryServiceTests` (period filter) 間接覆蓋。

## v0.17.4 - 2026-04-28

Pre-v0.17 refactor 稽核 Group C 的 code-quality 部分。VM 拆分 (#7 TransactionDialogViewModel 1194 行 / #8 PortfolioViewModel 1108 行) 無 UI 測試基礎設施下風險過高，以「明確延後」處理而非草率重構，避免 binding regression。

### 變更

- **`AppSettingsService` broad catch 收斂**：`SaveAsync` 的 file IO catch 限縮為 `IOException or UnauthorizedAccessException or JsonException`，`Changed` 訂閱者異常以 `when (ex is not OperationCanceledException)` 過濾掉取消；`LoadSettings` 同樣窄化。
- **`PortfolioBackfillService` swallow 窄化**：價格 fetch catch 改為 `HttpRequestException or TaskCanceledException or InvalidOperationException`；snapshot upsert catch 改為 `DbException or InvalidOperationException`。`OperationCanceledException` 自然向上傳播。
- **`ITradeRepository.GetByPeriodAsync(from, to, ct)`**：新增區間查詢介面方法，提供 default interface implementation（fallback 為 `GetAllAsync` + in-memory filter，相容所有現有 fakes）。`TradeSqliteRepository` override 為 `WHERE trade_date BETWEEN $from AND $to` SQL 條件，避免大資料集全量載入；`from/to` 以 UTC `"o"` 序列化與 BindTradeParams 一致。

### 延後

- **#7 `TransactionDialogViewModel` 拆分**（1194 行）— 與 dialog XAML 大量 two-way binding 緊耦合，無 UI snapshot test，refactor 風險 > 現階段收益。需先補 dialog smoke test 再做。
- **#8 `PortfolioViewModel` 進一步拆分**（1108 行）— v0.17 之前已抽出多個 SubViewModel，剩餘耦合與 UI 行為交織；同樣需要 UI 測試保護才適合動。

### 校驗

- **#12 timestamp format consistency**：稽核全部 `Persistence/*.cs` 後確認已一致——`DateOnly` 用 `"yyyy-MM-dd"`、`DateTime`/`DateTimeOffset` 用 `"o"`，無需修改。

### 測試

- 612/612 tests 綠（無新測，default interface implementation 行為與既有路徑等價，SQL override 由 `TradeSqliteRepositoryTests` 覆蓋既有 GetAllAsync 路徑——後續可補 GetByPeriodAsync 邊界測試）。

## v0.17.3 - 2026-04-28

Pre-v0.17 refactor 稽核 Group B — 兩項中型修整：FX N+1、JSON→SQLite 遷移結果可見性。

### 變更

- **`BalanceSheetService` FX 換算 N+1 收斂**：從每 row `_fx.ConvertAsync(amount, ccy, base, asOf)` 改為先 `ResolveFxFactorsAsync` 取得每個 distinct 外幣對 base 的 1 單位 factor，row-level 用 `ConvertWithCache` in-memory 乘算。多帳戶共用同幣別時的重複 FX provider 呼叫降為每幣別一次。同步補上 `GetAllAsync(ct)` / `GetSnapshotAsync(asOf, ct)` 的 ct 傳遞。
- **`DbMigrator.MigrateAsync` 結果回報**：原本失敗只寫 `LogWarning` 後 silently 返回，呼叫端無從察覺。改為回傳 `MigrationReport(PortfolioImported, AlertsImported, Failures)`；`DbInitializerService` 在 `HasFailures` 時透過 snackbar 提示「資料遷移有 N 項失敗」、整批失敗時提示「資料庫遷移失敗，現有資料不受影響」、成功時 LogInformation 紀錄匯入筆數。

### 測試

- 612/612 tests 綠（無新測，行為等價：FX 換算結果一致；MigrateAsync return type 變更為 record，呼叫端僅 WPF 一處）。

## v0.17.2 - 2026-04-28

Pre-v0.17 refactor audit Group A — 5 個 quick-win 加固，集中在 `CancellationToken` 傳播一致性與 broad-catch 收斂。無新功能、無 schema 變更，行為等價。

### 變更

- **CancellationToken 傳播完整性**（`Assetra.Core/Interfaces/IPortfolioPositionLogRepository.cs`、`IPortfolioSnapshotRepository.cs` + Sqlite 實作）：兩個 repository 介面所有方法補上 `CancellationToken ct = default`；`PortfolioPositionLogSqliteRepository` / `PortfolioSnapshotSqliteRepository` 全面 propagate ct 到 `OpenAsync` / `ExecuteNonQueryAsync` / `ReadAsync` / `BeginTransactionAsync` / `CommitAsync`；`LogBatchAsync` foreach 內加 `ct.ThrowIfCancellationRequested()`。
- **`PortfolioBackfillService.BackfillAsync`**：`_logRepo.GetAllAsync(ct)` / `_snapshotRepo.GetSnapshotsAsync(ct: ct)` / `_snapshotRepo.UpsertAsync(snapshot, ct)` 三處補上 ct，與 v0.13.3 ct 傳播標準對齊。
- **`IncomeStatementService.GenerateAsync`**：`_trades.GetAllAsync(ct)` 補上 ct（先前漏傳）。
- **`ImportBatchHistorySqliteRepository.SaveAsync`**：foreach entry loop 內加 `ct.ThrowIfCancellationRequested()`，避免大量 entry 時無法中斷。
- **`ImportRollbackService.RollbackAsync`**：`ApplyAtomicAsync` 之 broad `catch (Exception)` 收斂為 `when (ex is DbException or JsonException or InvalidOperationException)`，`OperationCanceledException` 自然向上 propagate；Application layer 不參考 `Microsoft.Data.Sqlite`，故用 `System.Data.Common.DbException` 作為基底。

### 測試

- 612/612 tests 綠（與 v0.10.1 同數）。
- `FakeSnapshotRepo`（`PnlAttributionServiceTests` / `MoneyWeightedReturnCalculatorTests`）介面方法補上 ct 參數。
- `SellWorkflowServiceTests`：`logRepo.LogAsync` Moq Setup 改為 2-arg 形式（含 `It.IsAny<CancellationToken>()`）。

## v0.10.1 - 2026-04-28

收尾 v0.10.0 release note 自承的測試缺口（line: 「本 sprint 主為 UI/連線改動，暫未新增 unit test」）。對 `ApplyResolutionAsync(diffId, resolution, note, sourceKind, options, ct)` overload 補上 service-level 單元測試，覆蓋 v0.10.0 新增的 Created / OverwrittenFromStatement 兩條會動到 trade 的執行路徑，以及 Deleted / MarkedResolved / Ignored 的副作用契約。

### 新增

- **單元測試**（`Assetra.Tests/Application/Reconciliation/ApplyResolutionAsyncTests.cs`）— 10 筆 service-level 測試、hand-rolled fakes（`FakeSessions` / `FakeTrades` / `FakeApplier`）：
  - `Created` 路徑：成功時 applier 收到正確 row + sourceKind，diff 更新；缺 applier / mapper 回 null 各拋例外。
  - `Deleted` 路徑：trade 被 `RemoveAsync(tid)`，diff 更新。
  - `OverwrittenFromStatement` 路徑：既有 trade 之 `CashAmount` 更新為 statement row 金額；trade 不存在時拋例外。
  - `MarkedResolved` / `Ignored` 路徑：不動 trade、不呼 applier，僅更新 diff resolution。
  - 防呆：illegal kind × resolution 在任何 side effect 前拋例外；未知 diffId 拋例外。

### 測試

- 612/612 tests 綠（v0.17.1 為 602；本 sprint +10）。
- 無 production code / schema / interface 變更；純粹補測，行為與 v0.10.0 完全等價。

## v0.17.1 - 2026-04-28

收尾 v0.17.0 延後的 PortfolioEvent 持久化、堆疊圖前置欄位、`YearlyExtreme` 偵測。`TrendsView` UI annotation / Line ↔ Stacked Area 切換 / event 編輯對話框需 UI 設計，仍延後。

### 新增

- **F1 PortfolioEvent 持久化**（`Assetra.Infrastructure/Persistence/`）：
  - `PortfolioEventSchemaMigrator`：`portfolio_event(id, event_date, kind, label, description, amount, symbol)` + `idx_portfolio_event_date`。
  - `PortfolioEventSqliteRepository`：CRUD + `GetRangeAsync(from, to)`；nullable `description / amount / symbol` 經 `DBNull` round-trip；`kind` 以字串持久化、parse 失敗 fallback 為 `UserNote`。
  - `IPortfolioEventRepository` 介面定義於 `Assetra.Core/Interfaces/`。
- **D1 PortfolioDailySnapshot 堆疊圖欄位**（`Assetra.Core/Models/PortfolioDailySnapshot.cs`）：新增 `decimal? CashValue` / `EquityValue` / `LiabilityValue` positional optional 欄位（向後相容；舊資料為 null）。
- **Schema migration**（`Assetra.Infrastructure/Persistence/PortfolioSnapshotSchemaMigrator.cs`）：透過 `EnsureColumn` helper 新增 `cash_value / equity_value / liability_value REAL` 三欄；CREATE 同步擴充。
- **F2 YearlyExtreme 偵測**（`Assetra.Application/Analysis/PortfolioEventDetectionService.cs`）：新增 `DetectYearlyExtremes(snapshots)` 純函式，依 `SnapshotDate.Year` 分組，取每年 `MarketValue` 最大 / 最小日，產出 `YearlyExtreme` 事件對。

### 已知範圍限制

仍延後（需 UI 設計）：
- `TrendsView` UI：折線圖 hover annotation、Line / Stacked Area 切換、event 編輯對話框（含 `MarketEvent` / `UserNote` 手動加註）。
- 堆疊圖 viewmodel：將 cash / equity / liability 三層拉到 chart series（需 `BalanceSheet` ↔ snapshot writer 的接線）。

### 測試

- 新增 5 個 `PortfolioEventSqliteRepositoryTests`：add/get round-trip、nullable round-trip、`GetRangeAsync` filter、update、remove。
- 新增 2 個 `PortfolioSnapshotBreakdownTests`：upsert 帶 cash/equity/liability round-trip、null breakdown round-trip。
- 新增 4 個 `PortfolioEventDetectionServiceTests`（YearlyExtreme 區段）：empty / 單一年度高低 / 跨年度分組 / null 拋例外。
- 602/602 tests 綠（v0.16.1 為 591；本 sprint +11）。

## v0.16.1 - 2026-04-28

收尾 v0.16.0 延後的 Goals 子系統：補上 `GoalFundingRule` 模型、milestone / funding rule 的 SQLite 持久化、`GoalProgressQueryService` 純函式進度計算。`GoalsView` UI 擴充（milestone timeline、progress bar 動畫）與 RecurringTransaction 整合需 UI 設計與 cross-feature scheduler 改造，仍延後。

### 新增

- **D1 GoalFundingRule**（`Assetra.Core/Models/GoalFundingRule.cs`）：record 含 `Id` / `GoalId` / `Amount` / `Frequency`（沿用 `RecurrenceFrequency`）/ `SourceCashAccountId` / `StartDate` / `EndDate` / `IsEnabled`。設計為與 `RecurringTransaction` 平行，可由後續 sprint 之 scheduler 物化。
- **D1 GoalProgress**（`Assetra.Core/Models/GoalProgress.cs`）：`GoalId` / `TargetAmount` / `AccumulatedFunding` / `CurrentAmount` / `ProgressPercent` / `IsAchieved`。
- **F1 持久化**（`Assetra.Infrastructure/Persistence/`）：
  - `GoalSchemaMigrator` 加入 `goal_milestone(id, goal_id, target_date, target_amount, label, is_achieved)` 與 `goal_funding_rule(id, goal_id, amount, frequency, source_cash_account_id, start_date, end_date, is_enabled)` 兩張表（含 `goal_id` index 與 `ON DELETE CASCADE`）。
  - `GoalMilestoneSqliteRepository` / `GoalFundingRuleSqliteRepository`：CRUD + `GetByGoalAsync`（依 target_date / start_date 排序）；FundingRule 額外提供 `GetAllAsync` 給未來 scheduler。
  - `IGoalMilestoneRepository` / `IGoalFundingRuleRepository` 介面定義於 `Assetra.Core/Interfaces/`。
- **F2 GoalProgressQueryService**（`Assetra.Application/Goals/`）：純函式 `Compute(goal, fundingRules, currentAmount, asOf)`。對每條 enabled rule 依 frequency 計算 `[startDate, min(endDate, asOf)]` 之間的撥款次數 × amount → `AccumulatedFunding`。`ProgressPercent` 優先以 caller 提供之 `currentAmount`（通常從 `BalanceSheet` 取淨資產）為準；caller 傳 null 時退回 `AccumulatedFunding`。

### 已知範圍限制

仍延後（需 UI 設計或 scheduler 改造）：
- `GoalsView` UI：milestone timeline、funding rule 編輯對話框、progress bar 動畫。
- `GoalFundingRule` ↔ `RecurringTransaction` 整合：將 enabled rule 物化為 RecurringTransaction 並由 `RecurringTransactionScheduler` 消費；需設計同步策略（manual sync vs auto-mirror）。
- 多幣別接線：目前 `currentAmount` 由 caller 提供，假設已轉為 base currency；後續可在 ViewModel 層接 `IMultiCurrencyValuationService`。

### 測試

- 新增 14 個 `GoalProgressQueryServiceTests`：no rules、Daily/Weekly/BiWeekly/Monthly/Quarterly/Yearly 累計、disabled rule / 其他 goal / 起始日未到 / 結束日已過 過濾、currentAmount 覆寫、達成上限、100% capping、負金額忽略。
- 新增 7 個 `GoalAuxiliaryRepositoryTests`：milestone CRUD round-trip、funding rule CRUD round-trip、null source/end-date 處理、disable 持久化、`GetAllAsync` 跨 goal。
- 591/591 tests 綠（v0.15.2 為 570；本 sprint +21）。

## v0.15.2 - 2026-04-28

收尾 v0.15.1 延後的跨市場處理：Yahoo history exchange-aware timezone、`PortfolioEntry.IsEtf` 持久化、`AddAssetWorkflowService` 自動帶入幣別 + ETF 旗標。`StockPickerView` 因 view 尚未存在故仍延後（需待 UI scaffolding）。

### 新增

- **Exchange-aware timezone**（`Assetra.Infrastructure/History/YahooFinanceHistoryProvider.cs`）：`ParseResponse(json, exchange)` 透過 `StockExchangeRegistry.TryGet(exchange).TimeZone` 解析 IANA tz id（NY / Tokyo / HK / Taipei）；舊單參數呼叫退化為 Taipei，與 v0.15.1 行為一致。`ResolveTimeZoneOrTaipei` 容錯：IANA → Windows id 後備 → UTC。
- **PortfolioEntry.IsEtf**（`Assetra.Core/Models/PortfolioEntry.cs`）：record 新增 `bool IsEtf = false` 欄位（positional optional，向後相容）。
- **Schema migration**（`Assetra.Infrastructure/Persistence/PortfolioSchemaMigrator.cs`）：`portfolio.is_etf INTEGER NOT NULL DEFAULT 0` 透過既有 `MigrateAddColumn` allowlist 機制加入，舊 DB 自動 backfill。
- **FindOrCreatePortfolioEntryAsync** 新增 `string? currency = null` / `bool isEtf = false` 參數（`IPortfolioRepository`）；`PortfolioSqliteRepository` 之 INSERT 改 bind `$cur` / `$etf`，`null` currency 退回 `"TWD"`。

### 變更（接線）

- **AddAssetWorkflowService.EnsureStockEntryAsync**：透過 `StockExchangeRegistry.ResolveDefaultCurrency(exchange)` 自動帶入幣別、透過 `IStockSearchService.IsEtf(symbol)` 設定 ETF 旗標；建立的 `PortfolioEntry` 同步攜帶兩者。

### 已知範圍限制

仍延後（需 UI scaffolding，本 sprint 不在範圍內）：
- `StockPickerView` Exchange filter / 顯示（view 尚未實作）。
- ETF metadata 細項（追蹤指數、配息頻率）：暫無資料來源。

### 測試

- 新增 5 個 `YahooFinanceHistoryProviderTests`：default/Taipei、NYSE、NASDAQ、TSE、unknown exchange fallback；證明 NY +5 → 2023-11-14，Taipei +8 → 2023-11-15。
- 新增 2 個 `FindOrCreateRepositoryTests`：明確 currency 持久化、null currency 預設 TWD。
- 570/570 tests 綠（v0.14.2 為 563；本 sprint +7）。`ImportBatchHistorySqliteRepositoryTests.MarkRolledBack_UpdatesFlagAndTimestamp` 並行 run 偶發 fail，獨立 run 穩定通過 — 屬 pre-existing SQLite + xUnit parallelism 議題。

## v0.14.2 - 2026-04-28

收尾 v0.14.1 延後的 snapshot 多幣別處理。`PortfolioDailySnapshot` 新增 `Currency` 欄位，`MoneyWeightedReturnCalculator` 在計算 IRR 前依 base currency 轉換 snapshot market value，與 trade flows 採同一保守策略（缺 FX → 回傳 null）。

### 新增

- **PortfolioDailySnapshot.Currency**（`Assetra.Core/Models/PortfolioDailySnapshot.cs`）：record 新增 `string Currency = "TWD"` positional 參數，向後相容。
- **Schema migration**（`Assetra.Infrastructure/Persistence/PortfolioSnapshotSchemaMigrator.cs`）：CREATE 加 `currency TEXT NOT NULL DEFAULT 'TWD'`；以 `pragma_table_info` 偵測舊 DB 並 `ALTER TABLE ADD COLUMN`。
- **MWR snapshot 轉換**（`Assetra.Application/Analysis/MoneyWeightedReturnCalculator.cs`）：`ConvertSnapshotMarketValueAsync` 比對 `snap.Currency` 與 `BaseCurrency`，不一致時走 `IMultiCurrencyValuationService.ConvertAsync`（asOf = snapshot date）；缺 rate → return null。
- **TryRecordSnapshotAsync** 透傳 `currency` 參數（`IPortfolioHistoryMaintenanceService` / `PortfolioSnapshotService` / WPF 包裝）；`PortfolioViewModel.RecordSnapshotAsync` 從 `AppSettings.BaseCurrency` 取值。

### 測試

- 新增 3 個 `MoneyWeightedReturnCalculatorTests`：snapshot 與 base 同幣（不轉換）/ 異幣經 FX 轉換（不同 rate → 不同 IRR，避開 IRR scale-invariance）/ 缺 rate 回 null。
- 563/563 tests 綠（v0.17.0 為 560；本 sprint +3）。

## v0.17.0 - 2026-04-28

趨勢圖增強的領域層骨架：建立 `PortfolioEvent` 模型 + `PortfolioEventDetectionService` 純函式偵測器。schema migration、堆疊圖 schema 擴充、TrendsView UI annotation 留待 v0.17.1+。

### 新增

- **D1 PortfolioEvent**（`Assetra.Core/Models/PortfolioEvent.cs`）：record 含 `Id` / `Date` / `Kind` / `Label` / `Description` / `Amount` / `Symbol`。`PortfolioEventKind` enum 涵蓋 `LargeTrade` / `FirstDividend` / `YearlyExtreme` / `MarketEvent` / `UserNote`。
- **F1 PortfolioEventDetectionService**（`Assetra.Application/Analysis/`）：純函式 `Detect(trades, largeTradeThreshold)`，從 trade journal 推導事件序列。
  - `LargeTrade`：Buy / Sell 之 `Price × Quantity ≥ threshold`（預設 100,000）。
  - `FirstDividend`：每個 Symbol 的第一筆 `CashDividend`（case-insensitive symbol 比對）。
  - 結果依日期 ascending 排序。

### 已知範圍限制

延後到 v0.17.1+：
- SQLite schema：`portfolio_event` 表 + repository（目前 service 為 in-memory 計算，需 caller 傳入 trades）。
- `PortfolioDailySnapshot` 擴充 `cash_value / equity_value / liability_value` 三欄（堆疊圖前置）。
- `TrendsView` UI：折線圖 hover annotation、Line / Stacked Area 切換、event 編輯對話框。
- `YearlyExtreme` 偵測（需要 daily snapshot 序列，非僅 trade list）。
- `MarketEvent` / `UserNote` 為使用者手動加註，需 UI + persistence。

### 測試

- 新增 9 個測試（`PortfolioEventDetectionServiceTests`）：empty / 單筆 large buy / below threshold / large sell / first dividend / 多 symbol 各自首筆 / 排序 / 負 threshold 拋例外 / null trades 拋例外。
- 560/560 tests 綠（v0.16.0 為 551；本 sprint +9）。

## v0.16.0 - 2026-04-28

Goals 子系統最小可用骨架：建立 `GoalMilestone` 領域模型 + `GoalPlanningService` 純函式計算器。本 sprint 聚焦於零依賴的領域 / 計算層，避開 schema migration 與 UI 重構。

### 新增

- **D1 GoalMilestone**（`Assetra.Core/Models/GoalMilestone.cs`）：record 含 `Id` / `GoalId` / `TargetDate` / `TargetAmount` / `Label` / `IsAchieved`。對應到既有 `FinancialGoal`，支援把單一目標拆成多個中繼里程碑。
- **F1 GoalPlanningService**（`Assetra.Application/Goals/`）：純函式計算器，無 I/O。
  - `RequiredMonthlyContribution(currentAmount, targetAmount, annualReturnRate, months, contributionAtBeginningOfPeriod)`：年金未來值反解 PMT，0% 報酬率退化為均分；複利後 PV ≥ target 回 0；月數 ≤ 0 且未達標回 null（無法在期限內達成）。
  - `MonthsToReachTarget(currentAmount, targetAmount, annualReturnRate, monthlyContribution, contributionAtBeginningOfPeriod)`：iterative simulation（最多 1200 個月 / 100 年），永遠無法達成回 null。

### 已知範圍限制

延後到 v0.16.1+：
- `GoalFundingRule` 模型（定期撥款規則：金額 / 頻率 / 來源帳戶）。
- Milestone / FundingRule 的 SQLite 持久化（schema migration + repository）。
- `GoalProgressQueryService`：聚合 cash + investment 計算每個 Goal 已撥款 / 進度比例（含 v0.14 多幣別接線）。
- `GoalsView` UI 擴充：milestone timeline、funding rule 編輯、progress bar 動畫。
- 與 `RecurringTransaction` 整合（`GoalFundingRule` → 實體化為 `RecurringTransaction`）。

### 測試

- 新增 14 個測試：`GoalPlanningServiceTests`(12) + `GoalMilestoneTests`(2)。涵蓋 0% / 正報酬率 / 期初 vs 期末撥款 / 已超過目標 / 期限已過已達標或未達標 / round-trip（PMT → 月數）。
- 551/551 tests 綠（v0.15.1 為 537；本 sprint +14）。
- 備註：`StockraImportServiceTests.ImportAsync_SkipsTable_WhenTargetAlreadyHasRows` 在某次 run 偶發 fail（同 v0.15.0 已記錄的 SQLite + xUnit parallelism flake）；獨立 / 後續執行穩定通過。屬 pre-existing 議題。

## v0.15.1 - 2026-04-28

收尾 v0.15.0 延後的跨市場 quote / history 路由：以既有 Yahoo Finance provider 支援美股 / HK / 日股，並抽出純函式 `YahooSymbolMapper` 統一處理交易所 → ticker suffix 轉換。零外部 API key 成本。

### 新增

- **YahooSymbolMapper**（`Assetra.Infrastructure/History/`）：純函式 `ToYahooSymbol(symbol, exchange)` 與 `IsForeignExchange(exchange)`。對應規則：TWSE→`.TW`、TPEX→`.TWO`、HKEX→`.HK`、TSE→`.T`、NYSE/NASDAQ/AMEX→bare symbol、未知→bare symbol（caller-validated）。case / whitespace tolerant。

### 變更

- **YahooFinanceHistoryProvider**：原本 inline 的 `exchange == "TPEX" ? .TWO : .TW` 改委派給 `YahooSymbolMapper`，自然支援 NYSE / NASDAQ / AMEX / HKEX / TSE。
- **DynamicHistoryProvider**：在 dispatch 之前先檢查 `IsForeignExchange`，若是非台灣交易所一律繞過使用者選的 provider 直接走 Yahoo（TWSE / TPEX / FinMind / Fugle clients 只懂台股 symbol，硬發會 404 或 schema mismatch）。台股流程不受影響。

### 已知範圍限制

延後到 v0.15.2+：
- `YahooFinanceHistoryProvider` 的日期 timezone 仍以 Taipei 計算（美股交易日因時差可能位移一天）— 待加入 exchange-aware timezone 後修正。
- `PortfolioEntry` ETF metadata（`IsEtf` / 追蹤指數 / 配息頻率）。
- 跨市場選股 UI：StockPickerView 加 Exchange filter / 顯示。
- `AddAssetWorkflowService` 套用 `StockExchangeRegistry.ResolveDefaultCurrency` 自動帶入幣別。

### 測試

- 新增 `YahooSymbolMapperTests`（4 個方法 / 17 個 Theory rows）：known exchange suffix × 7、case + whitespace tolerance × 3、unknown / null / blank fallback × 3、`IsForeignExchange` 真值表 × 9（含 BRK.B 帶 dot 的 edge case）。
- 537/537 tests 綠（v0.15.0 為 514；本 sprint +23 含 Theory 展開）。

## v0.15.0 - 2026-04-28

美股 / ETF Pipeline 的最小可用基礎：建立 `StockExchange` registry 與 `AssetType.Etf`。本 sprint 聚焦在 zero-cost 的領域層擴充，避開需要外部 API key 的整合（US quote / history HTTP providers）。

### 新增

- **D1 StockExchange 領域模型**：`Assetra.Core/Models/StockExchange.cs`，record 含 `Code` / `DisplayName` / `DefaultCurrency` / `Country` / `TimeZone(IANA)`。
- **D1 StockExchangeRegistry**：靜態 lookup，內建七個交易所常數 — TWSE / TPEX（TWD）、NYSE / NASDAQ / AMEX（USD）、HKEX（HKD）、TSE（JPY）。提供 case-insensitive `TryGet(code)` 與 `ResolveDefaultCurrency(code, fallback)`，後者可作為新增 `PortfolioEntry` 時自動帶入幣別的依據（與 v0.14.0 多幣別 base currency 相容）。
- **AssetType.Etf**：enum 末尾新增 `Etf = 5`（保留前面值順序穩定，DB 已存值不受影響）。

### 已知範圍限制

延後到 v0.15.1+：
- US 股票 / ETF quote + history HTTP providers（需外部 API key — Yahoo Finance / Alpha Vantage 之一）
- `DynamicHistoryProvider` 由「使用者選 provider」改為「依 exchange 自動 routing」
- `PortfolioEntry` 加 `IsEtf` 或 ETF metadata（追蹤指數 / 配息頻率）
- 跨市場選股 UI（StockPickerView 加 Exchange filter）
- AddAssetWorkflowService 套用 `StockExchangeRegistry.ResolveDefaultCurrency` 自動帶入幣別

### 測試

- 新增 6 個測試（StockExchangeRegistryTests）：already-known × 7 (Theory)、case-insensitive、unknown / blank fallback × 4、`ResolveDefaultCurrency` 行為、`Known.Count == 7`。
- 514/514 tests 綠（v0.14.1 為 499；本 sprint +6，另計 +9 從 Theory rows）。
- 備註：`TradeSqliteRepositoryInitializeTests.Initialize_LegacyLiabilityAccountTablePresent_BackfillRunsSuccessfully` 在某些並行 run 偶發 fail（SQLite + xUnit parallelism）；獨立執行穩定通過。屬 pre-existing 議題，非本 sprint 引入。

## v0.14.1 - 2026-04-28

收尾 v0.14.0 延後的 Performance / Risk 跨幣別接線：MWR 與 Concentration 改吃 base currency；XIRR 本身保持貨幣中立（純粹接受已轉換的 cash flow）。

### 新增

- **CashFlow 擴充**：`Assetra.Core/Models/Analysis/CashFlow.cs` 加上可選 `string? Currency = null`（null/empty 視為已是 base currency）。為 record 預設參數，現有 callers 不受影響。
- **MultiCurrencyCashFlowConverter**（`Assetra.Application/Fx/`）：將一組帶幣別的 `CashFlow` 統一轉為 base currency；任一筆缺匯率即整批回 null（避免靜默丟資料導致誤導性 IRR）。

### 變更（接線）

- **MWR**：`MoneyWeightedReturnCalculator` 增加可選 `IPortfolioRepository? portfolio` / `IMultiCurrencyValuationService? fx` / `IAppSettingsService? settings`。配置齊全時，依 trade 的 `PortfolioEntry.Currency` 對每筆 flow 標幣別、用 trade date 為 `asOf` 透過 FX 換算為 `BaseCurrency` 後再丟 XIRR；缺匯率回 null。Snapshot 的 `MarketValue` 視為已是 base currency（snapshot 本身的多幣別處理留待後續 sprint 一併處理）。
- **Concentration**：`ConcentrationAnalyzer` 增加可選 `IMultiCurrencyValuationService? fx` / `IAppSettingsService? settings`。配置齊全時，每個 entry 的 `TotalCost` 依 `entry.Currency` 換算為 base currency 後再進入 bucket 聚合；缺匯率該 bucket 跳過（避免分母混幣）。
- 兩者在未配置 FX/baseCurrency 時行為與 v0.13.x 完全一致（degraded mode）。

### 測試

- 新增 5 個測試：`MoneyWeightedReturnCalculatorTests`(3) + Concentration 跨幣別 (2)。
- 499/499 tests 綠（v0.14.0 為 494；本 sprint +5）。

## v0.14.0 - 2026-04-28

外幣基礎建設：建立 FX 匯率域模型、SQLite 持久化、估值服務，並將 BalanceSheet 接上「估值基準幣別」轉換。完成 Roadmap-v0.14-to-v1.0 的 v0.14.0 sprint。

### 新增

- **D1 領域模型**：`Assetra.Core/Models/Currency.cs`（ISO 4217 record + TWD/USD/JPY/HKD/EUR/CNY/GBP 常量 + `Known` / `FromCode`），`FxRate.cs`（`1 unit From = Rate units To` + `Inverse()`）。
- **D1 介面**：`IFxRateProvider`（`GetRateAsync` / `GetHistoricalSeriesAsync`）、`IFxRateRepository`（`UpsertAsync` / `UpsertManyAsync` / `GetAsync` / `GetRangeAsync`）、`Analysis.IMultiCurrencyValuationService`（`ConvertAsync`）。
- **F1 持久化**：`FxRateSchemaMigrator` 建表 `fx_rate(from_ccy, to_ccy, as_of_date, rate)` 複合主鍵 + `as_of_date` 索引；`FxRateSqliteRepository` 走 `ON CONFLICT DO UPDATE` upsert，`GetAsync` 以 `as_of_date <= $d ORDER BY DESC LIMIT 1` 取最近一筆。
- **F1 Provider**：`StaticFxRateProvider` 同幣別捷徑回 1.0、直接命中回 rate、否則嘗試反向倒數補齊。
- **F2 估值服務**：`MultiCurrencyValuationService` 同幣別回原值、缺匯率回 null、不靜默歸零。
- **F3 BalanceSheet 接線**：`BalanceSheetService` 建構子新增可選 `IMultiCurrencyValuationService` + `string? baseCurrency`；現金與負債列依 `item.Currency` 透過 `ConvertOrSelfAsync` 換算為 base currency；FX 未配置或匯率缺失時優雅退回原幣值（不靜默歸零）。
- **F4 Settings**：`AppSettings` 新增 `BaseCurrency = "TWD"` 欄位；`SettingsViewModel` + `SettingsView.xaml` 加上「估值基準幣別」下拉與 tooltip；`Languages/{zh-TW,en-US}.xaml` 補 `Settings.BaseCurrency` / `Settings.BaseCurrency.Tooltip` keys。
- **DI**：`Assetra.WPF/Infrastructure/FxServiceCollectionExtensions.AddFxContext(dbPath)` 註冊 IFxRateRepository / IFxRateProvider / IMultiCurrencyValuationService；`AppBootstrapper` 接入；`ReportsServiceCollectionExtensions` 將 fx + baseCurrency 注入 `IBalanceSheetService` factory。

### 已知範圍限制

- **XIRR / MWR / Concentration 跨幣別 wiring 延後到 v0.14.1**：完整接線需擴充 `CashFlow` record 攜帶幣別並更新所有相關測試（影響 480+ tests）；本 sprint 聚焦在交付可用的 FX 基礎並以 BalanceSheet 為首個對接點。

### 測試

- 新增 14 個測試：`StaticFxRateProviderTests`(4) + `MultiCurrencyValuationServiceTests`(4) + `FxRateSqliteRepositoryTests`(4) + BalanceSheet 跨幣別 (2)。
- 494/494 tests 綠（v0.13.4 為 480；本 sprint +14）。

## v0.13.4 - 2026-04-28

針對 v0.13.3 後 code review 剩下的 MEDIUM 違規做 cleanup；無新功能、行為等價，但邊界更乾淨、檔案更易維護。

### 修正

- **Obsolete API（MED #9）**：移除 `ImportApplyService` 的單參數便捷建構子（v0.7 留下、生產 DI 已不使用），13 處測試呼叫改為顯式傳入 `new ImportRowMapper()`；同時補上 `RemoveAsync` / `AddAsync` 漏傳的 `CancellationToken`。
- **Schema migrator whitelist（MED #7）**：`AutoCategorizationRuleSchemaMigrator.EnsureColumn` 原本將 `column` / `typeAndDefault` 直接內插到 `ALTER TABLE` SQL，無 allowlist 防護；改走 `SqliteSchemaHelper.MigrateAddColumn` 並新增 `AllowedColumns` / `AllowedTypeDefs`。`PortfolioSchemaMigrator.DropLegacyColumns` 改走新增的 `SqliteSchemaHelper.MigrateDropColumn`，本地 `ColumnExists` 改用 helper 版本（`PRAGMA table_info({table})` 不再裸內插）。
- **Oversize VM（MED #6）**：`PortfolioViewModel`（1905 行）拆為 main + `Filtering` / `Reload` / `Detail` / `NullServices` 4 個 partial（main 降至 1108 行）；`TransactionDialogViewModel`（1827 行）拆為 main + `Categories` / `Confirm` 2 個 partial（main 降至 1194 行）。`CommunityToolkit.Mvvm` 的 source generator 對 partial class 仍正確生成。

### 內部變更

- 新增 `SqliteSchemaHelper.MigrateDropColumn`（with table allowlist + per-call column allowlist）。
- 480/480 tests 綠。

## v0.13.3 - 2026-04-28

針對 v0.13.2 後 code review 發現的 HIGH / MEDIUM / LOW 違規做 hardening；無新功能、行為大致等價，但 cancellation 一致、import rollback 具原子性、JSON snapshot 更安全。

### 修正

- **CancellationToken（HIGH）**：`ITradeRepository` / `IPortfolioRepository` / `IAlertRepository` 各方法補上 `CancellationToken ct = default` 參數，並由對應 SQLite 實作 propagate 到 `OpenAsync` / `ExecuteNonQueryAsync` / `ReadAsync`；callers / fakes / Moq `.Callback<T>` 同步更新。
- **Atomic rollback（HIGH）**：新增 `ITradeRepository.ApplyAtomicAsync(IReadOnlyList<TradeMutation>, ct)` 在單一 SQLite transaction 套用整批 add / remove；`ImportRollbackService` 改為兩段式：planning 收集 pre-flight failures（snapshot 缺失 / 反序列化錯誤 / 空 Guid），確認無 failure 後才透過 atomic apply 執行，任一筆失敗整批 rollback，不再有半套用狀態。
- **JSON snapshot guard（MED）**：`ImportApplyService.SnapshotJsonOptions` 增加 `MaxDepth = 16`；`ImportRollbackService` 在反序列化 snapshot 時 catch `JsonException`、檢查 null 與 `Trade.Id == Guid.Empty`，違規即列為 pre-flight failure。
- **GetDiffByIdAsync（LOW）**：`IReconciliationSessionRepository` 新增 `GetDiffByIdAsync`；`ReconciliationService.FindDiffAsync` 由 O(N×M) session 全掃改為單筆 SQL `WHERE id = $id LIMIT 1`。

### 內部變更

- 新增 `Assetra.Core/Models/TradeMutation.cs`：`abstract record TradeMutation` + `AddTradeMutation` / `RemoveTradeMutation`。
- 9 個 `FakeTradeRepo` 測試替身補上 `ApplyAtomicAsync`；`ImportRollbackServiceTests` 改透過 `ApplyAtomicAsync` 捕捉 mutations。
- 480/480 tests 綠。

## v0.13.2 - 2026-04-28

針對 v0.13.1 後 code review 發現的 CRITICAL / HIGH 違規做 quick fix；無新功能、行為等價，但訊號乾淨且 benchmark 可配置。

### 修正

- **Async（CRITICAL）**：`PortfolioLoadService.LoadAsync` 7 處 `Task.Result` 改為各自 `await ... ConfigureAwait(false)`；移除 CLAUDE.md 禁用模式並降低後續 refactor deadlock 風險。
- **Error handling（HIGH）**：`ReportsViewModel.LoadPerformanceAsync` 的 benchmark 空 `catch { ... }` 改為帶診斷的 `catch (HttpRequestException or InvalidOperationException or TaskCanceledException)`，並寫入 Debug log；不再吞掉 HTTP / 解析錯誤。
- **Hardcoded value（HIGH）**：benchmark 代號 `"0050.TW"` 從 `LoadPerformanceAsync` 移出，改由 `AppSettings.BenchmarkSymbol` 提供（預設 `"0050.TW"`）；空字串可關閉 benchmark 比較。`ReportsViewModel` 新增可選 `IAppSettingsService` 注入。

### 內部變更

- `AppSettings` 新增 `string BenchmarkSymbol = "0050.TW"` 欄位（向後相容；既有序列化 JSON 缺欄位時 fallback 到預設）。
- 480/480 tests 綠。

## v0.13.1 - 2026-04-28

針對 v0.13.0 release 後 code review 發現的本地化債務與文件落差做 cleanup；無新功能、無行為改變。

### 修正

- **i18n（HIGH）**：補上 Reports / Risk / Performance Expander 內所有原本 hardcode 在 `StringFormat` 的英文 label（25+ keys）；改寫為 horizontal StackPanel + DynamicResource 模式。
- **i18n（HIGH）**：Portfolio / Allocation / Rebalance / Loan / DateRangePicker / PortfolioView 「債券 ETF」徽章 + tooltip 等先前漏進 Languages 的 zh-TW 字串補進 Languages，新增對應 en-US 翻譯。
- **i18n（HIGH）**：`ReportsViewModel.ExportStatus` 訊息原為 hardcode 字串，改走 `_localization.Get(key, fallback)`，並加入對應 `Reports.Export.Status.*` keys。
- **Theme（MED）**：Risk Expander 集中度警示文字 `Foreground="#D9534F"` 改用 `{DynamicResource AppDanger}`，跟隨 theme 切換。
- **CancellationToken（MED）**：`ConcentrationAnalyzer` 的 `ct` 參數改為 propagate 至 `BuildBucketsAsync`，呼叫 repo 前後 `ThrowIfCancellationRequested`。
- **Docs（HIGH/MED）**：`Bounded-Contexts.md` 第 5 節舊 Analysis Context 描述（與第 8 節重複）改為指向 #8 的 placeholder；`Next-Sprint-v0.13.0.md` F5 `ConcentrationAlertRule` 標註 descoped、F3 risk-free rate 註明 `IAppSettingsService` 推遲到 v0.14。

### 內部變更

- 新增 ~30 個 Languages keys（zh-TW + en-US）。
- 無 schema、interface、DI 變更；行為與 v0.13.0 一致。
- 因專案內存有未提交的 Recurring/Categories WIP（與此 cleanup 無關），test project 在本地 build 失敗；本 release 僅驗證 `Assetra.WPF` 主專案 build 全綠，`dotnet test` 待 WIP 收斂後再驗證。

## v0.13.0 - 2026-04-28

風險分析：在 Analysis Context 加入波動率、最大回撤、Sharpe ratio 與持股集中度（HHI），於 Reports 頁新增「Risk Metrics」Expander 並提供集中度警示。

### 重點

- **D1 Risk DTO 與介面** — `Assetra.Core/Models/Analysis/`：`DrawdownPoint`、`ConcentrationBucket`、`RiskMetrics`（含 `HasConcentrationWarning` 計算屬性，>30% 單一部位或 HHI >0.30 觸發）；`Assetra.Core/Interfaces/Analysis/` 四個 service 介面。
- **F1 VolatilityCalculator** — 由日 value 序列算日報酬，sample std × √252 得年化波動率；少於 2 筆報酬回 null。
- **F2 DrawdownCalculator** — running peak，dd = (peak − value) / peak；輸出 `DrawdownPoint` 序列 + `ComputeMaxDrawdown` 取最大值。
- **F3 SharpeRatioCalculator** — `(annualizedReturn − riskFreeRate) / annualizedVolatility`；vol = 0 或缺值回 null。預設 rf = 0.02。
- **F4 ConcentrationAnalyzer** — Top-N + Others bucket（label 為 `Symbol DisplayName`），權重以 `PositionSnapshot.TotalCost` 為 cost-basis proxy（無同步 quote service）；HHI = Σ wᵢ²。
- **F5 集中度警示** — 改以 `RiskMetrics.HasConcentrationWarning` flag 表達（>30% 單一部位或 HHI >0.30），不擴張現有 price-target 為主的 `AlertRule` 框架；UI 直接綁該 flag。
- **WPF Risk Tab** — `ReportsView` 新增第 5 個 Expander（Volatility / MaxDD / Sharpe / HHI + 警示 + Top Holdings ItemsControl）；`ReportsViewModel` 注入 5 個可選 dep（4 service + `IPortfolioSnapshotRepository`），`LoadAsync` 之後呼 `LoadRiskAsync`。

### 內部變更

- 新增 4 個 Analysis service 至 `AnalysisServiceCollectionExtensions.cs`。
- `Languages/zh-TW.xaml` / `en-US.xaml` 新增 `Reports.Risk.Title` / `Reports.Risk.ConcentrationWarning` / `Reports.Risk.TopHoldings`。
- 466 → 478 筆測試全綠（新增 3 筆 Volatility + 3 筆 Drawdown + 3 筆 Sharpe + 3 筆 Concentration）。

## v0.12.0 - 2026-04-28

投資績效分析：在 v0.11 報表 infra 上新增 Analysis bounded context，提供 XIRR / TWR / MWR、benchmark 對比與損益歸因，於 Reports 頁多一張「Performance」報表。

### 重點

- **D1 Analysis DTO 與介面** — `Assetra.Core/Models/Analysis/`：`CashFlow`、`PerformancePeriod`（Month/Year 工廠）、`PerformanceResult`（含 Alpha 計算）、`AttributionBucket`；`Assetra.Core/Interfaces/Analysis/` 五個 service 介面。
- **F1 XirrCalculator** — Newton-Raphson（max 100 iter, tol 1e-7）+ Bisection fallback（[-0.99, 10.0]）；要求至少一筆正、一筆負流，否則回 null。
- **F2 TimeWeightedReturnCalculator** — 在每筆外部 cash flow 切 sub-period，幾何鏈接 `Π(1 + R_i) − 1`，分離資金進出對報酬率的扭曲。
- **F3 MoneyWeightedReturnCalculator** — 對 portfolio：trade journal Buy/Sell/CashDividend → cash flow，加入起 / 終 `PortfolioDailySnapshot.MarketValue` 為合成 flow，呼 XIRR；亦支援單一 `PortfolioEntryId`。
- **F4 BenchmarkComparisonService** — 透過 `IStockHistoryProvider` 拉同期 benchmark（預設 0050.TW）收盤價，計算 `(endPx − startPx) / startPx`。
- **F5 PnlAttributionService** — 拆解期間損益為四桶：Realized（Sell.RealizedPnl）、Dividend（CashDividend.CashAmount）、Commission（負）、Unrealized Δ（end−start MarketValue 扣除淨投入）。
- **WPF Performance Tab** — `ReportsView` 新增第 4 個 Expander，顯示 MWR / Benchmark / Alpha + Attribution rows；`ReportsViewModel` 注入 3 個可選 Analysis service，`LoadAsync` 自動載入。

### 內部變更

- 新增 `Assetra.Application/Analysis/`（5 個 service）+ `Assetra.WPF/Infrastructure/AnalysisServiceCollectionExtensions.cs`，於 `AppBootstrapper` 加 `AddAnalysisContext()`。
- `Languages/zh-TW.xaml` / `en-US.xaml` 新增 `Reports.Performance.Title`。
- 455 → 466 筆測試全綠（新增 5 筆 XIRR + 3 筆 TWR + 3 筆 PnlAttribution）。

## v0.11.0 - 2026-04-28

Reports MVP：以 Trade Journal 為單一事實源，提供月度損益表 / 資產負債表 / 現金流量表三大報表，並支援 PDF（QuestPDF Community）+ CSV 匯出。

### 重點

- **D1 報表 DTO 與介面** — 新增 `Assetra.Core/Models/Reports/`：`ReportPeriod`（Month/Year 工廠 + `Prior()` 等長前期窗）、`StatementRow`、`StatementSection`、`IncomeStatement` / `BalanceSheet` / `CashFlowStatement`、`ExportFormat`；以及 `Assetra.Core/Interfaces/Reports/` 四個 service 介面。
- **F1 IncomeStatementService** — 以 `Trade.Date` ∈ Period 過濾，依 `CategoryId` 聚合 Income/Expense rows（與 `ICategoryRepository` 對照取 Label，未分類顯示 `(Uncategorized)`），輸出 Income/Expense Section + Net；可選 `includePrior` 遞迴生成等長前期數據作 MoM/YoY 對照。
- **F2 BalanceSheetService** — Cash 端依 `TradeType.PrimaryCashDelta(t)` 累積（Income/Sell/CashDividend/Deposit/LoanBorrow → +；Withdrawal/Buy/Repay → −），按 `CashAccountId` 分列；可選帶入最新 `PortfolioDailySnapshot.MarketValue` 為 Investments；Liabilities 以 `LiabilityAssetId`（信用卡）+ `LoanLabel`（貸款）兩類聚合。AsOf 截止日嚴格過濾。
- **F3 CashFlowStatementService** — Operating（Income / Withdrawal / Deposit / CashDividend）、Investing（Buy / Sell）、Financing（LoanBorrow / Repay / 信用卡刷卡 / 還款）三段；Opening cash 由 pre-period trades 累積、Closing = Opening + NetChange，建構式即保證恆等。
- **F4 ReportExportService** — 共用 QuestPDF `IDocument` 模板（標題、副標、Section grouping、Grand Total、page footer）+ 自寫 CSV（無新增 CsvHelper 依賴），單一進入點 `ExportAsync(payload, format, path)`。QuestPDF Community License 由 `Interlocked.Exchange` 一次性設定。
- **WPF Reports 頁** — `ReportsViewModel` 注入四個 service，`LoadAsync` 同時載入三大報表並暴露 `IncomeStatement` / `BalanceSheet` / `CashFlowStatement` 觀察屬性；`ReportsView.xaml` 三個 Expander 顯示資料 + 6 個 Export 命令（PDF/CSV ×3）走 `SaveFileDialog`。

### 內部變更

- `Assetra.Application/Reports/Statements/`、`Assetra.Application/Reports/ReportExportService.cs`。
- `Assetra.WPF/Infrastructure/ReportsServiceCollectionExtensions.cs` 註冊四個介面為 Singleton。
- `Languages/zh-TW.xaml` / `en-US.xaml` 新增 `Reports.IncomeStatement.Title` / `Reports.BalanceSheet.Title` / `Reports.CashFlow.Title` / `Reports.Export.Pdf` / `Reports.Export.Csv`。
- 444 → 455 筆測試全綠（新增 4 筆 `ReportPeriod` + 3 筆 `IncomeStatementService` + 2 筆 `BalanceSheetService` + 2 筆 `CashFlowStatementService`）。

## v0.10.0 - 2026-04-28

Reconciliation Phase 2：補上 v0.9.0 暫緩的「新建 Session UI／Created／OverwrittenFromStatement 執行路徑／餘額對帳面板／Kind 分組」。

### 重點

- **D1 共用 IImportRowApplier** — 從 `ImportApplyService` 抽出 `IImportRowApplier`（Core 介面）與 `DefaultImportRowApplier`（Application 實作），讓 Reconciliation 在「Created」處置時不必另寫 trade-from-row 邏輯，直接共用 ImportRowMapper + AutoCategorizationRule snapshot。
- **D1-2 期末餘額欄位** — `ReconciliationSession` 新增 `StatementEndingBalance` 欄；SQLite 透過 `SqliteSchemaHelper.MigrateAddColumn` 加 `statement_ending_balance REAL`，向下相容既有 session 列為 NULL。
- **F1 新建 Session 面板** — `ReconciliationView.xaml` 新增可摺疊新建面板（Account 下拉、起訖期間、來源切換 = 既有匯入批次 vs 上傳新檔、期末餘額），ViewModel 端整合 `IImportBatchHistoryRepository` / `IImportFormatDetector` / `ImportParserFactory` 兩種來源路徑。
- **F2 Created / OverwrittenFromStatement** — `IReconciliationService.ApplyResolutionAsync` 新增 `(sourceKind, options)` overload；Created → 透過 IImportRowApplier 把 statement row 寫入為 trade，OverwrittenFromStatement → `_trades.GetByIdAsync` + `with { CashAmount = srow.Amount }` 後 Update。動作按鈕視 Kind 動態顯示。
- **F3 Kind 分組 + 餘額面板** — DataGrid 改綁 `GroupedDiffs` ICollectionView（PropertyGroupDescription on KindDisplay）；右側面板顯示 `Statement Sum / Trades Sum / Δ / Ending balance` 簡化餘額對帳。

### 內部變更

- `Assetra.Core/Interfaces/Import/IImportRowApplier.cs`、`Assetra.Application/Import/DefaultImportRowApplier.cs`、`Assetra.WPF/Infrastructure/ImportServiceCollectionExtensions.cs` 註冊。
- `ReconciliationServiceCollectionExtensions.cs` 注入 `ITradeRepository` / `IReconciliationMatcher` / `IImportBatchHistoryRepository` / `IImportFormatDetector` / `ImportParserFactory` 至 ViewModel。
- `ReconciliationDiffRowViewModel` 暴露 `IsMissing` / `IsExtra` / `IsAmountMismatch` 供 XAML 動作按鈕 visibility binding。
- `Languages/*.xaml` 新增 14 組 `Reconciliation.NewSession.*` / `Reconciliation.Action.CreateTrade` / `Reconciliation.Action.Overwrite` / `Reconciliation.Balance.Title` 鍵。
- 444 → 444 筆測試全綠（既有測試相容；本 sprint 主為 UI/連線改動，暫未新增 unit test）。

## v0.9.0 - 2026-04-28

新增 Reconciliation bounded context：把對帳單預覽列與已匯入的 Trade 比對，找出 Missing / Extra / AmountMismatch 三類差異，並提供逐筆裁決與簽核流程。

### 重點

- **Reconciliation 領域模型（F1）** — 新增 `ReconciliationSession`（帳戶 + 期間 + 來源批次 + 狀態）與 `ReconciliationDiff`（Kind = Missing / Extra / AmountMismatch；Resolution = Pending / Created / Deleted / MarkedResolved / Ignored / OverwrittenFromStatement），並建立 `IReconciliationSessionRepository` + `ReconciliationSessionSqliteRepository`，以 `statement_rows_json` 將整批對帳單列存於 session row、diff 內 `statement_row_json` 保留個別列 snapshot 供後續比對與顯示。
- **Diff 比對演算法（F2）** — `ReconciliationService.ComputeDiffs` 以 `IReconciliationMatcher`（預設 `DefaultReconciliationMatcher`：日期 ±1 天、金額容忍 0.005，sign-aware）雙向配對對帳單列與 trade；金額完全相等 → 不產 diff，差異在容忍度內但不相等 → AmountMismatch。`EnsureLegalTransition` 強制 Kind × Resolution 合法表，杜絕 UI 端錯置裁決。
- **Reconciliation Tab（F3）** — Import 頁改為 TabControl，新增 Reconciliation 分頁；toolbar 內含 Session 下拉、Recompute、SignOff，DataGrid 顯示 Kind / Date / Amount / Counterparty / Resolution / Actions（MVP 動作集：Mark Resolved / Ignore / Delete Trade），底部顯示 Pending / Resolved / Total 計數。
- **D1 — 跨 context 共用準備** — `ImportPreviewRow` 標註可被 Reconciliation 沿用；`ImportBatchEntry` 新增 `PreviewRowJson` 並由 `ImportApplyService` 寫入，使 `IImportBatchHistoryRepository.GetPreviewRowsAsync` 能成為對帳單來源。

### 內部變更

- 新增 `Assetra.Core/Models/Reconciliation/`、`Assetra.Core/Interfaces/Reconciliation/`、`Assetra.Core/DomainServices/Reconciliation/`、`Assetra.Application/Reconciliation/`、`Assetra.Infrastructure/Persistence/Reconciliation*`、`Assetra.WPF/Features/Reconciliation/`。
- `AppBootstrapper` 加入 `AddReconciliationContext`；`SqliteSchemaHelper.KnownTables` 補上 `import_batch_history` / `import_batch_entry` / `reconciliation_session` / `reconciliation_diff`；`ImportBatchHistorySchemaMigrator` 以 `MigrateAddColumn` 新增 `preview_row_json` 欄。
- `ReconciliationService` 暴露 `public static ComputeDiffs(...,matcher)` 與 `public static EnsureLegalTransition(...)` 供 unit test 直接驅動，避免測試需要實例化 service。
- `Languages/zh-TW.xaml` / `en-US.xaml` 新增 `Import.Tab.Import` 與 14 組 `Reconciliation.*` 鍵（Tab / Title / Subtitle / Session / Recompute / SignOff / 6 個 Col.*、3 個 Action.*）。
- 421 → 444 筆測試全綠（新增 7 筆 `DefaultReconciliationMatcher` + 16 筆 `ReconciliationService`，含 Kind × Resolution 合法表 Theory）。

### 暫不納入（後續 sprint）

- `Created` / `OverwrittenFromStatement` 兩種 Resolution 在 UI 上的執行路徑（需先打通 `ImportRowMapper` 與 trade 寫入，目前服務端可接受、UI 暫未提供按鈕）。
- 「新建 Reconciliation Session」對話框（目前由程式碼路徑 `CreateAsync` 建立；UI 只能載入既有 session）。
- 即時餘額對帳面板與 DataGrid Kind 分組。

## v0.8.0 - 2026-04-27

收尾 Import Governance Phase 2：把匯入端與手動端的自動分類規則整合為單一規則系統，加入批次歷史與 rollback。

### 重點

- **統一自動分類規則（F1）** — 將匯入用 `ImportRule` 與手動用 `AutoCategorizationRule` 整合為同一個模型；新增 `Name`、`MatchField`（對方／備註／兩者任一／完整內文）、`MatchType`（包含／等於／開頭是／正規表達式）與 `[Flags] AppliesTo`（Manual / Import / Both）。既有規則以「AnyText + Contains + Both」做向後相容預設值。
- **匯入時自動帶入分類（F2）** — `ImportRowMapper` / `ImportApplyService` 套用規則 snapshot；命中規則時自動帶入 `Trade.CategoryId`，未命中保持空。
- **批次歷史 + Rollback（F3）** — `ImportBatchHistoryRepository` 紀錄每批匯入的 entries（新增 / 覆蓋 / 跳過 + JSON snapshot），`ImportRollbackService` 可一鍵還原已套用的批次；UI 新增 Import 歷史摺疊區與 rollback 按鈕。
- **Categories 進階規則 UX（C4）** — 規則行 inline 編輯下方新增「進階選項」Expander，包含 MatchField / MatchType 單選、AppliesTo 雙勾選與「即時測試」面板（輸入對方／備註範例即顯示 ✓／✗），新增規則表單同步擁有相同進階區。預設摺疊；簡單模式視覺零變化。

### 內部變更

- 擴充 `AutoCategorizationRule` 為 record-with 模式並向後相容（新欄位皆有預設值）；schema 以 `ALTER TABLE` 加上 `name` / `match_field` / `match_type` / `applies_to` 欄位。
- `AutoCategorizationEngine` 改為 dual-API：保留 `Match(string?, rules)` 給手動路徑，新增 `Match(AutoCategorizationContext, rules)` 並依 `AppliesTo & Source` 過濾。
- `ImportApplyService` 接收選用 `IAutoCategorizationRuleRepository`；DI 端在 `ImportServiceCollectionExtensions` 用 lambda factory 串接。
- `Languages/zh-TW.xaml` / `en-US.xaml` 新增 19 組 `Categories.Rule.*` 鍵（Advanced / MatchField / MatchType / AppliesTo / LiveTest 等）。
- 421 筆測試全綠（含 7 筆新引擎測試 + 2 筆 ImportApplyService 自動分類測試）。

## v0.7.0 - 2026-04-27

新增 Import Governance：把銀行對帳單與券商交易明細的 CSV / Excel 匯入到 Assetra，並自動偵測重複交易。

### 重點

- **匯入功能（v0.7 主題）** — 新增 `Import` bounded context，支援 Top 5 銀行（國泰世華 / 玉山 / 中信 / 台新 / 富邦）與 Top 5 券商（元大 / 富邦 / 凱基 / 永豐金 / 群益）對帳單。CSV 與 Excel（.xlsx / .xls）皆可，UTF-8 / Big5 編碼自動辨識。
- **格式驅動的 Parser** — 解析行為由 `CsvParserConfigs` / `ExcelParserConfigs` 宣告式定義；新增或修正某家銀行 / 券商格式時只要改 config 不必動程式碼。
- **重複交易偵測** — 以 `date | abs(amount) | symbol` 為跨資料庫比對 key，UI 預覽列以 Skip / Overwrite / Add anyway 三種處置方式呈現。
- **Modern UX 匯入頁** — 拖放區、自動偵測格式 chip、預覽 DataGrid（含每列衝突處理下拉）、現金帳戶選擇（必選才能套用）、結果 snackbar。

### 內部變更

- 新增 `Assetra.Core/Models/Import/`、`Assetra.Core/Interfaces/Import/`、`Assetra.Application/Import/`、`Assetra.Infrastructure/Import/`、`Assetra.WPF/Features/Import/`。
- 新增套件 `CsvHelper` 33.0.1、`ClosedXML` 0.105.0。
- `AppBootstrapper` 加入 `AddImportContext()`；NavRail 在 Settings 上方加入 Import 入口（Segoe Fluent `&#xE8B5;`）。
- 392 → 多筆 import 測試（Core models、parsers、format detector、conflict detector、apply service）全綠。

## v0.6.0 - 2026-04-26

收尾 v0.6.0 sprint：月結報告 UI、淨資產趨勢視覺化，以及 Goals MVP。

### 重點

- **月結報告 UI（F1）** — 新增 Reports 頁面，含月份選擇器、四張指標卡（收入 / 支出 / 淨額 / 儲蓄率，皆附與上月差額）、超支清單、近期到期清單，後端由 `MonthEndReportService` 提供。
- **淨資產趨勢（F2）** — 新增 Trends 頁面，沿用 `PortfolioHistoryViewModel`。提供 30 / 90 / 180 / 365 / All 預設區間按鈕，加上 `DateRangePicker` 自訂範圍（兩端皆設定時覆蓋預設）。
- **Goals MVP（F3）** — 全新 bounded context：`FinancialGoal` 模型、`IFinancialGoalRepository` + `GoalSqliteRepository`、`GoalSchemaMigrator`，並新增 Goals 頁面含進度條、期限顯示與新增表單（FormTextBox + FormDatePicker + 欄位標籤）。
- **SetupNotice 切片** — 從 `PortfolioViewModel` 抽出 `SetupNoticeViewModel`（顯示 / 標題 / 訊息 / 動作文字 + 執行命令），延續 v0.6.0 的 D1-2 SubViewModels 重構工作。
- **主題 / 語言 / 字級稽核** — 三個新頁面：`AppText` → `AppTextPrimary`、`FontSize="{StaticResource …}"` → `{DynamicResource …}`，確保執行期主題與字級切換能即時生效。

### 內部變更

- 新增 `Assetra.WPF/Features/Reports/`、`Assetra.WPF/Features/Trends/`、`Assetra.WPF/Features/Goals/` 功能資料夾，並在 Nav rail 與 `MainViewModel` / `MainWindow` 接好內容區塊。
- 在 `AppBootstrapper` 註冊 `AddGoalsContext` DI 擴充方法。
- `zh-TW.xaml` / `en-US.xaml` 新增 `Trends.CustomRange`、`Goals.Add.Deadline` 等語言鍵。

## v0.5.8 - 2026-04-26

本版優化在地化提示文字並整理專案文件。

### 重點

- 調整 zh-TW 與 en-US 的「現金 / 信用卡 / 負債」新增對話框提示文字，反映目前「建立時即記錄初始條目」的流程，取代舊的「之後再去新增記錄」說法。
- 補上 `CHANGELOG.md` 中遺漏的 v0.4.1、v0.5.6、v0.5.7 條目。
- 移除 `docs/INDEX.md` 中失效的 `Downloads` 連結。

## v0.5.7 - 2026-04-26

本版優化投資組合對話框與表格的響應式體驗。

### 重點

- 新增紀錄對話框中的切換配對強制以橫向兩欄排列（窄寬時不再降回直向）。
- 透過覆寫預設的 `ListBoxItem` 範本，修正投資 Position 卡片 hover 矩形溢出。
- 將固定寬度的 `WrapPanel` 換成 `UniformGrid`，讓 position 統計儲存格能填滿可用寬度，無尾端空隙。
- Accounts / Liability DataGrid 第一欄欄頭由「資產」改為「名稱」；投資欄頭改為「標的」。
- 將 Accounts / Liability 儲存格範本重構為單列 `Grid`，使預設徽章能對齊整個儲存格高度的垂直中線。
- 新增紀錄底部按鈕順序改為符合 Windows 對話框慣例（取消在左，確認在右），並把「取消編輯」縮短為「取消」。

## v0.5.6 - 2026-04-25

本版修正負債建立流程的回歸問題。

### 重點

- 修正在某些狀態下，負債建立對話框未顯示貸款區塊的問題。

## v0.5.5 - 2026-04-24

本版優化啟動穩定性與復原行為。

### 重點

- 啟動時不再每次都因更新檢查而被阻塞。
- 僅在前次啟動未完成時才執行復原型啟動更新檢查。
- 維持日常啟動速度，同時保留損壞安裝的修復路徑。

### 內部變更

- 在 `App.xaml.cs` 新增 `startup.pending` 標記流程。
- 一般啟動流程改為主視窗顯示後在背景檢查更新。
- 復原更新路徑只在前次啟動疑似失敗時觸發。

## v0.5.4 - 2026-04-24

本版改善 Windows 應用程式品牌資產的細緻度。

### 重點

- 將模糊的 Windows 應用程式圖示路徑替換為專屬的多尺寸 Windows `.ico`。
- 應用程式 / 視窗圖示改為 Windows 專用資產，不再沿用網頁 favicon。

### 內部變更

- 新增 `Assets/windows/assetra-app.ico`。
- 更新 WPF 專案的圖示串接與主視窗圖示資源。

## v0.5.3 - 2026-04-24

本版在發生損壞安裝事件後，導入「啟動優先」的更新安全網。

### 重點

- 啟動時於主視窗開啟前先檢查更新。
- 啟動失敗時嘗試自我修復更新。

### 內部變更

- 此版本為過渡性安全版本，後續在 `v0.5.5` 進一步精修。

## v0.5.2 - 2026-04-24

本版修正啟動崩潰並強化幣別資料載入。

### 重點

- 修正初始 UI 載入時因徽章樣式造成的崩潰。
- 強化 Frankfurter 匯率解析以容忍缺失的 JSON 欄位。

### 內部變更

- `PortfolioBadgeBase` 不再透過脆弱的啟動路徑解析 `CornerRadius`。
- `CurrencyService` 採用容忍式 JSON 解析與安全 fallback。

## v0.5.1 - 2026-04-24

本版修正啟動畫面 / 圖示啟動可靠性問題。

### 重點

- 修正因啟動畫面圖示資源載入造成的啟動失敗。
- 改善打包安裝版本的啟動穩定性。

### 內部變更

- 啟動畫面圖示載入由靜態 XAML 資源解析改為以程式碼處理。
- 收緊圖示資產的資源打包規則。

## v0.5.0 - 2026-04-24

本版加入信用卡流程，並進行大規模響應式 UI 整修。

### 重點

- 新增信用卡資產與交易流程。
- 重組品牌資產與套件 logo 流程。
- 修飾 portfolio、alerts、settings、shell 與對話框版面的響應式表現。

### 內部變更

- 新增信用卡 workflow、schema 支援與回歸測試。
- 重做許多 WPF 版面，以更好支援較大的 `UiScale` 與較窄的寬度。

## v0.4.1 - 2026-04-23

本版新增 Fugle API key 設定的應用內指引。

### 重點

- 新增可從 Settings 頁面開啟的 Fugle 說明對話框。
- 在 zh-TW 與 en-US 提供設定步驟，使用者不必離開 app 即可完成設定。

## v0.4.0 - 2026-04-23

本版專注於更安全的 portfolio 編輯與可設定的市場資料來源。

### 重點

- 新增 `Fugle` 作為可設定的即時報價與歷史價格來源。
- 新增 `Settings` 欄位，分別管理報價來源、歷史來源與本機 Fugle API key 儲存。
- 新增於 Git 之外安全設定 Fugle API key 的文件。
- 重做記錄編輯流程：
  - 安全編輯模式
  - 建立修訂版
  - 取代原紀錄 / 兩者保留
- 將通用歡迎橫幅替換為任務導向的設定提示。

### 內部變更

- `StockScheduler` 可使用 Fugle，並在失敗時 fallback 至 TWSE/TPEX 官方來源。
- `DynamicHistoryProvider` 支援 `fugle`，與 `twse`、`yahoo`、`finmind` 並列。
- `PortfolioViewModel` 與測試保留向後相容的建構路徑，同時改用較新的 application 層服務。

## v0.3.0 - 2026-04-22

本版是 Assetra 第一個聚焦於架構的里程碑。

### 重點

- 加入更清晰的 `Core -> Application -> Infrastructure -> WPF` 結構。
- 將大多數 `Portfolio` 變更流程移入 application workflow services。
- 將 summary / load / history / query 職責移入專屬的 application services。
- 模組化啟動、schema migration、repository 初始化的責任。
- 在 `Portfolio` UI 部分引入較薄的 WPF 端 controllers / sub-viewmodels。
- 將 `Alerts` 重新置於 application 層介面（`IAlertService`）之後。
- 新增 workflow 層級測試與架構文件。

### 內部變更

- `PortfolioViewModel` 將更多行為委派給：
  - workflow services
  - query services
  - WPF 端 controllers / sub-viewmodels
- `FinancialOverviewViewModel` 改為透過 application query service 讀取資料。
- `.superpowers/` 本機工具產物已加入忽略，且不再追蹤。

## v0.2.0

application 層與架構整理工作之前較早期的產品里程碑。
