# Assetra.Application 架構重構設計

**日期：** 2026-04-21  
**範圍：** 修正 Assetra.Application 層的 5 個架構缺點  
**策略：** 漸進式 — 3 個 Phase，低風險逐步推進

---

## 背景

引入 `Assetra.Application` 層後，發現以下 5 個缺點需修正：

| 代號 | 缺點 | Phase |
|------|------|-------|
| A | PortfolioViewModel 注入 15 個服務，職責過廣 | 3 |
| B | Pass-through 服務（Alert、LoanSchedule）無附加邏輯 | 1 |
| C | 執行責任不一致（Plan 模式 vs Execute 模式混用） | 2 |
| D | Namespace 不一致（專案 Assetra.Application，但 namespace Assetra.AppLayer） | 1 |
| E | Anemic Domain Model 風險（純計算邏輯在 Application 層而非 Core） | 2 |

---

## Phase 1 — 快速收益

### D：Namespace 統一

- 全域 find-and-replace：`Assetra.AppLayer` → `Assetra.Application`
- 影響：所有 `.cs` 的 `namespace` 宣告與 `using` 語句
- `Assetra.Application.csproj` 名稱已正確，不需改動

### B：移除 pass-through 服務

下列 3 個服務只薄包裝 repo，無附加邏輯，予以移除：

| 移除介面 / 實作 | ViewModel 改為直接注入 |
|----------------|----------------------|
| `IAlertQueryService` / `AlertQueryService` | `IAlertRepository` |
| `IAlertMutationService` / `AlertMutationService` | `IAlertRepository` |
| `ILoanScheduleQueryService` / `LoanScheduleQueryService` | `ILoanScheduleRepository` |

ViewModel 直接依賴 Core 介面（Core 層的穩定合約），完全符合依賴方向原則。

---

## Phase 2 — 結構調整

### C：統一 Execute 模式

`TransactionWorkflowService` 改為每個方法自行持久化，ViewModel 不再需要持有 `ITransactionService` 或執行 Plan 迭代。

| 舊方法 | 新方法 |
|--------|--------|
| `CreateIncomePlan(request)` | `RecordIncomeAsync(request, ct)` |
| `CreateCashDividendPlan(request)` | `RecordCashDividendAsync(request, ct)` |
| `CreateStockDividendPlan(request)` | `RecordStockDividendAsync(request, ct)` |
| `CreateCashFlowPlan(request)` | `RecordCashFlowAsync(request, ct)` |
| `CreateLoanPlan(request)` | `RecordLoanAsync(request, ct)` |
| `CreateTransferPlan(request)` | `RecordTransferAsync(request, ct)` |

同步移除：
- `TransactionWorkflowPlan` DTO
- ViewModel 中對 `ITransactionService` 的直接呼叫
- `ITransactionService` 在 PortfolioViewModel 的注入

**例外**：`BuildBuyPreview` 等純 read-only 計算方法保留 Preview 語意，不需改為 Execute 模式。

### E：PortfolioSummaryService 移至 Core

`PortfolioSummaryService` 已是純計算（無 IO 依賴），天生屬於 Domain Service。

- `IPortfolioSummaryService` + `PortfolioSummaryService` 移至 `Assetra.Core.DomainServices`
- namespace：`Assetra.Core.DomainServices`
- 相關 DTOs 移至 `Assetra.Core.Dtos`：
  - `PortfolioSummaryInput`、`PositionSummaryInput`
  - `CashBalanceInput`、`LiabilityBalanceInput`
  - `PortfolioSummaryResult`、`PositionWeightResult`
  - `AllocationSliceResult`、`AllocationSliceKind`
- `AppBootstrapper.cs` 新增手動登記：`services.AddSingleton<IPortfolioSummaryService, PortfolioSummaryService>()`，移除原 Application 層的對應登記

---

## Phase 3 — ViewModel 拆分

### A：Sub-ViewModel 組合

`PortfolioViewModel` 拆成主 VM + 4 個 Sub-VM，透過建構子組合。

#### Sub-VM 責任

| Sub-VM | 注入服務 | 負責功能 |
|--------|---------|---------|
| `AddAssetDialogViewModel` | `IAddAssetWorkflowService` | 股票搜尋、關閉價格查詢、買入預覽、執行買入、手動新增資產 |
| `TransactionDialogViewModel` | `ITransactionWorkflowService`、`ISellWorkflowService`、`ITradeDeletionWorkflowService`、`ITradeMetadataWorkflowService` | 賣出、現金流、股利、轉帳、刪除／編輯交易 |
| `AccountDialogViewModel` | `IAccountUpsertWorkflowService`、`IAccountMutationWorkflowService` | 帳戶新增、編輯、刪除、封存 |
| `LoanDialogViewModel` | `ILoanMutationWorkflowService`、`ILoanPaymentWorkflowService`、`ILoanScheduleRepository` | 貸款新增、還款、排程查詢 |

#### 主 PortfolioViewModel 保留（5 個服務）

- `IPortfolioLoadService`
- `IPortfolioSummaryService`（Phase 2 後來自 Core）
- `IPortfolioHistoryMaintenanceService`
- `IPositionDeletionWorkflowService`
- `IPositionMetadataWorkflowService`

#### DI 與 XAML

- 所有 Sub-VM 在 `AppBootstrapper.cs` 登記為 `AddSingleton`
- 主 PortfolioViewModel 透過建構子接收 Sub-VM
- XAML binding 路徑：`{Binding AddAssetDialog.SearchQuery}`、`{Binding Transaction.TxType}` 等

#### 檔案結構

```
Assetra.WPF/Features/Portfolio/
 ├── PortfolioViewModel.cs           (主 VM，5 個服務)
 ├── SubViewModels/
 │    ├── AddAssetDialogViewModel.cs
 │    ├── TransactionDialogViewModel.cs
 │    ├── AccountDialogViewModel.cs
 │    └── LoanDialogViewModel.cs
 ├── PortfolioViewModel.Assets.cs    (移除，邏輯移入 AddAssetDialogViewModel)
 └── PortfolioViewModel.Transactions.cs (移除，邏輯移入 TransactionDialogViewModel)
```

---

## 測試策略

- Phase 1：執行現有測試確保全部通過（只改 namespace 與 using）
- Phase 2：更新 `PortfolioViewModelTests` mock 設定；為 `PortfolioSummaryService` 在 Core 補充單元測試
- Phase 3：為每個 Sub-VM 新增獨立單元測試；更新整合測試中的 `CreateVm` helper

---

## 依賴方向確認

```
Core（DomainServices + Dtos）
  ↑
Application（Workflow Services，不含 Summary）
  ↑
Infrastructure / WPF
```
