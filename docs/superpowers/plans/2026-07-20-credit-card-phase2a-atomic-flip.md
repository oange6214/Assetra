# 信用卡 Phase 2a — 卡片變付款方式（原子變更）實作計畫

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans. Steps use checkbox (`- [ ]`) syntax.

**Goal:** 讓信用卡正式脫離「負債」、成為 `FinancialType.PaymentMethod`：投影不再把卡片交易算成負債、既有卡片搬遷、新建卡片產生付款方式、退役舊刷卡/繳卡費流程。落地後負債頁只剩貸款、淨值 +61,187、舊流程不可達。

**Architecture:** 這是**不可分割的原子變更**（見 Phase 1 計畫的執行修正）。順序：先投影（讓卡片退出負債頁 header 與淨值）→ 搬遷既有卡片 → 創建端改產付款方式 → 退役 UI/service 舊流程 → 正式 DB 驗證。卡片與貸款共用 `TxAssetKind.Liability`，只外科移除 creditCard 部分，貸款分支不動。`TradeType.CreditCardCharge/CreditCardPayment` **列舉值保留**（歷史交易引用；`PrimaryCashDelta` 仍靠 `CreditCardPayment` 處理歷史繳款的現金效果）——只是不再產生新的、不再於 UI 可選。

**Tech Stack:** .NET 10、C#、WPF、Microsoft.Data.Sqlite、xUnit。

**前置：** 每步 build+test 綠才 commit；建置前關閉 app。正式 DB（`%APPDATA%\Assetra\assetra.db`）只在 Task 6 動,需授權+備份。Phase 1（欄位）已落地（commit `b8fc990`）。

**依賴的既有事實（Phase 1 執行 + 兩次 Explore 已確認）：**
- `LiabilitySnapshot`（`Assetra.Core/Interfaces/IBalanceQueryService.cs:76-85`）：`Balance`、`OriginalAmount` 皆為 **`Money`**（非 decimal）；`LiabilitySnapshot.Empty = new(Money.Zero(Default), Money.Zero(Default))`。
- `GetLiabilitySnapshotAsync`（`BalanceQueryService.cs:40-47`）查無 label 時回 `(0,0)` 包成 Money，等同 Empty，**不丟例外**。
- `MapItem` 用 `Enum.Parse<FinancialType>`，故 `PaymentMethod` 字串自動吞下。

---

## Task 1：投影 — 信用卡交易不再算成負債

**Files:**
- Modify: `Assetra.Infrastructure/BalanceQueryService.cs`
- Test: `Assetra.Tests/Infrastructure/BalanceQueryServiceTests.cs`

- [ ] **Step 1：改寫既有投影測試成新意圖**

`BalanceQueryServiceTests.cs`：
1. `Liability_CreditCardChargeAndPayment_ProjectOutstandingBalance`（`:282`）整段換成：
```csharp
[Fact]
public async Task Liability_CreditCardTrades_NoLongerProjectedAsLiability()
{
    // 信用卡改為付款方式後，刷卡/繳卡費交易不再聚合成負債快照（歷史交易保留）。
    var card = Guid.NewGuid();
    var cash = Guid.NewGuid();
    var svc = Create(
        CreditCardCharge(card, "玉山 Pi 卡", 8_000m),
        CreditCardCharge(card, "玉山 Pi 卡", 2_000m),
        CreditCardPayment(card, "玉山 Pi 卡", cash, 3_500m));

    var snap = await svc.GetLiabilitySnapshotAsync("玉山 Pi 卡");

    Assert.Equal(0m, snap.Balance.Amount);        // Balance 是 Money，取 .Amount
    Assert.Equal(0m, snap.OriginalAmount.Amount); // 欄位名是 OriginalAmount
}
```
2. `GetAllLiabilitySnapshots_IncludesCreditCards`（`:297`）改名 `GetAllLiabilitySnapshots_ExcludesCreditCards`，斷言卡片 label 不在回傳字典中（保留其貸款 setup，改斷言卡片鍵不存在）。
3. **保留** `Cash_CreditCardPayment_DebitsCashAccount`（`:239`）不動——它驗的是 `PrimaryCashDelta` 的現金效果,本 Task 不碰 `PrimaryCashDelta`。

- [ ] **Step 2：跑改寫的兩個測試，確認 FAIL**（目前仍投影出 6,500 / 仍含卡片）。

Run：`dotnet test Assetra.Tests/Assetra.Tests.csproj --filter "FullyQualifiedName~BalanceQueryServiceTests"`

- [ ] **Step 3：`GetLiabilityLabel` 對卡片回 null + 移除死 case**

`BalanceQueryService.cs`：
1. `GetLiabilityLabel`（`:278-283`）改為：
```csharp
private static string? GetLiabilityLabel(Trade t) => t.Type switch
{
    TradeType.LoanBorrow or TradeType.LoanRepay => t.LoanLabel,
    // 信用卡改為「付款方式」——刷卡/繳卡費不再視為負債（歷史交易保留為紀錄）。
    _ => null,
};
```
2. `ProjectLiabilitySnapshots`（`:115-150`）的 `t.Type switch`：移除 `TradeType.CreditCardCharge` 與 `TradeType.CreditCardPayment` 兩個 case（label 已回 null，迴圈開頭即 `continue`，這兩 case 不可達）。
3. `ComputeLiabilitySnapshot`（`:218-251`）的 `switch`：同樣移除 `case TradeType.CreditCardCharge:` 與 `case TradeType.CreditCardPayment:`。
4. **不要動** `PrimaryCashDelta`（`:257-270`）——`CreditCardPayment => -(t.CashAmount)` 保留（歷史繳款仍需扣現金）。

- [ ] **Step 4：跑測試確認 PASS。**

- [ ] **Step 5：全量測試。** 預期此時 `PortfolioViewModelTests` 的卡片繳款測試會開始壞（Task 4 處理）——本 Task 只需確認 `BalanceQueryServiceTests` 全綠 + build 綠。**先不 commit**，因為此改動單獨會讓繳卡費 UI 破窗；與 Task 2–4 同屬原子批次，最後一起驗、一起 commit（見 Task 4 Step 末）。

> **原子性說明：** Task 1–4 是一個原子變更,理想上以連續 commit 落地、且中間不發版。若採 subagent 逐任務,可各自 commit（master 中間狀態不影響安裝版），但 **Task 4 完成前全套件不會全綠**——這是預期的,不是失敗。每個 Task 的驗證門檻寫在各自末尾。

---

## Task 2：搬遷 — 既有信用卡資產 Liability → PaymentMethod

**Files:**
- Modify: `Assetra.Infrastructure/Persistence/AssetSchemaMigrator.cs`
- Test: `Assetra.Tests/Infrastructure/AssetSqliteRepositoryTests.cs`

- [ ] **Step 1：寫失敗測試**

`AssetSqliteRepositoryTests.cs`：
```csharp
[Fact]
public async Task Migration_ExistingCreditCard_BecomesPaymentMethod()
{
    var repo1 = new AssetSqliteRepository(_dbPath);
    var legacyCard = new AssetItem(
        Guid.NewGuid(), "台新", FinancialType.Liability, null, "TWD",
        DateOnly.FromDateTime(DateTime.Today),
        LiabilitySubtype: LiabilitySubtype.CreditCard,
        IssuerName: "台新銀行");
    await repo1.AddItemAsync(legacyCard);
    Assert.Equal(FinancialType.Liability, (await repo1.GetByIdAsync(legacyCard.Id))!.Type);

    AssetSchemaMigrator.EnsureInitialized($"Data Source={_dbPath}"); // 代表升級後開 app

    var after = await new AssetSqliteRepository(_dbPath).GetByIdAsync(legacyCard.Id);
    Assert.Equal(FinancialType.PaymentMethod, after!.Type);
    Assert.Equal(LiabilitySubtype.CreditCard, after.LiabilitySubtype); // 保留供識別

    AssetSchemaMigrator.EnsureInitialized($"Data Source={_dbPath}"); // 冪等
    Assert.Equal(FinancialType.PaymentMethod,
        (await new AssetSqliteRepository(_dbPath).GetByIdAsync(legacyCard.Id))!.Type);
}
```

- [ ] **Step 2：跑測試確認 FAIL。**

- [ ] **Step 3：加冪等 backfill**

`AssetSchemaMigrator.cs`，在 `EnsureInitialized`（`:40-108`）的 `BackfillLiabilitySubtype(cmd);` **之後**（須先有 liability_subtype 值才能判斷卡片）插入：
```csharp
BackfillCreditCardsToPaymentMethod(cmd);
```
新方法（比照 `BackfillLiabilitySubtype` 的 `SqliteCommand` 風格）：
```csharp
// 信用卡改為「付款方式」：把既有 Liability + CreditCard subtype 的資產搬成 PaymentMethod。
// 冪等——搬完即無列符合。歷史 trade 不動。
private static void BackfillCreditCardsToPaymentMethod(SqliteCommand cmd)
{
    cmd.CommandText = """
        UPDATE asset SET asset_type = 'PaymentMethod'
         WHERE asset_type = 'Liability' AND liability_subtype = 'CreditCard';
        """;
    cmd.ExecuteNonQuery();
}
```

- [ ] **Step 4：跑測試確認 PASS（含冪等段）。** Build 綠。（全套件此時仍有 Task 4 待修的破窗測試——預期。）

---

## Task 3：創建端 — 新建卡片產生付款方式（且不再冒開帳刷卡）

**Files:**
- Modify: `Assetra.Application/Portfolio/Services/CreditCardMutationWorkflowService.cs`
- Modify: `Assetra.WPF/Features/Portfolio/SubViewModels/AddAssetDialogViewModel.cs`
- Test: `Assetra.Tests/Portfolio/CreditCardMutationWorkflowServiceTests.cs`

- [ ] **Step 1：改測試意圖**

`CreditCardMutationWorkflowServiceTests.cs`：`CreateAsync_PersistsCreditCardMetadata`（`:13`）與 `UpdateAsync_RewritesCreditCardMetadata`（`:42`）的斷言 `FinancialType.Liability` 改成 `FinancialType.PaymentMethod`（其餘 billing/due/limit/issuer/subtype 斷言不變）。

- [ ] **Step 2：跑測試確認 FAIL。**

- [ ] **Step 3：CreateAsync / UpdateAsync 產 PaymentMethod**

`CreditCardMutationWorkflowService.cs`：兩處 `new AssetItem(... FinancialType.Liability ...)` 都改為 `FinancialType.PaymentMethod`。

- [ ] **Step 4：AddCreditCardAsync 移除開帳刷卡區塊**

`AddAssetDialogViewModel.cs` 的 `AddCreditCardAsync`（`:914-982`）：**刪除** `if (AddInitialCreditCardBalanceEnabled) { ... ChargeAsync(...) ... }` 整段（付款方式無餘額概念,不再有「初始未繳金額」）。相關 reset 行（`AddInitialCreditCardBalance*`）保留無害,但這些欄位與其 UI 已無意義——標記於報告供 Phase 3 移除 `CreditCardCreateSection` 內的初始餘額 UI（`CreditCardCreateSection.xaml:178-218`）。若 `_creditCardTransactionWorkflow` 欄位在移除該段後變成未使用,一併移除該欄位與其注入（但注意 Task 4 會整個退役該 service——可留到 Task 4 一起清）。

- [ ] **Step 5：跑測試確認 PASS。** Build 綠。

---

## Task 4：退役舊刷卡/繳卡費流程（UI + service + 死碼 + 測試）

外科移除：只拔 creditCard 部分,貸款分支全留。`TradeType` 列舉值保留。

**Files（modify）:**
- `Assetra.WPF/Features/Portfolio/SubViewModels/TransactionDialogViewModel.cs`
- `Assetra.WPF/Features/Portfolio/SubViewModels/TransactionDialogViewModel.Confirm.cs`
- `Assetra.WPF/Infrastructure/PortfolioServiceCollectionExtensions.cs`
- `Assetra.WPF/Features/Portfolio/PortfolioViewModelFactory.cs`、`PortfolioDependencies.cs`、`PortfolioViewModel.cs`、`PortfolioViewModel.NullServices.cs`
- `Assetra.WPF/Features/Portfolio/Controls/AddRecordDialog.xaml`（移除 CreditCardTxForm 參照）

**Files（delete）:**
- `Assetra.WPF/Features/Portfolio/SubViewModels/TransactionDialogViewModel.Confirm.CreditCard.cs`
- `Assetra.WPF/Features/Portfolio/SubViewModels/Tx/CreditCardTxViewModel.cs`
- `Assetra.WPF/Features/Portfolio/Controls/TxForms/CreditCardTxForm.xaml`(.cs)
- `Assetra.Application/Portfolio/Services/CreditCardTransactionWorkflowService.cs`
- `Assetra.Application/Portfolio/Contracts/ICreditCardTransactionWorkflowService.cs`
- `Assetra.Application/Portfolio/Dtos/CreditCardTransactionRequests.cs`（`CreditCardChargeRequest/PaymentRequest/Result`）
- `Assetra.Tests/Portfolio/CreditCardTransactionWorkflowServiceTests.cs`

- [ ] **Step 1：移除交易類型與派工（reachability）**

`TransactionDialogViewModel.cs`：
- `_allTradeTypes`（`:293-307`）移除 `creditCardCharge`、`creditCardPayment` 兩列。
- `ResolveAvailableTypeKeys`（`:347-369`）`TxAssetKind.Liability` 的集合改為 `{ "loanBorrow", "loanRepay" }`。
- `CreditCardOptions`（`:1464-1465`）刪除。
- `SyncSelectedAssetIntoLiabilityState`（`:646-663`）刪除 `CreditCardOptions.FirstOrDefault` 卡片分支（`if (card is not null) { CreditCard.Card = card; return; }` 那段），保留貸款分支。
- `TxTypeIsCreditCard*`（`:1347-1349`）三個屬性刪除;`OnTxTypeChanged`（`:1703-1705`）對應的三行 `OnPropertyChanged` 刪除。
- `CreditCard` 屬性（`:1462`）與 ctor 訂閱（`:188-192`）刪除;`_creditCardTransactionWorkflowService` 欄位（`:100`、`:202`）與 `TransactionDialogDependencies.CreditCardTransaction`（`:31`）刪除。

`TransactionDialogViewModel.Confirm.cs`：`_confirmDispatch`（`:21-36`）移除 `["creditCardCharge"]`、`["creditCardPayment"]` 兩行;成功 snackbar switch（`:254-255`）移除其對應 case。

- [ ] **Step 2：刪除死碼檔案**（上方 delete 清單的 WPF/App 檔）+ 移除 `AddRecordDialog.xaml` 對 `CreditCardTxForm` 的參照（搜 `CreditCardTxForm`）。

- [ ] **Step 3：清 DI / 工廠 / null 物件**

- `PortfolioServiceCollectionExtensions.cs:157-163` 移除 `ICreditCardTransactionWorkflowService` 註冊（**保留** `ICreditCardMutationWorkflowService`——Task 3 仍用它建付款方式）。
- `PortfolioViewModelFactory.cs:71-72,82-83`、`PortfolioDependencies.cs:55-56`、`PortfolioViewModel.cs:485-486`、`PortfolioViewModel.NullServices.cs:169`：移除 transaction workflow 的解析/傳遞/null 實作。`NullServices.cs:136` 若是 mutation 的 null 實作則保留但改產 `PaymentMethod`。

- [ ] **Step 4：更新/刪除測試**（依 Explore F 清單）

- 刪整檔：`CreditCardTransactionWorkflowServiceTests.cs`。
- `BalanceQueryServiceTests`：Task 1 已改;若 `CreditCardCharge/Payment` helper（`:64/:69`）僅剩 `Cash_CreditCardPayment_DebitsCashAccount` 使用則保留,否則清未用的。
- `TransactionDialogViewModelTests.cs`：刪 `OpenTxDialogForLiability_CreditCardPaymentPreselectsCardAndLocksAssetSelector`（`:344`）、`TxType_CreditCardChargeAndPayment_BothMapToTxTypeIsCreditCard`（`:970`）;`EditTrade_LiabilityRowsPreselectLiabilityAndHydrateTypePicker`（`:597` Theory）移除 `CreditCardCharge`/`CreditCardPayment` 兩筆 InlineData（`:595-596`），保留貸款兩筆。
- `PortfolioViewModelTests.cs`：刪 `ConfirmTx_CreditCardCharge_IncreasesCardBalance`（`:1332`）、`ConfirmTx_CreditCardPayment_DecreasesCardBalanceAndCash`（`:1349`）、`EditTrade_CreditCardPayment_DateOnlyChange_OnPaidOffCard_DoesNotTriggerNoBalanceGuard`（`:1378`）;`AddCreditCard_ValidInput_AddsLiabilityRow`（`:1292`）改為斷言新建卡片**不**出現在 `vm.Liabilities`（已是 PaymentMethod）;清理其專用 helper `SeedCreditCardBaselineAsync`/`CreateVmWithCreditCardAndCashAsync`（`:1736`）若無其他引用。
- `RecurringViewModelTests.cs:50`：`AddTradeType = TradeType.CreditCardCharge`（測「不支援訂閱」）——`CreditCardCharge` 列舉值仍在,測試仍有效,**不動**（除非改用更中性的 unsupported type;可留註解說明）。
- `TradeSqliteRepositoryInitializeTests.cs:85`（`AddAsync_CreditCardTrade_RoundTripsLiabilityAssetId`）：`CreditCardCharge` 列舉仍存在、SQLite round-trip 仍有效,**保留**。
- `MonthlyBudgetSummaryServiceTests.cs:131`：`CreditCardCharge` 計為當月支出——列舉仍在,行為未改,**保留**（除非全套件顯示它壞,再評估）。

- [ ] **Step 5：全量測試——這裡才要求 0 失敗。**

Run：`dotnet test Assetra.Tests/Assetra.Tests.csproj`
Expected：**0 failed**。這是 Task 1–4 原子批次的總驗證門檻。

- [ ] **Step 6：commit（Task 1–4 可各自 commit 或合併;若逐任務 commit,確保本 commit 後全綠）**

```
git add -A
git commit -m "refactor(信用卡): 卡片改為付款方式，退出負債並退役刷卡/繳卡費舊流程"
```
> ⚠ 用 `git add -A` 前先 `git status` 確認沒有無關未追蹤檔（本 repo 有一個 `docs/.../2026-06-24-sell-total-mode-parity.md` 未追蹤,**勿**納入）。改用逐檔 `git add` 更保險。

---

## Task 5：i18n 清理

- [ ] **Step 1：清死 key**

搜尋 `Portfolio.Tx.CreditCardPayment.NoBalance`、`Portfolio.Tx.CreditCardPayment.ExceedsBalance`（Confirm.CreditCard.cs 用過,已刪）及其他僅刷卡/繳卡費用到的 key,於 `zh-TW.xaml` + `en-US.xaml` 逐一驗證零引用後刪除。**逐 key 用正則邊界比對**（key 後接 `"` 或 `}`；勿用簡單前綴,避免誤刪）。任一仍被別處引用者保留。

- [ ] **Step 2：build + 全量測試綠 → commit**
```
git commit -m "refactor(信用卡): 清除刷卡/繳卡費退役後的死語系 key"
```

---

## Task 6：正式 DB 搬遷驗證（手動、需授權 + 備份）

**動使用者正式資料庫,非自動。**

- [ ] 關閉 Assetra。
- [ ] 備份 `%APPDATA%\Assetra\assetra.db`(+`-wal`/`-shm`) 到帶日期備份檔。
- [ ] 複製一份到 scratch,用 Python(`sqlite3`)記錄搬遷前：台新卡 `asset_type`、負債總額(loans)、trade 筆數、各現金帳戶餘額、目前淨值。
- [ ] 以本階段 app 版本開啟（migrator 啟動時跑 backfill）或於副本跑等效 UPDATE。
- [ ] 對數字：(a) 台新卡 `asset_type='PaymentMethod'`；(b) 負債頁不含台新卡；(c) 淨值 = 原 + 61,187；(d) 各銀行帳戶餘額不變；(e) trade 筆數不變。
- [ ] 全符才套正式檔；任一不符 → 從備份還原、回報。

---

## Self-Review（對照 spec §4.4/§4.5/§4.6）

- **§4.4 卡片退出負債/淨值：** Task 1（投影）+ Task 2（搬遷）。投影是 string-label-keyed 且交易型別驅動——Task 1 從源頭讓卡片交易回 null（比在消費端過濾更乾淨,涵蓋 header 與淨值兩條路）。
- **§4.5 退役舊機制：** Task 3（創建改）+ Task 4（UI/service/死碼/測試）。
- **§4.6 搬遷：** Task 2（自動 backfill）+ Task 6（正式庫驗證）。
- **原子性：** Task 1–4 的全綠門檻集中在 Task 4 Step 5,明說中間狀態預期不綠。
- **型別一致：** `FinancialType.PaymentMethod`、`snap.Balance.Amount`/`OriginalAmount.Amount`（Money）、保留 `TradeType.CreditCardCharge/Payment` 列舉——全程一致。
- **風險：** 卡片/貸款共用 Liability kind → 只移除 creditCard 部分（Task 4 逐點）;`CreditCard.Card` 型別 `LiabilityRowViewModel?` 整個移除;開帳餘額機制（開帳刷卡）隨創建改而消失（Task 3）。

## 後續（各自成計畫）
- **Phase 2b：** 每月帳單 = `Withdrawal + PaymentMethodId`、結帳日 cycle 查詢、dashboard 提醒 nudge + 預填。
- **Phase 3：** 付款方式建/編/清單（移出 liability 對話框、移除 `CreditCardCreateSection` 初始餘額 UI）、交易記錄以 `PaymentMethodId` 分卡篩選。
- **跨階段：** 同步層 mapper（`AssetSyncMapper` 兩 default 欄、Trade sync mapper `payment_method_id`）於填值 Phase 前補（需懂 sync 協定版本相容性）。
