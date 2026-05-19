# 新增交易 Dialog Phase 2 — Asset-First IA

**Status:** 規劃中，等使用者校準後動工
**Last updated:** 2026-05-20
**Reference:** 使用者提供的 3 張外部 app 截圖（精準表單、5-step IA、500px 寬）
**Prior:** Phase 1 已完成
[f75c3b9](- type picker collapse) /
[a9e197e](- drop group + buy-mode) /
[63897d5](- discount→settings + drop actual-cash)

## TL;DR

Phase 1 把「12 個 RadioButton 分 4 組 + 多餘的折扣 / 群組 / 模式 / 實扣金額」收乾淨了。Phase 2 把整個 dialog 的 IA 從 **type-first** 翻成 **asset-first**：使用者先選資產，後面所有欄位（幣別、可選 type、推薦現金帳戶、單位/總額模式）都由那個資產驅動，主要動作從「填表」變成「確認自動帶的值」。

## 現況 vs 目標

### 現況（Phase 1 結束）
```
紀錄內容
  日期 | 類型 ComboBox            ← 平排 2 欄
  顯示位置提示                    ← 條件出現
  ─────────
  [Sub-panel：BuyTxForm / SellTxForm / ...]    ← 12 種類型各自的子表單
```
- IA：type 在前、asset 在後
- 幣別：藏在 sub-panel 內，使用者看不到也選不到
- 單價/總額 toggle：藏在 BuyTxForm
- 取得市價：button
- 總計：「預計現金流影響」一行在底部

### 目標
```
紀錄內容
  選擇資產 ▾                     ← 第一個欄位，驅動下面所有
  類型 ▾ | 日期                    ← 類型選項依資產類別過濾
  幣別 ▾                          ← 由資產帶，可手動覆寫
  數量 | 價格輸入模式 [單價/總額]   ← 通用 toggle
  單位價格 (USD)   [⚡ 取得市價]    ← inline link
    總計: 0                       ← inline mirror
  手續費 (USD)
  ☑ 從現金帳戶扣款 / 存入現金帳戶
    選擇現金帳戶 (USD) ▾
  備註
  [取消] [儲存]
```
- IA：asset → type → 細節
- 智慧預設：選資產後 80% 欄位有合理初值

## 架構改動

### 1. 新增「統一資產選擇器」

**新概念**：`TxAssetSubject` — dialog 內的 unified asset representation。包含：
- `Kind`: enum `{ Stock, Fund, Crypto, Metal, Bond, CashAccount, Liability, None }`
- `Id`: Guid? (PortfolioEntry / CashAccount / Liability 的 row id)
- `Symbol`: string?（股票才有）
- `Display`: 顯示名（"NVDA · NVIDIA Corp" / "USD Savings · USD" / "台新 7y B · TWD"）
- `Currency`: 預設幣別字串
- `SuggestedCashAccountId`: Guid?（同幣別的預設帳戶）

**VM 新欄位（TransactionDialogViewModel）**：
- `IReadOnlyList<TxAssetSubject> AvailableAssets`
  - 來源：`Positions ∪ CashAccounts ∪ Liabilities ∪ 「+ 搜尋新股票...」哨兵
- `[ObservableProperty] TxAssetSubject? _selectedAsset`
- `partial void OnSelectedAssetChanged(TxAssetSubject? v)` →
  - 設 `TxCurrency` = `v.Currency`
  - 設 `TxCashAccount` = `v.SuggestedCashAccountId` 對應的帳戶
  - 重新算 `AvailableTradeTypes`

### 2. Type-Asset 相容性矩陣

| 資產類別 → | Stock/Fund/Crypto/Metal/Bond | CashAccount | Liability | None（尚未選）|
|---|---|---|---|---|
| 買入 | ✓ | | | |
| 賣出 | ✓ (需有 lot) | | | |
| 現金股利 | ✓ (Stock/Fund) | | | |
| 股票股利 | ✓ (Stock/Fund) | | | |
| 收入 | | ✓ | | |
| 存入 | | ✓ | | |
| 提款 | | ✓ | | |
| 轉帳 | | ✓ | | |
| 借款 | | | ✓ | |
| 還款 | | | ✓ | |
| 信用卡刷卡 | | | ✓ (Card) | |
| 信用卡還款 | | | ✓ (Card) | |
| (任何) | | | | 全 disabled |

實作為 `IReadOnlyList<string> ResolveAvailableTypeKeys(TxAssetSubject? asset)`，回傳對應 key 集合。`AvailableTradeTypes` getter 過濾 by 此集合。

### 3. 通用化欄位

| 欄位 | 目前位置 | Phase 2 後 |
|---|---|---|
| 幣別 (TxCurrency) | 由 sub-panel 計算後顯示 | 通用 ComboBox（資產帶值、可覆寫） |
| 價格輸入模式 (`Buy.PriceMode`) | BuyTxForm 內部 | 通用 toggle（只在價格相關 type 顯示）|
| 取得市價 button | BuyTxForm 內部 | 通用 inline link（在價格欄位旁）|
| 總計 / 單位價格 derived | Buy 預覽卡片 | inline 顯示在價格欄正下方 |

**做法**：把 `Buy.PriceMode` / `Buy.TotalCost` 等屬性提升到 parent VM，BuyTxForm 從 ⇒ 通用區讀，舊 binding 留 facade 屬性以避免大改 sub-panel XAML。

### 4. 智慧預設 cascade

當 `SelectedAsset` 改變時，依序：

1. **設定幣別**：`TxCurrency = asset.Currency`（若使用者已手動改過則跳過 — 用 dirty flag）
2. **設定建議現金帳戶**：找 currency 相符 + 標記為 default 的 CashAccount；無則找第一個同幣別；都無則保留 null
3. **過濾類型清單**：`AvailableTradeTypes` 重新計算；若當前 `TxType` 不在新清單裡，重設為清單第一個
4. **觸發 sub-panel 預先填值**：例如選了 NVDA，Buy 的 Symbol 自動填好

## 分階段 commit 計畫

切成 4 個 commit 降低風險、每步都可 build+test：

### P2.1 — 引入 TxAssetSubject + AvailableAssets，加在頂部但不連動
- 新增 record `TxAssetSubject`
- `TransactionDialogViewModel.AvailableAssets` getter，組合 Positions/CashAccounts/Liabilities
- XAML 加 ComboBox 在類型上方，純展示用
- 既有 dialog 流程 100% 不動
- **可發布**：使用者看到下拉但選了沒有效果

### P2.2 — 連動幣別 + 推薦現金帳戶
- `OnSelectedAssetChanged` 設 `TxCurrency` + `TxCashAccount`
- 新增 `TxCurrency` ObservableProperty + 通用幣別 ComboBox（替換 sub-panel 內的 implicit display）
- 既有 sub-panel 仍然顯示自己的東西，但 currency 來自 parent
- **可發布**：選 NVDA → 幣別自動跳 USD + 推薦 USD Savings

### P2.3 — Asset-aware type filtering
- `ResolveAvailableTypeKeys` 實作 + `AvailableTradeTypes` 過濾
- 類型下拉只顯示資產相容的 type
- Selected asset 為 None 時 disabled type ComboBox + 提示「請先選擇資產」
- **可發布**：選股票 → 只看到買/賣/股利

### P2.4 — 通用化 PriceMode / 取得市價 / inline 總計
- 把 `Buy.PriceMode` 提升到 parent
- XAML：通用 toggle + 通用價格 TextBox + inline 取得市價 link + inline 總計/單位價格 mirror
- BuyTxForm 內部對應欄位移除（或留 facade binding）
- **可發布**：dialog 視覺接近參考設計

## 風險點 + 對策

| 風險 | 對策 |
|---|---|
| `Buy.PriceMode` 提升到 parent 後，現有 Buy 流程的 binding 全錯位 | P2.4 之前 sub-panel XAML 不動；P2.4 用 facade property（parent.PriceMode → 寫 Buy.PriceMode）漸進切換 |
| Type 過濾誤殺正常用法（例如使用者本來想用「存入」記某筆股利收入） | 矩陣設計刻意讓 CashAccount 支援「收入 / 存入 / 提款 / 轉帳」一組（不只看 type 字面）；user research 若不夠就放寬 |
| 編輯既有交易時，selected asset 該設成什麼 | EditTrade 時依 trade.Type / Symbol 反推：Buy/Sell → 找 Position，Income/Deposit → 找 CashAccount。找不到時退化為「(自由輸入)」mode |
| `TxAssetSubject.Display` 在多語系下不一致 | Symbol-name 直接由 entity 取，不走 i18n；類別 prefix (Stock / Cash) 走 lang resource |
| Currency 自動覆寫使用者已選的值 | 加 `_userTouchedCurrency` flag，使用者手動改過後 OnSelectedAssetChanged 不再覆寫 |

## 測試計畫

每個 P2.x 都要：
1. 既有的 Transaction / Buy / Sell / Dividend / Loan / Transfer / CreditCard 測試全綠
2. 新增測試：
   - P2.1：`AvailableAssets` 包含 positions + cash + liabilities
   - P2.2：選資產後 `TxCurrency` / `TxCashAccount` 自動帶
   - P2.2：使用者手動改 currency 後再選資產不會被覆寫
   - P2.3：`ResolveAvailableTypeKeys` 對每個 Kind 回傳正確子集
   - P2.4：PriceMode 切換時 inline mirror 正確顯示

## Open questions（請使用者校準）

1. **編輯既有 trade 進來時，SelectedAsset 該怎麼初始化？**
   - (a) 嚴格反推：找 Position/CashAccount 對應 row 設為 selected，找不到就 null（強迫使用者重選）
   - (b) 寬鬆模式：找不到時呈現 read-only badge「(已刪除的 NVDA 持倉)」
   - **建議 (b)**：避免編輯舊資料時被卡住

2. **新標的（不在 Positions 裡）怎麼進來？**
   - 參考圖看不出哨兵設計。目前 Assetra 的「新增投資」流程在 `OpenAddWatchlistDialogCommand`
   - 選項 (a)：保留 dialog 內的 symbol search box（給 Buy 用）— 跟資產選擇器並存
   - 選項 (b)：dialog 內加「+ 新增投資...」選項，點下去跳轉「新增投資」dialog；回來時自動帶入剛建的資產
   - 選項 (c)：直接拒絕，要求使用者先去「新增投資」建立資產，再回來下單
   - **建議 (b)**：清晰但仍要使用者多點一次

3. **AvailableTradeTypes 為空（selected asset = None）時，類型 ComboBox 該怎樣？**
   - (a) Disabled + tooltip「請先選擇資產」
   - (b) 顯示全部 12 個但選了會錯
   - **建議 (a)**

4. **舊版 EditTrade 流程的 dialog 該不該也走 asset-first？**
   - 編輯時 asset 已固定，理論上不該重選
   - 建議：編輯模式下資產選擇器變成 read-only label

5. **Currency dropdown 是否支援自由輸入 ISO 3-letter code 還是只能從固定清單選？**
   - Settings 已有 `SupportedCurrencies` 清單
   - **建議**：跟 Settings 共用清單

## 工時估算

| 階段 | 預估 token 消耗 | 落地檔案數 |
|---|---|---|
| P2.1 引入資產選擇器 | 中 | 2-3 (VM + XAML + lang) |
| P2.2 幣別 + 帳戶連動 | 中-高 | 3-4 |
| P2.3 Type 過濾 | 低-中 | 2-3 |
| P2.4 通用化 PriceMode / 取得市價 | 高 | 5-6（sub-panel 整合最重）|

不建議單一 session 一次做完全部。每個 P2.x 都應有獨立 commit + 可手動驗證。

## Phase 2 完成後的下一步候選

- 編輯既有 trade 時的 dialog 視覺一致性
- Trade row inline edit（不開 dialog 也能改）
- Asset selector 加入 fuzzy search / recent
