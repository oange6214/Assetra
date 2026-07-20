# 信用卡「每月帳單式」重構 — 實作計畫

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把信用卡從「貸款式負債」改為「付款方式」——移出負債與淨值、加好承載每月帳單所需的資料欄位,並把既有台新卡一次性搬遷過去,資料無損。

**Architecture:** 沿用房規的 per-table `*SchemaMigrator`（`CREATE TABLE IF NOT EXISTS` + 冪等 `MigrateAddColumn` + allowlist；無版本化 runner）。新增 `FinancialType.PaymentMethod`、`AssetItem.DefaultCashAccountId/DefaultCategoryId`、`Trade.PaymentMethodId`。卡片退出負債靠兩處：投影端讓 `GetLiabilityLabel` 對信用卡交易回 `null`（負債頁 header 與淨值走此路），資產端把卡片 `AssetItem.Type` 由 `Liability` 改為 `PaymentMethod`（FinancialOverview / BalanceSheet / 幣別查詢走此路）。

**Tech Stack:** .NET 10、C#、WPF、Microsoft.Data.Sqlite、xUnit。

**Scope（執行時修正）:** **Phase 1 ＝ 純疊加的資料模型（Task 1–3，已完成）**：`FinancialType.PaymentMethod` + `AssetItem` 兩欄 + `Trade.PaymentMethodId`。無任何行為變更、無破壞。

> ⚠ **執行發現的分階段修正（2026-07-20）：** 原計畫把 Task 4「卡片退出負債/淨值」放 Phase 1、把「退役刷卡/繳卡費舊流程」放 Phase 2。執行 Task 4 時證實這是**不可分割的原子變更**：單獨做 Task 4 會讓舊繳卡費守衛（`ConfirmCreditCardPaymentAsync` 的 `Balance <= 0`）擋死每筆新繳卡費,且**新建卡片仍是 `Liability`**（`CreditCardMutationWorkflowService.CreateAsync` 未改）→ 一建新卡即回歸;單獨做搬遷（Task 5）不做 Task 4,投影仍以卡名生出卡片快照,卡片以「無資產列」殘留負債。查證（Explore）確認：搬遷完成後舊流程**不可達**（卡片退出 `Liabilities` → 退出 picker 與 `CreditCardOptions` → `CreditCard.Card` 無法填）。故 Task 4/5 + 退役舊流程 + 新流程必須整包一次落地。
>
> **修正後：原 Task 4–8 全數移入 Phase 2**（下方原 Task 4–8 段落保留為 Phase 2 的技術參考,但 Phase 2 會重新以「原子變更」重寫成完整計畫）。**Phase 1 到 Task 3 為止即完成。**

**下方 Task 1–3 ＝ Phase 1（已執行完畢）。Task 4–8 ＝ Phase 2 技術參考（待重寫為原子計畫）。** Roadmap 見文末。

**Spec:** `docs/superpowers/specs/2026-07-20-credit-card-monthly-bill-design.md`

---

## 前置：建置與測試指令

- Build：`dotnet build D:\Workspaces\Finances\Assetra\Assetra.slnx`
- Test（全部）：`dotnet test D:\Workspaces\Finances\Assetra\Assetra.Tests\Assetra.Tests.csproj`
- Test（單一）：`dotnet test D:\Workspaces\Finances\Assetra\Assetra.Tests\Assetra.Tests.csproj --filter "FullyQualifiedName~<TestName>"`
- **建置前務必關閉執行中的 Assetra**（否則鎖住 DLL/EXE）。承諾：本 Phase **不碰使用者的正式 DB**（`%APPDATA%\Assetra\assetra.db`）——所有測試走 temp-file / in-memory；正式 DB 的搬遷驗證在 Task 8，需備份與明確授權。

---

## 檔案地圖（本 Phase 觸及）

| 檔案 | 責任 | 動作 |
|---|---|---|
| `Assetra.Core/Models/FinancialType.cs` | 頂層分類 enum | 加 `PaymentMethod` |
| `Assetra.Core/Models/AssetItem.cs` | 資產/負債 record | 加 `DefaultCashAccountId`、`DefaultCategoryId` |
| `Assetra.Core/Models/Trade.cs` | 交易 record | 加 `PaymentMethodId` |
| `Assetra.Infrastructure/Persistence/AssetSchemaMigrator.cs` | asset 表 DDL/ALTER/backfill | 加 2 欄 + 卡片搬遷 backfill |
| `Assetra.Infrastructure/Persistence/AssetSqliteRepository.cs` | asset 讀寫映射 | SELECT/Map/INSERT/UPDATE×2 + sync 序號 |
| `Assetra.Infrastructure/Persistence/TradeSchemaMigrator.cs` | trade 表 DDL/ALTER | 加 1 欄 |
| `Assetra.Infrastructure/Persistence/TradeSqliteRepository.cs` | trade 讀寫映射 | SELECT/Map/Bind/INSERT/UPDATE + sync 序號 |
| `Assetra.Infrastructure/BalanceQueryService.cs` | 餘額/負債投影 | `GetLiabilityLabel` 對卡片交易回 null |
| `Assetra.Tests/Infrastructure/AssetSqliteRepositoryTests.cs` | asset repo 測試 | 新增 round-trip 測試 |
| `Assetra.Tests/Infrastructure/TradeSqliteRepositoryTests.cs` | trade repo 測試 | 新增 round-trip 測試 |
| `Assetra.Tests/Infrastructure/BalanceQueryServiceTests.cs` | 投影測試 | 改寫既有卡片投影測試 |

---

## Task 1：新增 `FinancialType.PaymentMethod`

**Files:**
- Modify: `Assetra.Core/Models/FinancialType.cs`
- Test: `Assetra.Tests/Infrastructure/AssetSqliteRepositoryTests.cs`

- [ ] **Step 1：寫失敗測試**（證明 PaymentMethod 資產能持久化、且被型別過濾正確歸類）

加到 `AssetSqliteRepositoryTests.cs`：

```csharp
[Fact]
public async Task Items_PaymentMethodType_RoundTripsAndSeparatesFromLiability()
{
    var repo = new AssetSqliteRepository(_dbPath);
    var card = new AssetItem(
        Guid.NewGuid(),
        "台新 @GoGo",
        FinancialType.PaymentMethod,
        null,
        "TWD",
        DateOnly.FromDateTime(DateTime.Today),
        LiabilitySubtype: LiabilitySubtype.CreditCard,
        BillingDay: 15,
        IssuerName: "台新銀行");

    await repo.AddItemAsync(card);

    var found = await repo.GetByIdAsync(card.Id);
    Assert.NotNull(found);
    Assert.Equal(FinancialType.PaymentMethod, found!.Type);

    var paymentMethods = await repo.GetItemsByTypeAsync(FinancialType.PaymentMethod);
    Assert.Contains(paymentMethods, a => a.Id == card.Id);

    var liabilities = await repo.GetItemsByTypeAsync(FinancialType.Liability);
    Assert.DoesNotContain(liabilities, a => a.Id == card.Id);
}
```

- [ ] **Step 2：跑測試確認失敗**

Run：`dotnet test Assetra.Tests/Assetra.Tests.csproj --filter "FullyQualifiedName~Items_PaymentMethodType_RoundTripsAndSeparatesFromLiability"`
Expected：編譯失敗（`PaymentMethod` 不存在於 `FinancialType`）。

- [ ] **Step 3：加 enum 值**

`Assetra.Core/Models/FinancialType.cs`：

```csharp
public enum FinancialType { Asset, Investment, Liability, PaymentMethod }
```

- [ ] **Step 4：跑測試確認通過**

Run：同 Step 2。
Expected：PASS。
若失敗且訊息指向 `asset_type` 解析（`MapItem`）：代表該處用手寫 switch 而非 `Enum.Parse`。開 `AssetSqliteRepository.cs` 的 `MapItem`（約 767–806）找 `asset_type` → `FinancialType` 的轉換,補 `"PaymentMethod" => FinancialType.PaymentMethod`（或確認它走 `Enum.Parse<FinancialType>`，那就不需改）。`GetItemsByTypeAsync` 以 `type.ToString()` 比對 `asset_type` 欄，`PaymentMethod` 字串自然吻合。

- [ ] **Step 5：全量測試 + commit**

Run：`dotnet test Assetra.Tests/Assetra.Tests.csproj`（全綠）
```bash
git add Assetra.Core/Models/FinancialType.cs Assetra.Tests/Infrastructure/AssetSqliteRepositoryTests.cs
git commit -m "feat(信用卡): 新增 FinancialType.PaymentMethod 並驗證持久化與型別過濾"
```

---

## Task 2：`AssetItem` 加 `DefaultCashAccountId` + `DefaultCategoryId`

付款方式的「預設扣款銀行」「預設分類」。兩欄皆 `Guid?`，DB 存 `TEXT`。

**Files:**
- Modify: `Assetra.Core/Models/AssetItem.cs`
- Modify: `Assetra.Infrastructure/Persistence/AssetSchemaMigrator.cs`
- Modify: `Assetra.Infrastructure/Persistence/AssetSqliteRepository.cs`
- Test: `Assetra.Tests/Infrastructure/AssetSqliteRepositoryTests.cs`

- [ ] **Step 1：寫失敗測試**

```csharp
[Fact]
public async Task Items_PaymentMethodDefaults_RoundTrip()
{
    var repo = new AssetSqliteRepository(_dbPath);
    var bank = Guid.NewGuid();
    var category = Guid.NewGuid();
    var card = new AssetItem(
        Guid.NewGuid(),
        "台新 @GoGo",
        FinancialType.PaymentMethod,
        null,
        "TWD",
        DateOnly.FromDateTime(DateTime.Today),
        LiabilitySubtype: LiabilitySubtype.CreditCard,
        BillingDay: 15,
        IssuerName: "台新銀行",
        DefaultCashAccountId: bank,
        DefaultCategoryId: category);

    await repo.AddItemAsync(card);
    var found = await repo.GetByIdAsync(card.Id);

    Assert.NotNull(found);
    Assert.Equal(bank, found!.DefaultCashAccountId);
    Assert.Equal(category, found.DefaultCategoryId);
}
```

- [ ] **Step 2：跑測試確認失敗**

Run：`dotnet test Assetra.Tests/Assetra.Tests.csproj --filter "FullyQualifiedName~Items_PaymentMethodDefaults_RoundTrip"`
Expected：編譯失敗（`AssetItem` 無 `DefaultCashAccountId` 命名參數）。

- [ ] **Step 3：model 加兩個欄位**

`AssetItem.cs`，在 `string? IssuerName = null,` 之後、`string? Subtype = null)` 之前插入（保持既有參數順序不動,只在尾端 `Subtype` 前加）：

```csharp
    string? IssuerName = null,
    Guid? DefaultCashAccountId = null,
    Guid? DefaultCategoryId = null,
    // Free-form user-facing label for preset categorization (e.g., 房貸/車貸/數位活存)
    string? Subtype = null)
```

- [ ] **Step 4：schema migrator 加兩欄 + allowlist**

`AssetSchemaMigrator.cs`：
1. `AssetAllowedColumns`（約 19–28）加兩個字串：`"default_cash_account_id"`、`"default_category_id"`。（`AssetAllowedTypeDefs` 已含 `"TEXT"`，不必改。）
2. 在 `AddAssetMetadataColumns`（約 193–239）末尾、既有 `subtype` 那批之後，加：

```csharp
SqliteSchemaHelper.MigrateAddColumn(conn, tx, "asset", "default_cash_account_id", "TEXT",
    AssetAllowedColumns, AssetAllowedTypeDefs);
SqliteSchemaHelper.MigrateAddColumn(conn, tx, "asset", "default_category_id", "TEXT",
    AssetAllowedColumns, AssetAllowedTypeDefs);
```

- [ ] **Step 5：repo 讀寫映射 + sync 序號位移**

`AssetSqliteRepository.cs`——**六個編輯點**，缺一即壞：
1. `ItemSelectClause`（約 39–42）：在 `subtype` 後接兩欄，變 20 欄：`... issuer_name, default_cash_account_id, default_category_id, subtype`。
   > 注意：把新欄放在 `subtype` **前**，讓 `subtype` 仍是最後一欄（減少後續 ordinal 記憶負擔）。所以順序＝`... issuer_name, default_cash_account_id, default_category_id, subtype`。新欄 ordinal＝17、18；`subtype` 由 17 移到 19。
2. `MapItem`（約 767–806）：把讀 `subtype` 的 ordinal 由 17 改為 19，並在其前插入讀 ordinal 17/18：
```csharp
DefaultCashAccountId: reader.IsDBNull(17) ? null : Guid.Parse(reader.GetString(17)),
DefaultCategoryId: reader.IsDBNull(18) ? null : Guid.Parse(reader.GetString(18)),
```
   （對照既有 `GroupId`/`Guid?` 欄的讀法，沿用同一 `IsDBNull ? null : Guid.Parse` 慣例。）
3. `InsertItemSql`（約 707–718）：欄位清單與 `$` 參數清單各加 `default_cash_account_id, default_category_id` / `$default_cash_account_id, $default_category_id`（放在 `$issuer_name` 與 `$subtype` 之間，與 SELECT 同序）。
4. `BindItem`（約 808–828）：加兩個 bind（`Guid?` → `value?.ToString() ?? (object)DBNull.Value`，比照既有 `$group_id`）：
```csharp
cmd.Parameters.AddWithValue("$default_cash_account_id", item.DefaultCashAccountId?.ToString() ?? (object)DBNull.Value);
cmd.Parameters.AddWithValue("$default_category_id", item.DefaultCategoryId?.ToString() ?? (object)DBNull.Value);
```
5. `UpdateItemAsync`（約 244–256）**與** `CreateOrReviveAccountAsync` 的 revive 分支（約 218–230）：兩份 SET 清單都加 `default_cash_account_id = $default_cash_account_id, default_category_id = $default_category_id`。
6. **Sync 序號位移**：`ItemSyncSelectClause`（約 44–45）＝ItemSelectClause 之後接 sync 欄；`GetPendingPushAsync`（約 561–580）硬編 `reader.GetInt64(18)`（version）、`(19)`、`(20)`、`(21)`。因 item 欄由 18 增為 20，全部 **+2**：18→20、19→21、20→22、21→23。`ApplyRemoteAsync`（約 660–699）的 INSERT/UPSERT 欄位清單同樣要把兩新欄補進去（比照 SELECT 順序），否則遠端套用會漏欄。

- [ ] **Step 6：跑測試確認通過**

Run：`dotnet test Assetra.Tests/Assetra.Tests.csproj --filter "FullyQualifiedName~Items_PaymentMethodDefaults_RoundTrip"`
Expected：PASS。

- [ ] **Step 7：全量測試 + commit**

Run：`dotnet test Assetra.Tests/Assetra.Tests.csproj`（全綠——特別確認既有 `Items_CreditCardMetadata_RoundTrips` 與所有 asset sync 測試沒被序號位移弄壞）
```bash
git add Assetra.Core/Models/AssetItem.cs Assetra.Infrastructure/Persistence/AssetSchemaMigrator.cs Assetra.Infrastructure/Persistence/AssetSqliteRepository.cs Assetra.Tests/Infrastructure/AssetSqliteRepositoryTests.cs
git commit -m "feat(信用卡): AssetItem 加 DefaultCashAccountId/DefaultCategoryId 欄位與持久化"
```

---

## Task 3：`Trade` 加 `PaymentMethodId`

每月帳單支出標記「繳的是哪張卡」，供分卡統計。`Guid?`，DB 存 `TEXT`。

**Files:**
- Modify: `Assetra.Core/Models/Trade.cs`
- Modify: `Assetra.Infrastructure/Persistence/TradeSchemaMigrator.cs`
- Modify: `Assetra.Infrastructure/Persistence/TradeSqliteRepository.cs`
- Test: `Assetra.Tests/Infrastructure/TradeSqliteRepositoryTests.cs`

- [ ] **Step 1：寫失敗測試**

加到 `TradeSqliteRepositoryTests.cs`（比照該檔既有 round-trip 慣例；若需要 Trade 建構樣板,沿用檔內既有 helper）：

```csharp
[Fact]
public async Task Trade_PaymentMethodId_RoundTrips()
{
    var repo = new TradeSqliteRepository(_dbPath);
    var card = Guid.NewGuid();
    var bank = Guid.NewGuid();
    var trade = new Trade(
        Guid.NewGuid(), "", "", "台新卡 7月帳單",
        TradeType.Withdrawal, DateTime.Today, 0m, 1, null, null,
        CashAmount: 28_450m,
        CashAccountId: bank,
        CategoryId: Guid.NewGuid(),
        PaymentMethodId: card);

    await repo.AddAsync(trade);
    var found = (await repo.GetAllAsync()).Single(t => t.Id == trade.Id);

    Assert.Equal(card, found.PaymentMethodId);
}
```

- [ ] **Step 2：跑測試確認失敗**

Run：`dotnet test Assetra.Tests/Assetra.Tests.csproj --filter "FullyQualifiedName~Trade_PaymentMethodId_RoundTrips"`
Expected：編譯失敗（`Trade` 無 `PaymentMethodId`）。

- [ ] **Step 3：model 加欄位**

`Trade.cs`，在 record 尾端既有欄位之後加（放最後一個 positional param，`RealizedFxPnl` 之後）：

```csharp
    decimal? RealizedFxPnl = null,
    Guid? PaymentMethodId = null)
```

- [ ] **Step 4：schema migrator 加欄 + allowlist**

`TradeSchemaMigrator.cs`：
1. `AllowedColumns`（約 7–22）加 `"payment_method_id"`。（`AllowedTypes` 已含 `"TEXT"`。）
2. 在 `EnsureInitialized` 既有 `MigrateAddColumn(...)` 那批（約 62–107）末尾加：
```csharp
MigrateAddColumn(conn, tx, "payment_method_id", "TEXT");
```

- [ ] **Step 5：repo 讀寫映射 + sync 序號位移**

`TradeSqliteRepository.cs`——**五個編輯點**：
1. 頂部 ordinal 對照註解（約 48–62）：補一行 `// 33 payment_method_id`。
2. `SelectClause`（約 64–77）：在末欄後加 `payment_method_id`（成第 34 欄，ordinal 33）。
3. `MapTrade`（約 88–129）：末尾加讀 ordinal 33：
```csharp
PaymentMethodId: reader.IsDBNull(33) ? null : Guid.Parse(reader.GetString(33)),
```
4. `BindTradeParams`（約 131–183）：加 bind（比照既有 `$liability_asset_id`）：
```csharp
cmd.Parameters.AddWithValue("$payment_method_id", t.PaymentMethodId?.ToString() ?? (object)DBNull.Value);
```
5. `InsertSql`（約 569–596）欄位＋`$` 清單、`UpdateAsync` SET 清單（約 324–353）都加 `payment_method_id` / `$payment_method_id` / `payment_method_id = $payment_method_id`。
6. **Sync 序號位移**：`GetPendingPushAsync`（約 600–622）硬編 `reader.GetInt64(33)`、`(34)`、`(35)`、`GetInt32(36)`（附註明說「SelectClause 現有 33 欄」）。因主 SELECT 由 33 增為 34 欄，全部 **+1**：33→34、34→35、35→36、36→37；並把附註 33 改 34。`ApplyRemoteAsync`（約 702–768）的 INSERT + `ON CONFLICT DO UPDATE` 兩處欄位清單都補 `payment_method_id`。

- [ ] **Step 6：跑測試確認通過 + 全量 + commit**

Run：`dotnet test Assetra.Tests/Assetra.Tests.csproj`（全綠——特別確認 trade sync 測試未被序號位移弄壞）
```bash
git add Assetra.Core/Models/Trade.cs Assetra.Infrastructure/Persistence/TradeSchemaMigrator.cs Assetra.Infrastructure/Persistence/TradeSqliteRepository.cs Assetra.Tests/Infrastructure/TradeSqliteRepositoryTests.cs
git commit -m "feat(信用卡): Trade 加 PaymentMethodId 欄位與持久化"
```

---

## Task 4：信用卡交易退出負債投影（負債頁 header + 淨值）

`ProjectLiabilitySnapshots` 是**交易型別驅動**：`CreditCardCharge/Payment` 會被聚合成負債快照。讓 `GetLiabilityLabel` 對這兩型別回 `null`，卡片即從負債頁與 `PortfolioSummaryService` 淨值消失（淨值 +61,187）。歷史交易保留、現金餘額不受影響（`PrimaryCashDelta` 不動——歷史繳款仍正確扣現金）。

**Files:**
- Modify: `Assetra.Infrastructure/BalanceQueryService.cs`
- Modify: `Assetra.Tests/Infrastructure/BalanceQueryServiceTests.cs`

- [ ] **Step 1：改寫既有測試成新意圖**（既有 `Liability_CreditCardChargeAndPayment_ProjectOutstandingBalance` 守的是舊行為,會壞——改成「卡片交易不再產生負債快照」）

`BalanceQueryServiceTests.cs`，把該測試（約 282–293）整段換成：

```csharp
[Fact]
public async Task Liability_CreditCardTrades_NoLongerProjectedAsLiability()
{
    // 信用卡改為付款方式後，刷卡/繳卡費交易不再聚合成負債快照。
    // 歷史交易保留，但負債頁與淨值不再計入；現金餘額由 PrimaryCashDelta 各自處理（此處不驗）。
    var card = Guid.NewGuid();
    var cash = Guid.NewGuid();
    var svc = Create(
        CreditCardCharge(card, "玉山 Pi 卡", 8_000m),
        CreditCardCharge(card, "玉山 Pi 卡", 2_000m),
        CreditCardPayment(card, "玉山 Pi 卡", cash, 3_500m));

    var snap = await svc.GetLiabilitySnapshotAsync("玉山 Pi 卡");

    Assert.Equal(0m, snap.Balance);
    Assert.Equal(0m, snap.Original);
}
```

> 若 `GetLiabilitySnapshotAsync` 對「查無此 label」是回 `LiabilitySnapshot.Empty`（Balance/Original＝0）而非丟例外,上面即成立。實作前先確認該方法查無時的行為;若丟例外,改為 `Assert` 查無（比照 `LiabilitySnapshot.Empty` 語意）。

- [ ] **Step 2：跑測試確認失敗**

Run：`dotnet test Assetra.Tests/Assetra.Tests.csproj --filter "FullyQualifiedName~Liability_CreditCardTrades_NoLongerProjectedAsLiability"`
Expected：FAIL（目前仍聚合出 6,500 outstanding）。

- [ ] **Step 3：`GetLiabilityLabel` 對卡片交易回 null**

`BalanceQueryService.cs` 的 `GetLiabilityLabel`（約 278–283）改為：

```csharp
private static string? GetLiabilityLabel(Trade t) => t.Type switch
{
    TradeType.LoanBorrow or TradeType.LoanRepay => t.LoanLabel,
    // 信用卡改為「付款方式」——刷卡/繳卡費不再視為負債（歷史交易保留為紀錄）。
    _ => null,
};
```

> `ProjectLiabilitySnapshots` 的 `switch` 仍留 `CreditCardCharge/Payment` 分支無妨——`GetLiabilityLabel` 回 null 後那些交易在迴圈開頭即 `continue`，永不進 `switch`。為避免死碼誤導,可一併把該 `switch` 內 `CreditCardCharge/CreditCardPayment` 兩行移除,留註解說明「卡片已改付款方式,不再投影為負債」。

- [ ] **Step 4：跑測試確認通過**

Run：同 Step 2。Expected：PASS。

- [ ] **Step 5：加一條淨值排除測試**（鎖住「淨值不再含卡片未繳」的意圖）

若 `BalanceQueryServiceTests` 內有現成「淨值/負債總額」測試樣板則沿用；否則加一條直接針對投影的：

```csharp
[Fact]
public async Task Liability_OnlyLoansProjected_CreditCardExcluded()
{
    var loan = "台新 7y A";
    var card = Guid.NewGuid();
    var svc = Create(
        LoanBorrow(loan, 100_000m),
        CreditCardCharge(card, "台新卡", 61_187m));

    var loanSnap = await svc.GetLiabilitySnapshotAsync(loan);
    var cardSnap = await svc.GetLiabilitySnapshotAsync("台新卡");

    Assert.Equal(100_000m, loanSnap.Balance);   // 貸款仍在
    Assert.Equal(0m, cardSnap.Balance);          // 卡片不再計入
}
```

> `LoanBorrow` helper 若檔內尚無,比照既有 `CreditCardCharge` factory 新增一個設 `Type=LoanBorrow, LoanLabel=label, CashAmount=amount` 的私有 helper。

- [ ] **Step 6：全量測試 + commit**

Run：`dotnet test Assetra.Tests/Assetra.Tests.csproj`（全綠）
```bash
git add Assetra.Infrastructure/BalanceQueryService.cs Assetra.Tests/Infrastructure/BalanceQueryServiceTests.cs
git commit -m "feat(信用卡): 刷卡/繳卡費不再投影為負債，卡片退出負債頁與淨值"
```

---

## Task 5：既有信用卡資產搬遷（Liability → PaymentMethod）

沿用房規：在 `AssetSchemaMigrator` 加一個冪等 backfill，開 app 時自動把既有卡片資產型別改掉。這讓資產端（FinancialOverview `.Where(Type==Liability)`、`GetItemsByTypeAsync(Liability)`、幣別查詢）也不再看到卡片。

**Files:**
- Modify: `Assetra.Infrastructure/Persistence/AssetSchemaMigrator.cs`
- Test: `Assetra.Tests/Infrastructure/AssetSqliteRepositoryTests.cs`

- [ ] **Step 1：寫失敗測試**（在一顆 temp DB 上，先塞一張舊式卡片資產、跑第二個 repo 建構（觸發 migrator）、驗證型別已被搬）

```csharp
[Fact]
public async Task Migration_ExistingCreditCard_BecomesPaymentMethod()
{
    // 先以「舊式」寫入：Liability + CreditCard subtype（模擬升級前的資料）。
    var repo1 = new AssetSqliteRepository(_dbPath);
    var legacyCard = new AssetItem(
        Guid.NewGuid(), "台新", FinancialType.Liability, null, "TWD",
        DateOnly.FromDateTime(DateTime.Today),
        LiabilitySubtype: LiabilitySubtype.CreditCard,
        IssuerName: "台新銀行");
    await repo1.AddItemAsync(legacyCard);

    // 直接對同一 DB 檔跑一次 UPDATE 前的健檢：目前是 Liability
    var before = await repo1.GetByIdAsync(legacyCard.Id);
    Assert.Equal(FinancialType.Liability, before!.Type);

    // 觸發搬遷（backfill 於 EnsureInitialized 執行；以「重跑一次 migrator」代表升級後開 app）
    AssetSchemaMigrator.EnsureInitialized($"Data Source={_dbPath}");

    var after = await new AssetSqliteRepository(_dbPath).GetByIdAsync(legacyCard.Id);
    Assert.Equal(FinancialType.PaymentMethod, after!.Type);
    Assert.Equal(LiabilitySubtype.CreditCard, after.LiabilitySubtype); // subtype 保留供識別

    // 冪等：再跑一次不出錯、結果不變
    AssetSchemaMigrator.EnsureInitialized($"Data Source={_dbPath}");
    var again = await new AssetSqliteRepository(_dbPath).GetByIdAsync(legacyCard.Id);
    Assert.Equal(FinancialType.PaymentMethod, again!.Type);
}
```

> 若 `AssetSchemaMigrator.EnsureInitialized` 是 `internal`，測試專案已有 `InternalsVisibleTo`（既有測試直接 new repo 即觸發它）；若不可直接呼叫,改以「`new AssetSqliteRepository(_dbPath)` 第二次」代表重新初始化。

- [ ] **Step 2：跑測試確認失敗**

Run：`dotnet test Assetra.Tests/Assetra.Tests.csproj --filter "FullyQualifiedName~Migration_ExistingCreditCard_BecomesPaymentMethod"`
Expected：FAIL（型別仍是 Liability）。

- [ ] **Step 3：加冪等 backfill**

`AssetSchemaMigrator.cs`，在 `EnsureInitialized` 於 `AddAssetMetadataColumns` 與既有 `BackfillLiabilitySubtype` **之後**呼叫一個新方法（順序重要：先確保 `liability_subtype` 欄與其 backfill 完成,才依它判斷卡片）：

```csharp
BackfillCreditCardsToPaymentMethod(conn, tx);
```

方法本體（比照既有 `BackfillLiabilitySubtype` 風格，同一 conn/tx）：

```csharp
// 信用卡改為「付款方式」：把既有 Liability + CreditCard subtype 的資產型別搬成 PaymentMethod。
// 冪等——搬完即無列符合 asset_type='Liability'。歷史 trade 不動。
private static void BackfillCreditCardsToPaymentMethod(SqliteConnection conn, SqliteTransaction tx)
{
    using var cmd = conn.CreateCommand();
    cmd.Transaction = tx;
    cmd.CommandText =
        "UPDATE asset SET asset_type = 'PaymentMethod' " +
        "WHERE asset_type = 'Liability' AND liability_subtype = 'CreditCard';";
    cmd.ExecuteNonQuery();
}
```

- [ ] **Step 4：跑測試確認通過**

Run：同 Step 2。Expected：PASS（含冪等重跑段）。

- [ ] **Step 5：全量測試 + commit**

Run：`dotnet test Assetra.Tests/Assetra.Tests.csproj`（全綠）
```bash
git add Assetra.Infrastructure/Persistence/AssetSchemaMigrator.cs Assetra.Tests/Infrastructure/AssetSqliteRepositoryTests.cs
git commit -m "feat(信用卡): 既有信用卡資產搬遷為付款方式（冪等 backfill）"
```

---

## Task 6：資產端消費點確認排除付款方式

Task 5 把卡片改為 `PaymentMethod` 後,以下路徑因原本就 `.Where(Type == Liability)` / `GetItemsByTypeAsync(Liability)` 而自動排除卡片。本 Task 只是**加測試鎖住**、並巡檢是否有漏網。

**Files:**
- Test: `Assetra.Tests/Application/`（依既有 FinancialOverview / BalanceSheet 測試位置）

- [ ] **Step 1：巡檢並列出消費點**（無程式改動,先讀確認行為）

確認以下四處在卡片變 `PaymentMethod` 後自然排除卡片（都已按 `FinancialType.Liability` 過濾）：
- `FinancialOverviewQueryService.BuildLiabilityGroupsAsync`（`.Where(i => i.Type == FinancialType.Liability)`，約 :171）
- `BalanceSheetService.GenerateAsync`（`GetItemsByTypeAsync(FinancialType.Liability)`，約 :60）
- `PortfolioLoadService.LoadAsync`（`GetItemsByTypeAsync(FinancialType.Liability)`，約 :46）
- `BalanceQueryService` 幣別查詢（:166 / :185）——卡片交易已不投影（Task 4），故此路不再被卡片觸及。

若發現任何一處是以「交易型別」或「卡名」判斷負債而非 `FinancialType`,補進本 Task 修正 + 測試。

- [ ] **Step 2：加 FinancialOverview 排除測試**

於 `FinancialOverviewQueryService` 既有測試檔（沿用其建構樣板；若無則於 `Assetra.Tests/Application/` 新增對應測試類）加：

```csharp
[Fact]
public async Task FinancialOverview_PaymentMethodCard_NotInLiabilities()
{
    // 一張 PaymentMethod 卡片 + 一筆貸款；概覽負債只含貸款。
    // （建構方式沿用該測試檔既有的 assets/trades 注入樣板。）
    // Arrange：assets = [ 貸款(Liability), 台新卡(PaymentMethod) ]
    // Act：BuildAsync
    // Assert：liabilityGroups 不含台新卡；負債總額 = 貸款額
}
```

> 此步的具體 Arrange/Assert 依 `FinancialOverviewQueryService` 測試的既有 helper 補齊（讀該檔後填入真實建構碼）。

- [ ] **Step 3：跑測試 + commit**

Run：`dotnet test Assetra.Tests/Assetra.Tests.csproj`（全綠）
```bash
git add Assetra.Tests/Application/
git commit -m "test(信用卡): 鎖住付款方式卡片不計入概覽/報表負債"
```

---

## Task 7：負債頁不再顯示卡片（UI 層驗證）

Task 4 已讓卡片不進 `_liabilities`（投影無快照）+ Task 5 讓卡片資產非 Liability。負債頁 `ApplyLiabilities` 兩條加入路徑（snapshot join、與「有資產無交易」的補列）都不會再納入卡片。加一條特徵測試鎖住。

**Files:**
- Test: `Assetra.Tests/WPF/`（依 `PortfolioViewModel` 既有測試位置；用 `FakeTradeRepo` + 假 asset repo）

- [ ] **Step 1：寫測試**

```csharp
[Fact]
public void ApplyLiabilities_ExcludesPaymentMethodCards()
{
    // Arrange：PortfolioLoadResult 內 LiabilitySnapshots 只含貸款；
    //          LiabilityAssets 不含卡片（因卡片已非 FinancialType.Liability）。
    // Act：驅動 ApplyLiabilities（依既有 PortfolioViewModel 測試的注入方式）。
    // Assert：vm.Liabilities 無任何 IsCreditCard 列；HasNoLiabilities 依貸款數。
}
```

> 依 `PortfolioViewModel` 既有測試 helper 補齊 Arrange/Act 真實碼（讀該測試檔後填入）。

- [ ] **Step 2：跑測試 + commit**

Run：`dotnet test Assetra.Tests/Assetra.Tests.csproj`（全綠）
```bash
git add Assetra.Tests/WPF/
git commit -m "test(信用卡): 負債頁不再顯示付款方式卡片"
```

---

## Task 8：正式 DB 搬遷驗證（手動、需備份與明確授權）

**此 Task 動使用者正式資料庫,非自動,須逐項確認。**

- [ ] **Step 1：關閉 Assetra**（確保無鎖）。
- [ ] **Step 2：備份**：複製 `%APPDATA%\Assetra\assetra.db`（含 `-wal`/`-shm` 若存在）到帶日期的備份檔。
- [ ] **Step 3：在副本上先驗**：複製一份 DB 到 scratch 目錄,用 Python（`sqlite3` 標準庫）記錄搬遷前基準：卡片列的 `asset_type`、負債總額（loans）、trade 筆數。
- [ ] **Step 4：套用**：以本 Phase 建好的 app 版本開啟（migrator 於啟動時跑 backfill），或在副本上直接跑等效 `UPDATE`。
- [ ] **Step 5：對數字**：搬遷後——(a) 台新卡 `asset_type = 'PaymentMethod'`；(b) 負債頁不含台新卡；(c) 淨值 = 原淨值 + 61,187；(d) 各銀行帳戶餘額不變；(e) trade 筆數不變（歷史保留）。
- [ ] **Step 6：確認無誤後**才在正式檔上執行；任一項不符 → 從備份還原、回報。

> 此步不寫死金額於程式;61,187 為當前實際未繳額,以搬遷前查得的實際值為準。

---

## Self-Review（對照 spec）

**Spec 涵蓋度：** §4.1 資料模型→Task 1/2/3；§4.4 負債/淨值排除→Task 4（投影）+ Task 5（資產型別）+ Task 6/7（消費點鎖定）；§4.6 搬遷→Task 5（自動）+ Task 8（正式庫驗證）；§8 風險#3（投影是交易型別驅動）→Task 4 正面處理。**Phase 2（§4.2/§4.3 每月帳單 + 提醒）與 Phase 3（§4.7 管理 UI + 分卡篩選）不在本計畫**,見下方 Roadmap。

**型別一致性：** `DefaultCashAccountId`/`DefaultCategoryId`（`Guid?`）、`PaymentMethodId`（`Guid?`）、`FinancialType.PaymentMethod` 在 Task 1–7 用法一致；DB 欄名 `default_cash_account_id`/`default_category_id`/`payment_method_id` 全程一致。

**已知需實作時就地確認（非佔位,是「讀檔補真實碼」）：** Task 1 Step 4 的 `asset_type` 解析寫法、Task 4 Step 1 的 `GetLiabilitySnapshotAsync` 查無語意、Task 6/7 依既有測試 helper 補 Arrange/Assert。這些都標明了要讀哪個檔、補什麼,不是模糊指示。

---

## Phase 2 / Phase 3 Roadmap（各自成計畫，於 Phase 1 驗證後撰寫）

> 不在此展開真實碼——它們依賴 Phase 1 落地後的欄位與 migrator 行為。此處僅記任務輪廓,供下一輪 writing-plans。

**跨階段待辦 — 同步層 mapper（Phase 1 執行時發現，刻意暫緩）**
- Phase 1 只更新了 SQLite repo 層（含 `ApplyRemoteAsync` 的 SQL）。**JSON 跨裝置同步 DTO 尚未帶新欄位**：`Assetra.Infrastructure/Sync/AssetSyncMapper.cs`（`default_cash_account_id`/`default_category_id`）與對應的 Trade sync mapper（`payment_method_id`）。
- 後果：新欄位跨裝置同步會掉成 null。因這些欄位要到 Phase 2（PaymentMethodId）/Phase 3（AssetItem 兩 default）才被填值，Phase 1 期間無實害。
- 需在**填值的那個 Phase 之前**補上，且需先弄懂 sync 協定的版本相容性（新欄位對舊版 peer 的影響）——非機械改動,不可盲補。Trade mapper（Phase 2 用）優先。

**Phase 2 — 卡片變付款方式（原子變更）+ 每月帳單流程 + 結帳日提醒**

_「卡片變付款方式」以下五項必須同一批落地,缺一即留下不一致（見上方執行修正）：_
- **投影端**：`BalanceQueryService.GetLiabilityLabel` 對 `CreditCardCharge/Payment` 回 `null`（原 Task 4）。並移除 `ProjectLiabilitySnapshots` / `ComputeLiabilitySnapshot` 內死掉的卡片 case。
- **資產端搬遷**：`AssetSchemaMigrator` 冪等 backfill `Liability→PaymentMethod`（原 Task 5）。
- **創建端**：`CreditCardMutationWorkflowService.CreateAsync` 改為建立 `PaymentMethod` 卡（否則新建卡回歸為 Liability）。
- **退役舊流程**：移除刷卡/繳卡費 tx 類型 chip、`ConfirmCreditCard*Async` 派工與 `CreditCard*WorkflowService`、`CreditCardOptions`；連同其測試（`ConfirmTx_CreditCardPayment_*`、`GetAllLiabilitySnapshots_*` 等守舊行為者）一併更新/移除。
- **正式 DB 驗證**（原 Task 8）：備份 + 副本對數字（淨值 +61,187、負債頁無卡片、餘額不變）。
- 每月帳單記為 `Trade{ Type=Withdrawal, CashAccountId=銀行, CategoryId, PaymentMethodId=卡, CashAmount }`；沿用既有支出/現金流機制（`PrimaryCashDelta` 已處理 Withdrawal）。
- 結帳日 cycle 查詢 service：`[最近結帳日, 下次結帳日)`，短月 clamp 月底；「本期未記」＝無 `Withdrawal(PaymentMethodId=卡, TradeDate>=最近結帳日)`。
- Dashboard 提醒 nudge（in-app，非推播）+ 一鍵開預填交易對話框（卡/銀行/分類預填、金額空白）。
- 退役刷卡/繳卡費類型：新增交易對話框移除該兩型別 chip；`CreditCard*WorkflowService` 依引用移除或保留給歷史顯示。

**Phase 3 — 付款方式管理 UI + 分卡篩選**
- 付款方式建/編/清單（卡名/發卡行/結帳日/預設扣款銀行/預設分類/信用額度）；移出 `EditLiabilityDialog` 的 liability 分支與 `CreditCardCreateSection`。
- nav 位置定案（spec §10 待小確認）。
- 交易記錄以 `PaymentMethodId` 篩選,呈現「每張卡每月刷多少」。
