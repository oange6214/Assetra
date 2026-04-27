# v0.10.0 Sprint Plan — Reconciliation Phase 2

> 範圍：1.5–2 週。延續 v0.9.0 MVP，把「能載入既有 session」的半成品補成「日常可用」工作流。
> 完成後，Reconciliation context 不再需要靠程式碼建 session，且 `Created` / `OverwrittenFromStatement` 兩個 Resolution 在 UI 上可執行。

## 一、版本目標

| # | 項目 | Bounded Context | 預估 |
|---|------|---|---|
| D1 | 抽 `IImportRowApplier` —— 把 `ImportRowMapper` + `ITradeRepository.AddAsync` 收成一個跨 context 介面 | Importing | S |
| F1 | `NewReconciliationSessionDialog` —— 選帳戶 / 期間 / 來源（上傳新檔或既有 Import batch） | Reconciliation / WPF | M |
| F2 | `Created` / `OverwrittenFromStatement` 兩個 Resolution 的 UI 執行路徑 | Reconciliation / WPF | M |
| F3 | DataGrid Kind 分組 + 帳戶餘額對帳面板 | WPF | S |

## 二、缺口全景

### P0（本 sprint 範圍）

- D1：`IImportRowApplier.ApplyAsync(ImportPreviewRow row, Guid accountId, ImportConflictResolution resolution)` —— 從 v0.7 `ImportApplyService` 抽 single-row apply 路徑出來，讓 Reconciliation UI 不需要把整個 batch 拉進來
- F1：對帳工作台 toolbar 新增「新建對帳作業」按鈕，彈 modal dialog
  - 帳戶 ComboBox（沿用 `IAssetRepository.GetItemsByTypeAsync(FinancialType.Asset)`）
  - 期間 DatePickerStart / DatePickerEnd（預設上個月 1 號至月底）
  - 來源 RadioButton：`既有 Import batch`（ComboBox 列既有 `ImportBatchHistory`，via `GetPreviewRowsAsync`）or `上傳新檔`（沿用 v0.7 拖放區）
  - 確認 → 呼叫 `IReconciliationService.CreateAsync` 並切換到新 session
- F2：DataGrid Actions 欄位
  - **Missing → Created**：呼叫 `IImportRowApplier.ApplyAsync` 寫入 trade，再呼叫 `ApplyResolutionAsync(diffId, Created)`
  - **AmountMismatch → OverwrittenFromStatement**：以對帳單列覆蓋 trade（`ITradeRepository.UpdateAsync` 帶新 amount），再標記 resolution
  - 兩個動作完成後 `RecomputeAsync` 刷新 diff 列表
- F3：UI 收尾
  - DataGrid 套 `CollectionViewSource` + `GroupStyle`，依 Kind 分三組（Missing / Extra / AmountMismatch），各組可摺疊
  - 右側 panel 顯示「對帳單期末餘額」vs「trade 累加」vs「差額」三個數字（期末餘額由使用者在 dialog 輸入，存於 `ReconciliationSession.Note` 或新增欄位）

### P1（下一輪）

- 對帳簽收後產出對帳報告（屬 v0.11 Reports）
- 多帳戶合併對帳（同期間銀行 + 券商）
- 自動依 `ImportBatchHistory.AccountId + Period` 推薦來源 batch

### P2（範圍邊緣）

- 自動排程對帳提醒（屬 Recurring 子系統擴充）
- AmountMismatch 顯示金額差異與差異率（UI 美化）

## 三、動工前要先處理的技術債

### D1-1 抽 `IImportRowApplier`

v0.7 `ImportApplyService.ApplyAsync(batch, accountId, perRowResolutions)` 是 batch-oriented；F2 需要單列 apply（每按一次「建立交易」就跑一次）。

```csharp
public interface IImportRowApplier
{
    Task<Guid> ApplyAsync(
        ImportPreviewRow row,
        Guid accountId,
        ImportRowMapping mapping,                  // 沿用既有 mapper 結果
        CancellationToken ct = default);
}
```

實作 `DefaultImportRowApplier`：依 `ImportRowMapper` 把 row 轉成 `Trade`，呼叫 `ITradeRepository.AddAsync`，回傳 trade id。`ImportApplyService` 內部改成迭代呼叫 applier，避免邏輯分裂。

### D1-2 `ReconciliationSession` 增 `StatementEndingBalance` 欄位

F3 餘額對帳需要對帳單期末餘額。新增可選 `decimal? StatementEndingBalance`，並透過 `ALTER TABLE` migration 補 `statement_ending_balance REAL`。Computed balance 由 `ITradeRepository.GetByCashAccountAsync` 在期間內累加得到，不需存。

## 四、F1 / F2 / F3 設計要點

### F1 NewReconciliationSessionDialog

- `Features/Reconciliation/NewSessionDialog.xaml` —— Modal Window，遵循 v0.6 `FormTextBox` / `FormDatePicker` 樣式
- `NewSessionDialogViewModel` —— ObservableObject，欄位：SelectedAccount / PeriodStart / PeriodEnd / SourceMode / SelectedBatch / UploadedFile / EndingBalance
- 確認流程：
  1. 來源 = 既有 batch → `_history.GetPreviewRowsAsync(batchId)`
  2. 來源 = 上傳新檔 → 沿用 `ImportFormatDetector` + `ImportParserFactory` parse 出 `IReadOnlyList<ImportPreviewRow>`
  3. 呼叫 `IReconciliationService.CreateAsync(...)` 取得 session
  4. `DialogResult = true`，父 ViewModel 把新 session 加進 ObservableCollection 並選中

### F2 UI 動作執行路徑

```
[Missing diff row] → button "建立交易" 
  → ReconciliationViewModel.CreateTradeAsync(diff)
  → applier.ApplyAsync(diff.StatementRow, session.AccountId, mapping)
  → service.ApplyResolutionAsync(diff.Id, Created)
  → service.RecomputeAsync(session.Id) → 重綁 Diffs

[AmountMismatch diff row] → button "覆蓋為對帳單"
  → ReconciliationViewModel.OverwriteAsync(diff)
  → trades.UpdateAsync(trade with { CashAmount = diff.StatementRow.Amount })
  → service.ApplyResolutionAsync(diff.Id, OverwrittenFromStatement)
  → service.RecomputeAsync(session.Id)
```

`ImportRowMapping` 預設用「Income / accountId / 對帳單 Counterparty 為描述」；分類規則由 v0.8 `AutoCategorizationEngine` 自動套（Source = Import）。

### F3 UI 收尾

#### DataGrid 分組

```xml
<CollectionViewSource x:Key="GroupedDiffs" Source="{Binding Diffs}">
  <CollectionViewSource.GroupDescriptions>
    <PropertyGroupDescription PropertyName="KindDisplay" />
  </CollectionViewSource.GroupDescriptions>
</CollectionViewSource>
```

`ReconciliationDiffRowViewModel.KindDisplay` 暴露 i18n string（紅 / 黃 / 橘前綴 emoji or icon column）。

#### 餘額面板

右側固定 200px 寬 panel：

```
對帳單期末餘額    100,000
─────────────────────
依 trade 推算    98,500
─────────────────────
差額              1,500   ← 紅字若 ≠ 0
```

當所有 diff 都 Resolved 但差額仍 ≠ 0 → 提示「請確認對帳單期末餘額或補登手續費」。

## 五、測試重點

| 層 | 重點 |
|---|---|
| Application | `DefaultImportRowApplier.ApplyAsync` 單列正向 / 重複偵測命中 / mapping 失敗 |
| Application | Reconciliation 端對 `Created` 路徑：apply → resolution → recompute 後該 diff 消失 |
| Application | Reconciliation 端對 `OverwrittenFromStatement` 路徑：trade 金額更新 + diff 從 AmountMismatch 變空 |
| Infrastructure | `statement_ending_balance` 欄位 round-trip |
| Tests count | 預估 +12–18 筆，總數應達 ~456–462 |

## 六、文件 / 收尾

- CHANGELOG v0.10.0 條目（D1 / F1 / F2 / F3）
- `docs/architecture/Bounded-Contexts.md`：Reconciliation Context 把「Created / OverwrittenFromStatement UI 路徑」與「新建 session 對話框」從 ⏳ 改 ✅
- `docs/planning/Implementation-Roadmap.md` 對應 checkbox 打勾
- 標 v0.10.0 tag
