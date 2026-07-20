# 信用卡「每月帳單式」重新設計 — Design

**日期：** 2026-07-20
**狀態：** 設計定案，待寫實作計畫
**觸及：** 財務資料庫（需備份 + 副本驗證 + 一次性搬遷）

---

## 1. 背景與問題

目前信用卡跟貸款**共用同一個模型**：都是 `AssetItem` 掛 `FinancialType.Liability`，差別只有一個 `LiabilitySubtype { Loan, CreditCard }` 旗標（`Assetra.Core/Models/AssetItem.cs`、`LiabilitySubtype.cs`）。餘額從不儲存，一律由 `Trade` 日誌投影（`Assetra.Infrastructure/BalanceQueryService.cs` 的 `ProjectLiabilitySnapshots`）。

這造成三個問題：

1. **貸款概念被硬套在循環信用上。** 卡片的「總借款金額」其實是 `Original` ＝「歷來刷卡總額」，每刷一筆就漲；「已繳%」＝`(Original − Balance)/Original`，對循環卡毫無意義。截圖的台新卡 `剩餘 = 總借款 = 61,187、已繳 0%` 就是開卡記了一筆 61,187 開帳刷卡、還沒繳的結果。
2. **開帳餘額是假的。** 模型沒有餘額欄位，開帳餘額只能偷偷寫一筆 `CreditCardCharge` 交易冒充（`AddAssetDialogViewModel` 建卡後的 opening-charge 分支）。
3. **餘額用「卡名」join（潛在 bug）。** `GetLiabilityLabel` 對卡片回傳 `t.Name`，快照字典以卡名為 key（`BalanceQueryService`、`PortfolioLoadService`），而交易其實已帶 `LiabilityAssetId` GUID FK 卻被投影忽略。改卡名或兩卡同名會靜默算錯。

使用者的實際用法：**每月結帳、自動扣款、每次只在月底才知道金額**，不逐筆記刷卡。把它當「一筆借款」建立又消帳，流程彆扭。

---

## 2. 目標 / 非目標

### 目標
- 信用卡**移出「負債」**，改為獨立的**付款方式（Payment Method）**：不是負債、不計入淨值、沒有卡片餘額。
- **每月帳單 ＝ 一筆從銀行帳戶的分類支出**，標記是哪張卡；消滅「建負債 → 消帳」流程。
- **卡片內建結帳日提醒**：到結帳日、下次開 app 時提示「本月帳單還沒記」，一鍵開預填支出。
- 保留卡名/發卡行/歷史交易；一次性搬遷既有台新卡，**不刪任何資料**。

### 非目標（明確排除，避免 scope 膨脹）
- 逐筆刷卡明細追蹤。
- 卡片餘額 / 循環信用 / 最低應繳 / 利息計算。
- 手機背景推播（桌面 app 只能在開啟時做 in-app 提醒）。
- 完整 per-card 報表儀表板 —— v1 只用「交易記錄篩選」看每張卡每月刷多少。
- 動用既有的「訂閱排程」引擎（`Assetra.Application.Recurring`）—— **明確不走這條**：信用卡帳單不是固定金額訂閱，硬塞進去語意怪且要改引擎。提醒改綁在卡片本身。

---

## 3. 使用者決策摘要（本次 brainstorming 拍板）

| 決策點 | 選擇 |
|---|---|
| 追蹤程度 | 只記每月帳單，不追卡片餘額 |
| 卡片去留 | 保留為「付款方式」標籤（保卡名/發卡行，可分卡統計） |
| 提醒機制 | **卡片內建結帳日提醒**（非訂閱排程） |
| 現有 61,187 搬遷 | 保留歷史交易為紀錄、不再計入負債/淨值；下月實際扣款時才記成支出。**淨值現在 +61,187** |

---

## 4. 設計

### 4.1 資料模型：付款方式

- 卡片仍是 `AssetItem`，但新增 `FinancialType.PaymentMethod`（第四個列舉值），台新卡的 `Type` 由 `Liability` 改為 `PaymentMethod`。付款方式**同時排除於「負債」頁、「資金帳戶」頁與淨值計算**。
- **沿用**既有欄位：`Name`、`IssuerName`、`CreditLimit`（選填）、`BillingDay`（結帳日，已存在）。
- **新增**兩個選填欄位（付款方式專用）：
  - `DefaultCashAccountId : Guid?` —— 預設扣款銀行帳戶。
  - `DefaultCategoryId : Guid?` —— 每月帳單的預設分類。
- 貸款不受影響，仍為 `FinancialType.Liability` + 攤還欄位。

> **設計取捨：** 用「新 `FinancialType`」而非新資料表，是為了沿用 `AssetItem` 既有的卡片 metadata 欄位、把搬遷縮到「改一個 Type + 補兩個欄位」。代價是所有 `switch (FinancialType)` 的點都要新增 `PaymentMethod` case（計畫階段須全面 audit：淨值計算、各清單過濾、群組）。

### 4.2 每月帳單交易

每月帳單 ＝ **一筆 `Trade`**：
- `Type = Withdrawal`（沿用既有支出機制：進 收支、可分類、扣銀行；`PrimaryCashDelta` 已對 `Withdrawal` 記 `−amount`，餘額邏輯零新增）。
- `CashAccountId = 扣款銀行`、`CategoryId = 分類`、`CashAmount = 當月帳單金額`。
- `PaymentMethodId : Guid?`（**Trade 新增欄位**）＝ 這筆帳單繳的是哪張卡。用於「分卡統計每月刷多少」。

> **設計取捨：** 另立 `PaymentMethodId` 而非重用 `LiabilityAssetId`，是為語意清楚（`LiabilityAssetId` 保留給貸款/歷史卡片交易）。既然本功能本來就要一次 schema 搬遷，多一個 nullable 欄位成本低。歷史 `CreditCardCharge/Payment` 交易的 `LiabilityAssetId` 不動。

### 4.3 提醒機制（卡片內建，非訂閱排程）

- **觸發：** 卡片 `BillingDay`。「本期」定義為 `[最近一次結帳日, 下次結帳日)`；結帳日落在短月不存在的日（29–31）時 **clamp 到當月最後一天**。
- **判斷「本期未記」：** 查是否存在一筆 `Withdrawal` 且 `PaymentMethodId = 該卡` 且 `trade_date >= 最近一次結帳日`。不存在才提醒；記了（或補記）即消失；記了又刪則重新出現。
- **提醒面：** 首頁 dashboard 的 nudge「台新卡本月帳單還沒記」＋一顆按鈕。點擊 → 開新增交易對話框，**預填** 付款方式=該卡、扣款銀行=`DefaultCashAccountId`、分類=`DefaultCategoryId`，**金額留空**待使用者填 → 確認。
- **誠實限制：** 桌面 app 僅能在**開啟時**做 in-app 提醒，非背景推播。所以是「結帳日當天或之後、你開 app 時」看到，不會主動戳你。
- **多張卡：** 各自獨立 cycle 與提醒。

### 4.4 負債頁 / 淨值

- 負債頁（`LiabilityTabPanel` / `PortfolioViewModel.LiabilitiesView`）過濾掉 `PaymentMethod`，只剩貸款。
- 淨值計算對 `PaymentMethod` 貢獻 0（不是資產也不是負債）。
- 台新卡搬遷後，淨值即 +61,187（那 61,187 不再被當負債）。

### 4.5 退役舊機制

- `TradeType.CreditCardCharge` / `CreditCardPayment`：**歷史交易保留為只讀紀錄**；新資料不再產生。
- `CreditCardTransactionWorkflowService`（Charge/Pay）、`CreditCardMutationWorkflowService`（Create）、`TransactionDialogViewModel.Confirm.CreditCard.cs`：計畫階段依引用決定「移除」或「僅保留給歷史顯示」。原「刷卡/繳卡費」類型 chip 從新增交易對話框移除。
- 卡片建立/編輯移出 `EditLiabilityDialogViewModel` 的 liability 分支與 `CreditCardCreateSection`，改到付款方式管理（見 4.7）。

### 4.6 現有資料搬遷

一次性、**冪等**、可驗證：
1. 台新卡 `AssetItem`：`Type` `Liability → PaymentMethod`。`LiabilitySubtype` 保留或清為 null（僅識別用，不影響邏輯）。
2. 歷史 `CreditCardCharge/Payment` 交易：**不動、保留為紀錄**。因為卡片已非 `Liability`，`ProjectLiabilitySnapshots` 自然不再把它算進負債（須確認投影是以 `FinancialType` 過濾，而非只看交易型別 —— 計畫階段驗證此點，必要時調整投影只對 `Liability` 資產計算）。
3. 未繳的 61,187：**不轉換**。下個月實際被扣款時，使用者用新流程記成一筆帳單支出。
4. 安全程序（實作計畫必含）：
   - 動手前**備份** `%APPDATA%\Assetra\assetra.db`、**關閉 app**。
   - 先在 **DB 副本**上跑搬遷、對搬遷前後數字（負債總額、淨值、各帳戶餘額）。
   - 搬遷程式帶前後檢查（count/總額 assertions），失敗即中止不寫。

### 4.7 管理 UI（付款方式）

- **建/編付款方式：** 欄位＝卡名、發卡行、結帳日、預設扣款銀行、預設分類、信用額度（選填）。
- **清單位置（待小確認，不阻擋 spec）：** 提議放在「資金帳戶」頁下一個輕量「付款方式」區塊，或收支區。v1 可先併入既有卡片建立入口。
- **分卡看每月刷多少：** v1 用「交易記錄」以 `PaymentMethodId` 篩選呈現，不另做儀表板。

---

## 5. 元件邊界（high-level，供 writing-plans 展開）

| 層 | 檔案 | 變更 |
|---|---|---|
| Core | `Models/FinancialType.cs` | 新增 `PaymentMethod` |
| Core | `Models/AssetItem.cs` | 新增 `DefaultCashAccountId`、`DefaultCategoryId` |
| Core | `Models/Trade.cs` | 新增 `PaymentMethodId` |
| Infra | `Persistence/*`（schema + 對應 repo） | asset_items 兩欄、trades 一欄、migration |
| Infra | `BalanceQueryService.cs` | 淨值/負債投影排除 `PaymentMethod`；確認以 `FinancialType` 過濾 |
| App | Credit-card workflow services | 退役 / 保留給歷史 |
| App | 提醒查詢 service（新） | 「本期未記」cycle 查詢 |
| WPF | 新增交易對話框 | 移除刷卡/繳卡費類型；月帳單走 Withdrawal + `PaymentMethodId` |
| WPF | Dashboard | 帳單提醒 nudge |
| WPF | 付款方式建/編/清單 | 新，取代 liability 卡片分支 |
| WPF | 負債頁 | 過濾 `PaymentMethod` |

---

## 6. 錯誤處理 / 邊界情況

- 結帳日 29–31 落在短月 → clamp 到當月最後一天。
- 卡片沒設 `DefaultCashAccountId`：提醒仍出現，但預填時銀行留空、要求使用者選才能確認。
- 本期記了又刪 → 提醒重新出現。
- 一個月多次帳單（少見）：本期已存在一筆即不提醒；使用者仍可手動再記。
- 搬遷冪等：重跑不重複改（以 `Type == Liability && LiabilitySubtype == CreditCard` 為判準，改完即不符合）。

---

## 7. 測試策略（特徵測試風格，鎖住意圖）

- **搬遷：** 於 DB 副本，搬遷後（a）負債頁不含付款方式、（b）淨值恰 +61,187、（c）各銀行帳戶餘額不變、（d）歷史交易筆數不變。
- **月帳單：** 記一筆帳單 → 恰一筆 `Withdrawal`、扣對銀行、帶 `PaymentMethodId` 與分類；淨值/銀行餘額如預期。
- **提醒 cycle：** 本期未記→提醒出現；記了→消失；刪了→重現；短月結帳日 clamp 正確。
- **負債頁 / 淨值：** 付款方式不出現在負債頁；淨值計算排除付款方式。
- **分卡統計：** 以 `PaymentMethodId` 篩選只回該卡帳單。

---

## 8. 風險

1. **動財務 DB。** 以備份 + 副本驗證 + 冪等 + 前後 assertion 緩解。
2. **`FinancialType` 新增 case 波及面。** 計畫階段須 audit 所有 `switch (FinancialType)` / 型別過濾點（淨值、清單、群組），逐一補 `PaymentMethod`。
3. **負債投影過濾依據。** 若 `ProjectLiabilitySnapshots` 是以交易型別而非 `FinancialType` 選資料，光改卡片 `Type` 不足以讓歷史卡片交易退出負債 —— 須確認並在必要時改投影邏輯。
4. **淨值 +61,187 的觀感。** 已與使用者確認接受（那筆錢仍在銀行、非債）。

---

## 9. 分階段提示（給 writing-plans）

- **Phase 1 — 資料模型 + 搬遷：** `FinancialType.PaymentMethod`、`AssetItem`/`Trade` 新欄位、schema migration、台新卡搬遷（含備份/副本驗證），負債頁與淨值排除付款方式。可獨立驗收（負債頁乾淨、淨值 +61,187、資料無損）。
- **Phase 2 — 每月帳單流程 + 提醒：** 月帳單走 `Withdrawal + PaymentMethodId`、結帳日 cycle 查詢、dashboard nudge + 預填。退役刷卡/繳卡費類型。
- **Phase 3 — 管理 UI + 分卡篩選：** 付款方式建/編/清單、交易記錄以 `PaymentMethodId` 篩選。

---

## 10. 待小確認（不阻擋 spec）

- 付款方式清單放哪個 nav 位置（資金帳戶下 / 收支下 / 併入既有入口）。
